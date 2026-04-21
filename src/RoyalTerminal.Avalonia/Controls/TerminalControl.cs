// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Main terminal control.

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Pipe;
using RoyalTerminal.Terminal.Transport.Pty;
using RoyalTerminal.Terminal.Transport.Raw;
using RoyalTerminal.Terminal.Transport.Serial;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using RoyalTerminal.Terminal.Transport.Telnet;

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
    // Managed VT transport output is parsed off the UI thread, but UI finalize
    // work still stays bounded so input and layout can preempt output floods.
    private const int MaxQueuedOutputChunkBytes = 1024;
    private const int MaxPendingOutputChunksPerDispatch = 4;
    private const int MaxPendingOutputBytesPerDispatch = 8 * 1024;
    private static readonly TimeSpan MaxPendingOutputDispatchDuration = TimeSpan.FromMilliseconds(2);
    // Keep substantially more PTY output buffered than we render per UI slice.
    // Native terminals keep draining the PTY aggressively under flood; blocking
    // after only a few kilobytes can leave shell loops stuck in write syscalls
    // and degrade Ctrl+C/Ctrl+Z responsiveness over repeated runs.
    private const int MaxPendingOutputQueueBytes = 256 * 1024;
    private const int ResumePendingOutputQueueBytes = MaxPendingOutputQueueBytes / 2;
    private const int MaxPendingOutputQueueChunks = 256;
    private const int ResumePendingOutputQueueChunks = MaxPendingOutputQueueChunks / 2;
    // Managed VT parsing already yields in small UI batches, so draining at
    // Background priority avoids starvation without monopolizing the UI thread.
    private static readonly DispatcherPriority ManagedPendingOutputDrainPriority = DispatcherPriority.Background;
    private static readonly DispatcherPriority NativePendingOutputDrainPriority = DispatcherPriority.Background;
    private static readonly long UrgentControlVtResponseSuppressionWindowTicks =
        (long)(TimeSpan.FromSeconds(1).TotalSeconds * Stopwatch.Frequency);

    #region Styled Properties

    /// <summary>The font family used for terminal text.</summary>
    public static readonly StyledProperty<string> FontFamilyNameProperty =
        AvaloniaProperty.Register<TerminalControl, string>(nameof(FontFamilyName), TerminalDefaults.DefaultMonoFont);

    /// <summary>The source used to resolve the terminal font.</summary>
    public static readonly StyledProperty<TerminalFontSource> FontSourceProperty =
        AvaloniaProperty.Register<TerminalControl, TerminalFontSource>(nameof(FontSource), TerminalFontSource.System);

    /// <summary>The font file path used when <see cref="FontSource"/> is <see cref="TerminalFontSource.File"/>.</summary>
    public static readonly StyledProperty<string> FontFilePathProperty =
        AvaloniaProperty.Register<TerminalControl, string>(nameof(FontFilePath), string.Empty);

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

    /// <summary>Gets or sets the source used to resolve the terminal font.</summary>
    public TerminalFontSource FontSource
    {
        get => GetValue(FontSourceProperty);
        set => SetValue(FontSourceProperty, value);
    }

    /// <summary>Gets or sets the font file path used when <see cref="FontSource"/> is <see cref="TerminalFontSource.File"/>.</summary>
    public string FontFilePath
    {
        get => GetValue(FontFilePathProperty);
        set => SetValue(FontFilePathProperty, value);
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
    private readonly List<TerminalSearchMatch> _searchMatchScratch = [];
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
    private readonly object _pendingTransportOutputDrainExecutionSync = new();
    private readonly Queue<byte[]> _pendingTransportOutput = new();
    private readonly Queue<PendingTransportUiBatch> _pendingTransportUiBatches = new();
    private int _pendingTransportOutputBytes;
    private int _pendingTransportUiBatchBytes;
    private int _pendingTransportUiBatchChunks;
    private long _suppressTransportVtResponsesUntilTimestamp;
    private bool _pendingTransportOutputDrainScheduled;
    private bool _pendingTransportUiDrainScheduled;
    private bool _acceptPendingTransportOutput = true;
    private readonly List<SuspendedAncestorKeyBinding> _suspendedAncestorKeyBindings = [];
    private bool _reservedAncestorKeyBindingsSuppressed;

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
    /// Gets or sets keyboard shortcut bindings for clipboard/select-all actions.
    /// </summary>
    public TerminalShortcutConfiguration ShortcutConfiguration { get; set; } = TerminalShortcutConfiguration.Default;

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

    internal IVtProcessor? ActiveVtProcessor => _vtProcessor;

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
    public bool CanHorizontallyScroll
    {
        get => _canHScroll;
        set => _canHScroll = value;
    }

    /// <inheritdoc />
    public bool CanVerticallyScroll
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
        RegisterKeyboardFallbackHandlers();
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

        if (change.Property == FontFamilyNameProperty ||
            change.Property == FontSourceProperty ||
            change.Property == FontFilePathProperty ||
            change.Property == TerminalFontSizeProperty)
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
        string? fontFilePath = FontSource == TerminalFontSource.File
            ? FontFilePath
            : null;
        SkiaTerminalRenderer renderer = new(
            family,
            (float)TerminalFontSize,
            FontSource,
            fontFilePath);
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

        InvalidateScreen();
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

        bool gridChanged = safeColumns != _lastAppliedColumns || safeRows != _lastAppliedRows;
        bool pixelSizeChanged = widthPx != _lastAppliedWidthPx || heightPx != _lastAppliedHeightPx;

        if (!force && !gridChanged && !pixelSizeChanged)
        {
            return;
        }

        if (_screen is not null && (force || gridChanged || pixelSizeChanged))
        {
            lock (_screen.SyncRoot)
            {
                if (force || gridChanged)
                {
                    _screen.Resize(safeColumns, safeRows);
                }

                _vtProcessor?.NotifyResize(safeColumns, safeRows, widthPx, heightPx);
            }
        }

        if (force || gridChanged)
        {
            _scrollData?.UpdateExtent(_screen?.TotalRows ?? 0, AutoScroll);
        }

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

        if (force || gridChanged)
        {
            Endpoint?.SetSize(widthPx, heightPx);
            TerminalSessionService.ResizeSession(safeColumns, safeRows, widthPx, heightPx);
        }

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
        RestoreReservedAncestorKeyBindings();
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
            if (finalSize.Width < _renderer.CellWidth || finalSize.Height < _renderer.CellHeight)
            {
                // Avoid collapsing terminal state to a destructive 1x1 grid when a full cell
                // cannot be displayed during transient tiny layout bounds.
                _presenter?.NotifyResize(finalSize);
                return finalSize;
            }

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

        bool resetMouseSelection = ProcessOutputCore(data);
        if (resetMouseSelection)
        {
            ApplyMouseModeSelectionResetOnUiThread();
        }

        // Span input may come from transient memory, so copy to a managed payload for event consumers.
        DataReceived?.Invoke(this, new TerminalDataEventArgs(data.ToArray()));

        FinalizeOutputBatchOnUiThread();
    }

    private void WriteOutputOnUiThread(ReadOnlyMemory<byte> data, bool finalizeOutputBatch = true)
    {
        bool resetMouseSelection = ProcessOutputCore(data.Span);
        if (resetMouseSelection)
        {
            ApplyMouseModeSelectionResetOnUiThread();
        }

        // Raise event without copying when input is already managed memory.
        DataReceived?.Invoke(this, new TerminalDataEventArgs(data));

        if (!finalizeOutputBatch)
        {
            return;
        }

        FinalizeOutputBatchOnUiThread();
    }

    private void FinalizeOutputBatchOnUiThread()
    {
        if (_screen is null)
        {
            TerminalScrollService.HandleOutput(_scrollData, null, AutoScroll, _presenter, RaiseScrollInvalidated);
            UpdateRendererCursorForViewport();
            NotifyOutputUiUpdatedOnUiThread();
            return;
        }

        lock (_screen.SyncRoot)
        {
            if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
            {
                SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
            }
            else
            {
                TerminalScrollService.HandleOutput(
                    _scrollData,
                    _screen,
                    AutoScroll,
                    presenter: null,
                    raiseScrollInvalidated: static () => { });
            }

            UpdateRendererCursorForViewportLocked();
        }

        NotifyOutputUiUpdatedOnUiThread();
    }

    private bool ProcessOutputCore(ReadOnlySpan<byte> data)
    {
        if (_screen is null)
        {
            return false;
        }

        bool resetMouseSelection = false;
        // Lock screen during VT processing — composition thread reads cells concurrently
        lock (_screen.SyncRoot)
        {
            bool mouseModeChanged = _mouseModeTracker.Process(data);
            if (mouseModeChanged && IsMouseReportingActiveForInput())
            {
                resetMouseSelection = true;
            }

            int restoreScrollOffset = -1;
            if (!TryGetViewportScrollSource(out _) && _screen.ScrollOffset != 0)
            {
                // The managed VT processor writes through TerminalScreen.GetViewportRow.
                // Keep those writes anchored to the live terminal viewport, not the
                // user's scrollback viewport.
                restoreScrollOffset = _screen.ScrollOffset;
                _screen.ScrollOffset = 0;
            }

            try
            {
                _vtProcessor?.Process(data);
            }
            finally
            {
                if (restoreScrollOffset >= 0)
                {
                    _screen.ScrollOffset = restoreScrollOffset;
                }
            }

            TryUpdateHoveredLinkFromPointerLocked();
            UpdateRendererParityStateLocked();
        }

        return resetMouseSelection;
    }

    private void ApplyMouseModeSelectionResetOnUiThread()
    {
        if (_screen is null)
        {
            return;
        }

        ResetPointerButtons();
        _isMouseSelecting = false;
        TerminalSelectionService.ClearSelection(_screen, _renderer, _presenter);
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
        if (IsUrgentTransportControlInput(data))
        {
            ArmUrgentControlVtResponseSuppressionWindow();
        }

        TerminalSessionService.SendInput(data);
    }

    #endregion

    #region Keyboard Input

    private void RegisterKeyboardFallbackHandlers()
    {
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(TextInputEvent, OnTextInputTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (!e.Handled)
        {
            return;
        }

        HandleKeyDownCore(e);
    }

    private void OnKeyUpTunnel(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (!e.Handled)
        {
            return;
        }

        HandleKeyUpCore(e);
    }

    private void OnTextInputTunnel(object? sender, TextInputEventArgs e)
    {
        _ = sender;
        if (!e.Handled)
        {
            return;
        }

        HandleTextInputCore(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        HandleKeyDownCore(e);
        if (!e.Handled)
        {
            base.OnKeyDown(e);
        }
    }

    private void HandleKeyDownCore(KeyEventArgs e)
    {
        if (TryHandleUrgentTransportControlKeyDown(e))
        {
            return;
        }

        if (TerminalShortcutDispatcher.TryHandleCommonShortcut(
                e.Key,
                e.KeyModifiers,
                hasSelection: HasSelection,
                copyAction: () => _ = CopySelectionAsync(),
                pasteAction: () => _ = PasteAsync(),
                cutAction: () => _ = CutSelectionAsync(),
                selectAllAction: SelectAll,
                configuration: ShortcutConfiguration))
        {
            e.Handled = true;
            return;
        }

        if (TerminalInputAdapter.HandleKeyDown(e, TerminalSessionService, _vtProcessor))
        {
            e.Handled = true;
        }
    }

    private bool TryHandleUrgentTransportControlKeyDown(KeyEventArgs e)
    {
        if (!IsUrgentTransportControlChord(e.Key, e.KeyModifiers))
        {
            return false;
        }

        ArmUrgentControlVtResponseSuppressionWindow();

        if (!HasTransportOrDirectPtyInputPath() && TerminalSessionService.InputSink is null)
        {
            return false;
        }

        if (!TerminalInputAdapter.HandleKeyDown(e, TerminalSessionService, _vtProcessor))
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        HandleKeyUpCore(e);
        if (!e.Handled)
        {
            base.OnKeyUp(e);
        }
    }

    private void HandleKeyUpCore(KeyEventArgs e)
    {
        if (TerminalInputAdapter.HandleKeyUp(e, TerminalSessionService))
        {
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        HandleTextInputCore(e);
        if (!e.Handled)
        {
            base.OnTextInput(e);
        }
    }

    private void HandleTextInputCore(TextInputEventArgs e)
    {
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
            InvalidateScreen();
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
            InvalidateScreen();
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

        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            int deltaRows = e.Delta.Y > 0
                ? -3
                : e.Delta.Y < 0
                    ? 3
                    : 0;
            if (deltaRows != 0)
            {
                viewportScrollSource.ScrollViewportByRows(deltaRows);
                lock (_screen!.SyncRoot)
                {
                    SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
                    UpdateRendererCursorForViewportLocked();
                }

                _presenter?.Invalidate();
                RaiseScrollInvalidated();
            }

            e.Handled = true;
            return;
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

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        SuppressReservedAncestorKeyBindings();
        Endpoint?.SetFocus(true);
        SendFocusEventIfNeeded(focused: true);
        _cursorBlinkVisiblePhase = true;
        UpdateRendererCursorForViewport();
        _presenter?.Invalidate();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        RestoreReservedAncestorKeyBindings();
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
    /// Returns whether the active VT processor can export the requested snapshot format.
    /// </summary>
    public bool SupportsSnapshotFormat(TerminalSnapshotExportFormat format)
    {
        return _vtProcessor is ITerminalSnapshotExportSource snapshotExporter &&
               snapshotExporter.SupportsSnapshotFormat(format);
    }

    /// <summary>
    /// Attempts to export the active terminal snapshot through the current VT processor.
    /// </summary>
    public bool TryExportSnapshot(
        TerminalSnapshotExportFormat format,
        in TerminalSnapshotExportOptions options,
        out string snapshot)
    {
        if (_vtProcessor is not ITerminalSnapshotExportSource snapshotExporter ||
            !snapshotExporter.SupportsSnapshotFormat(format))
        {
            snapshot = string.Empty;
            return false;
        }

        return snapshotExporter.TryExportSnapshot(format, options, out snapshot);
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
    /// Copies selection text and clears selection state.
    /// </summary>
    public async Task CutSelectionAsync()
    {
        if (!HasSelection)
        {
            return;
        }

        await CopySelectionAsync();
        ClearSelection();
    }

    /// <summary>
    /// Selects all visible terminal content.
    /// </summary>
    public void SelectAll()
    {
        if (_renderer is null || _screen is null || _screen.Columns <= 0 || _screen.ViewportRows <= 0)
        {
            return;
        }

        _renderer.SelectionStart = (0, 0);
        _renderer.SelectionEnd = (_screen.Columns - 1, _screen.ViewportRows - 1);
        InvalidateScreen();
        _presenter?.Invalidate();
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
        InvalidateScreen();
        _presenter?.Invalidate(fullRedraw: true);
    }

    /// <summary>
    /// Scrolls the terminal by the given number of rows.
    /// </summary>
    public void ScrollByRows(int rows)
    {
        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            viewportScrollSource.ScrollViewportByRows(rows);
            if (_screen is not null)
            {
                lock (_screen.SyncRoot)
                {
                    SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
                }
            }

            _presenter?.Invalidate();
        }
        else
        {
            TerminalScrollService.ScrollByRows(rows, _scrollData, _screen, _presenter);
        }

        UpdateRendererCursorForViewport();
        UpdateRendererParityStateFromScreen();
    }

    /// <summary>
    /// Scrolls to the bottom of the terminal output.
    /// </summary>
    public void ScrollToBottom()
    {
        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            viewportScrollSource.ScrollViewportToBottom();
            if (_screen is not null)
            {
                lock (_screen.SyncRoot)
                {
                    SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
                }
            }

            _presenter?.Invalidate();
        }
        else
        {
            TerminalScrollService.ScrollToBottom(_scrollData, _screen, _presenter);
        }

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
        SetPendingTransportOutputAcceptance(acceptOutput: true);
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
    /// Starts a raw TCP transport-backed terminal session.
    /// </summary>
    public ValueTask StartRawTcpAsync(RawTcpTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Starts a Telnet transport-backed terminal session.
    /// </summary>
    public ValueTask StartTelnetAsync(TelnetTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Starts a serial transport-backed terminal session.
    /// </summary>
    public ValueTask StartSerialAsync(SerialTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Stops the PTY and kills the child shell process.
    /// </summary>
    public void StopPty()
    {
        SetPendingTransportOutputAcceptance(acceptOutput: false);
        TerminalSessionService.StopSessionAsync(_vtProcessor, OnPtyDataReceived, OnPtyProcessExited)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        if (Dispatcher.UIThread.CheckAccess())
        {
            DrainPendingTransportOutput(flushAll: true);
            DrainPendingTransportOutputUiBatches(flushAll: true);
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(() =>
                {
                    DrainPendingTransportOutput(flushAll: true);
                    DrainPendingTransportOutputUiBatches(flushAll: true);
                })
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
        if (data.Length == 0)
        {
            return;
        }

        // During sustained output floods (for example binary streams like /dev/urandom),
        // random bytes can accidentally form VT query sequences. Responding to every such
        // query can backpressure the PTY input path and delay user control bytes.
        if (ShouldSuppressVtProcessorResponse())
        {
            return;
        }

        TerminalSessionService.SendInput(data);
    }

    private bool ShouldSuppressVtProcessorResponse()
    {
        if (TerminalSessionService.Transport is null && TerminalSessionService.Pty is null)
        {
            return false;
        }

        if (IsUrgentControlVtResponseSuppressionWindowActive())
        {
            return true;
        }

        if (TerminalSessionService.Pty is null)
        {
            return false;
        }

        lock (_pendingTransportOutputSync)
        {
            return GetPendingTransportBacklogBytesLocked() >= ResumePendingOutputQueueBytes ||
                   GetPendingTransportBacklogChunksLocked() >= ResumePendingOutputQueueChunks;
        }
    }

    private void ArmUrgentControlVtResponseSuppressionWindow()
    {
        if (TerminalSessionService.Transport is null && TerminalSessionService.Pty is null)
        {
            return;
        }

        long suppressUntil = Stopwatch.GetTimestamp() + UrgentControlVtResponseSuppressionWindowTicks;
        Interlocked.Exchange(ref _suppressTransportVtResponsesUntilTimestamp, suppressUntil);
    }

    private bool IsUrgentControlVtResponseSuppressionWindowActive()
    {
        long suppressUntil = Volatile.Read(ref _suppressTransportVtResponsesUntilTimestamp);
        return suppressUntil > 0 && Stopwatch.GetTimestamp() < suppressUntil;
    }

    private static bool IsUrgentTransportControlChord(Key key, KeyModifiers modifiers)
    {
        if (!modifiers.HasFlag(KeyModifiers.Control))
        {
            return false;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt) || modifiers.HasFlag(KeyModifiers.Meta))
        {
            return false;
        }

        return key is Key.C or Key.Z or Key.OemBackslash;
    }

    private static bool IsUrgentTransportControlInput(ReadOnlySpan<byte> payload)
    {
        return payload.Length == 1 && payload[0] is 0x03 or 0x1A or 0x1C;
    }

    private void SuppressReservedAncestorKeyBindings()
    {
        if (_reservedAncestorKeyBindingsSuppressed)
        {
            return;
        }

        _suspendedAncestorKeyBindings.Clear();

        foreach (Visual visual in this.GetVisualAncestors())
        {
            if (visual is not InputElement inputElement)
            {
                continue;
            }

            IList<KeyBinding> keyBindings = inputElement.KeyBindings;
            for (int index = keyBindings.Count - 1; index >= 0; index--)
            {
                KeyBinding keyBinding = keyBindings[index];
                if (!IsReservedTerminalKeyBinding(keyBinding))
                {
                    continue;
                }

                _suspendedAncestorKeyBindings.Add(new SuspendedAncestorKeyBinding(inputElement, keyBinding, index));
                keyBindings.RemoveAt(index);
            }
        }

        _reservedAncestorKeyBindingsSuppressed = true;
    }

    private void RestoreReservedAncestorKeyBindings()
    {
        if (!_reservedAncestorKeyBindingsSuppressed)
        {
            return;
        }

        for (int i = _suspendedAncestorKeyBindings.Count - 1; i >= 0; i--)
        {
            SuspendedAncestorKeyBinding suspended = _suspendedAncestorKeyBindings[i];
            IList<KeyBinding> keyBindings = suspended.Owner.KeyBindings;
            if (keyBindings.Contains(suspended.Binding))
            {
                continue;
            }

            int restoreIndex = Math.Min(suspended.Index, keyBindings.Count);
            keyBindings.Insert(restoreIndex, suspended.Binding);
        }

        _suspendedAncestorKeyBindings.Clear();
        _reservedAncestorKeyBindingsSuppressed = false;
    }

    private bool IsReservedTerminalKeyBinding(KeyBinding keyBinding)
    {
        if (keyBinding.Gesture is not KeyGesture gesture)
        {
            return false;
        }

        return IsUrgentTransportControlChord(gesture.Key, gesture.KeyModifiers) ||
               IsConfiguredTerminalShortcutGesture(ShortcutConfiguration.CopyGestures, gesture) ||
               IsConfiguredTerminalShortcutGesture(ShortcutConfiguration.PasteGestures, gesture) ||
               IsConfiguredTerminalShortcutGesture(ShortcutConfiguration.CutGestures, gesture) ||
               IsConfiguredTerminalShortcutGesture(ShortcutConfiguration.SelectAllGestures, gesture);
    }

    private static bool IsConfiguredTerminalShortcutGesture(
        IReadOnlyList<TerminalShortcutGesture> gestures,
        KeyGesture gesture)
    {
        for (int i = 0; i < gestures.Count; i++)
        {
            TerminalShortcutGesture configured = gestures[i];
            if (configured.Key == gesture.Key && configured.Modifiers == gesture.KeyModifiers)
            {
                return true;
            }
        }

        return false;
    }

    private void OnVtProcessorBell()
    {
        Dispatcher.UIThread.Post(() => Bell?.Invoke(this, EventArgs.Empty));
    }

    private readonly record struct SuspendedAncestorKeyBinding(
        InputElement Owner,
        KeyBinding Binding,
        int Index);

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

        // The PTY reader reuses its read buffer, so we must copy before queueing
        // output for managed/background processing.
        // Split large PTY reads to bound per-dispatch parse work and UI finalization.
        int offset = 0;
        while (offset < length)
        {
            int chunkLength = Math.Min(MaxQueuedOutputChunkBytes, length - offset);
            byte[] copy = data.AsSpan(offset, chunkLength).ToArray();
            EnqueueOutputForUiThread(copy);
            offset += chunkLength;
        }
    }

    private void EnqueueOutputForUiThread(byte[] copy)
    {
        if (copy.Length == 0)
        {
            return;
        }

        bool scheduleDrain = false;
        bool canBlockForCapacity = !Dispatcher.UIThread.CheckAccess();
        lock (_pendingTransportOutputSync)
        {
            while (canBlockForCapacity &&
                   _acceptPendingTransportOutput &&
                   IsPendingTransportBacklogAtCapacityLocked())
            {
                Monitor.Wait(_pendingTransportOutputSync);
            }

            if (!_acceptPendingTransportOutput)
            {
                return;
            }

            _pendingTransportOutput.Enqueue(copy);
            _pendingTransportOutputBytes += copy.Length;
            if (!_pendingTransportOutputDrainScheduled)
            {
                _pendingTransportOutputDrainScheduled = true;
                scheduleDrain = true;
            }
        }

        if (scheduleDrain)
        {
            SchedulePendingTransportOutputDrain();
        }
    }

    private void OnPtyProcessExited(int exitCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DrainPendingTransportOutput(flushAll: true);
            DrainPendingTransportOutputUiBatches(flushAll: true);
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
        if (ShouldUseBackgroundManagedOutputPipeline())
        {
            DrainPendingTransportOutputInBackground(flushAll);
            return;
        }

        DrainPendingTransportOutputOnUiThread(flushAll);
    }

    private void DrainPendingTransportOutputOnUiThread(bool flushAll)
    {
        int processedChunks = 0;
        int processedBytes = 0;
        bool scheduleContinuation = false;
        List<byte[]>? pendingBatch = flushAll ? null : [];
        Stopwatch? dispatchStopwatch = flushAll ? null : Stopwatch.StartNew();

        while (true)
        {
            byte[] nextChunk;
            lock (_pendingTransportOutputSync)
            {
                if (_pendingTransportOutput.Count == 0)
                {
                    _pendingTransportOutputDrainScheduled = false;
                    Monitor.PulseAll(_pendingTransportOutputSync);
                    break;
                }

                if (!flushAll &&
                    processedChunks > 0 &&
                    dispatchStopwatch is not null &&
                    ShouldYieldPendingOutputDrain(
                        processedChunks,
                        processedBytes,
                        dispatchStopwatch.Elapsed))
                {
                    scheduleContinuation = true;
                    break;
                }

                nextChunk = _pendingTransportOutput.Dequeue();
                _pendingTransportOutputBytes = Math.Max(0, _pendingTransportOutputBytes - nextChunk.Length);
                if (_pendingTransportOutputBytes <= ResumePendingOutputQueueBytes &&
                    _pendingTransportOutput.Count <= ResumePendingOutputQueueChunks)
                {
                    Monitor.PulseAll(_pendingTransportOutputSync);
                }
            }

            if (flushAll)
            {
                WriteOutputOnUiThread(nextChunk, finalizeOutputBatch: false);
            }
            else
            {
                pendingBatch!.Add(nextChunk);
            }

            processedChunks++;
            processedBytes += nextChunk.Length;
        }

        if (!flushAll && processedChunks > 0)
        {
            WriteOutputBatchOnUiThread(pendingBatch!, processedBytes);
        }

        if (processedChunks > 0)
        {
            FinalizeOutputBatchOnUiThread();
        }

        if (scheduleContinuation)
        {
            SchedulePendingTransportOutputDrain();
        }
    }

    private static bool ShouldYieldPendingOutputDrain(
        int processedChunks,
        int processedBytes,
        TimeSpan elapsed)
    {
        if (processedChunks >= MaxPendingOutputChunksPerDispatch ||
            processedBytes >= MaxPendingOutputBytesPerDispatch)
        {
            return true;
        }

        return elapsed >= MaxPendingOutputDispatchDuration;
    }

    private void DrainPendingTransportOutputInBackground(bool flushAll)
    {
        lock (_pendingTransportOutputDrainExecutionSync)
        {
            while (TryDequeuePendingTransportOutputBatch(flushAll, out List<byte[]>? chunks, out int totalBytes))
            {
                PendingTransportUiBatch pendingBatch = ProcessPendingTransportOutputBatch(chunks!, totalBytes);
                EnqueuePendingTransportUiBatch(pendingBatch);

                if (!flushAll)
                {
                    SchedulePendingTransportOutputDrainIfNeeded();
                    return;
                }
            }
        }
    }

    private bool TryDequeuePendingTransportOutputBatch(
        bool flushAll,
        out List<byte[]>? chunks,
        out int totalBytes)
    {
        chunks = null;
        totalBytes = 0;

        lock (_pendingTransportOutputSync)
        {
            if (_pendingTransportOutput.Count == 0)
            {
                _pendingTransportOutputDrainScheduled = false;
                Monitor.PulseAll(_pendingTransportOutputSync);
                return false;
            }

            chunks = [];
            Stopwatch? batchStopwatch = flushAll ? null : Stopwatch.StartNew();
            while (_pendingTransportOutput.Count > 0)
            {
                if (!flushAll &&
                    chunks.Count > 0 &&
                    batchStopwatch is not null &&
                    ShouldYieldPendingOutputDrain(
                        chunks.Count,
                        totalBytes,
                        batchStopwatch.Elapsed))
                {
                    break;
                }

                byte[] nextChunk = _pendingTransportOutput.Dequeue();
                _pendingTransportOutputBytes = Math.Max(0, _pendingTransportOutputBytes - nextChunk.Length);
                chunks.Add(nextChunk);
                totalBytes += nextChunk.Length;
            }

            _pendingTransportOutputDrainScheduled = false;
            return chunks.Count > 0;
        }
    }

    private PendingTransportUiBatch ProcessPendingTransportOutputBatch(List<byte[]> chunks, int totalBytes)
    {
        if (chunks.Count == 1)
        {
            bool resetMouseSelection = ProcessOutputCore(chunks[0]);
            return new PendingTransportUiBatch(chunks, totalBytes, resetMouseSelection);
        }

        byte[] mergedBuffer = ArrayPool<byte>.Shared.Rent(totalBytes);
        int offset = 0;
        try
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                byte[] chunk = chunks[i];
                Buffer.BlockCopy(chunk, 0, mergedBuffer, offset, chunk.Length);
                offset += chunk.Length;
            }

            bool resetMouseSelection = ProcessOutputCore(mergedBuffer.AsSpan(0, totalBytes));
            return new PendingTransportUiBatch(chunks, totalBytes, resetMouseSelection);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mergedBuffer);
        }
    }

    private void EnqueuePendingTransportUiBatch(PendingTransportUiBatch batch)
    {
        bool scheduleUiDrain = false;
        lock (_pendingTransportOutputSync)
        {
            _pendingTransportUiBatches.Enqueue(batch);
            _pendingTransportUiBatchBytes += batch.TotalBytes;
            _pendingTransportUiBatchChunks += batch.ChunkCount;
            if (!_pendingTransportUiDrainScheduled)
            {
                _pendingTransportUiDrainScheduled = true;
                scheduleUiDrain = true;
            }
        }

        if (scheduleUiDrain)
        {
            Dispatcher.UIThread.Post(DrainPendingTransportOutputUiBatches, ManagedPendingOutputDrainPriority);
        }
    }

    private void DrainPendingTransportOutputUiBatches()
    {
        DrainPendingTransportOutputUiBatches(flushAll: false);
    }

    private void DrainPendingTransportOutputUiBatches(bool flushAll)
    {
        int processedChunks = 0;
        int processedBytes = 0;
        bool scheduleContinuation = false;
        bool mouseSelectionResetApplied = false;
        Stopwatch? dispatchStopwatch = flushAll ? null : Stopwatch.StartNew();

        while (true)
        {
            PendingTransportUiBatch nextBatch;
            lock (_pendingTransportOutputSync)
            {
                if (_pendingTransportUiBatches.Count == 0)
                {
                    _pendingTransportUiDrainScheduled = false;
                    Monitor.PulseAll(_pendingTransportOutputSync);
                    break;
                }

                if (!flushAll &&
                    processedChunks > 0 &&
                    dispatchStopwatch is not null &&
                    ShouldYieldPendingOutputDrain(
                        processedChunks,
                        processedBytes,
                        dispatchStopwatch.Elapsed))
                {
                    scheduleContinuation = true;
                    break;
                }

                nextBatch = _pendingTransportUiBatches.Dequeue();
                _pendingTransportUiBatchBytes = Math.Max(0, _pendingTransportUiBatchBytes - nextBatch.TotalBytes);
                _pendingTransportUiBatchChunks = Math.Max(0, _pendingTransportUiBatchChunks - nextBatch.ChunkCount);
                if (CanResumePendingTransportBacklogLocked())
                {
                    Monitor.PulseAll(_pendingTransportOutputSync);
                }
            }

            if (nextBatch.ResetMouseSelection && !mouseSelectionResetApplied)
            {
                ApplyMouseModeSelectionResetOnUiThread();
                mouseSelectionResetApplied = true;
            }

            for (int i = 0; i < nextBatch.Chunks.Count; i++)
            {
                DataReceived?.Invoke(this, new TerminalDataEventArgs(nextBatch.Chunks[i]));
            }

            processedChunks += nextBatch.ChunkCount;
            processedBytes += nextBatch.TotalBytes;
        }

        if (processedChunks > 0)
        {
            FinalizeOutputBatchOnUiThread();
        }

        if (scheduleContinuation)
        {
            Dispatcher.UIThread.Post(DrainPendingTransportOutputUiBatches, ManagedPendingOutputDrainPriority);
        }
    }

    private void WriteOutputBatchOnUiThread(List<byte[]> chunks, int totalBytes)
    {
        if (chunks.Count == 1)
        {
            WriteOutputOnUiThread(chunks[0], finalizeOutputBatch: false);
            return;
        }

        byte[] mergedBuffer = ArrayPool<byte>.Shared.Rent(totalBytes);
        int offset = 0;
        try
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                byte[] chunk = chunks[i];
                Buffer.BlockCopy(chunk, 0, mergedBuffer, offset, chunk.Length);
                offset += chunk.Length;
            }

            bool resetMouseSelection = ProcessOutputCore(mergedBuffer.AsSpan(0, totalBytes));
            if (resetMouseSelection)
            {
                ApplyMouseModeSelectionResetOnUiThread();
            }

            for (int i = 0; i < chunks.Count; i++)
            {
                DataReceived?.Invoke(this, new TerminalDataEventArgs(chunks[i]));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mergedBuffer);
        }
    }

    private void ResetPendingTransportOutputQueue()
    {
        lock (_pendingTransportOutputSync)
        {
            _pendingTransportOutput.Clear();
            _pendingTransportUiBatches.Clear();
            _pendingTransportOutputBytes = 0;
            _pendingTransportUiBatchBytes = 0;
            _pendingTransportUiBatchChunks = 0;
            _pendingTransportOutputDrainScheduled = false;
            _pendingTransportUiDrainScheduled = false;
            Monitor.PulseAll(_pendingTransportOutputSync);
        }
    }

    private void SetPendingTransportOutputAcceptance(bool acceptOutput)
    {
        lock (_pendingTransportOutputSync)
        {
            _acceptPendingTransportOutput = acceptOutput;
            Monitor.PulseAll(_pendingTransportOutputSync);
        }
    }

    private void SchedulePendingTransportOutputDrain()
    {
        if (ShouldUseBackgroundManagedOutputPipeline())
        {
            ThreadPool.UnsafeQueueUserWorkItem(
                static state => ((TerminalControl)state!).DrainPendingTransportOutput(),
                this);
            return;
        }

        Dispatcher.UIThread.Post(DrainPendingTransportOutput, NativePendingOutputDrainPriority);
    }

    private void SchedulePendingTransportOutputDrainIfNeeded()
    {
        bool scheduleDrain = false;
        lock (_pendingTransportOutputSync)
        {
            if (_pendingTransportOutput.Count == 0 || _pendingTransportOutputDrainScheduled)
            {
                return;
            }

            _pendingTransportOutputDrainScheduled = true;
            scheduleDrain = true;
        }

        if (scheduleDrain)
        {
            SchedulePendingTransportOutputDrain();
        }
    }

    private bool ShouldUseBackgroundManagedOutputPipeline()
    {
        return _appliedVtProcessorPreference == VtProcessorPreference.Managed ||
               _vtProcessor is BasicVtProcessor;
    }

    private int GetPendingTransportBacklogBytesLocked()
    {
        return _pendingTransportOutputBytes + _pendingTransportUiBatchBytes;
    }

    private int GetPendingTransportBacklogChunksLocked()
    {
        return _pendingTransportOutput.Count + _pendingTransportUiBatchChunks;
    }

    private bool IsPendingTransportBacklogAtCapacityLocked()
    {
        return GetPendingTransportBacklogBytesLocked() >= MaxPendingOutputQueueBytes ||
               GetPendingTransportBacklogChunksLocked() >= MaxPendingOutputQueueChunks;
    }

    private bool CanResumePendingTransportBacklogLocked()
    {
        return GetPendingTransportBacklogBytesLocked() <= ResumePendingOutputQueueBytes &&
               GetPendingTransportBacklogChunksLocked() <= ResumePendingOutputQueueChunks;
    }

    private void NotifyOutputUiUpdatedOnUiThread()
    {
        _presenter?.Invalidate();
        RaiseScrollInvalidated();
    }

    private sealed class PendingTransportUiBatch
    {
        public PendingTransportUiBatch(List<byte[]> chunks, int totalBytes, bool resetMouseSelection)
        {
            Chunks = chunks;
            TotalBytes = totalBytes;
            ResetMouseSelection = resetMouseSelection;
        }

        public List<byte[]> Chunks { get; }

        public int TotalBytes { get; }

        public bool ResetMouseSelection { get; }

        public int ChunkCount => Chunks.Count;
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
                new RawTcpTerminalTransportProvider(),
                new TelnetTerminalTransportProvider(),
                new SerialTerminalTransportProvider(),
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

        if (!HasTransportOrDirectPtyInputPath())
        {
            return false;
        }

        if (_vtProcessor is ITerminalPointerSequenceEncoderSource nativeEncoder &&
            _renderer is not null &&
            nativeEncoder.TryEncodePointer(
                pointerEvent,
                new TerminalPointerEncodingContext(
                    ScreenWidthPx: Math.Max(1, (int)Math.Round(Bounds.Width)),
                    ScreenHeightPx: Math.Max(1, (int)Math.Round(Bounds.Height)),
                    CellWidthPx: Math.Max(1, (int)Math.Ceiling(_renderer.CellWidth)),
                    CellHeightPx: Math.Max(1, (int)Math.Ceiling(_renderer.CellHeight))),
                out byte[] nativeEncoded))
        {
            TerminalSessionService.SendInput(nativeEncoded);
            return true;
        }

        if (!_mouseModeTracker.ModeState.IsMouseReportingEnabled ||
            !TryResolvePointerCell(pointerEvent.X, pointerEvent.Y, out int column, out int row))
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
        if (_vtProcessor is ITerminalMouseReportingStateSource nativeSource &&
            nativeSource.MouseReportingEnabled)
        {
            return TerminalSessionService.InputSink is not null ||
                HasTransportOrDirectPtyInputPath();
        }

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
        if (_vtProcessor is ITerminalSearchSource terminalSearchSource)
        {
            terminalSearchSource.PopulateSearchMatches(needle, _searchMatchScratch);
        }
        else
        {
            PopulateSearchMatchesFromScreenLocked(needle);
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

        int viewportTopAbsoluteRow = GetViewportTopAbsoluteRowLocked();
        for (int index = 0; index < _searchMatchScratch.Count; index++)
        {
            TerminalSearchMatch match = _searchMatchScratch[index];
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

    private void PopulateSearchMatchesFromScreenLocked(string needle)
    {
        if (_screen is null)
        {
            return;
        }

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
                    _searchMatchScratch.Add(new TerminalSearchMatch(absoluteRow, startColumn, endColumn));
                }

                searchFrom = found + Math.Max(needle.Length, 1);
            }
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

        ITerminalViewportScrollSource? viewportScrollSource = null;
        int nextTopAbsoluteRow = 0;
        bool changed = false;
        lock (_screen.SyncRoot)
        {
            if ((uint)_searchSelected >= (uint)_searchMatchScratch.Count)
            {
                return false;
            }

            int selectedAbsoluteRow = _searchMatchScratch[_searchSelected].AbsoluteRow;
            if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? nativeViewportScrollSource))
            {
                viewportScrollSource = nativeViewportScrollSource;
            }

            int viewportTopAbsoluteRow = GetViewportTopAbsoluteRowLocked(viewportScrollSource);
            int viewportBottomAbsoluteRow = viewportTopAbsoluteRow + _screen.ViewportRows - 1;
            if (selectedAbsoluteRow >= viewportTopAbsoluteRow && selectedAbsoluteRow <= viewportBottomAbsoluteRow)
            {
                return false;
            }

            if (viewportScrollSource is not null)
            {
                int maxTopAbsoluteRow = GetViewportMaxTopAbsoluteRow(viewportScrollSource.ViewportScrollState);
                nextTopAbsoluteRow = selectedAbsoluteRow < viewportTopAbsoluteRow
                    ? selectedAbsoluteRow
                    : selectedAbsoluteRow - _screen.ViewportRows + 1;
                nextTopAbsoluteRow = Math.Clamp(nextTopAbsoluteRow, 0, maxTopAbsoluteRow);
                changed = (ulong)nextTopAbsoluteRow != GetViewportTopAbsoluteRowUlong(viewportScrollSource.ViewportScrollState);
            }
            else
            {
                int maxTopAbsoluteRow = Math.Max(0, _screen.TotalRows - _screen.ViewportRows);
                nextTopAbsoluteRow = selectedAbsoluteRow < viewportTopAbsoluteRow
                    ? selectedAbsoluteRow
                    : selectedAbsoluteRow - _screen.ViewportRows + 1;
                nextTopAbsoluteRow = Math.Clamp(nextTopAbsoluteRow, 0, maxTopAbsoluteRow);
                int nextScrollOffset = _screen.TotalRows - _screen.ViewportRows - nextTopAbsoluteRow;
                nextScrollOffset = Math.Clamp(nextScrollOffset, 0, _screen.MaxScrollOffset);
                if (_screen.ScrollOffset != nextScrollOffset)
                {
                    _screen.ScrollOffset = nextScrollOffset;
                    _screen.InvalidateViewport();
                    changed = true;
                }
            }
        }

        if (!changed)
        {
            return false;
        }

        if (viewportScrollSource is not null)
        {
            viewportScrollSource.SetViewportOffsetRows((ulong)nextTopAbsoluteRow);
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

    private void InvalidateScreen()
    {
        if (_screen is null)
        {
            return;
        }

        lock (_screen.SyncRoot)
        {
            _screen.InvalidateAll();
        }
    }

    private void SyncScreenScrollOffsetFromScrollData()
    {
        if (_scrollData is null || _screen is null)
        {
            return;
        }

        lock (_screen.SyncRoot)
        {
            if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
            {
                SyncNativeViewportFromScrollDataLocked(viewportScrollSource);
                return;
            }

            int nextOffset = _scrollData.ToScreenScrollOffsetRows(_screen.MaxScrollOffset);
            if (_screen.ScrollOffset == nextOffset)
            {
                return;
            }

            _screen.ScrollOffset = nextOffset;
            _screen.InvalidateViewport();
        }
    }

    private bool TryGetViewportScrollSource([NotNullWhen(true)] out ITerminalViewportScrollSource? viewportScrollSource)
    {
        viewportScrollSource = _vtProcessor as ITerminalViewportScrollSource;
        return viewportScrollSource is not null && _scrollData is not null && _screen is not null;
    }

    private int GetViewportTopAbsoluteRowLocked(ITerminalViewportScrollSource? viewportScrollSource = null)
    {
        if (_screen is null)
        {
            return 0;
        }

        if (viewportScrollSource is null && !TryGetViewportScrollSource(out viewportScrollSource))
        {
            return Math.Max(0, _screen.TotalRows - _screen.ViewportRows - _screen.ScrollOffset);
        }

        ulong topAbsoluteRow = GetViewportTopAbsoluteRowUlong(viewportScrollSource.ViewportScrollState);
        return topAbsoluteRow > int.MaxValue
            ? int.MaxValue
            : (int)topAbsoluteRow;
    }

    private static ulong GetViewportTopAbsoluteRowUlong(TerminalViewportScrollState viewportState)
    {
        return Math.Min(viewportState.OffsetRows, viewportState.MaxOffsetRows);
    }

    private static int GetViewportMaxTopAbsoluteRow(TerminalViewportScrollState viewportState)
    {
        ulong maxTopAbsoluteRow = viewportState.MaxOffsetRows;
        return maxTopAbsoluteRow > int.MaxValue
            ? int.MaxValue
            : (int)maxTopAbsoluteRow;
    }

    private void SyncScrollDataFromNativeViewportLocked(ITerminalViewportScrollSource viewportScrollSource)
    {
        if (_scrollData is null || _screen is null)
        {
            return;
        }

        TerminalViewportScrollState viewportState = viewportScrollSource.ViewportScrollState;
        double cellHeight = Math.Max(1d, _scrollData.CellHeight);
        int totalRows = viewportState.TotalRows > int.MaxValue
            ? int.MaxValue
            : (int)viewportState.TotalRows;
        _scrollData.Extent = totalRows * cellHeight;

        ulong clampedOffsetRows = Math.Min(viewportState.OffsetRows, viewportState.MaxOffsetRows);
        double targetOffset = clampedOffsetRows * cellHeight;
        if (targetOffset > _scrollData.MaxOffset)
        {
            targetOffset = _scrollData.MaxOffset;
        }

        _scrollData.Offset = targetOffset;

        if (_screen.ScrollOffset != 0)
        {
            _screen.ScrollOffset = 0;
            _screen.InvalidateViewport();
        }
    }

    private void SyncNativeViewportFromScrollDataLocked(ITerminalViewportScrollSource viewportScrollSource)
    {
        if (_scrollData is null || _screen is null)
        {
            return;
        }

        TerminalViewportScrollState viewportState = viewportScrollSource.ViewportScrollState;
        ulong maxOffsetRows = viewportState.MaxOffsetRows;
        ulong targetOffsetRows;
        if (_scrollData.MaxOffset <= 0 || maxOffsetRows == 0)
        {
            targetOffsetRows = 0;
        }
        else
        {
            double normalizedOffset = _scrollData.Offset / _scrollData.MaxOffset;
            normalizedOffset = Math.Clamp(normalizedOffset, 0d, 1d);
            targetOffsetRows = (ulong)Math.Round(normalizedOffset * maxOffsetRows, MidpointRounding.AwayFromZero);
        }

        if (targetOffsetRows == viewportState.OffsetRows)
        {
            if (_screen.ScrollOffset != 0)
            {
                _screen.ScrollOffset = 0;
                _screen.InvalidateViewport();
            }

            return;
        }

        viewportScrollSource.SetViewportOffsetRows(targetOffsetRows);
        SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
    }

    private void SyncScrollDataFromScreenOffset()
    {
        if (_scrollData is null || _screen is null)
        {
            return;
        }

        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            lock (_screen.SyncRoot)
            {
                SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
            }

            return;
        }

        int screenMaxOffsetRows;
        int scrollOffset;
        lock (_screen.SyncRoot)
        {
            screenMaxOffsetRows = _screen.MaxScrollOffset;
            scrollOffset = _screen.ScrollOffset;
        }

        if (screenMaxOffsetRows <= 0 || _scrollData.MaxOffset <= 0)
        {
            _scrollData.Offset = _scrollData.MaxOffset;
            return;
        }

        int topAnchoredRows = screenMaxOffsetRows - scrollOffset;
        topAnchoredRows = Math.Clamp(topAnchoredRows, 0, screenMaxOffsetRows);
        double scaledOffset = (_scrollData.MaxOffset * topAnchoredRows) / screenMaxOffsetRows;
        _scrollData.Offset = scaledOffset;
    }

    private void UpdateRendererCursorForViewport()
    {
        if (_screen is null)
        {
            return;
        }

        lock (_screen.SyncRoot)
        {
            UpdateRendererCursorForViewportLocked();
        }
    }

    private void UpdateRendererCursorForViewportLocked()
    {
        if (_renderer is null || _screen is null || _vtProcessor is null)
        {
            return;
        }

        int cursorColumn = _vtProcessor.CursorCol;
        int cursorRow = _vtProcessor.CursorRow;
        bool atLiveBottom;
        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            atLiveBottom = viewportScrollSource.ViewportScrollState.OffsetRows >= viewportScrollSource.ViewportScrollState.MaxOffsetRows;
        }
        else
        {
            cursorRow += _screen.ScrollOffset;
            atLiveBottom = _screen.ScrollOffset == 0;
        }

        _renderer.CursorColumn = cursorColumn;
        _renderer.CursorRow = cursorRow;
        bool blinkEnabled = UpdateRendererCursorStyleFromVtProcessor();

        bool rowVisible = (uint)cursorRow < (uint)_screen.ViewportRows;
        bool columnVisible = (uint)cursorColumn < (uint)_screen.Columns;
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
