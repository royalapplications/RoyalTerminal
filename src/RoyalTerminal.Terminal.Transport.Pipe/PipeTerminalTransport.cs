// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Pipe - Process pipe transport implementation.

using System.Buffers;
using System.Diagnostics;

namespace RoyalTerminal.Terminal.Transport.Pipe;

/// <summary>Terminal transport over redirected process pipes.</summary>
public sealed class PipeTerminalTransport : ITerminalTransport, ITerminalChildIdentity
{
    private readonly object _sync = new();
    private Process? _process;
    private Stream? _stdin;
    private TransportWritePump? _writePump;
    private CancellationTokenSource? _readerCts;
    private Task? _stdoutReaderTask;
    private Task? _stderrReaderTask;
    private bool _disposed;
    private int _exitRaised;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceived;
    /// <inheritdoc />
    public event Action<int>? ProcessExited;
    /// <inheritdoc />
    public bool IsRunning => LiveChildId.HasValue;
    /// <inheritdoc />
    public int? LiveChildId { get { lock (_sync) return _process is { HasExited: false } p ? p.Id : null; } }

    /// <inheritdoc />
    public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        if (options is not PipeTransportOptions spec) throw new ArgumentException("Invalid options type for pipe transport.", nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        ProcessStartInfo startInfo = new(spec.Command.FileName)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true,
        };
        if (!string.IsNullOrWhiteSpace(spec.WorkingDirectory)) startInfo.WorkingDirectory = spec.WorkingDirectory;
        foreach (string argument in spec.Command.Arguments) startInfo.ArgumentList.Add(argument);
        if (spec.Environment is not null) foreach ((string key, string value) in spec.Environment) startInfo.Environment[key] = value;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is not null) throw new InvalidOperationException("Pipe transport is already running.");
            Process process = new() { StartInfo = startInfo };
            try
            {
                if (!process.Start()) throw new InvalidOperationException("Failed to start pipe transport process.");
                var stdout = process.StandardOutput.BaseStream; var stderr = process.StandardError.BaseStream;
                _readerCts = new(); var ct = _readerCts.Token;
                _stdin = process.StandardInput.BaseStream;
                _stdoutReaderTask = Task.Run(() => ReadLoopAsync(stdout, true, ct));
                _stderrReaderTask = Task.Run(() => ReadLoopAsync(stderr, spec.MergeStdErrIntoStdOut, ct));
                _writePump = new("RoyalTerminal.Pipe.Transport.Write", WriteInputDirect, OnWritePumpFaulted);
                _process = process; _exitRaised = 0;
                // Enabling after publication also catches processes which exited before Start returned.
                process.Exited += OnProcessExited; process.EnableRaisingEvents = true;
            }
            catch
            {
                process.Exited -= OnProcessExited;
                try { if (!process.HasExited) process.Kill(entireProcessTree:true); } catch (InvalidOperationException) { }
                _writePump?.RequestStop(discardPendingWrites:true); _readerCts?.Cancel(); _stdin?.Dispose();
                _readerCts?.Dispose(); _readerCts=null; _stdin=null; _writePump=null; _process=null;
                process.Dispose(); throw;
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> utf8)
    {
        if (_stdin is not null && !utf8.IsEmpty) _writePump?.TryEnqueue(utf8);
    }
    /// <inheritdoc />
    public void Resize(TerminalSessionDimensions dimensions) { /* Redirected pipes have no OS window. */ }

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        Process? process; CancellationTokenSource? cts; Task? stdoutTask, stderrTask; Stream? stdin; TransportWritePump? writePump;
        lock (_sync)
        {
            process = _process; cts = _readerCts; stdoutTask = _stdoutReaderTask; stderrTask = _stderrReaderTask;
            stdin = _stdin; writePump = _writePump;
            _process=null; _readerCts=null; _stdoutReaderTask=null; _stderrReaderTask=null; _stdin=null; _writePump=null;
        }
        if (process is null) return;
        try
        {
            writePump?.RequestStop(discardPendingWrites:true); cts?.Cancel();
            if (!process.HasExited) try { process.Kill(entireProcessTree:true); } catch { /* Best effort on explicit stop. */ }
            if (stdoutTask is not null) await SuppressReadExceptionsAsync(stdoutTask).ConfigureAwait(false);
            if (stderrTask is not null) await SuppressReadExceptionsAsync(stderrTask).ConfigureAwait(false);
        }
        finally
        {
            int code = process.HasExited ? process.ExitCode : -1;
            stdin?.Dispose(); _ = writePump?.Join(TimeSpan.FromSeconds(5)); process.Exited -= OnProcessExited;
            process.Dispose(); cts?.Dispose(); RaiseProcessExitedOnce(code);
        }
    }
    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync) { if (_disposed) return; _disposed=true; }
        _ = StopAsync();
    }

    private async Task ReadLoopAsync(Stream stream, bool emitOutput, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int n=await stream.ReadAsync(buffer.AsMemory(),cancellationToken).ConfigureAwait(false);
                if (n==0) break;
                if (emitOutput) { byte[] owned=buffer.AsSpan(0,n).ToArray(); DataReceived?.Invoke(owned,n); }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch { /* Exit lifecycle surfaces transport failures. */ }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process) return;
        Task? stdout,stderr; int code;
        lock (_sync)
        {
            if (!ReferenceEquals(_process,process)) return;
            code=process.ExitCode; stdout=_stdoutReaderTask; stderr=_stderrReaderTask;
        }
        _ = DrainReadersAndRaiseExitAsync(stdout,stderr,code);
    }
    private async Task DrainReadersAndRaiseExitAsync(Task? stdout,Task? stderr,int code)
    {
        if(stdout is not null) await SuppressReadExceptionsAsync(stdout).ConfigureAwait(false);
        if(stderr is not null) await SuppressReadExceptionsAsync(stderr).ConfigureAwait(false);
        RaiseProcessExitedOnce(code);
    }
    private void RaiseProcessExitedOnce(int code) { if(Interlocked.Exchange(ref _exitRaised,1)==0) ProcessExited?.Invoke(code); }
    private void WriteInputDirect(byte[] payload)
    {
        lock (_sync) { if (_stdin is { } input) { input.Write(payload,0,payload.Length); input.Flush(); } }
    }
    private void OnWritePumpFaulted(Exception exception) { RaiseProcessExitedOnce(-1); _=Task.Run(async()=>await StopAsync().ConfigureAwait(false)); }
    private static async Task SuppressReadExceptionsAsync(Task task) { try { await task.ConfigureAwait(false); } catch { /* Non-fatal during teardown. */ } }
}

/// <summary>Provider for process pipe transport sessions.</summary>
public sealed class PipeTerminalTransportProvider : ITerminalTransportProvider
{
    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Pipe;
    /// <inheritdoc />
    public bool CanHandle(ITerminalTransportOptions options) => options is PipeTransportOptions;
    /// <inheritdoc />
    public ITerminalTransport Create() => new PipeTerminalTransport();
}
