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

    public WorkspaceTerminalController(RemoteTerminalControl control)
    {
        _control = control;
        _control.RemoteFactory.Created += OnTransportCreated;
        _control.RemoteSession.InputFailed += OnInputFailed;
    }

    public event Action<string>? StatusChanged;
    public bool IsAttached => _transport?.IsRunning == true;
    public bool HasControl => !_exited && _transport?.HasControl == true;

    public async ValueTask AttachAsync(MultiplexingTransportOptions options, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        await DetachAsync();
        _detaching = false; _exited = false;
        _attachment = new CancellationTokenSource(); _options = options;
        int generation = _generation;
        using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(_attachment.Token, cancellationToken);
        try
        {
            await _control.StartSessionAsync(options with { AfterSequence = 0 }, preserveScrollback: false, startup.Token);
            if (_generation != generation) return;
            _initialReady = true;
            _control.FlushPendingTransportOutput(); _control.Focus();
            if (!_exited) StatusChanged?.Invoke(HasControl ? "Attached · controller" : "Attached · observer");
        }
        catch
        {
            if (_generation == generation) await DetachAsync();
            throw;
        }
    }

    public async ValueTask DetachAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        _detaching = true; _initialReady = false; ++_generation;
        CancellationTokenSource? lifetime = _attachment; _attachment = null;
        lifetime?.Cancel();
        Task? recovery = _recovery; _recovery = null;
        try
        {
            if (recovery is not null) await recovery;
            if (_transport is { } transport)
            {
                await transport.StopAsync();
                transport.AuthoritativeDimensionsChanged -= OnDimensions;
                transport.ConnectionLost -= OnConnectionLost;
                transport.ProcessExited -= OnProcessExited;
                _transport = null;
            }
            _control.StopPty(); _options = null;
        }
        finally { lifetime?.Dispose(); }
        StatusChanged?.Invoke("Detached · server processes remain running");
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
    }
    private void OnTransportCreated(MultiplexingTerminalTransport transport)
    {
        _transport = transport;
        transport.AuthoritativeDimensionsChanged += OnDimensions;
        transport.ConnectionLost += OnConnectionLost;
        transport.ProcessExited += OnProcessExited;
    }
    private void OnDimensions(TerminalSessionDimensions dimensions)
    {
        void Apply() { if (!_detaching) _control.ApplyRemoteDimensions(dimensions); }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.InvokeAsync(Apply).GetAwaiter().GetResult();
    }

    // A normal close may be an administrator disconnect. Never automatically reclaim a released lease.
    private static bool Retryable(Exception? error) => error is not MultiplexingProtocolException &&
        error is (IOException or WebSocketException or TimeoutException or OperationCanceledException);

    private void OnConnectionLost(Exception? error)
    {
        MultiplexingTerminalTransport? source = _transport; int generation = Volatile.Read(ref _generation);
        Dispatcher.UIThread.Post(() =>
        {
            if (_detaching || _disposed || _exited || generation != _generation || !ReferenceEquals(source, _transport) || source is null || source.IsRunning) return;
            if (_recovery is { IsCompleted: false }) return;
            if (!_initialReady || !Retryable(error) || _attachment is null || _options is null)
            {
                StatusChanged?.Invoke(error is null ? "Disconnected · attach explicitly to reconnect" : "Disconnected · " + error.Message); return;
            }
            _recovery = RecoverAsync(source, _options, generation, _attachment.Token);
        });
    }
    private async Task RecoverAsync(MultiplexingTerminalTransport transport, MultiplexingTransportOptions options, int generation, CancellationToken cancellation)
    {
        const int attempts = 6;
        try
        {
            for (int attempt = 0; attempt < attempts; ++attempt)
            {
                if (_generation != generation || _exited) return;
                StatusChanged?.Invoke($"Connection lost · reconnecting {attempt + 1}/{attempts}");
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(4000, 200 << attempt)), cancellation);
                await transport.StopAsync(); cancellation.ThrowIfCancellationRequested();
                if (_exited || generation != _generation) return;
                // LastSequence means the control accepted the bytes, not that its dispatcher parsed them.
                _control.FlushPendingTransportOutput();
                long cursor = transport.LastSequence;
                try
                {
                    // No parser reset or input retry. Use the original connection identity, not editable view fields.
                    await transport.StartAsync(options with { AfterSequence = cursor }, cancellation);
                    cancellation.ThrowIfCancellationRequested(); _control.FlushPendingTransportOutput();
                    if (!_exited) StatusChanged?.Invoke(HasControl ? "Reconnected · controller" : "Reconnected · observer");
                    return;
                }
                catch (Exception error) when (!cancellation.IsCancellationRequested && Retryable(error) && attempt + 1 < attempts) { }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_detaching && _generation == generation && !_exited)
            {
                // A failed handshake can queue another UI ConnectionLost notification. Once exhausted
                // or denied, only a new explicit attachment may start a new retry budget.
                _initialReady = false;
                StatusChanged?.Invoke("Reconnect stopped · " + error.Message);
            }
        }
    }
    private void OnProcessExited(int code)
    {
        _exited = true; MultiplexingTerminalTransport? source = _transport;
        Dispatcher.UIThread.Post(() => { if (!_detaching && ReferenceEquals(source, _transport)) StatusChanged?.Invoke($"Process exited · code {code}"); });
    }
    private void OnInputFailed(Exception error)
    {
        MultiplexingTerminalTransport? source = _transport;
        Dispatcher.UIThread.Post(() => { if (!_detaching && ReferenceEquals(source, _transport)) StatusChanged?.Invoke("Input rejected · " + error.Message); });
    }
}
