// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Pipe - Process pipe transport implementation.

using System.Buffers;
using System.Diagnostics;

namespace RoyalTerminal.Terminal.Transport.Pipe;

/// <summary>
/// Terminal transport over redirected process pipes.
/// </summary>
public sealed class PipeTerminalTransport : ITerminalTransport
{
    private readonly object _sync = new();

    private Process? _process;
    private Stream? _stdin;
    private TransportWritePump? _writePump;
    private CancellationTokenSource? _readerCts;
    private Task? _stdoutReaderTask;
    private Task? _stderrReaderTask;
    private bool _emitStdErr;
    private bool _disposed;
    private int _exitRaised;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceived;

    /// <inheritdoc />
    public event Action<int>? ProcessExited;

    /// <inheritdoc />
    public bool IsRunning => _process is { HasExited: false };

    /// <inheritdoc />
    public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (options is not PipeTransportOptions pipeOptions)
        {
            throw new ArgumentException("Invalid options type for pipe transport.", nameof(options));
        }

        lock (_sync)
        {
            if (_process is not null)
            {
                throw new InvalidOperationException("Pipe transport is already running.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = new(pipeOptions.Command.FileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(pipeOptions.WorkingDirectory))
        {
            startInfo.WorkingDirectory = pipeOptions.WorkingDirectory;
        }

        for (int i = 0; i < pipeOptions.Command.Arguments.Count; i++)
        {
            startInfo.ArgumentList.Add(pipeOptions.Command.Arguments[i]);
        }

        if (pipeOptions.Environment is not null)
        {
            foreach ((string key, string value) in pipeOptions.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        process.Exited += OnProcessExited;

        if (!process.Start())
        {
            process.Exited -= OnProcessExited;
            process.Dispose();
            throw new InvalidOperationException("Failed to start pipe transport process.");
        }

        _emitStdErr = pipeOptions.MergeStdErrIntoStdOut;
        _readerCts = new CancellationTokenSource();
        _stdin = process.StandardInput.BaseStream;
        _stdoutReaderTask = Task.Run(() => ReadLoopAsync(process.StandardOutput.BaseStream, emitOutput: true, _readerCts.Token));
        _stderrReaderTask = Task.Run(() => ReadLoopAsync(process.StandardError.BaseStream, emitOutput: _emitStdErr, _readerCts.Token));
        _writePump = new TransportWritePump(
            "RoyalTerminal.Pipe.Transport.Write",
            WriteInputDirect,
            OnWritePumpFaulted);

        lock (_sync)
        {
            _process = process;
            _exitRaised = 0;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> utf8)
    {
        if (_stdin is null || utf8.IsEmpty)
        {
            return;
        }

        _writePump?.TryEnqueue(utf8);
    }

    /// <inheritdoc />
    public void Resize(TerminalSessionDimensions dimensions)
    {
        _ = dimensions;
        // Pipe transport does not provide PTY window sizing semantics.
    }

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        Process? process;
        CancellationTokenSource? cts;
        Task? stdoutTask;
        Task? stderrTask;
        Stream? stdin;
        TransportWritePump? writePump;

        lock (_sync)
        {
            process = _process;
            cts = _readerCts;
            stdoutTask = _stdoutReaderTask;
            stderrTask = _stderrReaderTask;
            stdin = _stdin;
            writePump = _writePump;

            _process = null;
            _readerCts = null;
            _stdoutReaderTask = null;
            _stderrReaderTask = null;
            _stdin = null;
            _writePump = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            writePump?.RequestStop(discardPendingWrites: true);
            cts?.Cancel();

            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort only.
                }
            }

            if (stdoutTask is not null)
            {
                await SuppressReadExceptionsAsync(stdoutTask).ConfigureAwait(false);
            }

            if (stderrTask is not null)
            {
                await SuppressReadExceptionsAsync(stderrTask).ConfigureAwait(false);
            }
        }
        finally
        {
            int exitCode = process.HasExited ? process.ExitCode : -1;

            stdin?.Dispose();
            _ = writePump?.Join(TimeSpan.FromSeconds(5));
            process.Exited -= OnProcessExited;
            process.Dispose();
            cts?.Dispose();

            RaiseProcessExitedOnce(exitCode);
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

    private async Task ReadLoopAsync(Stream stream, bool emitOutput, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    break;
                }

                if (!emitOutput)
                {
                    continue;
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
            // Input/output failures are surfaced through process exit lifecycle.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
        {
            return;
        }

        bool stopInProgress;
        lock (_sync)
        {
            stopInProgress = _readerCts is null;
        }

        if (stopInProgress)
        {
            // StopAsync controls reader teardown and process exit signaling.
            return;
        }

        _ = DrainReadersAndRaiseExitAsync(process.ExitCode);
    }

    private async Task DrainReadersAndRaiseExitAsync(int exitCode)
    {
        Task? stdoutTask;
        Task? stderrTask;

        lock (_sync)
        {
            stdoutTask = _stdoutReaderTask;
            stderrTask = _stderrReaderTask;
        }

        if (stdoutTask is not null)
        {
            await SuppressReadExceptionsAsync(stdoutTask).ConfigureAwait(false);
        }

        if (stderrTask is not null)
        {
            await SuppressReadExceptionsAsync(stderrTask).ConfigureAwait(false);
        }

        RaiseProcessExitedOnce(exitCode);
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
            Stream? stdin = _stdin;
            if (stdin is null)
            {
                return;
            }

            stdin.Write(payload, 0, payload.Length);
            stdin.Flush();
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
            // Reader faults are non-fatal for stop lifecycle.
        }
    }
}

/// <summary>
/// Provider for process pipe transport sessions.
/// </summary>
public sealed class PipeTerminalTransportProvider : ITerminalTransportProvider
{
    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Pipe;

    /// <inheritdoc />
    public bool CanHandle(ITerminalTransportOptions options)
    {
        return options is PipeTransportOptions;
    }

    /// <inheritdoc />
    public ITerminalTransport Create()
    {
        return new PipeTerminalTransport();
    }
}
