// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Serial - Serial line transport implementation.

using System.Buffers;
using System.IO.Ports;

namespace RoyalTerminal.Terminal.Transport.Serial;

/// <summary>
/// Terminal transport over a serial line.
/// </summary>
public sealed class SerialTerminalTransport : ITerminalTransport
{
    private readonly object _sync = new();
    private SerialPort? _serialPort;
    private TransportWritePump? _writePump;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private bool _disposed;
    private int _exitRaised;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceived;

    /// <inheritdoc />
    public event Action<int>? ProcessExited;

    /// <inheritdoc />
    public bool IsRunning => _serialPort?.IsOpen ?? false;

    /// <inheritdoc />
    public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (options is not SerialTransportOptions serialOptions)
        {
            throw new ArgumentException("Invalid options type for serial transport.", nameof(options));
        }

        string portName = serialOptions.PortName.Trim();
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new InvalidOperationException("Serial port name is required.");
        }

        if (serialOptions.BaudRate <= 0)
        {
            throw new InvalidOperationException("Serial baud rate must be greater than zero.");
        }

        if (serialOptions.DataBits is < 5 or > 8)
        {
            throw new InvalidOperationException("Serial data bits must be in range 5-8.");
        }

        lock (_sync)
        {
            if (_serialPort is not null)
            {
                throw new InvalidOperationException("Serial transport is already running.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        SerialPort serialPort = new(
            portName,
            serialOptions.BaudRate,
            MapParity(serialOptions.Parity),
            serialOptions.DataBits,
            MapStopBits(serialOptions.StopBits))
        {
            Handshake = MapHandshake(serialOptions.Handshake),
            NewLine = serialOptions.NewLine,
        };

        try
        {
            serialPort.Open();
        }
        catch
        {
            serialPort.Dispose();
            throw;
        }

        CancellationTokenSource readerCts = new();
        Task readerTask = Task.Run(() => ReadLoopAsync(serialPort.BaseStream, readerCts.Token));
        TransportWritePump writePump = new(
            "RoyalTerminal.Serial.Transport.Write",
            WriteInputDirect,
            OnWritePumpFaulted);

        lock (_sync)
        {
            _serialPort = serialPort;
            _writePump = writePump;
            _readerCts = readerCts;
            _readerTask = readerTask;
            _exitRaised = 0;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return;
        }

        _writePump?.TryEnqueue(utf8);
    }

    /// <inheritdoc />
    public void Resize(TerminalSessionDimensions dimensions)
    {
        _ = dimensions;
        // Serial transport does not provide resize semantics.
    }

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        SerialPort? serialPort;
        TransportWritePump? writePump;
        CancellationTokenSource? readerCts;
        Task? readerTask;

        lock (_sync)
        {
            serialPort = _serialPort;
            writePump = _writePump;
            readerCts = _readerCts;
            readerTask = _readerTask;

            _serialPort = null;
            _writePump = null;
            _readerCts = null;
            _readerTask = null;
        }

        if (serialPort is null)
        {
            return;
        }

        try
        {
            writePump?.RequestStop(discardPendingWrites: true);
            readerCts?.Cancel();
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }

            if (readerTask is not null)
            {
                await SuppressReadExceptionsAsync(readerTask).ConfigureAwait(false);
            }
        }
        finally
        {
            serialPort.Dispose();
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

    private async Task ReadLoopAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
        bool remoteClosed = false;

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

                byte[] payload = buffer.AsSpan(0, bytesRead).ToArray();
                DataReceived?.Invoke(payload, payload.Length);
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
            SerialPort? serialPort = _serialPort;
            if (serialPort is null || !serialPort.IsOpen)
            {
                return;
            }

            Stream stream = serialPort.BaseStream;
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

    private static Parity MapParity(TerminalSerialParity parity)
    {
        return parity switch
        {
            TerminalSerialParity.None => Parity.None,
            TerminalSerialParity.Odd => Parity.Odd,
            TerminalSerialParity.Even => Parity.Even,
            TerminalSerialParity.Mark => Parity.Mark,
            TerminalSerialParity.Space => Parity.Space,
            _ => Parity.None,
        };
    }

    private static StopBits MapStopBits(TerminalSerialStopBits stopBits)
    {
        return stopBits switch
        {
            TerminalSerialStopBits.One => StopBits.One,
            TerminalSerialStopBits.OnePointFive => StopBits.OnePointFive,
            TerminalSerialStopBits.Two => StopBits.Two,
            _ => StopBits.One,
        };
    }

    private static Handshake MapHandshake(TerminalSerialHandshake handshake)
    {
        return handshake switch
        {
            TerminalSerialHandshake.None => Handshake.None,
            TerminalSerialHandshake.XOnXOff => Handshake.XOnXOff,
            TerminalSerialHandshake.RequestToSend => Handshake.RequestToSend,
            TerminalSerialHandshake.RequestToSendXOnXOff => Handshake.RequestToSendXOnXOff,
            _ => Handshake.None,
        };
    }
}

/// <summary>
/// Provider for serial transport sessions.
/// </summary>
public sealed class SerialTerminalTransportProvider : ITerminalTransportProvider
{
    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Serial;

    /// <inheritdoc />
    public bool CanHandle(ITerminalTransportOptions options)
    {
        return options is SerialTransportOptions;
    }

    /// <inheritdoc />
    public ITerminalTransport Create()
    {
        return new SerialTerminalTransport();
    }
}
