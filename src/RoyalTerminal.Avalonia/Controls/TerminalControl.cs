// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Main terminal control.

using System.Buffers;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
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
    private const float RendererBackgroundOpacity = 0.82f;
    private const bool RendererBackgroundOpacityCells = true;
    private static readonly TimeSpan CursorBlinkInterval = TimeSpan.FromMilliseconds(530);
    private const int MaxPendingOutputChunksPerDispatch = 64;

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

    /// <summary>
    /// Active immutable terminal theme snapshot.
    /// </summary>
    public static new readonly DirectProperty<TerminalControl, TerminalTheme?> ThemeProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, TerminalTheme?>(
            nameof(Theme),
            o => o.Theme,
            (o, v) => o.Theme = v);

    private TerminalTheme? _theme;

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

    /// <summary>
    /// Gets or sets the active terminal theme.
    /// </summary>
    public new TerminalTheme? Theme
    {
        get => _theme;
        set => SetAndRaise(ThemeProperty, ref _theme, value);
    }

    #endregion

    #region Events

    /// <summary>Raised when terminal data is received from the PTY.</summary>
    public event EventHandler<TerminalDataEventArgs>? DataReceived;

    /// <summary>Raised when the terminal title changes.</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>Raised when the terminal bell rings.</summary>
    public event EventHandler? Bell;

    /// <summary>Raised when the terminal process exits.</summary>
    public event EventHandler<int>? ProcessExited;

    /// <summary>Raised when the host/application should close the terminal surface.</summary>
    public event EventHandler? CloseRequested;

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
    private bool _leftPointerDown;
    private bool _middlePointerDown;
    private bool _rightPointerDown;
    private bool _suppressGridPropertyApply;
    private bool _suppressLegacyColorThemeBridge;
    private int _lastPointerColumn = -1;
    private int _lastPointerRow = -1;
    private bool _backgroundOpacityEnabled;
    private string? _hoveredLinkUrl;
    private string? _searchNeedle;
    private int _searchTotal;
    private int _searchSelected = -1;
    private readonly List<TerminalHighlightSpan> _highlightSpanScratch = [];
    private readonly List<SearchMatch> _searchMatchScratch = [];
    private readonly StringBuilder _rowTextScratch = new();
    private readonly StringBuilder _linkTokenScratch = new();
    private readonly List<int> _rowColumnMapScratch = [];
    private DispatcherTimer? _cursorBlinkTimer;
    private bool _cursorBlinkVisiblePhase = true;
    private int _lastBlinkCursorColumn = -1;
    private int _lastBlinkCursorRow = -1;
    private CursorStyle _lastBlinkCursorStyle = CursorStyle.Block;
    private int _lastAppliedColumns = -1;
    private int _lastAppliedRows = -1;
    private int _lastAppliedWidthPx = -1;
    private int _lastAppliedHeightPx = -1;
    private VtProcessorPreference _appliedVtProcessorPreference = VtProcessorPreference.Auto;
    private string? _activeTransportId;
    private readonly TerminalMouseModeTracker _mouseModeTracker = new();
    private readonly object _pendingTransportOutputSync = new();
    private readonly Queue<byte[]> _pendingTransportOutput = new();
    private bool _pendingTransportOutputDrainScheduled;

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
    /// Gets or sets the safety policy applied to clipboard paste operations.
    /// </summary>
    public TerminalPasteSafetyPolicy PasteSafetyPolicy { get; set; } = TerminalPasteSafetyPolicy.None;

    /// <summary>
    /// Gets or sets an optional callback used to decide unsafe paste handling.
    /// Used only when <see cref="PasteSafetyPolicy"/> is set to
    /// <see cref="TerminalPasteSafetyPolicy.ConfirmUnsafe"/>.
    /// </summary>
    public TerminalUnsafePasteHandler? UnsafePasteHandler { get; set; }

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

    /// <summary>
    /// Gets or sets whether Ghostty-style background opacity rendering is enabled.
    /// </summary>
    public bool BackgroundOpacityEnabled
    {
        get => _backgroundOpacityEnabled;
        set
        {
            if (_backgroundOpacityEnabled == value)
            {
                return;
            }

            _backgroundOpacityEnabled = value;
            UpdateRendererParityStateFromScreen();
        }
    }

    /// <summary>Gets the currently hovered hyperlink URL, if known.</summary>
    public string? HoveredLinkUrl => _hoveredLinkUrl;

    /// <summary>Gets the active search needle used for search highlight spans.</summary>
    public string? SearchNeedle => _searchNeedle;

    /// <summary>Gets the reported total number of active search matches.</summary>
    public int SearchTotal => _searchTotal;

    /// <summary>Gets the selected match index for active search highlights.</summary>
    public int SearchSelected => _searchSelected;

    /// <summary>Gets whether the terminal currently has a text selection.</summary>
    public bool HasSelection =>
        (TerminalSessionService.SelectionSource?.HasSelection).GetValueOrDefault() ||
        (_renderer is not null &&
         _renderer.SelectionStart is not null &&
         _renderer.SelectionEnd is not null);

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
                SyncScreenScrollOffsetFromScrollData();
                UpdateRendererCursorForViewport();
                UpdateRendererParityStateFromScreen();
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
        BackgroundProperty.OverrideDefaultValue<TerminalControl>(Brushes.Transparent);
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
            new KnownHostsSshHostKeyValidator(),
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
            new KnownHostsSshHostKeyValidator(),
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
        _theme = (Theme ?? TerminalTheme.Dark)
            .WithDefaultForeground(ColorToArgb(DefaultForeground))
            .WithDefaultBackground(ColorToArgb(DefaultBackground))
            .WithCursorColor(ColorToArgb(DefaultForeground));

        InitializeTerminal();
        RegisterPointerFallbackHandlers();
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

        TerminalTheme activeTheme = (_theme ?? TerminalTheme.Dark)
            .WithDefaultForeground(ColorToArgb(fg))
            .WithDefaultBackground(ColorToArgb(bg))
            .WithCursorColor(ColorToArgb(fg));
        _theme = activeTheme;
        _screen.ApplyTheme(activeTheme);

        _vtProcessor = VtProcessorFactory.Create(_screen, VtProcessorPreference);
        if (_vtProcessor is ITerminalThemeSink themeSink)
        {
            lock (_screen.SyncRoot)
            {
                themeSink.ApplyTheme(activeTheme);
            }
        }
        _appliedVtProcessorPreference = VtProcessorPreference;

        _renderer = CreateRenderer(previous: null);
        ApplyThemeToRenderer(activeTheme, _renderer);

        _scrollData = new TerminalScrollData
        {
            CellHeight = _renderer.CellHeight,
            Viewport = Rows * _renderer.CellHeight,
        };
        _scrollData.UpdateExtent(_screen.TotalRows, true);

        _scrollViewer = new VirtualizedTerminalScrollViewer(_screen, _scrollData);
        UpdateRendererParityStateFromScreen();
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

        if (change.Property == ThemeProperty)
        {
            ApplyThemeFromProperty();
            return;
        }

        if (change.Property == DefaultForegroundProperty || change.Property == DefaultBackgroundProperty)
        {
            if (_suppressLegacyColorThemeBridge)
            {
                ApplyColorDefaults();
            }
            else
            {
                ApplyLegacyColorBridge();
            }
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
            SyncScreenScrollOffsetFromScrollData();
            UpdateRendererCursorForViewport();
        }

        lock (_screen.SyncRoot)
        {
            _screen.InvalidateAll();
            UpdateRendererParityStateLocked();
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
        renderer.BackgroundOpacityEnabled = _backgroundOpacityEnabled;
        renderer.BackgroundOpacityCells = RendererBackgroundOpacityCells;
        renderer.BackgroundOpacity = RendererBackgroundOpacity;

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
        renderer.SearchHighlightColor = previous.SearchHighlightColor;
        renderer.SearchSelectedHighlightColor = previous.SearchSelectedHighlightColor;
        renderer.HyperlinkHoverUnderlineColor = previous.HyperlinkHoverUnderlineColor;
        renderer.EnableBackgroundOpacityHeuristics = previous.EnableBackgroundOpacityHeuristics;
        renderer.BackgroundOpacityEnabled = previous.BackgroundOpacityEnabled;
        renderer.BackgroundOpacityCells = previous.BackgroundOpacityCells;
        renderer.BackgroundOpacity = previous.BackgroundOpacity;
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

    private void ApplyLegacyColorBridge()
    {
        TerminalTheme next = (_theme ?? TerminalTheme.Dark)
            .WithDefaultForeground(ColorToArgb(DefaultForeground))
            .WithDefaultBackground(ColorToArgb(DefaultBackground));
        ApplyThemeCore(next, updateStyledDefaults: false);
    }

    private void ApplyThemeFromProperty()
    {
        TerminalTheme next = Theme
            ?? (_theme ?? TerminalTheme.Dark)
                .WithDefaultForeground(ColorToArgb(DefaultForeground))
                .WithDefaultBackground(ColorToArgb(DefaultBackground));
        ApplyThemeCore(next, updateStyledDefaults: true);
    }

    private void ApplyThemeCore(TerminalTheme theme, bool updateStyledDefaults)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;

        if (_screen is not null)
        {
            lock (_screen.SyncRoot)
            {
                _screen.ApplyTheme(theme, invalidateRows: true);

                if (_vtProcessor is ITerminalThemeSink themeSink)
                {
                    themeSink.ApplyTheme(theme);
                }
            }
        }
        else if (_vtProcessor is ITerminalThemeSink themeSink)
        {
            themeSink.ApplyTheme(theme);
        }

        if (_renderer is not null)
        {
            ApplyThemeToRenderer(theme, _renderer);
        }

        if (updateStyledDefaults)
        {
            _suppressLegacyColorThemeBridge = true;
            try
            {
                SetCurrentValue(DefaultForegroundProperty, ArgbToAvaloniaColor(theme.DefaultForeground));
                SetCurrentValue(DefaultBackgroundProperty, ArgbToAvaloniaColor(theme.DefaultBackground));
            }
            finally
            {
                _suppressLegacyColorThemeBridge = false;
            }
        }

        _screen?.InvalidateAll();
        _presenter?.Invalidate(fullRedraw: true);
    }

    private static void ApplyThemeToRenderer(TerminalTheme theme, SkiaTerminalRenderer renderer)
    {
        renderer.CursorColor = ArgbToSkColor(theme.CursorColor);
        if (theme.SelectionBackground is uint selectionBg)
        {
            // Use a translucent selection fill to keep glyphs legible.
            uint translucent = (selectionBg & 0x00FFFFFFu) | 0x80000000u;
            renderer.SelectionColor = ArgbToSkColor(translucent);
        }
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
        if (_theme is not null && _vtProcessor is ITerminalThemeSink themeSink)
        {
            lock (_screen.SyncRoot)
            {
                themeSink.ApplyTheme(_theme);
            }
        }
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
            SyncScreenScrollOffsetFromScrollData();
            UpdateRendererCursorForViewport();
            UpdateRendererParityStateFromScreen();
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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        EnsureCursorBlinkTimerRunning(false);
    }

    private void EnsurePresenter()
    {
        if (_presenter is not null)
        {
            _presenter.IsHitTestVisible = true;
            return;
        }

        _presenter = new TerminalPresenter();
        _presenter.IsHitTestVisible = true;
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        IBrush? background = Background;
        if (background is not null)
        {
            // Provide a hit-test surface even when terminal pixels are drawn by
            // a composition child visual outside the regular draw operation list.
            context.FillRectangle(background, new Rect(Bounds.Size));
        }
    }

    #region Endpoint Integration

    /// <summary>
    /// Connects this control to a terminal endpoint for terminal I/O.
    /// </summary>
    public void AttachEndpoint(ITerminalEndpoint endpoint)
    {
        _mouseModeTracker.Reset();
        ResetPointerButtons();
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
        _mouseModeTracker.Reset();
        ResetPointerButtons();
        EnsureCursorBlinkTimerRunning(false);
    }

    /// <summary>
    /// Writes data to the terminal screen model.
    /// Called when output is received from the PTY/terminal endpoint.
    /// </summary>
    public void WriteOutput(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            EnqueueOutputForUiThread(data.AsSpan().ToArray());
            return;
        }

        WriteOutputOnUiThread(data);
    }

    /// <summary>
    /// Writes data to the terminal screen model.
    /// Called when output is received from the PTY/terminal endpoint.
    /// </summary>
    public void WriteOutput(ReadOnlyMemory<byte> data)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            EnqueueOutputForUiThread(data.Span.ToArray());
            return;
        }

        WriteOutputOnUiThread(data);
    }

    /// <summary>
    /// Writes data to the terminal screen model.
    /// Called when output is received from the PTY/terminal endpoint.
    /// </summary>
    public void WriteOutput(ReadOnlySpan<byte> data)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            EnqueueOutputForUiThread(data.ToArray());
            return;
        }

        WriteOutputCore(data);

        // Span input may come from transient memory, so copy to a managed payload for event consumers.
        DataReceived?.Invoke(this, new TerminalDataEventArgs(data.ToArray()));

        TerminalScrollService.HandleOutput(_scrollData, _screen, AutoScroll, _presenter, RaiseScrollInvalidated);
        UpdateRendererCursorForViewport();
    }

    private void WriteOutputOnUiThread(ReadOnlyMemory<byte> data)
    {
        WriteOutputCore(data.Span);

        // Raise event without copying when input is already managed memory.
        DataReceived?.Invoke(this, new TerminalDataEventArgs(data));

        TerminalScrollService.HandleOutput(_scrollData, _screen, AutoScroll, _presenter, RaiseScrollInvalidated);
        UpdateRendererCursorForViewport();
    }

    private void WriteOutputCore(ReadOnlySpan<byte> data)
    {
        if (_screen is null)
        {
            return;
        }

        bool mouseModeChanged = _mouseModeTracker.Process(data);
        if (mouseModeChanged && IsMouseReportingActiveForInput())
        {
            ResetPointerButtons();
            _isMouseSelecting = false;
            TerminalSelectionService.ClearSelection(_screen, _renderer, _presenter);
        }

        // Lock screen during VT processing — composition thread reads cells concurrently
        lock (_screen.SyncRoot)
        {
            _vtProcessor?.Process(data);
            TryUpdateHoveredLinkFromPointerLocked();
            UpdateRendererParityStateLocked();
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

    private void RegisterPointerFallbackHandlers()
    {
        AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMovedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleasedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChangedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (!e.Handled)
        {
            return;
        }

        HandlePointerPressedCore(e);
    }

    private void OnPointerMovedTunnel(object? sender, PointerEventArgs e)
    {
        _ = sender;
        if (!e.Handled)
        {
            return;
        }

        HandlePointerMovedCore(e);
    }

    private void OnPointerReleasedTunnel(object? sender, PointerReleasedEventArgs e)
    {
        _ = sender;
        if (!e.Handled)
        {
            return;
        }

        HandlePointerReleasedCore(e);
    }

    private void OnPointerWheelChangedTunnel(object? sender, PointerWheelEventArgs e)
    {
        _ = sender;
        if (!e.Handled)
        {
            return;
        }

        HandlePointerWheelChangedCore(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled)
        {
            return;
        }

        HandlePointerPressedCore(e);
    }

    private void HandlePointerPressedCore(PointerPressedEventArgs e)
    {
        Focus();

        var point = e.GetPosition(this);
        UpdatePointerCell(point);
        var props = e.GetCurrentPoint(this).Properties;
        TerminalMouseButton button = ResolvePressedMouseButton(props);
        if (button == TerminalMouseButton.None &&
            IsPrimaryPointer(e.Pointer.Type))
        {
            // Some platform input paths (notably certain macOS touchpad flows)
            // can surface pressed events without explicit left-button metadata.
            button = TerminalMouseButton.Left;
        }

        if ((button == TerminalMouseButton.Left || props.IsLeftButtonPressed) &&
            TryActivateHyperlinkFromPointer(e.KeyModifiers))
        {
            _isMouseSelecting = false;
            e.Handled = true;
            return;
        }

        bool pointerSent = false;
        if (button != TerminalMouseButton.None)
        {
            SetPointerButtonState(button, isDown: true);
            pointerSent = SendPointerEvent(new TerminalPointerEvent(
                Kind: TerminalPointerEventKind.Button,
                X: point.X,
                Y: point.Y,
                Button: button,
                Action: TerminalInputAction.Press,
                Modifiers: ConvertTerminalModifiers(e.KeyModifiers)));
        }

        // Start text selection on left click
        if ((!IsMouseReportingActiveForInput() || !pointerSent) &&
            (button == TerminalMouseButton.Left || props.IsLeftButtonPressed) &&
            _renderer is not null)
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
        if (e.Handled)
        {
            return;
        }

        HandlePointerMovedCore(e);
    }

    private void HandlePointerMovedCore(PointerEventArgs e)
    {

        var point = e.GetPosition(this);
        UpdatePointerCell(point);
        var props = e.GetCurrentPoint(this).Properties;
        // Preserve tracked button state when move events omit button flags.
        SyncPointerButtonState(props, preserveWhenNoButtons: true);
        TerminalMouseButton button = GetPrimaryPressedMouseButton(props);
        _ = SendPointerEvent(new TerminalPointerEvent(
            Kind: TerminalPointerEventKind.Move,
            X: point.X,
            Y: point.Y,
            Button: button,
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
        if (e.Handled)
        {
            return;
        }

        HandlePointerReleasedCore(e);
    }

    private void HandlePointerReleasedCore(PointerReleasedEventArgs e)
    {
        var point = e.GetPosition(this);
        UpdatePointerCell(point);
        var props = e.GetCurrentPoint(this).Properties;
        TerminalMouseButton button = ConvertMouseButton(e.InitialPressMouseButton);
        if (button == TerminalMouseButton.None)
        {
            button = ResolveReleasedMouseButton(props);
        }
        if (button == TerminalMouseButton.None)
        {
            button = GetTrackedPressedMouseButton();
        }
        if (button == TerminalMouseButton.None &&
            IsPrimaryPointer(e.Pointer.Type))
        {
            button = TerminalMouseButton.Left;
        }

        SendPointerEvent(new TerminalPointerEvent(
            Kind: TerminalPointerEventKind.Button,
            X: point.X,
            Y: point.Y,
            Button: button,
            Action: TerminalInputAction.Release,
            Modifiers: ConvertTerminalModifiers(e.KeyModifiers)));

        if (button != TerminalMouseButton.None)
        {
            SetPointerButtonState(button, isDown: false);
        }
        else
        {
            SyncPointerButtonState(props);
        }

        _isMouseSelecting = false;
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Handled)
        {
            return;
        }

        HandlePointerWheelChangedCore(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_lastPointerColumn >= 0 || _lastPointerRow >= 0)
        {
            _lastPointerColumn = -1;
            _lastPointerRow = -1;
            if (_hoveredLinkUrl is not null)
            {
                _hoveredLinkUrl = null;
                UpdateRendererParityStateFromScreen();
            }
        }
    }

    private void HandlePointerWheelChangedCore(PointerWheelEventArgs e)
    {

        if (IsMouseReportingActiveForInput())
        {
            Point point = e.GetPosition(this);
            bool sent = SendPointerEvent(new TerminalPointerEvent(
                Kind: TerminalPointerEventKind.Scroll,
                X: point.X,
                Y: point.Y,
                Button: TerminalMouseButton.None,
                Action: TerminalInputAction.Press,
                Modifiers: ConvertTerminalModifiers(e.KeyModifiers),
                DeltaX: e.Delta.X,
                DeltaY: e.Delta.Y));
            if (sent)
            {
                e.Handled = true;
                return;
            }
        }

        TerminalScrollService.HandlePointerWheel(
            e,
            _scrollViewer,
            TerminalSessionService,
            _presenter,
            RaiseScrollInvalidated);
        UpdateRendererCursorForViewport();
        e.Handled = true;
    }

    #endregion

    #region Focus

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        Endpoint?.SetFocus(true);
        SendFocusEventIfNeeded(focused: true);
        _cursorBlinkVisiblePhase = true;
        UpdateRendererCursorForViewport();
        _presenter?.Invalidate();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        Endpoint?.SetFocus(false);
        SendFocusEventIfNeeded(focused: false);
        EnsureCursorBlinkTimerRunning(false);
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
        bool bracketedPaste = IsBracketedPasteActiveForInput();
        TerminalPasteRequest request = new(
            BracketedPasteEnabled: bracketedPaste,
            SafetyPolicy: PasteSafetyPolicy,
            UnsafePasteHandler: UnsafePasteHandler);
        await TerminalSelectionService.PasteAsync(this, SendInput, request);
    }

    /// <summary>
    /// Clears the current text selection.
    /// </summary>
    public void ClearSelection()
    {
        TerminalSelectionService.ClearSelection(_screen, _renderer, _presenter);
    }

    /// <summary>
    /// Updates the hovered hyperlink URL used for hyperlink-hover underline styling.
    /// </summary>
    public void SetHoveredLinkUrl(string? url)
    {
        string? normalized = string.IsNullOrWhiteSpace(url) ? null : url;
        if (string.Equals(_hoveredLinkUrl, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _hoveredLinkUrl = normalized;
        UpdateRendererParityStateFromScreen();
    }

    /// <summary>
    /// Starts a search-highlight session with the specified needle.
    /// </summary>
    public void StartSearch(string? needle)
    {
        _searchNeedle = string.IsNullOrWhiteSpace(needle) ? null : needle;
        _searchSelected = 0;
        _searchTotal = 0;
        UpdateRendererParityStateFromScreen();
        _ = ScrollSelectedSearchMatchIntoView();
        UpdateRendererParityStateFromScreen();
    }

    /// <summary>
    /// Ends the current search-highlight session.
    /// </summary>
    public void EndSearch()
    {
        if (_searchNeedle is null && _searchSelected < 0 && _searchTotal == 0)
        {
            return;
        }

        _searchNeedle = null;
        _searchSelected = -1;
        _searchTotal = 0;
        _searchMatchScratch.Clear();
        UpdateRendererParityStateFromScreen();
    }

    /// <summary>
    /// Updates total match count for the active search-highlight session.
    /// </summary>
    public void SetSearchTotal(int total)
    {
        int normalized = Math.Max(0, total);
        if (_searchTotal == normalized)
        {
            return;
        }

        _searchTotal = normalized;
        UpdateRendererParityStateFromScreen();
    }

    /// <summary>
    /// Updates the selected search match index for the active highlight session.
    /// </summary>
    public void SetSearchSelected(int selected)
    {
        if (_searchSelected == selected)
        {
            return;
        }

        _searchSelected = selected;
        _ = ScrollSelectedSearchMatchIntoView();
        UpdateRendererParityStateFromScreen();
    }

    /// <summary>
    /// Selects the next search result. Returns false when no active matches exist.
    /// </summary>
    public bool SelectNextSearchMatch()
    {
        return SelectSearchMatchRelative(1);
    }

    /// <summary>
    /// Selects the previous search result. Returns false when no active matches exist.
    /// </summary>
    public bool SelectPreviousSearchMatch()
    {
        return SelectSearchMatchRelative(-1);
    }

    /// <summary>
    /// Toggles Ghostty-style background opacity mode.
    /// </summary>
    public void ToggleBackgroundOpacity()
    {
        BackgroundOpacityEnabled = !BackgroundOpacityEnabled;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Applies a terminal theme at runtime.
    /// </summary>
    public void ApplyTheme(TerminalTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        SetCurrentValue(ThemeProperty, theme);
    }

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
        UpdateRendererCursorForViewport();
        UpdateRendererParityStateFromScreen();
    }

    /// <summary>
    /// Scrolls to the bottom of the terminal output.
    /// </summary>
    public void ScrollToBottom()
    {
        TerminalScrollService.ScrollToBottom(_scrollData, _screen, _presenter);
        UpdateRendererCursorForViewport();
        UpdateRendererParityStateFromScreen();
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
    /// <param name="arguments">Optional command arguments passed to the shell/program.</param>
    public void StartPty(
        string? shell = null,
        string? workingDirectory = null,
        IReadOnlyList<string>? arguments = null)
    {
        IReadOnlyList<string> normalizedArguments = arguments ?? Array.Empty<string>();
        TerminalCommandSpec? command = string.IsNullOrWhiteSpace(shell)
            ? (normalizedArguments.Count > 0
                ? new TerminalCommandSpec(string.Empty, normalizedArguments)
                : null)
            : new TerminalCommandSpec(shell, normalizedArguments);
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
        ResetPendingTransportOutputQueue();
        _mouseModeTracker.Reset();
        ResetPointerButtons();
        _isMouseSelecting = false;
        EnsureCursorBlinkTimerRunning(false);

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
        if (Dispatcher.UIThread.CheckAccess())
        {
            DrainPendingTransportOutput(flushAll: true);
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(() => DrainPendingTransportOutput(flushAll: true))
                .GetAwaiter()
                .GetResult();
        }
        ResetPendingTransportOutputQueue();
        _activeTransportId = null;
        _mouseModeTracker.Reset();
        ResetPointerButtons();
        _isMouseSelecting = false;
        EnsureCursorBlinkTimerRunning(false);
    }

    /// <summary>Requests the host/application to close the terminal surface.</summary>
    public void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
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
        if (length <= 0)
        {
            return;
        }

        // The PTY reader reuses its read buffer, so we must copy before posting
        // to the UI thread — otherwise the buffer may be overwritten by the next read.
        byte[] copy = data.AsSpan(0, length).ToArray();
        EnqueueOutputForUiThread(copy);
    }

    private void EnqueueOutputForUiThread(byte[] copy)
    {
        if (copy.Length == 0)
        {
            return;
        }

        bool scheduleDrain = false;
        lock (_pendingTransportOutputSync)
        {
            _pendingTransportOutput.Enqueue(copy);
            if (!_pendingTransportOutputDrainScheduled)
            {
                _pendingTransportOutputDrainScheduled = true;
                scheduleDrain = true;
            }
        }

        if (scheduleDrain)
        {
            Dispatcher.UIThread.Post(DrainPendingTransportOutput);
        }
    }

    private void OnPtyProcessExited(int exitCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DrainPendingTransportOutput(flushAll: true);
            _activeTransportId = null;
            // Write exit message to screen
            string msg = $"\r\n[Process exited with code {exitCode}]\r\n";
            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            WriteOutput(bytes);
            ProcessExited?.Invoke(this, exitCode);
        });
    }

    private void DrainPendingTransportOutput()
    {
        DrainPendingTransportOutput(flushAll: false);
    }

    private void DrainPendingTransportOutput(bool flushAll)
    {
        int processed = 0;
        while (true)
        {
            byte[] nextChunk;
            lock (_pendingTransportOutputSync)
            {
                if (_pendingTransportOutput.Count == 0)
                {
                    _pendingTransportOutputDrainScheduled = false;
                    return;
                }

                if (!flushAll && processed >= MaxPendingOutputChunksPerDispatch)
                {
                    Dispatcher.UIThread.Post(DrainPendingTransportOutput);
                    return;
                }

                nextChunk = _pendingTransportOutput.Dequeue();
            }

            WriteOutputOnUiThread(nextChunk);
            processed++;
        }
    }

    private void ResetPendingTransportOutputQueue()
    {
        lock (_pendingTransportOutputSync)
        {
            _pendingTransportOutput.Clear();
            _pendingTransportOutputDrainScheduled = false;
        }
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

    private bool SendPointerEvent(TerminalPointerEvent pointerEvent)
    {
        ITerminalInputSink? inputSink = TerminalSessionService.InputSink;
        if (inputSink is not null)
        {
            if (inputSink.SendPointer(pointerEvent))
            {
                return true;
            }
        }

        if (!_mouseModeTracker.ModeState.IsMouseReportingEnabled ||
            !HasTransportOrDirectPtyInputPath())
        {
            return false;
        }

        if (!TryResolvePointerCell(pointerEvent.X, pointerEvent.Y, out int column, out int row))
        {
            return false;
        }

        if (!TerminalMouseProtocolEncoder.TryEncode(
                pointerEvent,
                _mouseModeTracker.ModeState,
                column,
                row,
                out byte[] encoded))
        {
            return false;
        }

        TerminalSessionService.SendInput(encoded);
        return true;
    }

    private bool IsMouseReportingActiveForInput()
    {
        if (!_mouseModeTracker.ModeState.IsMouseReportingEnabled)
        {
            return false;
        }

        return TerminalSessionService.InputSink is not null
            || HasTransportOrDirectPtyInputPath();
    }

    private bool IsBracketedPasteActiveForInput()
    {
        ITerminalModeSource? modeSource = TerminalSessionService.ModeSource;
        if (modeSource is not null)
        {
            return modeSource.ModeState.BracketedPaste;
        }

        return _vtProcessor?.BracketedPaste ?? false;
    }

    private bool IsFocusEventModeActiveForInput()
    {
        if (!HasTransportOrDirectPtyInputPath() || TerminalSessionService.InputSink is not null)
        {
            return false;
        }

        if (_vtProcessor is ITerminalFocusEventModeSource focusModeSource)
        {
            return focusModeSource.FocusEventsEnabled;
        }

        if (TerminalSessionService.ModeSource is ITerminalFocusEventModeSource focusModeFromSource)
        {
            return focusModeFromSource.FocusEventsEnabled;
        }

        return false;
    }

    private void SendFocusEventIfNeeded(bool focused)
    {
        if (!IsFocusEventModeActiveForInput())
        {
            return;
        }

        TerminalSessionService.SendInput(focused ? "\x1b[I" : "\x1b[O");
    }

    private bool HasTransportOrDirectPtyInputPath()
    {
        if (TerminalSessionService.Endpoint is not null)
        {
            return true;
        }

        if (TerminalSessionService.HasActiveTransport)
        {
            return true;
        }

        // Support legacy/direct PTY paths where no transport instance is attached.
        return TerminalSessionService.Transport is null && TerminalSessionService.HasPty;
    }

    private bool TryResolvePointerCell(double x, double y, out int column, out int row)
    {
        column = 0;
        row = 0;

        if (_renderer is null || _renderer.CellWidth <= 0 || _renderer.CellHeight <= 0)
        {
            return false;
        }

        int col = Math.Max(0, (int)(x / _renderer.CellWidth));
        int rowIndex = Math.Max(0, (int)(y / _renderer.CellHeight));
        column = Math.Clamp(col + 1, 1, Columns);
        row = Math.Clamp(rowIndex + 1, 1, Rows);
        return true;
    }

    private void UpdatePointerCell(Point point)
    {
        if (_renderer is null || _screen is null)
        {
            return;
        }

        if (_renderer.CellWidth <= 0f || _renderer.CellHeight <= 0f)
        {
            return;
        }

        if (_screen.Columns <= 0 || _screen.ViewportRows <= 0)
        {
            return;
        }

        int col = (int)Math.Floor(point.X / _renderer.CellWidth);
        int row = (int)Math.Floor(point.Y / _renderer.CellHeight);
        col = Math.Clamp(col, 0, _screen.Columns - 1);
        row = Math.Clamp(row, 0, _screen.ViewportRows - 1);

        if (col == _lastPointerColumn && row == _lastPointerRow)
        {
            return;
        }

        bool hadHover = _hoveredLinkUrl is not null;
        _lastPointerColumn = col;
        _lastPointerRow = row;
        bool hoverChanged;
        lock (_screen.SyncRoot)
        {
            hoverChanged = TryUpdateHoveredLinkFromPointerLocked();
        }

        if (hoverChanged || hadHover || _hoveredLinkUrl is not null)
        {
            UpdateRendererParityStateFromScreen();
        }
    }

    private bool TryUpdateHoveredLinkFromPointerLocked()
    {
        if (_screen is null ||
            _lastPointerColumn < 0 ||
            _lastPointerRow < 0 ||
            (uint)_lastPointerColumn >= (uint)_screen.Columns ||
            (uint)_lastPointerRow >= (uint)_screen.ViewportRows)
        {
            return false;
        }

        string? nextUrl = TryResolveHoveredLinkUrlAtPointerLocked(out string? resolvedUrl)
            ? resolvedUrl
            : null;

        if (string.Equals(_hoveredLinkUrl, nextUrl, StringComparison.Ordinal))
        {
            return false;
        }

        _hoveredLinkUrl = nextUrl;
        return true;
    }

    private bool TryResolveHoveredLinkUrlAtPointerLocked(out string? resolvedUrl)
    {
        resolvedUrl = null;

        if (_screen is null ||
            _lastPointerColumn < 0 ||
            _lastPointerRow < 0 ||
            (uint)_lastPointerColumn >= (uint)_screen.Columns ||
            (uint)_lastPointerRow >= (uint)_screen.ViewportRows)
        {
            return false;
        }

        TerminalRow row = _screen.GetViewportRow(_lastPointerRow);
        int hyperlinkId = row[_lastPointerColumn].HyperlinkId;
        if (hyperlinkId > 0 && _screen.TryGetHyperlinkUrl(hyperlinkId, out string? resolved))
        {
            resolvedUrl = resolved;
            return true;
        }

        return TryResolveInlineHoveredLinkUrlLocked(row, _lastPointerColumn, out resolvedUrl);
    }

    private bool TryActivateHyperlinkFromPointer(KeyModifiers modifiers)
    {
        if (_screen is null || !HasHyperlinkActivationModifier(modifiers))
        {
            return false;
        }

        string? url;
        lock (_screen.SyncRoot)
        {
            if (!TryResolveHoveredLinkUrlAtPointerLocked(out url) || string.IsNullOrWhiteSpace(url))
            {
                return false;
            }
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return TryActivateHyperlink(uri);
    }

    /// <summary>
    /// Activates a hyperlink resolved from terminal content (for example on Ctrl/Cmd+click).
    /// Override in tests or hosts that need custom navigation behavior.
    /// </summary>
    protected virtual bool TryActivateHyperlink(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Launcher is null)
        {
            return false;
        }

        _ = LaunchHyperlinkAsync(topLevel, uri);
        return true;
    }

    private static async Task LaunchHyperlinkAsync(TopLevel topLevel, Uri uri)
    {
        try
        {
            _ = await topLevel.Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            // Swallow launch failures to keep terminal input flow uninterrupted.
        }
    }

    private static bool HasHyperlinkActivationModifier(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);

    private bool TryResolveInlineHoveredLinkUrlLocked(TerminalRow row, int column, out string? url)
    {
        url = null;

        if (!TryResolveLinkTokenSpanLocked(row, column, out int tokenStart, out int tokenEnd))
        {
            return false;
        }

        if (!TryReadLinkTokenTextLocked(row, tokenStart, tokenEnd, out string token))
        {
            return false;
        }

        if (!TryNormalizeHoveredLinkToken(token, out string normalized))
        {
            return false;
        }

        url = normalized;
        return true;
    }

    private bool TryReadLinkTokenTextLocked(
        TerminalRow row,
        int startColumn,
        int endColumn,
        out string token)
    {
        _linkTokenScratch.Clear();

        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        if (cells.IsEmpty)
        {
            token = string.Empty;
            return false;
        }

        int start = Math.Clamp(startColumn, 0, cells.Length - 1);
        int end = Math.Clamp(endColumn, start, cells.Length - 1);
        for (int col = start; col <= end; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (cell.Width == 0 || (cell.Attributes & CellAttributes.Hidden) != 0)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                _linkTokenScratch.Append(cell.Grapheme);
                continue;
            }

            if (cell.Codepoint > 0 && Rune.IsValid(cell.Codepoint))
            {
                _linkTokenScratch.Append(char.ConvertFromUtf32(cell.Codepoint));
            }
        }

        if (_linkTokenScratch.Length == 0)
        {
            token = string.Empty;
            return false;
        }

        token = _linkTokenScratch.ToString();
        return true;
    }

    private static bool TryNormalizeHoveredLinkToken(string token, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string candidate = token.Trim();
        candidate = candidate.Trim('"', '\'', '<', '>', '(', ')', '[', ']', '{', '}', '.', ',', ';', '!', '?');
        if (candidate.Length == 0)
        {
            return false;
        }

        bool hasScheme = candidate.Contains("://", StringComparison.Ordinal);
        bool startsWithWww = candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
        if (!hasScheme && !startsWithWww)
        {
            return false;
        }

        string parseCandidate = startsWithWww
            ? $"https://{candidate}"
            : candidate;
        if (!Uri.TryCreate(parseCandidate, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = parseCandidate;
        return true;
    }

    private void UpdateRendererParityStateFromScreen()
    {
        if (_screen is null || _renderer is null)
        {
            return;
        }

        lock (_screen.SyncRoot)
        {
            UpdateRendererParityStateLocked();
        }

        _presenter?.Invalidate();
    }

    private void UpdateRendererParityStateLocked()
    {
        if (_screen is null || _renderer is null)
        {
            return;
        }

        _ = TryUpdateHoveredLinkFromPointerLocked();
        _renderer.BackgroundOpacityEnabled = _backgroundOpacityEnabled;
        _renderer.BackgroundOpacityCells = RendererBackgroundOpacityCells;
        _renderer.BackgroundOpacity = RendererBackgroundOpacity;
        _renderer.SetHighlightSpans(BuildHighlightSpansLocked());
    }

    private TerminalHighlightSpan[] BuildHighlightSpansLocked()
    {
        if (_screen is null)
        {
            return Array.Empty<TerminalHighlightSpan>();
        }

        _highlightSpanScratch.Clear();
        AppendSearchHighlightSpansLocked();
        AppendHoveredLinkSpanLocked();

        if (_highlightSpanScratch.Count == 0)
        {
            return Array.Empty<TerminalHighlightSpan>();
        }

        return _highlightSpanScratch.ToArray();
    }

    private void AppendSearchHighlightSpansLocked()
    {
        if (_screen is null || string.IsNullOrEmpty(_searchNeedle))
        {
            _searchTotal = 0;
            _searchSelected = -1;
            _searchMatchScratch.Clear();
            return;
        }

        string needle = _searchNeedle;
        if (needle.Length == 0)
        {
            _searchTotal = 0;
            _searchSelected = -1;
            _searchMatchScratch.Clear();
            return;
        }

        _searchMatchScratch.Clear();
        int viewportTopAbsoluteRow = Math.Max(0, _screen.TotalRows - _screen.ViewportRows - _screen.ScrollOffset);
        for (int absoluteRow = 0; absoluteRow < _screen.TotalRows; absoluteRow++)
        {
            TerminalRow terminalRow = _screen.GetRow(absoluteRow);
            if (!TryBuildRowTextColumnMap(terminalRow, out string rowText))
            {
                continue;
            }

            int searchFrom = 0;
            while (searchFrom <= rowText.Length - needle.Length)
            {
                int found = rowText.IndexOf(needle, searchFrom, StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                int mapEnd = found + needle.Length - 1;
                if ((uint)found < (uint)_rowColumnMapScratch.Count &&
                    (uint)mapEnd < (uint)_rowColumnMapScratch.Count)
                {
                    int startColumn = _rowColumnMapScratch[found];
                    int endColumn = _rowColumnMapScratch[mapEnd];
                    _searchMatchScratch.Add(new SearchMatch(absoluteRow, startColumn, endColumn));
                }
                searchFrom = found + Math.Max(needle.Length, 1);
            }
        }

        _searchTotal = _searchMatchScratch.Count;
        if (_searchTotal <= 0)
        {
            _searchSelected = -1;
            return;
        }

        if (_searchSelected < 0)
        {
            _searchSelected = 0;
        }
        else if (_searchSelected >= _searchTotal)
        {
            _searchSelected = _searchTotal - 1;
        }

        for (int index = 0; index < _searchMatchScratch.Count; index++)
        {
            SearchMatch match = _searchMatchScratch[index];
            int viewportRow = match.AbsoluteRow - viewportTopAbsoluteRow;
            if ((uint)viewportRow >= (uint)_screen.ViewportRows)
            {
                continue;
            }

            TerminalHighlightKind kind = index == _searchSelected
                ? TerminalHighlightKind.SearchSelected
                : TerminalHighlightKind.SearchMatch;
            _highlightSpanScratch.Add(
                new TerminalHighlightSpan(
                    viewportRow,
                    match.StartColumn,
                    match.EndColumn,
                    kind));
        }
    }

    private void AppendHoveredLinkSpanLocked()
    {
        if (_screen is null ||
            _lastPointerRow < 0 ||
            _lastPointerColumn < 0)
        {
            return;
        }

        int row = _lastPointerRow;
        int column = _lastPointerColumn;
        if ((uint)row >= (uint)_screen.ViewportRows || (uint)column >= (uint)_screen.Columns)
        {
            return;
        }

        if (TryResolveHyperlinkSpanFromPointerLocked(row, column, out TerminalHighlightSpan span))
        {
            _highlightSpanScratch.Add(span);
            return;
        }

        if (!string.IsNullOrEmpty(_hoveredLinkUrl) &&
            TryResolveHoveredLinkSpanLocked(row, column, _hoveredLinkUrl, out span))
        {
            _highlightSpanScratch.Add(span);
        }
    }

    private bool TryResolveHyperlinkSpanFromPointerLocked(
        int row,
        int column,
        out TerminalHighlightSpan span)
    {
        span = default;
        if (_screen is null)
        {
            return false;
        }

        TerminalRow terminalRow = _screen.GetViewportRow(row);
        ReadOnlySpan<TerminalCell> cells = terminalRow.ReadOnlyCells;
        if ((uint)column >= (uint)cells.Length)
        {
            return false;
        }

        int hyperlinkId = cells[column].HyperlinkId;
        if (hyperlinkId <= 0)
        {
            return false;
        }

        int startColumn = column;
        int endColumn = column;
        while (startColumn > 0 && cells[startColumn - 1].HyperlinkId == hyperlinkId)
        {
            startColumn--;
        }

        while (endColumn + 1 < cells.Length && cells[endColumn + 1].HyperlinkId == hyperlinkId)
        {
            endColumn++;
        }

        span = new TerminalHighlightSpan(
            row,
            startColumn,
            endColumn,
            TerminalHighlightKind.HyperlinkHover);
        return true;
    }

    private bool TryResolveHoveredLinkSpanLocked(
        int row,
        int column,
        string url,
        out TerminalHighlightSpan span)
    {
        span = default;

        if (_screen is null)
        {
            return false;
        }

        TerminalRow terminalRow = _screen.GetViewportRow(row);
        if (TryBuildRowTextColumnMap(terminalRow, out string rowText))
        {
            int searchFrom = 0;
            while (searchFrom <= rowText.Length - url.Length)
            {
                int found = rowText.IndexOf(url, searchFrom, StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                int mapEnd = found + url.Length - 1;
                if ((uint)found < (uint)_rowColumnMapScratch.Count &&
                    (uint)mapEnd < (uint)_rowColumnMapScratch.Count)
                {
                    int startColumn = _rowColumnMapScratch[found];
                    int endColumn = _rowColumnMapScratch[mapEnd];
                    if (column >= startColumn && column <= endColumn)
                    {
                        span = new TerminalHighlightSpan(
                            row,
                            startColumn,
                            endColumn,
                            TerminalHighlightKind.HyperlinkHover);
                        return true;
                    }
                }

                searchFrom = found + Math.Max(url.Length, 1);
            }
        }

        if (TryResolveLinkTokenSpanLocked(terminalRow, column, out int tokenStart, out int tokenEnd))
        {
            span = new TerminalHighlightSpan(
                row,
                tokenStart,
                tokenEnd,
                TerminalHighlightKind.HyperlinkHover);
            return true;
        }

        span = new TerminalHighlightSpan(
            row,
            column,
            column,
            TerminalHighlightKind.HyperlinkHover);
        return true;
    }

    private bool TryBuildRowTextColumnMap(TerminalRow row, out string rowText)
    {
        _rowTextScratch.Clear();
        _rowColumnMapScratch.Clear();

        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        for (int col = 0; col < cells.Length; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (cell.Width == 0 || (cell.Attributes & CellAttributes.Hidden) != 0)
            {
                continue;
            }

            string? text = null;
            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                text = cell.Grapheme;
            }
            else if (cell.Codepoint > 0 && Rune.IsValid(cell.Codepoint))
            {
                text = char.ConvertFromUtf32(cell.Codepoint);
            }

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            int start = _rowTextScratch.Length;
            _rowTextScratch.Append(text);
            int end = _rowTextScratch.Length;
            for (int i = start; i < end; i++)
            {
                _rowColumnMapScratch.Add(col);
            }
        }

        if (_rowTextScratch.Length == 0)
        {
            rowText = string.Empty;
            return false;
        }

        rowText = _rowTextScratch.ToString();
        return true;
    }

    private static bool TryResolveLinkTokenSpanLocked(
        TerminalRow row,
        int column,
        out int startColumn,
        out int endColumn)
    {
        startColumn = 0;
        endColumn = 0;

        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        if ((uint)column >= (uint)cells.Length)
        {
            return false;
        }

        if (!TryGetPrimaryRune(in cells[column], out Rune centerRune) ||
            !IsLinkTokenRune(centerRune))
        {
            return false;
        }

        int start = column;
        int end = column;

        while (start > 0 &&
               TryGetPrimaryRune(in cells[start - 1], out Rune rune) &&
               IsLinkTokenRune(rune))
        {
            start--;
        }

        while (end + 1 < cells.Length &&
               TryGetPrimaryRune(in cells[end + 1], out Rune rune) &&
               IsLinkTokenRune(rune))
        {
            end++;
        }

        startColumn = start;
        endColumn = end;
        return true;
    }

    private static bool TryGetPrimaryRune(ref readonly TerminalCell cell, out Rune rune)
    {
        rune = default;

        if (cell.Width == 0 || (cell.Attributes & CellAttributes.Hidden) != 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(cell.Grapheme))
        {
            return Rune.DecodeFromUtf16(cell.Grapheme.AsSpan(), out rune, out int consumed) == OperationStatus.Done &&
                   consumed > 0;
        }

        if (!Rune.IsValid(cell.Codepoint))
        {
            return false;
        }

        rune = new Rune(cell.Codepoint);
        return true;
    }

    private static bool IsLinkTokenRune(Rune rune)
    {
        if (Rune.IsLetterOrDigit(rune))
        {
            return true;
        }

        return rune.Value switch
        {
            '-' or '_' or '.' or '~' or ':' or '/' or '?' or '#' or '[' or ']' or '@' or
            '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or ';' or '%' or '=' =>
                true,
            _ => false,
        };
    }

    private bool SelectSearchMatchRelative(int delta)
    {
        if (string.IsNullOrEmpty(_searchNeedle))
        {
            return false;
        }

        UpdateRendererParityStateFromScreen();
        if (_searchTotal <= 0)
        {
            return false;
        }

        int previousSelection = _searchSelected;
        int selected = _searchSelected;
        if (selected < 0 || selected >= _searchTotal)
        {
            selected = 0;
        }
        else
        {
            selected = (selected + delta) % _searchTotal;
            if (selected < 0)
            {
                selected += _searchTotal;
            }
        }

        if (selected == previousSelection && _searchTotal == 1)
        {
            return false;
        }

        _searchSelected = selected;
        bool scrolled = ScrollSelectedSearchMatchIntoView();
        UpdateRendererParityStateFromScreen();
        if (scrolled)
        {
            RaiseScrollInvalidated();
        }

        return true;
    }

    private bool ScrollSelectedSearchMatchIntoView()
    {
        if (_screen is null || _searchSelected < 0)
        {
            return false;
        }

        bool changed = false;
        lock (_screen.SyncRoot)
        {
            if ((uint)_searchSelected >= (uint)_searchMatchScratch.Count)
            {
                return false;
            }

            int selectedAbsoluteRow = _searchMatchScratch[_searchSelected].AbsoluteRow;
            int viewportTopAbsoluteRow = Math.Max(0, _screen.TotalRows - _screen.ViewportRows - _screen.ScrollOffset);
            int viewportBottomAbsoluteRow = viewportTopAbsoluteRow + _screen.ViewportRows - 1;
            if (selectedAbsoluteRow >= viewportTopAbsoluteRow && selectedAbsoluteRow <= viewportBottomAbsoluteRow)
            {
                return false;
            }

            int maxTopAbsoluteRow = Math.Max(0, _screen.TotalRows - _screen.ViewportRows);
            int nextTopAbsoluteRow = selectedAbsoluteRow < viewportTopAbsoluteRow
                ? selectedAbsoluteRow
                : selectedAbsoluteRow - _screen.ViewportRows + 1;
            nextTopAbsoluteRow = Math.Clamp(nextTopAbsoluteRow, 0, maxTopAbsoluteRow);
            int nextScrollOffset = _screen.TotalRows - _screen.ViewportRows - nextTopAbsoluteRow;
            nextScrollOffset = Math.Clamp(nextScrollOffset, 0, _screen.MaxScrollOffset);
            if (_screen.ScrollOffset != nextScrollOffset)
            {
                _screen.ScrollOffset = nextScrollOffset;
                _screen.InvalidateAll();
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        SyncScrollDataFromScreenOffset();
        UpdateRendererCursorForViewport();
        return true;
    }

    private void ResetPointerButtons()
    {
        _leftPointerDown = false;
        _middlePointerDown = false;
        _rightPointerDown = false;
    }

    private void SyncPointerButtonState(PointerPointProperties properties, bool preserveWhenNoButtons = false)
    {
        bool anyButtonPressed =
            properties.IsLeftButtonPressed ||
            properties.IsMiddleButtonPressed ||
            properties.IsRightButtonPressed;

        if (preserveWhenNoButtons && !anyButtonPressed)
        {
            return;
        }

        _leftPointerDown = properties.IsLeftButtonPressed;
        _middlePointerDown = properties.IsMiddleButtonPressed;
        _rightPointerDown = properties.IsRightButtonPressed;
    }

    private void SetPointerButtonState(TerminalMouseButton button, bool isDown)
    {
        switch (button)
        {
            case TerminalMouseButton.Left:
                _leftPointerDown = isDown;
                break;
            case TerminalMouseButton.Middle:
                _middlePointerDown = isDown;
                break;
            case TerminalMouseButton.Right:
                _rightPointerDown = isDown;
                break;
        }
    }

    private TerminalMouseButton GetPrimaryPressedMouseButton(PointerPointProperties properties)
    {
        if (_leftPointerDown || properties.IsLeftButtonPressed)
        {
            return TerminalMouseButton.Left;
        }

        if (_middlePointerDown || properties.IsMiddleButtonPressed)
        {
            return TerminalMouseButton.Middle;
        }

        if (_rightPointerDown || properties.IsRightButtonPressed)
        {
            return TerminalMouseButton.Right;
        }

        return TerminalMouseButton.None;
    }

    private TerminalMouseButton GetTrackedPressedMouseButton()
    {
        if (_leftPointerDown)
        {
            return TerminalMouseButton.Left;
        }

        if (_middlePointerDown)
        {
            return TerminalMouseButton.Middle;
        }

        if (_rightPointerDown)
        {
            return TerminalMouseButton.Right;
        }

        return TerminalMouseButton.None;
    }

    private static bool IsPrimaryPointer(PointerType pointerType)
    {
        return pointerType is PointerType.Mouse or PointerType.Touch;
    }

    private static TerminalMouseButton ResolvePressedMouseButton(PointerPointProperties properties)
    {
        TerminalMouseButton fromUpdateKind = properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => TerminalMouseButton.Left,
            PointerUpdateKind.MiddleButtonPressed => TerminalMouseButton.Middle,
            PointerUpdateKind.RightButtonPressed => TerminalMouseButton.Right,
            _ => TerminalMouseButton.None,
        };

        if (fromUpdateKind != TerminalMouseButton.None)
        {
            return fromUpdateKind;
        }

        return ConvertPressedMouseButton(properties);
    }

    private static TerminalMouseButton ResolveReleasedMouseButton(PointerPointProperties properties) =>
        properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonReleased => TerminalMouseButton.Left,
            PointerUpdateKind.MiddleButtonReleased => TerminalMouseButton.Middle,
            PointerUpdateKind.RightButtonReleased => TerminalMouseButton.Right,
            _ => TerminalMouseButton.None,
        };

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

    private void SyncScreenScrollOffsetFromScrollData()
    {
        if (_scrollData is null || _screen is null)
        {
            return;
        }

        int nextOffset = _scrollData.ToScreenScrollOffsetRows(_screen.MaxScrollOffset);
        if (_screen.ScrollOffset == nextOffset)
        {
            return;
        }

        _screen.ScrollOffset = nextOffset;
        _screen.InvalidateAll();
    }

    private void SyncScrollDataFromScreenOffset()
    {
        if (_scrollData is null || _screen is null)
        {
            return;
        }

        int screenMaxOffsetRows = _screen.MaxScrollOffset;
        if (screenMaxOffsetRows <= 0 || _scrollData.MaxOffset <= 0)
        {
            _scrollData.Offset = _scrollData.MaxOffset;
            return;
        }

        int topAnchoredRows = screenMaxOffsetRows - _screen.ScrollOffset;
        topAnchoredRows = Math.Clamp(topAnchoredRows, 0, screenMaxOffsetRows);
        double scaledOffset = (_scrollData.MaxOffset * topAnchoredRows) / screenMaxOffsetRows;
        _scrollData.Offset = scaledOffset;
    }

    private void UpdateRendererCursorForViewport()
    {
        if (_renderer is null || _screen is null || _vtProcessor is null)
        {
            return;
        }

        int cursorColumn = _vtProcessor.CursorCol;
        int cursorRow = _vtProcessor.CursorRow + _screen.ScrollOffset;
        _renderer.CursorColumn = cursorColumn;
        _renderer.CursorRow = cursorRow;
        bool blinkEnabled = UpdateRendererCursorStyleFromVtProcessor();

        bool rowVisible = (uint)cursorRow < (uint)_screen.ViewportRows;
        bool columnVisible = (uint)cursorColumn < (uint)_screen.Columns;
        bool atLiveBottom = _screen.ScrollOffset == 0;
        bool baseVisible = _vtProcessor.CursorVisible && atLiveBottom && rowVisible && columnVisible;
        bool blinkPhaseActive = blinkEnabled && IsFocused;

        if (baseVisible &&
            blinkPhaseActive &&
            (_lastBlinkCursorColumn != cursorColumn ||
             _lastBlinkCursorRow != cursorRow ||
             _lastBlinkCursorStyle != _renderer.CursorStyle))
        {
            _cursorBlinkVisiblePhase = true;
            _lastBlinkCursorColumn = cursorColumn;
            _lastBlinkCursorRow = cursorRow;
            _lastBlinkCursorStyle = _renderer.CursorStyle;
        }

        EnsureCursorBlinkTimerRunning(baseVisible && blinkPhaseActive);
        _renderer.CursorVisible = baseVisible && (!blinkPhaseActive || _cursorBlinkVisiblePhase);
    }

    private bool UpdateRendererCursorStyleFromVtProcessor()
    {
        if (_renderer is null || _vtProcessor is not ITerminalCursorStyleSource cursorStyleSource)
        {
            return false;
        }

        _renderer.CursorStyle = ConvertCursorStyle(cursorStyleSource.CursorStyle);
        return cursorStyleSource.CursorBlinking;
    }

    private void EnsureCursorBlinkTimerRunning(bool enabled)
    {
        if (enabled)
        {
            _cursorBlinkTimer ??= CreateCursorBlinkTimer();
            if (!_cursorBlinkTimer.IsEnabled)
            {
                _cursorBlinkTimer.Start();
            }

            return;
        }

        if (_cursorBlinkTimer is not null && _cursorBlinkTimer.IsEnabled)
        {
            _cursorBlinkTimer.Stop();
        }

        _cursorBlinkVisiblePhase = true;
    }

    private DispatcherTimer CreateCursorBlinkTimer()
    {
        DispatcherTimer timer = new()
        {
            Interval = CursorBlinkInterval,
        };
        timer.Tick += OnCursorBlinkTick;
        return timer;
    }

    private void OnCursorBlinkTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _cursorBlinkVisiblePhase = !_cursorBlinkVisiblePhase;
        UpdateRendererCursorForViewport();
        _presenter?.Invalidate();
    }

    private static CursorStyle ConvertCursorStyle(TerminalCursorStyle style)
    {
        return style switch
        {
            TerminalCursorStyle.Underline => CursorStyle.Underline,
            TerminalCursorStyle.Bar => CursorStyle.Bar,
            TerminalCursorStyle.BlockHollow => CursorStyle.BlockHollow,
            _ => CursorStyle.Block,
        };
    }

    private readonly record struct SearchMatch(int AbsoluteRow, int StartColumn, int EndColumn);

    private static Color ArgbToAvaloniaColor(uint argb) =>
        Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));

    private static SKColor ArgbToSkColor(uint argb) =>
        new(
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));

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
