using System.Net.WebSockets;
using Avalonia.Threading;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.Services;

internal sealed class WorkspaceTerminalController : IWorkspaceTerminal, IAsyncDisposable
{
    private readonly RemoteTerminalControl _control;
    private MultiplexingTerminalTransport? _transport;
    private MultiplexingTransportOptions? _options;
    private CancellationTokenSource? _attachment;
    private Task? _recovery;
    private int _generation;
    private volatile bool _detaching, _exited;
    private bool _initialReady, _disposed;

    public WorkspaceTerminalController(RemoteTerminalControl control, HttpClient? checkpointHttp = null)
    {
        _control = control;
        if (checkpointHttp is not null)
            control.RemoteSession.Recover = (options, ct) => NativeCheckpointRecovery.RecoverAsync(control, checkpointHttp, options, ct);
        control.RemoteFactory.Created += OnTransportCreated;
        control.RemoteSession.InputFailed += OnInputFailed;
        control.RemoteSession.RecoveredExit += OnProcessExited;
    }
    public event Action<string>? StatusChanged;
    public bool IsAttached => _transport?.IsRunning == true;
    public bool HasControl => !_exited && _transport?.HasControl == true;

    public async ValueTask AttachAsync(MultiplexingTransportOptions options, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this);
        await DetachAsync();
        _detaching = false; _exited = false;
        _attachment = new CancellationTokenSource(); _options = options;
        int generation = _generation;
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(_attachment.Token, cancellationToken);
        try
        {
            await _control.StartSessionAsync(options with { AfterSequence = 0 }, false, startup.Token);
            if (_generation != generation) return;
            _initialReady = true; _control.FlushPendingTransportOutput(); _control.Focus();
            if (!_exited) StatusChanged?.Invoke(HasControl ? "Attached · controller" : "Attached · observer");
        }
        catch { if (_generation == generation) await DetachAsync(); throw; }
    }
    public async ValueTask DetachAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        _detaching = true; _initialReady = false; ++_generation;
        var lifetime = _attachment; _attachment = null; lifetime?.Cancel();
        var recovery = _recovery; _recovery = null;
        try
        {
            if (recovery is not null) await recovery;
            if (_transport is { } transport) await transport.StopAsync();
            UnbindTransport(); _control.StopPty(); _options = null; _control.RemoteSession.StartFromCheckpoint = false;
        }
        finally { lifetime?.Dispose(); }
        StatusChanged?.Invoke("Detached · server processes remain running");
    }
    private void UnbindTransport()
    {
        if (_transport is not { } transport) return;
        transport.AuthoritativeDimensionsChanged -= OnDimensions;
        transport.ConnectionLost -= OnConnectionLost;
        transport.ProcessExited -= OnProcessExited;
        _transport = null;
    }
    public void RequestDimensions(TerminalSessionDimensions dimensions)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!HasControl || _transport is null) throw new InvalidOperationException("Attach as controller before changing the shared grid.");
        _transport.Resize(dimensions);
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; await DetachAsync();
        _control.RemoteFactory.Created -= OnTransportCreated;
        _control.RemoteSession.InputFailed -= OnInputFailed;
        _control.RemoteSession.RecoveredExit -= OnProcessExited;
        _control.RemoteSession.Recover = null;
    }
    private void OnTransportCreated(MultiplexingTerminalTransport transport)
    {
        UnbindTransport(); _transport = transport;
        transport.AuthoritativeDimensionsChanged += OnDimensions;
        transport.ConnectionLost += OnConnectionLost;
        transport.ProcessExited += OnProcessExited;
    }
    private void OnDimensions(TerminalSessionDimensions d)
    {
        void Apply() { if (!_detaching) _control.ApplyRemoteDimensions(d); }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.InvokeAsync(Apply).GetAwaiter().GetResult();
    }
    private static bool Retryable(Exception? error) => error is not MultiplexingProtocolException &&
        error is (IOException or WebSocketException or TimeoutException or OperationCanceledException);
    private void OnConnectionLost(Exception? error)
    {
        var source = _transport; int generation = Volatile.Read(ref _generation);
        Dispatcher.UIThread.Post(() =>
        {
            if (_detaching || _disposed || _exited || generation != _generation || source is null || !ReferenceEquals(source, _transport) || source.IsRunning) return;
            if (_recovery is { IsCompleted: false }) return;
            if (!_initialReady || !Retryable(error) || _attachment is null || _options is null)
            { StatusChanged?.Invoke(error is null ? "Disconnected · attach explicitly to reconnect" : "Disconnected · " + error.Message); return; }
            _recovery = RecoverAsync(source, _options, generation, _attachment.Token);
        });
    }
    private async Task RecoverAsync(MultiplexingTerminalTransport transport, MultiplexingTransportOptions options, int generation, CancellationToken ct)
    {
        const int attempts = 6;
        try
        {
            for (int i = 0; i < attempts; i++)
            {
                if (generation != _generation || _exited) return;
                StatusChanged?.Invoke($"Connection lost · reconnecting {i + 1}/{attempts}");
                await Task.Delay(Math.Min(4000, 200 << i), ct);
                await transport.StopAsync(); ct.ThrowIfCancellationRequested();
                if (_exited || generation != _generation) return;
                _control.FlushPendingTransportOutput();
                try { await transport.StartAsync(options with { AfterSequence = transport.LastSequence }, ct); }
                catch (MultiplexingProtocolException ex) when (ex.Code == "HistoryGap" && _control.RemoteSession.Recover is not null)
                {
                    // The previous reader is joined and its queued output is drained. Rebind both
                    // rendering and input-mode sources before the replacement transport sees any suffix.
                    ct.ThrowIfCancellationRequested(); UnbindTransport(); _control.StopPty();
                    _control.RemoteSession.StartFromCheckpoint = true;
                    await _control.StartSessionAsync(options, false, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && Retryable(ex) && i + 1 < attempts) { continue; }
                ct.ThrowIfCancellationRequested(); _control.FlushPendingTransportOutput();
                if (!_exited) StatusChanged?.Invoke(HasControl ? "Reconnected · controller" : "Reconnected · observer");
                return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        { if (!_detaching && generation == _generation && !_exited) { _initialReady = false; StatusChanged?.Invoke("Reconnect stopped · " + error.Message); } }
    }
    private void OnProcessExited(int code)
    {
        _exited = true; var source = _transport;
        Dispatcher.UIThread.Post(() => { if (!_detaching && ReferenceEquals(source, _transport)) StatusChanged?.Invoke($"Process exited · code {code}"); });
    }
    private void OnInputFailed(Exception error)
    {
        var source = _transport;
        Dispatcher.UIThread.Post(() => { if (!_detaching && ReferenceEquals(source, _transport)) StatusChanged?.Invoke("Input rejected · " + error.Message); });
    }
}
