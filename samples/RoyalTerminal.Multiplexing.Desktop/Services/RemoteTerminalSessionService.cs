using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.Services;

/// <summary>
/// Sample-specific policy boundary: the server owns terminal replies and geometry;
/// only explicit user input from the controlling attachment is forwarded.
/// </summary>
internal sealed class RemoteTerminalSessionService : ITerminalSessionService
{
    private readonly TerminalSessionService _inner = new();
    public event Action<Exception>? InputFailed;
    public IVtProcessor? Processor { get; private set; }
    public event EventHandler<TerminalSessionInputEventArgs>? InputSent
    { add => _inner.InputSent += value; remove => _inner.InputSent -= value; }
    public ITerminalEndpoint? Endpoint => _inner.Endpoint;
    public ITerminalInputSink? InputSink => _inner.InputSink;
    public ITerminalSelectionSource? SelectionSource => _inner.SelectionSource;
    public ITerminalModeSource? ModeSource => _inner.ModeSource;
    public ITerminalTransport? Transport => _inner.Transport;
    public bool HasActiveTransport => _inner.HasActiveTransport;
    public IPty? Pty => _inner.Pty;
    public bool HasPty => _inner.HasPty;
    public void AttachEndpoint(ITerminalEndpoint endpoint) => _inner.AttachEndpoint(endpoint);
    public void DetachEndpoint() => _inner.DetachEndpoint();

    public void SendInput(string text) => SendInput(Encoding.UTF8.GetBytes(text));
    public void SendInput(ReadOnlySpan<byte> data)
    {
        if (Transport is not MultiplexingTerminalTransport { HasControl: true }) { return; }
        try { _inner.SendInput(data); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        { InputFailed?.Invoke(exception); }
    }

    public void StartPty(IPtyFactory ptyFactory, string? shell, int columns, int rows, string? workingDirectory,
        IVtProcessor? vtProcessor, Action<byte[], int> onPtyDataReceived, Action<int> onPtyProcessExited,
        Action<byte[]> onVtResponse, Action onVtBell, Action<string> onVtTitleChanged, IReadOnlyList<string>? arguments = null)
        => throw new NotSupportedException("This sample attaches only to server-owned blocks.");

    public async ValueTask StartSessionAsync(ITerminalTransportFactory transportFactory, ITerminalTransportOptions transportOptions,
        IVtProcessor? vtProcessor, Action<byte[], int> onTransportDataReceived, Action<int> onTransportProcessExited,
        Action<byte[]> onVtResponse, Action onVtBell, Action<string> onVtTitleChanged, CancellationToken cancellationToken = default)
    {
        Processor = vtProcessor;
        // A replayed DSR/DA query must never produce another PTY response from every viewer.
        // The server inspection processor is the sole response owner, including after replay.
        try
        {
            await _inner.StartSessionAsync(transportFactory, transportOptions, vtProcessor,
                onTransportDataReceived, onTransportProcessExited, static _ => { }, onVtBell,
                onVtTitleChanged, cancellationToken).ConfigureAwait(false);
        }
        catch { Processor = null; throw; }
    }

    public void StopPty(IVtProcessor? vtProcessor, Action<byte[], int> onPtyDataReceived, Action<int> onPtyProcessExited)
        => _inner.StopPty(vtProcessor, onPtyDataReceived, onPtyProcessExited);
    public async ValueTask StopSessionAsync(IVtProcessor? vtProcessor, Action<byte[], int> onTransportDataReceived, Action<int> onTransportProcessExited)
    {
        try { await _inner.StopSessionAsync(vtProcessor, onTransportDataReceived, onTransportProcessExited).ConfigureAwait(false); }
        finally { Processor = null; }
    }

    // Control layout and local DPI never negotiate the shared remote PTY size.
    // The user-facing resize command sends one explicit request and awaits its stream echo.
    public void ResizePty(int columns, int rows, int widthPixels, int heightPixels) { }
    public void ResizeSession(int columns, int rows, int widthPixels, int heightPixels) { }
}
