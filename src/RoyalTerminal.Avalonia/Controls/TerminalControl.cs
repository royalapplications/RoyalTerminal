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
    private int _lastAppliedColumns = -1;
    private int _lastAppliedRows = -1;
    private int _lastAppliedWidthPx = -1;
    private int _lastAppliedHeightPx = -1;
    private VtProcessorPreference _appliedVtProcessorPreference = VtProcessorPreference.Auto;
    private string? _activeTransportId;
    private readonly TerminalMouseModeTracker _mouseModeTracker = new();

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

        TerminalScrollService.HandleOutput(_scrollData, _screen, AutoScroll, _presenter, RaiseScrollInvalidated);
        UpdateRendererCursorForViewport();
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
        var props = e.GetCurrentPoint(this).Properties;
        TerminalMouseButton button = ResolvePressedMouseButton(props);
        if (button == TerminalMouseButton.None &&
            IsPrimaryPointer(e.Pointer.Type))
        {
            // Some platform input paths (notably certain macOS touchpad flows)
            // can surface pressed events without explicit left-button metadata.
            button = TerminalMouseButton.Left;
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
        UpdateRendererCursorForViewport();
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
    }

    /// <summary>
    /// Scrolls to the bottom of the terminal output.
    /// </summary>
    public void ScrollToBottom()
    {
        TerminalScrollService.ScrollToBottom(_scrollData, _screen, _presenter);
        UpdateRendererCursorForViewport();
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
        _mouseModeTracker.Reset();
        ResetPointerButtons();
        _isMouseSelecting = false;

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
        _mouseModeTracker.Reset();
        ResetPointerButtons();
        _isMouseSelecting = false;
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

    private bool HasTransportOrDirectPtyInputPath()
    {
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

        bool rowVisible = (uint)cursorRow < (uint)_screen.ViewportRows;
        bool columnVisible = (uint)cursorColumn < (uint)_screen.Columns;
        _renderer.CursorVisible = _vtProcessor.CursorVisible && rowVisible && columnVisible;
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
