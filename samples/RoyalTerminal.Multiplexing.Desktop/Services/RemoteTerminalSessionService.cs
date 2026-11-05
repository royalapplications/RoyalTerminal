using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.Services;

/// <summary>Remote policy: only explicit user input is forwarded; the server owns replies and geometry.</summary>
internal sealed class RemoteTerminalSessionService : ITerminalSessionService
{
    private readonly TerminalSessionService _inner = new();
    internal Func<MultiplexingTransportOptions,CancellationToken,ValueTask<NativeCheckpointStart>>? Recover { get; set; }
    internal event Action<int>? RecoveredExit;
    internal bool StartFromCheckpoint { get; set; }
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
        if (Transport is not MultiplexingTerminalTransport { HasControl: true }) return;
        try { _inner.SendInput(data); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { InputFailed?.Invoke(ex); }
    }
    public void StartPty(IPtyFactory ptyFactory, string? shell, int columns, int rows, string? workingDirectory,
        IVtProcessor? vtProcessor, Action<byte[], int> onPtyDataReceived, Action<int> onPtyProcessExited,
        Action<byte[]> onVtResponse, Action onVtBell, Action<string> onVtTitleChanged, IReadOnlyList<string>? arguments = null)
        => throw new NotSupportedException("Only server-owned blocks are supported.");

    public async ValueTask StartSessionAsync(ITerminalTransportFactory transportFactory, ITerminalTransportOptions transportOptions,
        IVtProcessor? vtProcessor, Action<byte[], int> onTransportDataReceived, Action<int> onTransportProcessExited,
        Action<byte[]> onVtResponse, Action onVtBell, Action<string> onVtTitleChanged, CancellationToken cancellationToken = default)
    {
        var options = (MultiplexingTransportOptions)transportOptions;
        var recover = Recover;
        bool needsCut = StartFromCheckpoint;
        StartFromCheckpoint = false;
        Processor = vtProcessor;
        try
        {
            for (int cuts = 0;;)
            {
                int? exit = null;
                if (needsCut)
                {
                    var cut = await (recover ?? throw new InvalidOperationException("Checkpoint recovery is not configured."))(options, cancellationToken);
                    Processor = cut.Processor; options = options with { AfterSequence = cut.Sequence }; exit = cut.ExitCode;
                    cuts++;
                }
                try
                {
                    await _inner.StartSessionAsync(transportFactory, options, Processor, onTransportDataReceived, onTransportProcessExited,
                        static _ => { }, onVtBell, onVtTitleChanged, cancellationToken);
                    if (exit is int code) { onTransportProcessExited(code); RecoveredExit?.Invoke(code); }
                    return;
                }
                catch (MultiplexingProtocolException ex) when (ex.Code == "HistoryGap" && recover is not null && cuts < 3)
                { cancellationToken.ThrowIfCancellationRequested(); needsCut = true; }
            }
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
    public void ResizePty(int columns, int rows, int widthPixels, int heightPixels) { }
    public void ResizeSession(int columns, int rows, int widthPixels, int heightPixels) { }
}
