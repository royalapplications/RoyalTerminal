// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Raw - Raw TCP transport implementation.

using System.Buffers;
using System.Net.Sockets;

namespace RoyalTerminal.Terminal.Transport.Raw;

/// <summary>
/// Terminal transport over a raw TCP socket.
/// </summary>
public sealed class RawTcpTerminalTransport : ITerminalTransport
{
    private readonly object _sync = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private bool _disposed;
    private int _exitRaised;

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

        if (options is not RawTcpTransportOptions rawOptions)
        {
            throw new ArgumentException("Invalid options type for raw TCP transport.", nameof(options));
        }

        string host = rawOptions.Host.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Raw TCP host is required.");
        }

        if (rawOptions.Port is <= 0 or > 65_535)
        {
            throw new InvalidOperationException("Raw TCP port must be in range 1-65535.");
        }

        lock (_sync)
        {
            if (_client is not null)
            {
                throw new InvalidOperationException("Raw TCP transport is already running.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        TcpClient client = new();
        try
        {
            await client.ConnectAsync(host, rawOptions.Port, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        NetworkStream stream = client.GetStream();
        CancellationTokenSource readerCts = new();
        Task readerTask = Task.Run(() => ReadLoopAsync(stream, readerCts.Token));

        lock (_sync)
        {
            _client = client;
            _stream = stream;
            _readerCts = readerCts;
            _readerTask = readerTask;
            _exitRaised = 0;
        }
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return;
        }

        NetworkStream? stream = _stream;
        if (stream is null)
        {
            return;
        }

        byte[] copy = utf8.ToArray();
        lock (_sync)
        {
            if (_stream is null)
            {
                return;
            }

            stream.Write(copy, 0, copy.Length);
            stream.Flush();
        }
    }

    /// <inheritdoc />
    public void Resize(TerminalSessionDimensions dimensions)
    {
        _ = dimensions;
        // Raw TCP transport has no resize semantics.
    }

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        TcpClient? client;
        NetworkStream? stream;
        CancellationTokenSource? readerCts;
        Task? readerTask;

        lock (_sync)
        {
            client = _client;
            stream = _stream;
            readerCts = _readerCts;
            readerTask = _readerTask;

            _client = null;
            _stream = null;
            _readerCts = null;
            _readerTask = null;
        }

        if (client is null)
        {
            return;
        }

        try
        {
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
}

/// <summary>
/// Provider for raw TCP transport sessions.
/// </summary>
public sealed class RawTcpTerminalTransportProvider : ITerminalTransportProvider
{
    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.RawTcp;

    /// <inheritdoc />
    public bool CanHandle(ITerminalTransportOptions options)
    {
        return options is RawTcpTransportOptions;
    }

    /// <inheritdoc />
    public ITerminalTransport Create()
    {
        return new RawTcpTerminalTransport();
    }
}
