// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Telnet - Telnet transport implementation.

using System.Buffers;
using System.Net.Sockets;
using System.Text;

namespace RoyalTerminal.Terminal.Transport.Telnet;

/// <summary>
/// Terminal transport over a Telnet TCP session.
/// </summary>
public sealed class TelnetTerminalTransport : ITerminalTransport
{
    private const byte Iac = 255;
    private const byte Do = 253;
    private const byte Dont = 254;
    private const byte Will = 251;
    private const byte Wont = 252;
    private const byte Sb = 250;
    private const byte Se = 240;
    private const byte TerminalTypeOption = 24;
    private const byte SuppressGoAheadOption = 3;
    private const byte EchoOption = 1;
    private const byte NawsOption = 31;
    private const byte TerminalTypeIs = 0;
    private const byte TerminalTypeSend = 1;

    private readonly object _sync = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private TransportWritePump? _writePump;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private bool _disposed;
    private int _exitRaised;

    private string _terminalType = "xterm";
    private bool _nawsEnabled;
    private int _columns = 80;
    private int _rows = 24;

    private TelnetParserState _parserState = TelnetParserState.Data;
    private byte _negotiationCommand;
    private byte _subnegotiationOption;
    private readonly List<byte> _subnegotiationBuffer = [];

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceived;

    /// <inheritdoc />
    public event Action<int>? ProcessExited;

    /// <inheritdoc />
    public bool IsRunning => _client is { Connected: true };

    /// <inheritdoc />
    public async ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (options is not TelnetTransportOptions telnetOptions)
        {
            throw new ArgumentException("Invalid options type for Telnet transport.", nameof(options));
        }

        string host = telnetOptions.Host.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Telnet host is required.");
        }

        if (telnetOptions.Port is <= 0 or > 65_535)
        {
            throw new InvalidOperationException("Telnet port must be in range 1-65535.");
        }

        lock (_sync)
        {
            if (_client is not null)
            {
                throw new InvalidOperationException("Telnet transport is already running.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        TcpClient client = new();
        try
        {
            await client.ConnectAsync(host, telnetOptions.Port, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _terminalType = string.IsNullOrWhiteSpace(telnetOptions.TerminalType)
            ? "xterm"
            : telnetOptions.TerminalType.Trim();
        _columns = Math.Max(1, telnetOptions.Dimensions.Columns);
        _rows = Math.Max(1, telnetOptions.Dimensions.Rows);
        _nawsEnabled = false;
        _parserState = TelnetParserState.Data;
        _negotiationCommand = 0;
        _subnegotiationOption = 0;
        _subnegotiationBuffer.Clear();

        NetworkStream stream = client.GetStream();
        CancellationTokenSource readerCts = new();
        Task readerTask = Task.Run(() => ReadLoopAsync(stream, readerCts.Token));
        TransportWritePump writePump = new(
            "RoyalTerminal.Telnet.Transport.Write",
            WriteInputDirect,
            OnWritePumpFaulted);

        lock (_sync)
        {
            _client = client;
            _stream = stream;
            _writePump = writePump;
            _readerCts = readerCts;
            _readerTask = readerTask;
            _exitRaised = 0;
        }

        if (!string.IsNullOrWhiteSpace(telnetOptions.InitialCommand))
        {
            byte[] command = Encoding.UTF8.GetBytes($"{telnetOptions.InitialCommand}\r\n");
            SendInput(command);
        }
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return;
        }

        byte[] escaped = EscapeIac(utf8);
        _writePump?.TryEnqueue(escaped);
    }

    /// <inheritdoc />
    public void Resize(TerminalSessionDimensions dimensions)
    {
        _columns = Math.Max(1, dimensions.Columns);
        _rows = Math.Max(1, dimensions.Rows);

        if (!_nawsEnabled)
        {
            return;
        }

        List<byte> payload = [];
        AppendNaws(payload, _columns, _rows);
        WriteTelnetBytes(payload);
    }

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        TcpClient? client;
        NetworkStream? stream;
        TransportWritePump? writePump;
        CancellationTokenSource? readerCts;
        Task? readerTask;

        lock (_sync)
        {
            client = _client;
            stream = _stream;
            writePump = _writePump;
            readerCts = _readerCts;
            readerTask = _readerTask;

            _client = null;
            _stream = null;
            _writePump = null;
            _readerCts = null;
            _readerTask = null;
        }

        if (client is null)
        {
            return;
        }

        try
        {
            writePump?.RequestStop(discardPendingWrites: true);
            readerCts?.Cancel();
            stream?.Dispose();
            client.Dispose();

            if (readerTask is not null)
            {
                await SuppressReadExceptionsAsync(readerTask).ConfigureAwait(false);
            }
        }
        finally
        {
            _ = writePump?.Join(TimeSpan.FromSeconds(5));
            readerCts?.Dispose();
            RaiseProcessExitedOnce(0);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = StopAsync();
    }

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        bool remoteClosed = false;
        List<byte> output = [];
        List<byte> responses = [];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    remoteClosed = true;
                    break;
                }

                output.Clear();
                responses.Clear();
                ProcessIncoming(buffer.AsSpan(0, bytesRead), output, responses);

                if (responses.Count > 0)
                {
                    WriteTelnetBytes(responses);
                }

                if (output.Count > 0)
                {
                    byte[] payload = output.ToArray();
                    DataReceived?.Invoke(payload, payload.Length);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Expected during shutdown.
        }
        catch
        {
            RaiseProcessExitedOnce(-1);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (remoteClosed)
            {
                RaiseProcessExitedOnce(0);
            }
        }
    }

    private void ProcessIncoming(ReadOnlySpan<byte> input, List<byte> output, List<byte> responses)
    {
        for (int i = 0; i < input.Length; i++)
        {
            byte b = input[i];

            switch (_parserState)
            {
                case TelnetParserState.Data:
                    if (b == Iac)
                    {
                        _parserState = TelnetParserState.Iac;
                    }
                    else
                    {
                        output.Add(b);
                    }
                    break;

                case TelnetParserState.Iac:
                    if (b == Iac)
                    {
                        output.Add(Iac);
                        _parserState = TelnetParserState.Data;
                    }
                    else if (b is Do or Dont or Will or Wont)
                    {
                        _negotiationCommand = b;
                        _parserState = TelnetParserState.NegotiationOption;
                    }
                    else if (b == Sb)
                    {
                        _subnegotiationOption = 0;
                        _subnegotiationBuffer.Clear();
                        _parserState = TelnetParserState.SubnegotiationOption;
                    }
                    else
                    {
                        _parserState = TelnetParserState.Data;
                    }
                    break;

                case TelnetParserState.NegotiationOption:
                    HandleNegotiation(_negotiationCommand, b, responses);
                    _parserState = TelnetParserState.Data;
                    break;

                case TelnetParserState.SubnegotiationOption:
                    _subnegotiationOption = b;
                    _subnegotiationBuffer.Clear();
                    _parserState = TelnetParserState.Subnegotiation;
                    break;

                case TelnetParserState.Subnegotiation:
                    if (b == Iac)
                    {
                        _parserState = TelnetParserState.SubnegotiationIac;
                    }
                    else
                    {
                        _subnegotiationBuffer.Add(b);
                    }
                    break;

                case TelnetParserState.SubnegotiationIac:
                    if (b == Iac)
                    {
                        _subnegotiationBuffer.Add(Iac);
                        _parserState = TelnetParserState.Subnegotiation;
                    }
                    else if (b == Se)
                    {
                        HandleSubnegotiation(_subnegotiationOption, _subnegotiationBuffer, responses);
                        _subnegotiationBuffer.Clear();
                        _parserState = TelnetParserState.Data;
                    }
                    else
                    {
                        _subnegotiationBuffer.Clear();
                        _parserState = TelnetParserState.Data;
                    }
                    break;
            }
        }
    }

    private void HandleNegotiation(byte command, byte option, List<byte> responses)
    {
        bool accept = option is EchoOption or SuppressGoAheadOption or TerminalTypeOption or NawsOption;

        switch (command)
        {
            case Do:
                AppendNegotiationResponse(responses, accept ? Will : Wont, option);
                if (option == NawsOption)
                {
                    _nawsEnabled = accept;
                    if (accept)
                    {
                        AppendNaws(responses, _columns, _rows);
                    }
                }
                break;

            case Dont:
                if (option == NawsOption)
                {
                    _nawsEnabled = false;
                }

                AppendNegotiationResponse(responses, Wont, option);
                break;

            case Will:
                AppendNegotiationResponse(responses, accept ? Do : Dont, option);
                break;

            case Wont:
                if (option == NawsOption)
                {
                    _nawsEnabled = false;
                }

                AppendNegotiationResponse(responses, Dont, option);
                break;
        }
    }

    private void HandleSubnegotiation(byte option, List<byte> payload, List<byte> responses)
    {
        if (option != TerminalTypeOption || payload.Count == 0)
        {
            return;
        }

        if (payload[0] != TerminalTypeSend)
        {
            return;
        }

        responses.Add(Iac);
        responses.Add(Sb);
        responses.Add(TerminalTypeOption);
        responses.Add(TerminalTypeIs);

        byte[] encodedType = Encoding.ASCII.GetBytes(_terminalType);
        for (int i = 0; i < encodedType.Length; i++)
        {
            byte next = encodedType[i];
            responses.Add(next);
            if (next == Iac)
            {
                responses.Add(Iac);
            }
        }

        responses.Add(Iac);
        responses.Add(Se);
    }

    private void AppendNaws(List<byte> target, int columns, int rows)
    {
        target.Add(Iac);
        target.Add(Sb);
        target.Add(NawsOption);
        AppendSubnegotiationValue(target, (byte)((columns >> 8) & 0xFF));
        AppendSubnegotiationValue(target, (byte)(columns & 0xFF));
        AppendSubnegotiationValue(target, (byte)((rows >> 8) & 0xFF));
        AppendSubnegotiationValue(target, (byte)(rows & 0xFF));
        target.Add(Iac);
        target.Add(Se);
    }

    private static void AppendSubnegotiationValue(List<byte> target, byte value)
    {
        target.Add(value);
        if (value == Iac)
        {
            target.Add(Iac);
        }
    }

    private static void AppendNegotiationResponse(List<byte> responses, byte command, byte option)
    {
        responses.Add(Iac);
        responses.Add(command);
        responses.Add(option);
    }

    private void WriteTelnetBytes(List<byte> bytes)
    {
        if (bytes.Count == 0)
        {
            return;
        }

        NetworkStream? stream = _stream;
        if (stream is null)
        {
            return;
        }

        byte[] payload = bytes.ToArray();
        WriteInputDirect(payload);
    }

    private static byte[] EscapeIac(ReadOnlySpan<byte> input)
    {
        int escapeCount = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == Iac)
            {
                escapeCount++;
            }
        }

        if (escapeCount == 0)
        {
            return input.ToArray();
        }

        byte[] escaped = new byte[input.Length + escapeCount];
        int writeIndex = 0;
        for (int i = 0; i < input.Length; i++)
        {
            byte next = input[i];
            escaped[writeIndex++] = next;
            if (next == Iac)
            {
                escaped[writeIndex++] = Iac;
            }
        }

        return escaped;
    }

    private void RaiseProcessExitedOnce(int exitCode)
    {
        if (Interlocked.Exchange(ref _exitRaised, 1) == 1)
        {
            return;
        }

        ProcessExited?.Invoke(exitCode);
    }

    private void WriteInputDirect(byte[] payload)
    {
        lock (_sync)
        {
            NetworkStream? stream = _stream;
            if (stream is null)
            {
                return;
            }

            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }
    }

    private void OnWritePumpFaulted(Exception exception)
    {
        _ = exception;
        RaiseProcessExitedOnce(-1);
        _ = Task.Run(async () => await StopAsync().ConfigureAwait(false));
    }

    private static async Task SuppressReadExceptionsAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Reader faults are non-fatal during teardown.
        }
    }

    private enum TelnetParserState
    {
        Data,
        Iac,
        NegotiationOption,
        SubnegotiationOption,
        Subnegotiation,
        SubnegotiationIac,
    }
}

/// <summary>
/// Provider for Telnet transport sessions.
/// </summary>
public sealed class TelnetTerminalTransportProvider : ITerminalTransportProvider
{
    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Telnet;

    /// <inheritdoc />
    public bool CanHandle(ITerminalTransportOptions options)
    {
        return options is TelnetTransportOptions;
    }

    /// <inheritdoc />
    public ITerminalTransport Create()
    {
        return new TelnetTerminalTransport();
    }
}
