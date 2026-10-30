using Avalonia.Threading;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.Services;

internal sealed class WorkspaceTerminalController : IWorkspaceTerminal, IAsyncDisposable
{
    private readonly RemoteTerminalControl _control;
    private MultiplexingTerminalTransport? _transport;
    private volatile bool _detaching;
    private bool _disposed;

    public WorkspaceTerminalController(RemoteTerminalControl control)
    {
        _control = control;
        _control.RemoteFactory.Created += OnTransportCreated;
        _control.RemoteSession.InputFailed += OnInputFailed;
    }

    public event Action<string>? StatusChanged;
    public bool IsAttached => _transport?.IsRunning == true;
    public bool HasControl => _transport?.HasControl == true;

    public async ValueTask AttachAsync(MultiplexingTransportOptions options, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        await DetachAsync();
        _detaching = false;
        try
        {
            await _control.StartSessionAsync(options with { AfterSequence = 0 }, preserveScrollback: false, cancellationToken);
            _control.FlushPendingTransportOutput();
            _control.Focus();
            StatusChanged?.Invoke(HasControl ? "Attached · controller" : "Attached · observer");
        }
        catch
        {
            await DetachAsync();
            throw;
        }
    }

    public async ValueTask DetachAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        _detaching = true;
        MultiplexingTerminalTransport? transport = _transport;
        if (transport is not null)
        {
            // Await network teardown before the control's synchronous cleanup. This
            // leaves the dispatcher free to finish an in-flight ordered geometry callback.
            await transport.StopAsync();
            transport.AuthoritativeDimensionsChanged -= OnDimensions;
            transport.ConnectionLost -= OnConnectionLost;
            transport.ProcessExited -= OnProcessExited;
            _transport = null;
        }
        _control.StopPty();
        StatusChanged?.Invoke("Detached · server processes remain running");
    }

    public void RequestDimensions(TerminalSessionDimensions dimensions)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_transport is not { HasControl: true }) { throw new InvalidOperationException("Attach as controller before changing the shared grid."); }
        _transport.Resize(dimensions);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        _disposed = true;
        await DetachAsync();
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
        void Apply() { if (!_detaching) { _control.ApplyRemoteDimensions(dimensions); } }
        if (Dispatcher.UIThread.CheckAccess()) { Apply(); }
        else { Dispatcher.UIThread.InvokeAsync(Apply).GetAwaiter().GetResult(); }
    }

    private void OnConnectionLost(Exception? error)
    {
        MultiplexingTerminalTransport? source = _transport;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_detaching && ReferenceEquals(source, _transport))
            { StatusChanged?.Invoke(error is null ? "Disconnected · reattach to replay" : "Disconnected · " + error.Message); }
        });
    }
    private void OnProcessExited(int code)
    {
        MultiplexingTerminalTransport? source = _transport;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_detaching && ReferenceEquals(source, _transport))
            { StatusChanged?.Invoke($"Process exited · code {code}"); }
        });
    }
    private void OnInputFailed(Exception error)
    {
        MultiplexingTerminalTransport? source = _transport;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_detaching && ReferenceEquals(source, _transport))
            { StatusChanged?.Invoke("Input rejected · " + error.Message); }
        });
    }
}
