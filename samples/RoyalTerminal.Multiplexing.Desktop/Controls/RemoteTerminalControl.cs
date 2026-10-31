using Avalonia;
using Avalonia.Layout;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Multiplexing.Desktop.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;

namespace RoyalTerminal.Multiplexing.Desktop.Controls;

/// <summary>A terminal whose parser grid is authoritative on the remote server, independent of local window size.</summary>
public sealed class RemoteTerminalControl : TerminalControl
{
    private const int MaximumGridCells = 1024 * 1024;
    private TerminalSessionDimensions _remoteDimensions = new(80, 24, 0, 0);
    internal RemoteTerminalSessionService RemoteSession { get; }
    internal RemoteTransportFactory RemoteFactory { get; }

    /// <summary>Creates a managed-VT remote terminal surface using the system WebSocket implementation.</summary>
    public RemoteTerminalControl() : this(new MultiplexingSocketConnector()) { }

    internal RemoteTerminalControl(IMultiplexingSocketConnector connector)
        : this(new RemoteTerminalSessionService(), new RemoteTransportFactory(connector)) { }

    private RemoteTerminalControl(RemoteTerminalSessionService session, RemoteTransportFactory factory)
        : base(session, new DefaultTerminalInputAdapter(), new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(), new DefaultVtProcessorFactory(), new DefaultPtyFactory(),
            new NullSshCredentialProvider(), new RejectAllSshHostKeyValidator(), factory)
    {
        RemoteSession = session;
        RemoteFactory = factory;
        VtProcessorPreference = VtProcessorPreference.Managed;
        PreserveScrollbackOnSessionStart = false;
        Padding = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
    }

    internal void ApplyRemoteDimensions(TerminalSessionDimensions dimensions)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (dimensions.Columns is < 1 or > 4096 || dimensions.Rows is < 1 or > 4096
            || (long)dimensions.Columns * dimensions.Rows > MaximumGridCells)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "The remote grid exceeds this client's bounded viewport capacity.");
        }
        // Output delivery is queued by TerminalControl. Drain the earlier prefix before
        // changing parser geometry; later stream events cannot overtake this callback.
        FlushPendingTransportOutput();
        ScrollbackLimit = Math.Min(10_000, Math.Max(0, MaximumGridCells / dimensions.Columns - dimensions.Rows));
        _remoteDimensions = dimensions;
        InvalidateMeasure();
        InvalidateArrange();
        Size size = GetRemoteSize();
        Measure(size);
        Arrange(new Rect(Bounds.Position, size));
        // Base layout uses local glyph pixel metrics. Protocol reports instead use the
        // shared server pixel dimensions, without sending a resize back to the host.
        if (Screen is { } screen)
        {
            lock (screen.SyncRoot)
            {
                RemoteSession.Processor?.NotifyResize(dimensions.Columns, dimensions.Rows,
                    dimensions.WidthPixels, dimensions.HeightPixels);
            }
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => base.MeasureOverride(GetRemoteSize());

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Base ApplyGridFromLayout sets columns and rows as one operation. Assigning
        // the public properties individually would transiently reflow at mixed sizes.
        _ = base.ArrangeOverride(GetRemoteSize());
        return finalSize;
    }

    private Size GetRemoteSize()
    {
        double cellWidth = Renderer?.CellWidth ?? 8;
        double cellHeight = Renderer?.CellHeight ?? 16;
        return new Size(_remoteDimensions.Columns * cellWidth + 0.01,
            _remoteDimensions.Rows * cellHeight + 0.01);
    }
}
