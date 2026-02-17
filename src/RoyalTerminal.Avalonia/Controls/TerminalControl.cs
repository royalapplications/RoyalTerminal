// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Main terminal control.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Pipe;
using RoyalTerminal.Terminal.Transport.Pty;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Full-featured Avalonia terminal control with backend-neutral endpoint integration.
/// Features:
/// - SkiaSharp rendering via Avalonia CustomVisual composition
/// - Keyboard input with IME support
/// - Mouse input (clicks, selection, scrolling)
/// - Virtualized scrolling with large buffer support
/// - Focus management
/// - Content scaling (DPI awareness)
/// </summary>
public class TerminalControl : TemplatedControl, ILogicalScrollable
{
    #region Styled Properties

    /// <summary>The font family used for terminal text.</summary>
    public static readonly StyledProperty<string> FontFamilyNameProperty =
        AvaloniaProperty.Register<TerminalControl, string>(nameof(FontFamilyName), TerminalDefaults.DefaultMonoFont);

    /// <summary>The font size for terminal text.</summary>
    public static readonly StyledProperty<double> TerminalFontSizeProperty =
        AvaloniaProperty.Register<TerminalControl, double>(nameof(TerminalFontSize), 14.0);

    /// <summary>Number of columns in the terminal grid.</summary>
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<TerminalControl, int>(nameof(Columns), 80);

    /// <summary>Number of rows in the terminal viewport.</summary>
    public static readonly StyledProperty<int> RowsProperty =
        AvaloniaProperty.Register<TerminalControl, int>(nameof(Rows), 24);

    /// <summary>Maximum number of scrollback rows.</summary>
    public static readonly StyledProperty<int> ScrollbackLimitProperty =
        AvaloniaProperty.Register<TerminalControl, int>(nameof(ScrollbackLimit), 10_000);

    /// <summary>Default foreground color.</summary>
    public static readonly StyledProperty<Color> DefaultForegroundProperty =
        AvaloniaProperty.Register<TerminalControl, Color>(nameof(DefaultForeground),
            Color.FromRgb(0xD4, 0xD4, 0xD4));

    /// <summary>Default background color.</summary>
    public static readonly StyledProperty<Color> DefaultBackgroundProperty =
        AvaloniaProperty.Register<TerminalControl, Color>(nameof(DefaultBackground),
            Color.FromRgb(0x1E, 0x1E, 0x1E));

    /// <summary>Whether to auto-scroll to bottom on new output.</summary>
    public static readonly StyledProperty<bool> AutoScrollProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(AutoScroll), true);

    /// <summary>
    /// Preferred VT processor implementation.
    /// </summary>
    public static readonly DirectProperty<TerminalControl, VtProcessorPreference> VtProcessorPreferenceProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, VtProcessorPreference>(
            nameof(VtProcessorPreference),
            o => o.VtProcessorPreference,
            (o, v) => o.VtProcessorPreference = v);

    private VtProcessorPreference _vtProcessorPreference = VtProcessorPreference.Auto;

    public string FontFamilyName
    {
        get => GetValue(FontFamilyNameProperty);
        set => SetValue(FontFamilyNameProperty, value);
    }

    public double TerminalFontSize
    {
        get => GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public int ScrollbackLimit
    {
        get => GetValue(ScrollbackLimitProperty);
        set => SetValue(ScrollbackLimitProperty, value);
    }

    public Color DefaultForeground
    {
        get => GetValue(DefaultForegroundProperty);
        set => SetValue(DefaultForegroundProperty, value);
    }

    public Color DefaultBackground
    {
        get => GetValue(DefaultBackgroundProperty);
        set => SetValue(DefaultBackgroundProperty, value);
    }

    public bool AutoScroll
    {
        get => GetValue(AutoScrollProperty);
        set => SetValue(AutoScrollProperty, value);
    }

    /// <summary>
    /// Gets or sets the preferred VT processor implementation.
    /// </summary>
    public VtProcessorPreference VtProcessorPreference
    {
        get => _vtProcessorPreference;
        set => SetAndRaise(VtProcessorPreferenceProperty, ref _vtProcessorPreference, value);
    }

    #endregion

    #region Events

    /// <summary>Raised when terminal data is received from the PTY.</summary>
    public event EventHandler<TerminalDataEventArgs>? DataReceived;

    /// <summary>Raised when the terminal title changes.</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>Raised when the terminal bell rings.</summary>
    public event EventHandler? Bell;

    /// <summary>Raised when the terminal is resized (columns, rows).</summary>
    public event EventHandler<TerminalSizeEventArgs>? TerminalResized;

    #endregion

    private TerminalPresenter? _presenter;
    private SkiaTerminalRenderer? _renderer;
    private TerminalScreen? _screen;
    private TerminalScrollData? _scrollData;
    private VirtualizedTerminalScrollViewer? _scrollViewer;
    private IVtProcessor? _vtProcessor;
    private bool _isMouseSelecting;
    private bool _suppressGridPropertyApply;
    private int _lastAppliedColumns = -1;
    private int _lastAppliedRows = -1;
    private int _lastAppliedWidthPx = -1;
    private int _lastAppliedHeightPx = -1;
    private VtProcessorPreference _appliedVtProcessorPreference = VtProcessorPreference.Auto;
    private string? _activeTransportId;

    /// <summary>
    /// Gets the session service responsible for surface and PTY lifecycle.
    /// </summary>
    public ITerminalSessionService TerminalSessionService { get; }

    /// <summary>
    /// Gets the input adapter used for key/text/mouse protocol mapping.
    /// </summary>
    public ITerminalInputAdapter TerminalInputAdapter { get; }

    /// <summary>
    /// Gets the selection service used for copy/paste/selection operations.
    /// </summary>
    public ITerminalSelectionService TerminalSelectionService { get; }

    /// <summary>
    /// Gets the scroll service used for scroll coordination.
    /// </summary>
    public ITerminalScrollService TerminalScrollService { get; }

    /// <summary>
    /// Gets the factory used to create VT processor implementations.
    /// </summary>
    public IVtProcessorFactory VtProcessorFactory { get; }

    /// <summary>
    /// Gets the factory used to create PTY implementations.
    /// </summary>
    public IPtyFactory PtyFactory { get; }

    /// <summary>
    /// Gets the transport factory used for runtime session startup.
    /// </summary>
    public ITerminalTransportFactory TerminalTransportFactory { get; }

    /// <summary>
    /// Gets the SSH credential provider used by the default SSH transport provider.
    /// </summary>
    public ISshCredentialProvider SshCredentialProvider { get; }

    /// <summary>
    /// Gets the SSH host-key validator used by the default SSH transport provider.
    /// </summary>
    public ISshHostKeyValidator SshHostKeyValidator { get; }

    /// <summary>Gets the currently attached terminal endpoint, if any.</summary>
    public ITerminalEndpoint? Endpoint => TerminalSessionService.Endpoint;

    /// <summary>Gets whether a transport-backed session is active.</summary>
    public bool HasActiveSession => TerminalSessionService.HasActiveTransport;

    /// <summary>Gets the active transport id, if a session is active.</summary>
    public string? ActiveTransportId => HasActiveSession ? _activeTransportId : null;

    /// <summary>Gets the terminal screen model.</summary>
    public TerminalScreen? Screen => _screen;

    /// <summary>Gets the renderer.</summary>
    public SkiaTerminalRenderer? Renderer => _renderer;

    /// <summary>Gets the scroll data.</summary>
    public TerminalScrollData? ScrollData => _scrollData;

    #region ILogicalScrollable

    private bool _canHScroll;
    private bool _canVScroll = true;
    private EventHandler? _scrollInvalidated;

    /// <inheritdoc />
    bool ILogicalScrollable.IsLogicalScrollEnabled => true;

    /// <inheritdoc />
    Size ILogicalScrollable.ScrollSize =>
        new(10, _scrollData?.CellHeight ?? 16);

    /// <inheritdoc />
    Size ILogicalScrollable.PageScrollSize =>
        new(Bounds.Width, Bounds.Height);

    /// <inheritdoc />
    bool ILogicalScrollable.CanHorizontallyScroll
    {
        get => _canHScroll;
        set => _canHScroll = value;
    }

    /// <inheritdoc />
    bool ILogicalScrollable.CanVerticallyScroll
    {
        get => _canVScroll;
        set => _canVScroll = value;
    }

    /// <inheritdoc />
    Size IScrollable.Extent =>
        new(Bounds.Width, _scrollData?.Extent ?? Bounds.Height);

    /// <inheritdoc />
    Vector IScrollable.Offset
    {
        get => new(0, _scrollData?.Offset ?? 0);
        set
        {
            if (_scrollData is not null)
            {
                _scrollData.Offset = value.Y;
                if (_scrollViewer is not null)
                {
                    _screen!.ScrollOffset = _scrollData.OffsetRows;
                }
                _presenter?.Invalidate();
                RaiseScrollInvalidated();
            }
        }
    }

    /// <inheritdoc />
    Size IScrollable.Viewport =>
        new(Bounds.Width, _scrollData?.Viewport ?? Bounds.Height);

    event EventHandler? ILogicalScrollable.ScrollInvalidated
    {
        add => _scrollInvalidated += value;
        remove => _scrollInvalidated -= value;
    }

    /// <inheritdoc />
    bool ILogicalScrollable.BringIntoView(Control target, Rect targetRect) => false;

    /// <inheritdoc />
    Control? ILogicalScrollable.GetControlInDirection(NavigationDirection direction, Control? from) => null;

    /// <inheritdoc />
    void ILogicalScrollable.RaiseScrollInvalidated(EventArgs e) =>
        _scrollInvalidated?.Invoke(this, e);

    private void RaiseScrollInvalidated() =>
        _scrollInvalidated?.Invoke(this, EventArgs.Empty);

    #endregion

    static TerminalControl()
    {
        FocusableProperty.OverrideDefaultValue<TerminalControl>(true);
    }

    public TerminalControl()
        : this(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory(),
            new DefaultPtyFactory(),
            new NullSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            transportFactory: null)
    {
    }

    /// <summary>
    /// Initializes a terminal control with explicit service/factory dependencies.
    /// </summary>
    public TerminalControl(
        ITerminalSessionService terminalSessionService,
        ITerminalInputAdapter terminalInputAdapter,
        ITerminalSelectionService terminalSelectionService,
        ITerminalScrollService terminalScrollService,
        IVtProcessorFactory vtProcessorFactory,
        IPtyFactory ptyFactory)
        : this(
            terminalSessionService,
            terminalInputAdapter,
            terminalSelectionService,
            terminalScrollService,
            vtProcessorFactory,
            ptyFactory,
            new NullSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            transportFactory: null)
    {
    }

    /// <summary>
    /// Initializes a terminal control with explicit transport dependencies.
    /// </summary>
    public TerminalControl(
        ITerminalSessionService terminalSessionService,
        ITerminalInputAdapter terminalInputAdapter,
        ITerminalSelectionService terminalSelectionService,
        ITerminalScrollService terminalScrollService,
        IVtProcessorFactory vtProcessorFactory,
        IPtyFactory ptyFactory,
        ISshCredentialProvider sshCredentialProvider,
        ISshHostKeyValidator sshHostKeyValidator,
        ITerminalTransportFactory? transportFactory)
    {
        TerminalSessionService = terminalSessionService ?? throw new ArgumentNullException(nameof(terminalSessionService));
        TerminalInputAdapter = terminalInputAdapter ?? throw new ArgumentNullException(nameof(terminalInputAdapter));
        TerminalSelectionService = terminalSelectionService ?? throw new ArgumentNullException(nameof(terminalSelectionService));
        TerminalScrollService = terminalScrollService ?? throw new ArgumentNullException(nameof(terminalScrollService));
        VtProcessorFactory = vtProcessorFactory ?? throw new ArgumentNullException(nameof(vtProcessorFactory));
        PtyFactory = ptyFactory ?? throw new ArgumentNullException(nameof(ptyFactory));
        SshCredentialProvider = sshCredentialProvider ?? throw new ArgumentNullException(nameof(sshCredentialProvider));
        SshHostKeyValidator = sshHostKeyValidator ?? throw new ArgumentNullException(nameof(sshHostKeyValidator));
        TerminalTransportFactory = transportFactory
            ?? CreateDefaultTransportFactory(PtyFactory, SshCredentialProvider, SshHostKeyValidator);

        InitializeTerminal();
    }

    private void InitializeTerminal()
    {
        var fg = DefaultForeground;
        var bg = DefaultBackground;

        _screen = new TerminalScreen(
            Columns, Rows, ScrollbackLimit)
        {
            DefaultForeground = ColorToArgb(fg),
            DefaultBackground = ColorToArgb(bg),
        };

        _vtProcessor = VtProcessorFactory.Create(_screen, VtProcessorPreference);
        _appliedVtProcessorPreference = VtProcessorPreference;

        _renderer = CreateRenderer(previous: null);

        _scrollData = new TerminalScrollData
        {
            CellHeight = _renderer.CellHeight,
            Viewport = Rows * _renderer.CellHeight,
        };
        _scrollData.UpdateExtent(_screen.TotalRows, true);

        _scrollViewer = new VirtualizedTerminalScrollViewer(_screen, _scrollData);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FontFamilyNameProperty || change.Property == TerminalFontSizeProperty)
        {
            ApplyFontSettings();
            return;
        }

        if (change.Property == VtProcessorPreferenceProperty)
        {
            ApplyVtProcessorPreference();
            return;
        }

        if (change.Property == DefaultForegroundProperty || change.Property == DefaultBackgroundProperty)
        {
            ApplyColorDefaults();
            return;
        }

        if (change.Property == ColumnsProperty || change.Property == RowsProperty)
        {
            if (_suppressGridPropertyApply)
            {
                return;
            }

            ApplyGridFromProperties();
            return;
        }

        if (change.Property == AutoScrollProperty)
        {
            ApplyAutoScrollSetting();
        }
    }

    private void ApplyFontSettings()
    {
        if (_screen is null)
        {
            return;
        }

        SkiaTerminalRenderer nextRenderer = CreateRenderer(_renderer);
        _renderer = nextRenderer;

        if (_scrollData is not null)
        {
            _scrollData.CellHeight = nextRenderer.CellHeight;
            _scrollData.Viewport = Bounds.Height > 0
                ? Bounds.Height
                : Rows * nextRenderer.CellHeight;
            _scrollData.UpdateExtent(_screen.TotalRows, AutoScroll);
        }

        if (_scrollData is not null)
        {
            _scrollViewer?.UpdateViewport(_scrollData.Viewport, nextRenderer.CellHeight);
        }

        lock (_screen.SyncRoot)
        {
            _screen.InvalidateAll();
        }

        _presenter?.SetRenderState(nextRenderer, _screen);
        _presenter?.NotifyResize(Bounds.Size);
        _presenter?.Invalidate(fullRedraw: true);
        RaiseScrollInvalidated();
        InvalidateMeasure();
    }

    private SkiaTerminalRenderer CreateRenderer(SkiaTerminalRenderer? previous)
    {
        string family = string.IsNullOrWhiteSpace(FontFamilyName)
            ? TerminalDefaults.DefaultMonoFont
            : FontFamilyName;
        SkiaTerminalRenderer renderer = new(family, (float)TerminalFontSize);

        if (previous is null)
        {
            return renderer;
        }

        renderer.CursorColumn = previous.CursorColumn;
        renderer.CursorRow = previous.CursorRow;
        renderer.CursorVisible = previous.CursorVisible;
        renderer.CursorStyle = previous.CursorStyle;
        renderer.CursorColor = previous.CursorColor;
        renderer.SelectionColor = previous.SelectionColor;
        renderer.SelectionStart = previous.SelectionStart;
        renderer.SelectionEnd = previous.SelectionEnd;
        renderer.EnableTextRenderDiagnostics = previous.EnableTextRenderDiagnostics;
        renderer.EnableTextShaping = previous.EnableTextShaping;
        renderer.TextDirectionMode = previous.TextDirectionMode;
        renderer.EnableLigatures = previous.EnableLigatures;
        return renderer;
    }

    private void ApplyColorDefaults()
    {
        if (_screen is null)
        {
            return;
        }

        lock (_screen.SyncRoot)
        {
            _screen.DefaultForeground = ColorToArgb(DefaultForeground);
            _screen.DefaultBackground = ColorToArgb(DefaultBackground);
            _screen.InvalidateAll();
        }

        _presenter?.Invalidate(fullRedraw: true);
    }

    private void ApplyAutoScrollSetting()
    {
        if (!AutoScroll)
        {
            return;
        }

        ScrollToBottom();
        RaiseScrollInvalidated();
    }

    private void ApplyVtProcessorPreference()
    {
        if (_screen is null || TerminalSessionService.HasActiveTransport)
        {
            return;
        }

        EnsureVtProcessorPreferenceApplied();
    }

    private void EnsureVtProcessorPreferenceApplied()
    {
        if (_screen is null)
        {
            return;
        }

        if (_vtProcessor is not null && _appliedVtProcessorPreference == VtProcessorPreference)
        {
            return;
        }

        IVtProcessor nextProcessor = VtProcessorFactory.Create(_screen, VtProcessorPreference);
        IVtProcessor? previousProcessor = _vtProcessor;
        _vtProcessor = nextProcessor;
        _appliedVtProcessorPreference = VtProcessorPreference;
        previousProcessor?.Dispose();
    }

    private void ApplyGridFromLayout(int columns, int rows, Size finalSize)
    {
        _suppressGridPropertyApply = true;
        try
        {
            if (Columns != columns)
            {
                SetCurrentValue(ColumnsProperty, columns);
            }

            if (Rows != rows)
            {
                SetCurrentValue(RowsProperty, rows);
            }
        }
        finally
        {
            _suppressGridPropertyApply = false;
        }

        ApplyTerminalSize(columns, rows, finalSize.Width, finalSize.Height, raiseTerminalResized: true, invalidateMeasure: true, force: true);
    }

    private void ApplyGridFromProperties()
    {
        ApplyTerminalSize(Columns, Rows, Bounds.Width, Bounds.Height, raiseTerminalResized: true, invalidateMeasure: true);
    }

    private void ApplyTerminalSize(
        int columns,
        int rows,
        double width,
        double height,
        bool raiseTerminalResized,
        bool invalidateMeasure,
        bool force = false)
    {
        if (_renderer is null)
        {
            return;
        }

        int safeColumns = Math.Max(1, columns);
        int safeRows = Math.Max(1, rows);

        if (safeColumns != columns || safeRows != rows)
        {
            _suppressGridPropertyApply = true;
            try
            {
                if (safeColumns != Columns)
                {
                    SetCurrentValue(ColumnsProperty, safeColumns);
                }

                if (safeRows != Rows)
                {
                    SetCurrentValue(RowsProperty, safeRows);
                }
            }
            finally
            {
                _suppressGridPropertyApply = false;
            }
        }

        int widthPx = width > 0
            ? Math.Max(1, (int)Math.Round(width))
            : Math.Max(1, (int)Math.Ceiling(safeColumns * _renderer.CellWidth));
        int heightPx = height > 0
            ? Math.Max(1, (int)Math.Round(height))
            : Math.Max(1, (int)Math.Ceiling(safeRows * _renderer.CellHeight));

        if (!force &&
            safeColumns == _lastAppliedColumns &&
            safeRows == _lastAppliedRows &&
            widthPx == _lastAppliedWidthPx &&
            heightPx == _lastAppliedHeightPx)
        {
            return;
        }

        if (_screen is not null)
        {
            lock (_screen.SyncRoot)
            {
                _screen.Resize(safeColumns, safeRows);
                _vtProcessor?.NotifyResize(safeColumns, safeRows, widthPx, heightPx);
            }
        }

        _scrollData?.UpdateExtent(_screen?.TotalRows ?? 0, AutoScroll);

        if (_scrollData is not null)
        {
            _scrollData.Viewport = height > 0
                ? height
                : safeRows * _renderer.CellHeight;
            _scrollViewer?.UpdateViewport(_scrollData.Viewport, _renderer.CellHeight);
        }

        Endpoint?.SetSize(widthPx, heightPx);
        TerminalSessionService.ResizeSession(safeColumns, safeRows, widthPx, heightPx);

        bool gridChanged = safeColumns != _lastAppliedColumns || safeRows != _lastAppliedRows;
        _lastAppliedColumns = safeColumns;
        _lastAppliedRows = safeRows;
        _lastAppliedWidthPx = widthPx;
        _lastAppliedHeightPx = heightPx;

        RaiseScrollInvalidated();
        if (raiseTerminalResized && gridChanged)
        {
            TerminalResized?.Invoke(this, new TerminalSizeEventArgs(safeColumns, safeRows));
        }

        _presenter?.NotifyResize(new Size(widthPx, heightPx));
        _presenter?.Invalidate();

        if (invalidateMeasure)
        {
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Gets whether a native VT processor is active instead of the managed fallback.
    /// </summary>
    public bool IsUsingNativeVtProcessor => _vtProcessor is not null && _vtProcessor is not BasicVtProcessor;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // Look for the presenter in the template, or create one
        _presenter = e.NameScope.Find<TerminalPresenter>("PART_Presenter");
        EnsurePresenter();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // TemplatedControl without a template never fires OnApplyTemplate.
        // Create the presenter here as a fallback so rendering always works.
        EnsurePresenter();
    }

    private void EnsurePresenter()
    {
        if (_presenter is not null) return;

        _presenter = new TerminalPresenter();
        ((ISetLogicalParent)_presenter).SetParent(this);
        VisualChildren.Add(_presenter);

        if (_renderer is not null && _screen is not null)
            _presenter.SetRenderState(_renderer, _screen);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_renderer is null)
            return base.MeasureOverride(availableSize);

        // Calculate desired size based on columns/rows
        var desiredWidth = Columns * _renderer.CellWidth;
        var desiredHeight = Rows * _renderer.CellHeight;

        // A terminal should fill all available space so that ArrangeOverride
        // receives the full container size and can recalculate cols/rows.
        // Only fall back to the cell-based desired size when the available
        // dimension is infinite (e.g. unconstrained axis in a StackPanel).
        return new Size(
            double.IsInfinity(availableSize.Width) ? desiredWidth : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? desiredHeight : availableSize.Height
        );
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_presenter is not null)
        {
            _presenter.Arrange(new Rect(finalSize));
        }

        // Recalculate grid dimensions based on actual size
        if (_renderer is not null && _renderer.CellWidth > 0 && _renderer.CellHeight > 0)
        {
            var newCols = Math.Max(1, (int)(finalSize.Width / _renderer.CellWidth));
            var newRows = Math.Max(1, (int)(finalSize.Height / _renderer.CellHeight));

            if (newCols != Columns || newRows != Rows)
            {
                ApplyGridFromLayout(newCols, newRows, finalSize);
            }
            else
            {
                ApplyTerminalSize(newCols, newRows, finalSize.Width, finalSize.Height, raiseTerminalResized: false, invalidateMeasure: false);
            }
        }

        return finalSize;
    }

    #region Endpoint Integration

    /// <summary>
    /// Connects this control to a terminal endpoint for terminal I/O.
    /// </summary>
    public void AttachEndpoint(ITerminalEndpoint endpoint)
    {
        TerminalSessionService.AttachEndpoint(endpoint);

        if (_renderer is not null)
        {
            int widthPx = Bounds.Width > 0
                ? Math.Max(1, (int)Math.Round(Bounds.Width))
                : Math.Max(1, (int)Math.Ceiling(Columns * _renderer.CellWidth));
            int heightPx = Bounds.Height > 0
                ? Math.Max(1, (int)Math.Round(Bounds.Height))
                : Math.Max(1, (int)Math.Ceiling(Rows * _renderer.CellHeight));
            endpoint.SetSize(widthPx, heightPx);
        }

        endpoint.SetFocus(IsFocused);
    }

    /// <summary>
    /// Disconnects the current terminal endpoint.
    /// </summary>
    public void DetachEndpoint()
    {
        TerminalSessionService.DetachEndpoint();
    }

    /// <summary>
    /// Writes data to the terminal screen model.
    /// Called when output is received from the PTY/terminal endpoint.
    /// </summary>
    public void WriteOutput(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        WriteOutput((ReadOnlyMemory<byte>)data);
    }

    /// <summary>
    /// Writes data to the terminal screen model.
    /// Called when output is received from the PTY/terminal endpoint.
    /// </summary>
    public void WriteOutput(ReadOnlyMemory<byte> data)
    {
        WriteOutputCore(data.Span);

        // Raise event without copying when input is already managed memory.
        DataReceived?.Invoke(this, new TerminalDataEventArgs(data));

        TerminalScrollService.HandleOutput(_scrollData, AutoScroll, _presenter, RaiseScrollInvalidated);
    }

    /// <summary>
    /// Writes data to the terminal screen model.
    /// Called when output is received from the PTY/terminal endpoint.
    /// </summary>
    public void WriteOutput(ReadOnlySpan<byte> data)
    {
        WriteOutputCore(data);

        // Span input may come from transient memory, so copy to a managed payload for event consumers.
        DataReceived?.Invoke(this, new TerminalDataEventArgs(data.ToArray()));

        TerminalScrollService.HandleOutput(_scrollData, AutoScroll, _presenter, RaiseScrollInvalidated);
    }

    private void WriteOutputCore(ReadOnlySpan<byte> data)
    {
        if (_screen is null)
        {
            return;
        }

        // Lock screen during VT processing — composition thread reads cells concurrently
        lock (_screen.SyncRoot)
        {
            _vtProcessor?.Process(data);

            // Update cursor position on the renderer
            if (_vtProcessor is not null && _renderer is not null)
            {
                _renderer.CursorColumn = _vtProcessor.CursorCol;
                _renderer.CursorRow = _vtProcessor.CursorRow;
                _renderer.CursorVisible = _vtProcessor.CursorVisible;
            }
        }
    }

    /// <summary>
    /// Sends input text to the active terminal endpoint or PTY.
    /// </summary>
    public void SendInput(string text)
    {
        TerminalSessionService.SendInput(text);
    }

    /// <summary>
    /// Sends input bytes to the active terminal endpoint or PTY.
    /// </summary>
    public void SendInput(ReadOnlySpan<byte> data)
    {
        TerminalSessionService.SendInput(data);
    }

    #endregion

    #region Keyboard Input

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (TerminalInputAdapter.HandleKeyDown(e, TerminalSessionService, _vtProcessor))
        {
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (TerminalInputAdapter.HandleKeyUp(e, TerminalSessionService))
        {
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (TerminalInputAdapter.HandleTextInput(e, TerminalSessionService))
        {
            e.Handled = true;
        }
    }

    #endregion

    #region Mouse Input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        TerminalMouseButton button = ConvertPressedMouseButton(props);
        if (button != TerminalMouseButton.None)
        {
            SendPointerEvent(new TerminalPointerEvent(
                Kind: TerminalPointerEventKind.Button,
                X: point.X,
                Y: point.Y,
                Button: button,
                Action: TerminalInputAction.Press,
                Modifiers: ConvertTerminalModifiers(e.KeyModifiers)));
        }

        // Start text selection on left click
        if (props.IsLeftButtonPressed && _renderer is not null)
        {
            _isMouseSelecting = true;

            var col = (int)(point.X / _renderer.CellWidth);
            var row = (int)(point.Y / _renderer.CellHeight);
            _renderer.SelectionStart = (col, row);
            _renderer.SelectionEnd = (col, row);
            _screen?.InvalidateAll();
            _presenter?.Invalidate();
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetPosition(this);
        SendPointerEvent(new TerminalPointerEvent(
            Kind: TerminalPointerEventKind.Move,
            X: point.X,
            Y: point.Y,
            Button: TerminalMouseButton.None,
            Action: TerminalInputAction.Press,
            Modifiers: ConvertTerminalModifiers(e.KeyModifiers)));

        if (_isMouseSelecting && _renderer is not null)
        {
            var col = (int)(point.X / _renderer.CellWidth);
            var row = (int)(point.Y / _renderer.CellHeight);
            _renderer.SelectionEnd = (col, row);
            _screen?.InvalidateAll();
            _presenter?.Invalidate();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var point = e.GetPosition(this);
        SendPointerEvent(new TerminalPointerEvent(
            Kind: TerminalPointerEventKind.Button,
            X: point.X,
            Y: point.Y,
            Button: ConvertMouseButton(e.InitialPressMouseButton),
            Action: TerminalInputAction.Release,
            Modifiers: ConvertTerminalModifiers(e.KeyModifiers)));

        _isMouseSelecting = false;
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        TerminalScrollService.HandlePointerWheel(
            e,
            _scrollViewer,
            TerminalSessionService,
            _presenter,
            RaiseScrollInvalidated);
        e.Handled = true;
    }

    #endregion

    #region Focus

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        Endpoint?.SetFocus(true);
        _renderer?.SetCursorVisible(true);
        _presenter?.Invalidate();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        Endpoint?.SetFocus(false);
        _renderer?.SetCursorVisible(false);
        _presenter?.Invalidate();
    }

    #endregion

    #region Text Selection

    /// <summary>
    /// Copies the current selection to the clipboard.
    /// </summary>
    public async Task CopySelectionAsync()
    {
        await TerminalSelectionService.CopySelectionAsync(this, TerminalSessionService, _screen, _renderer);
    }

    /// <summary>
    /// Pastes text from the clipboard into the terminal.
    /// </summary>
    public async Task PasteAsync()
    {
        await TerminalSelectionService.PasteAsync(this, SendInput);
    }

    /// <summary>
    /// Clears the current text selection.
    /// </summary>
    public void ClearSelection()
    {
        TerminalSelectionService.ClearSelection(_screen, _renderer, _presenter);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Forces a full re-render of the terminal.
    /// </summary>
    public void InvalidateTerminal()
    {
        _screen?.InvalidateAll();
        _presenter?.Invalidate(fullRedraw: true);
    }

    /// <summary>
    /// Scrolls the terminal by the given number of rows.
    /// </summary>
    public void ScrollByRows(int rows)
    {
        TerminalScrollService.ScrollByRows(rows, _scrollData, _screen, _presenter);
    }

    /// <summary>
    /// Scrolls to the bottom of the terminal output.
    /// </summary>
    public void ScrollToBottom()
    {
        TerminalScrollService.ScrollToBottom(_scrollData, _screen, _presenter);
    }

    /// <summary>
    /// Updates the content scale for DPI changes.
    /// </summary>
    public void SetContentScale(double scaleX, double scaleY)
    {
        if (Endpoint is ITerminalScaleSink scaleSink)
        {
            scaleSink.SetContentScale(scaleX, scaleY);
        }
    }

    #endregion

    #region Standalone PTY Mode

    /// <summary>
    /// Starts a shell process with a standalone PTY-backed session.
    /// On macOS/Linux uses POSIX forkpty(); on Windows uses ConPTY.
    /// </summary>
    /// <param name="shell">Shell path, or null for auto-detect.</param>
    /// <param name="workingDirectory">Working directory, or null for home.</param>
    public void StartPty(string? shell = null, string? workingDirectory = null)
    {
        TerminalCommandSpec? command = string.IsNullOrWhiteSpace(shell)
            ? null
            : new TerminalCommandSpec(shell, Array.Empty<string>());
        int widthPx = Math.Max(1, (int)Math.Round(Bounds.Width > 0 ? Bounds.Width : Columns * (_renderer?.CellWidth ?? 1)));
        int heightPx = Math.Max(1, (int)Math.Round(Bounds.Height > 0 ? Bounds.Height : Rows * (_renderer?.CellHeight ?? 1)));

        PtyTransportOptions options = new(
            Command: command,
            WorkingDirectory: workingDirectory,
            Environment: null,
            Dimensions: new TerminalSessionDimensions(Columns, Rows, widthPx, heightPx));

        StartSessionAsync(options).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Starts a terminal session using a transport-specific options payload.
    /// </summary>
    public async ValueTask StartSessionAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        EnsureVtProcessorPreferenceApplied();

        await TerminalSessionService.StartSessionAsync(
                TerminalTransportFactory,
                options,
                _vtProcessor,
                OnPtyDataReceived,
                OnPtyProcessExited,
                OnVtProcessorResponse,
                OnVtProcessorBell,
                OnVtProcessorTitleChanged,
                cancellationToken)
            .ConfigureAwait(false);

        _activeTransportId = options.TransportId;
    }

    /// <summary>
    /// Starts a pipe transport-backed terminal session.
    /// </summary>
    public ValueTask StartPipeAsync(PipeTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Starts an SSH transport-backed terminal session.
    /// </summary>
    public ValueTask StartSshAsync(SshTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Stops the PTY and kills the child shell process.
    /// </summary>
    public void StopPty()
    {
        TerminalSessionService.StopSessionAsync(_vtProcessor, OnPtyDataReceived, OnPtyProcessExited)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        _activeTransportId = null;
    }

    /// <summary>Whether a standalone PTY is active.</summary>
    public bool HasPty => TerminalSessionService.HasPty;

    /// <summary>Gets the managed PTY, if started.</summary>
    public IPty? Pty => TerminalSessionService.Pty;

    /// <summary>
    /// Handles terminal query responses (DSR, DA, etc.) by writing them back to the active transport.
    /// Called from the VT processor when it detects a query sequence in the output stream.
    /// </summary>
    private void OnVtProcessorResponse(byte[] data)
    {
        TerminalSessionService.SendInput(data);
    }

    private void OnVtProcessorBell()
    {
        Dispatcher.UIThread.Post(() => Bell?.Invoke(this, EventArgs.Empty));
    }

    private void OnVtProcessorTitleChanged(string title)
    {
        Dispatcher.UIThread.Post(() => TitleChanged?.Invoke(this, title));
    }

    private void OnPtyDataReceived(byte[] data, int length)
    {
        // The PTY reader reuses its read buffer, so we must copy before posting
        // to the UI thread — otherwise the buffer may be overwritten by the next read.
        var copy = data.AsSpan(0, length).ToArray();
        Dispatcher.UIThread.Post(() =>
        {
            WriteOutput(copy);
        });
    }

    private void OnPtyProcessExited(int exitCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _activeTransportId = null;
            // Write exit message to screen
            var msg = $"\r\n[Process exited with code {exitCode}]\r\n";
            var bytes = Encoding.UTF8.GetBytes(msg);
            WriteOutput(bytes);
        });
    }

    #endregion

    #region Helpers

    private static ITerminalTransportFactory CreateDefaultTransportFactory(
        IPtyFactory ptyFactory,
        ISshCredentialProvider sshCredentialProvider,
        ISshHostKeyValidator sshHostKeyValidator)
    {
        return new CompositeTerminalTransportFactory(
            new ITerminalTransportProvider[]
            {
                new PtyTerminalTransportProvider(ptyFactory),
                new PipeTerminalTransportProvider(),
                new SshNetTerminalTransportProvider(sshCredentialProvider, sshHostKeyValidator),
            });
    }

    private void SendPointerEvent(TerminalPointerEvent pointerEvent)
    {
        TerminalSessionService.InputSink?.SendPointer(pointerEvent);
    }

    private static TerminalMouseButton ConvertPressedMouseButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed) return TerminalMouseButton.Left;
        if (properties.IsMiddleButtonPressed) return TerminalMouseButton.Middle;
        if (properties.IsRightButtonPressed) return TerminalMouseButton.Right;
        return TerminalMouseButton.None;
    }

    private static TerminalMouseButton ConvertMouseButton(MouseButton button) =>
        button switch
        {
            MouseButton.Left => TerminalMouseButton.Left,
            MouseButton.Middle => TerminalMouseButton.Middle,
            MouseButton.Right => TerminalMouseButton.Right,
            _ => TerminalMouseButton.None,
        };

    private static TerminalModifiers ConvertTerminalModifiers(KeyModifiers keyModifiers)
    {
        TerminalModifiers mods = TerminalModifiers.None;
        if (keyModifiers.HasFlag(KeyModifiers.Shift)) mods |= TerminalModifiers.Shift;
        if (keyModifiers.HasFlag(KeyModifiers.Control)) mods |= TerminalModifiers.Control;
        if (keyModifiers.HasFlag(KeyModifiers.Alt)) mods |= TerminalModifiers.Alt;
        if (keyModifiers.HasFlag(KeyModifiers.Meta)) mods |= TerminalModifiers.Meta;
        return mods;
    }

    private static uint ColorToArgb(Color c) =>
        ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    #endregion
}

/// <summary>
/// Internal extension methods for the renderer.
/// </summary>
internal static class RendererExtensions
{
    internal static void SetCursorVisible(this SkiaTerminalRenderer renderer, bool visible)
    {
        renderer.CursorVisible = visible;
    }
}
