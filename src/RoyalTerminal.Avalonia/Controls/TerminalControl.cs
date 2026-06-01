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
using RoyalTerminal.Shaders;
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
    private const int MaxQueuedOutputChunkBytes = 4096;
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
    private const double SelectionAutoScrollMargin = 60d;
    private const int SelectionAutoScrollSpeed = 50;
    private const ulong ComparableRowFnvOffsetBasis = 14695981039346656037UL;
    private const ulong ComparableRowFnvPrime = 1099511628211UL;
    private const int ComparableRowStackCharLimit = 256;
    // ConPTY can emit repaint fragments for every intermediate drag width.
    // Keep local reflow immediate, but only send the settled PTY size downstream.
    private static readonly TimeSpan WindowsPtyTransportResizeDebounceInterval = TimeSpan.FromMilliseconds(75);
    // Managed VT parsing already yields in small UI batches, so draining at
    // Background priority avoids starvation without monopolizing the UI thread.
    private static readonly DispatcherPriority ManagedPendingOutputDrainPriority = DispatcherPriority.Background;
    private static readonly DispatcherPriority NativePendingOutputDrainPriority = DispatcherPriority.Background;
    private static readonly TimeSpan SelectionAutoScrollInterval = TimeSpan.FromMilliseconds(SelectionAutoScrollSpeed);
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

    /// <summary>Whether terminal text uses subpixel glyph positioning.</summary>
    public static readonly StyledProperty<bool> FontSubpixelPositioningProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(
            nameof(FontSubpixelPositioning),
            TerminalFontRenderingSettings.Default.SubpixelPositioning);

    /// <summary>Terminal text edge rendering mode.</summary>
    public static readonly StyledProperty<TerminalFontEdging> FontEdgingProperty =
        AvaloniaProperty.Register<TerminalControl, TerminalFontEdging>(
            nameof(FontEdging),
            TerminalFontRenderingSettings.Default.Edging);

    /// <summary>Terminal text font hinting mode.</summary>
    public static readonly StyledProperty<TerminalFontHinting> FontHintingProperty =
        AvaloniaProperty.Register<TerminalControl, TerminalFontHinting>(
            nameof(FontHinting),
            TerminalFontRenderingSettings.Default.Hinting);

    /// <summary>Whether terminal text glyphs snap to pixel boundaries on the baseline.</summary>
    public static readonly StyledProperty<bool> FontBaselineSnapProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(
            nameof(FontBaselineSnap),
            TerminalFontRenderingSettings.Default.BaselineSnap);

    /// <summary>Whether terminal text uses embedded bitmap strikes when available.</summary>
    public static readonly StyledProperty<bool> FontEmbeddedBitmapsProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(
            nameof(FontEmbeddedBitmaps),
            TerminalFontRenderingSettings.Default.EmbeddedBitmaps);

    /// <summary>Whether terminal text glyphs are algorithmically emboldened.</summary>
    public static readonly StyledProperty<bool> FontEmboldenProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(
            nameof(FontEmbolden),
            TerminalFontRenderingSettings.Default.Embolden);

    /// <summary>Whether terminal text forces auto-hinting.</summary>
    public static readonly StyledProperty<bool> FontForceAutoHintingProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(
            nameof(FontForceAutoHinting),
            TerminalFontRenderingSettings.Default.ForceAutoHinting);

    /// <summary>Whether terminal text metrics ignore hinting for improved precision.</summary>
    public static readonly StyledProperty<bool> FontLinearMetricsProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(
            nameof(FontLinearMetrics),
            TerminalFontRenderingSettings.Default.LinearMetrics);

    /// <summary>Number of columns in the terminal grid.</summary>
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<TerminalControl, int>(nameof(Columns), 80);

    /// <summary>Number of rows in the terminal viewport.</summary>
    public static readonly StyledProperty<int> RowsProperty =
        AvaloniaProperty.Register<TerminalControl, int>(nameof(Rows), 24);

    /// <summary>Maximum number of scrollback rows.</summary>
    public static readonly StyledProperty<int> ScrollbackLimitProperty =
        AvaloniaProperty.Register<TerminalControl, int>(
            nameof(ScrollbackLimit),
            10_000,
            coerce: static (_, value) => Math.Max(0, value));

    /// <summary>
    /// Whether starting a new session preserves the previous session's scrollback history.
    /// </summary>
    public static readonly StyledProperty<bool> PreserveScrollbackOnSessionStartProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(
            nameof(PreserveScrollbackOnSessionStart),
            false);

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
    /// Whether terminal keyboard input scrolls the normal screen buffer back to the live bottom.
    /// </summary>
    public static readonly DirectProperty<TerminalControl, bool> ScrollToBottomOnInputProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, bool>(
            nameof(ScrollToBottomOnInput),
            o => o.ScrollToBottomOnInput,
            (o, v) => o.ScrollToBottomOnInput = v);

    private bool _scrollToBottomOnInput = true;

    /// <summary>Whether buffered terminal rows reflow when the terminal width changes.</summary>
    public static readonly StyledProperty<bool> ReflowOnResizeProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(ReflowOnResize), true);

    /// <summary>Whether managed VT sixel image decoding is enabled.</summary>
    public static readonly StyledProperty<bool> SixelGraphicsEnabledProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(SixelGraphicsEnabled), false);

    /// <summary>Text rendering pipeline used by the Skia renderer.</summary>
    public static readonly StyledProperty<TerminalTextRenderPipeline> TextRenderPipelineProperty =
        AvaloniaProperty.Register<TerminalControl, TerminalTextRenderPipeline>(
            nameof(TextRenderPipeline),
            TerminalTextRenderPipeline.HarfBuzz);

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

    /// <summary>
    /// Configured terminal framebuffer shader sources.
    /// </summary>
    public static readonly DirectProperty<TerminalControl, IReadOnlyList<TerminalShaderSource>?> ShaderSourcesProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, IReadOnlyList<TerminalShaderSource>?>(
            nameof(ShaderSources),
            o => o.ShaderSources,
            (o, v) => o.ShaderSources = v);

    private IReadOnlyList<TerminalShaderSource>? _shaderSources;

    /// <summary>
    /// Regex-based foreground/background text highlight rules.
    /// </summary>
    public static readonly DirectProperty<TerminalControl, IReadOnlyList<TerminalTextHighlightRule>?> TextHighlightRulesProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, IReadOnlyList<TerminalTextHighlightRule>?>(
            nameof(TextHighlightRules),
            o => o.TextHighlightRules,
            (o, v) => o.TextHighlightRules = v);

    private IReadOnlyList<TerminalTextHighlightRule>? _textHighlightRules;

    /// <summary>
    /// Regex-based text highlighting evaluation mode.
    /// </summary>
    public static readonly DirectProperty<TerminalControl, TerminalTextHighlightingMode> TextHighlightingModeProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, TerminalTextHighlightingMode>(
            nameof(TextHighlightingMode),
            o => o.TextHighlightingMode,
            (o, v) => o.TextHighlightingMode = v);

    private TerminalTextHighlightingMode _textHighlightingMode = TerminalTextHighlightingMode.Static;

    /// <summary>
    /// Whether configured framebuffer shaders may request continuous animation frames.
    /// </summary>
    public static readonly DirectProperty<TerminalControl, bool> ShaderAnimationEnabledProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, bool>(
            nameof(ShaderAnimationEnabled),
            o => o.ShaderAnimationEnabled,
            (o, v) => o.ShaderAnimationEnabled = v);

    private bool _shaderAnimationEnabled = true;

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

    /// <summary>Gets or sets whether terminal text uses subpixel glyph positioning.</summary>
    public bool FontSubpixelPositioning
    {
        get => GetValue(FontSubpixelPositioningProperty);
        set => SetValue(FontSubpixelPositioningProperty, value);
    }

    /// <summary>Gets or sets terminal text edge rendering mode.</summary>
    public TerminalFontEdging FontEdging
    {
        get => GetValue(FontEdgingProperty);
        set => SetValue(FontEdgingProperty, value);
    }

    /// <summary>Gets or sets terminal text font hinting mode.</summary>
    public TerminalFontHinting FontHinting
    {
        get => GetValue(FontHintingProperty);
        set => SetValue(FontHintingProperty, value);
    }

    /// <summary>Gets or sets whether terminal text glyphs snap to pixel boundaries on the baseline.</summary>
    public bool FontBaselineSnap
    {
        get => GetValue(FontBaselineSnapProperty);
        set => SetValue(FontBaselineSnapProperty, value);
    }

    /// <summary>Gets or sets whether terminal text uses embedded bitmap strikes when available.</summary>
    public bool FontEmbeddedBitmaps
    {
        get => GetValue(FontEmbeddedBitmapsProperty);
        set => SetValue(FontEmbeddedBitmapsProperty, value);
    }

    /// <summary>Gets or sets whether terminal text glyphs are algorithmically emboldened.</summary>
    public bool FontEmbolden
    {
        get => GetValue(FontEmboldenProperty);
        set => SetValue(FontEmboldenProperty, value);
    }

    /// <summary>Gets or sets whether terminal text forces auto-hinting.</summary>
    public bool FontForceAutoHinting
    {
        get => GetValue(FontForceAutoHintingProperty);
        set => SetValue(FontForceAutoHintingProperty, value);
    }

    /// <summary>Gets or sets whether terminal text metrics ignore hinting for improved precision.</summary>
    public bool FontLinearMetrics
    {
        get => GetValue(FontLinearMetricsProperty);
        set => SetValue(FontLinearMetricsProperty, value);
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

    /// <summary>
    /// Gets or sets whether <see cref="StartSessionAsync(ITerminalTransportOptions, CancellationToken)"/>
    /// preserves scrollback from the previous completed session.
    /// </summary>
    public bool PreserveScrollbackOnSessionStart
    {
        get => GetValue(PreserveScrollbackOnSessionStartProperty);
        set => SetValue(PreserveScrollbackOnSessionStartProperty, value);
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
    /// Gets or sets whether accepted terminal keyboard input scrolls the normal screen buffer back to the live bottom.
    /// </summary>
    public bool ScrollToBottomOnInput
    {
        get => _scrollToBottomOnInput;
        set => SetAndRaise(ScrollToBottomOnInputProperty, ref _scrollToBottomOnInput, value);
    }

    /// <summary>Gets or sets whether buffered terminal rows reflow when the terminal width changes.</summary>
    public bool ReflowOnResize
    {
        get => GetValue(ReflowOnResizeProperty);
        set => SetValue(ReflowOnResizeProperty, value);
    }

    /// <summary>Gets or sets whether managed VT sixel image decoding is enabled.</summary>
    public bool SixelGraphicsEnabled
    {
        get => GetValue(SixelGraphicsEnabledProperty);
        set => SetValue(SixelGraphicsEnabledProperty, value);
    }

    /// <summary>Gets or sets the text rendering pipeline used by the Skia renderer.</summary>
    public TerminalTextRenderPipeline TextRenderPipeline
    {
        get => GetValue(TextRenderPipelineProperty);
        set => SetValue(TextRenderPipelineProperty, value);
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

    /// <summary>
    /// Gets or sets terminal framebuffer shader sources.
    /// </summary>
    public IReadOnlyList<TerminalShaderSource>? ShaderSources
    {
        get => _shaderSources;
        set
        {
            IReadOnlyList<TerminalShaderSource>? next = NormalizeShaderSources(value);
            if (ReferenceEquals(_shaderSources, next))
            {
                return;
            }

            SetAndRaise(ShaderSourcesProperty, ref _shaderSources, next);
            UpdatePresenterShaderState();
        }
    }

    /// <summary>
    /// Gets or sets regex-based foreground/background text highlight rules.
    /// </summary>
    public IReadOnlyList<TerminalTextHighlightRule>? TextHighlightRules
    {
        get => _textHighlightRules;
        set
        {
            IReadOnlyList<TerminalTextHighlightRule>? next = NormalizeTextHighlightRules(value);
            if (AreTextHighlightRulesEqual(_textHighlightRules, next))
            {
                return;
            }

            SetAndRaise(TextHighlightRulesProperty, ref _textHighlightRules, next);
            ApplyTextHighlightRules();
        }
    }

    /// <summary>
    /// Gets or sets regex-based text highlighting evaluation mode.
    /// </summary>
    public TerminalTextHighlightingMode TextHighlightingMode
    {
        get => _textHighlightingMode;
        set
        {
            TerminalTextHighlightingMode next = NormalizeTextHighlightingMode(value);
            if (_textHighlightingMode == next)
            {
                return;
            }

            SetAndRaise(TextHighlightingModeProperty, ref _textHighlightingMode, next);
            ApplyTextHighlightingMode();
        }
    }

    /// <summary>
    /// Gets or sets whether configured shaders can animate without terminal output.
    /// </summary>
    public bool ShaderAnimationEnabled
    {
        get => _shaderAnimationEnabled;
        set
        {
            if (_shaderAnimationEnabled == value)
            {
                return;
            }

            SetAndRaise(ShaderAnimationEnabledProperty, ref _shaderAnimationEnabled, value);
            UpdatePresenterShaderState();
        }
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
    private bool _autoScrollPinnedToBottom = true;
    private bool _isMouseSelecting;
    private DispatcherTimer? _selectionAutoScrollTimer;
    private int _selectionAnchorColumn;
    private int _selectionAnchorAbsoluteRow;
    private int _selectionActiveColumn;
    private int _selectionActiveAbsoluteRow;
    private bool _hasAnchoredSelection;
    private TerminalHighlightSpan[] _selectionAnchorSpans = Array.Empty<TerminalHighlightSpan>();
    private TerminalHighlightSpan[]? _selectionViewportSpansSource;
    private SkiaTerminalRenderer? _selectionViewportSpansRenderer;
    private int _selectionViewportSpansTopRow = int.MinValue;
    private Point _lastSelectionPointerPoint;
    private KeyModifiers _lastSelectionKeyModifiers;
    private bool _leftPointerDown;
    private bool _middlePointerDown;
    private bool _rightPointerDown;
    private bool _pointerInputStartedInContent;
    private bool _suppressGridPropertyApply;
    private bool _suppressLegacyColorThemeBridge;
    private int _lastPointerColumn = -1;
    private int _lastPointerRow = -1;
    private bool _backgroundOpacityEnabled;
    private string? _hoveredLinkUrl;
    private string? _searchNeedle;
    private int _searchTotal;
    private int _searchSelected = -1;
    private const int InitialRowTextScratchCapacity = 256;
    private readonly List<TerminalHighlightSpan> _highlightSpanScratch = [];
    private readonly List<TerminalSearchMatch> _searchMatchScratch = [];
    private char[] _rowTextScratch = Array.Empty<char>();
    private readonly StringBuilder _linkTokenScratch = new();
    private int[] _rowColumnMapScratch = Array.Empty<int>();
    private DispatcherTimer? _cursorBlinkTimer;
    private bool _cursorBlinkVisiblePhase = true;
    private int _lastBlinkCursorColumn = -1;
    private int _lastBlinkCursorRow = -1;
    private CursorStyle _lastBlinkCursorStyle = CursorStyle.Block;
    private int _lastAppliedColumns = -1;
    private int _lastAppliedRows = -1;
    private int _lastAppliedWidthPx = -1;
    private int _lastAppliedHeightPx = -1;
    private bool _preserveNativeViewportBottomOnNextResize;
    private TerminalSessionDimensions? _pendingTransportResize;
    private double _lastAppliedLayoutWidth = double.NaN;
    private double _lastAppliedLayoutHeight = double.NaN;
    private TopLevel? _scalingTopLevel;
    private double _contentScaleX = 1d;
    private double _contentScaleY = 1d;
    private VtProcessorPreference _appliedVtProcessorPreference = VtProcessorPreference.Auto;
    private string? _activeTransportId;
    private int _transportSessionGeneration;
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
    private DispatcherTimer? _transportResizeDebounceTimer;
    private readonly List<SuspendedAncestorKeyBinding> _suspendedAncestorKeyBindings = [];
    private bool _reservedAncestorKeyBindingsSuppressed;
    private bool _suppressNextScrollbackEscapeKeyUp;

    private enum SelectionResizeAnchorSpace
    {
        Absolute,
        NativeViewport,
    }

    private sealed class NativeSelectionResizeContext
    {
        public NativeSelectionResizeContext(
            TerminalScreen snapshot,
            TerminalGridPosition[] anchors,
            bool anchorsAreSpans)
        {
            Snapshot = snapshot;
            Anchors = anchors;
            AnchorsAreSpans = anchorsAreSpans;
        }

        public TerminalScreen Snapshot { get; }

        public TerminalGridPosition[] Anchors { get; }

        public bool AnchorsAreSpans { get; }
    }

    private readonly record struct ComparableRowKey(int TextLength, ulong Hash);

    private readonly record struct NativeSelectionOffsetScore(int Matches, long Score);

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
         ((_renderer.SelectionStart is not null &&
           _renderer.SelectionEnd is not null) ||
          !_renderer.GetSelectionSpans().IsEmpty));

    #region ILogicalScrollable

    private bool _canHScroll;
    private bool _canVScroll = true;
    private EventHandler? _scrollInvalidated;
    private bool _syncingAncestorScrollViewerOffset;
    private bool _ancestorScrollViewerOffsetSyncPending;
    private bool _preserveAutoScrollBottomDuringAncestorOffsetSync;
    private ScrollViewer? _containingScrollViewer;
    private bool _suppressNextWindowsPtyResizeRepaint;

    /// <inheritdoc />
    bool ILogicalScrollable.IsLogicalScrollEnabled => true;

    /// <inheritdoc />
    Size ILogicalScrollable.ScrollSize =>
        new(10, _scrollData?.CellHeight ?? 16);

    /// <inheritdoc />
    Size ILogicalScrollable.PageScrollSize =>
        GetTerminalContentSize(Bounds.Size);

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
        new(GetTerminalContentSize(Bounds.Size).Width, _scrollData?.Extent ?? GetTerminalContentSize(Bounds.Size).Height);

    /// <inheritdoc />
    Vector IScrollable.Offset
    {
        get => new(0, _scrollData?.Offset ?? 0);
        set
        {
            if (_syncingAncestorScrollViewerOffset)
            {
                return;
            }

            if (_scrollData is not null)
            {
                if (_preserveAutoScrollBottomDuringAncestorOffsetSync &&
                    _autoScrollPinnedToBottom &&
                    AutoScroll &&
                    value.Y < _scrollData.MaxOffset - 1)
                {
                    _scrollData.ScrollToBottom();
                    SyncScreenScrollOffsetFromScrollData();
                    UpdateRendererCursorForViewport();
                    UpdateRendererParityStateFromScreen();
                    _presenter?.Invalidate();
                    RaiseScrollInvalidated();
                    return;
                }

                CaptureRendererSelectionForCurrentViewport();
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
        new(GetTerminalContentSize(Bounds.Size).Width, _scrollData?.Viewport ?? GetTerminalContentSize(Bounds.Size).Height);

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
        RaiseScrollInvalidated(e);

    private void RaiseScrollInvalidated() =>
        RaiseScrollInvalidated(EventArgs.Empty);

    private void RaiseScrollInvalidated(EventArgs e)
    {
        _scrollInvalidated?.Invoke(this, e);
        RequestAncestorScrollViewerOffsetSync();
    }

    private void RequestAncestorScrollViewerOffsetSync()
    {
        if (_scrollData is null)
        {
            return;
        }

        if (FindContainingScrollViewer() is null)
        {
            _preserveAutoScrollBottomDuringAncestorOffsetSync = false;
            return;
        }

        SynchronizeAncestorScrollViewerOffset();
        if (_ancestorScrollViewerOffsetSyncPending)
        {
            return;
        }

        _ancestorScrollViewerOffsetSyncPending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _ancestorScrollViewerOffsetSyncPending = false;
                SynchronizeAncestorScrollViewerOffset();
                _preserveAutoScrollBottomDuringAncestorOffsetSync = false;
            },
            DispatcherPriority.Loaded);
    }

    private void SynchronizeAncestorScrollViewerOffset()
    {
        if (_scrollData is null || _syncingAncestorScrollViewerOffset)
        {
            return;
        }

        ScrollViewer? containingScrollViewer = FindContainingScrollViewer();
        if (containingScrollViewer is null)
        {
            return;
        }

        Vector currentOffset = containingScrollViewer.Offset;
        double targetOffsetY = _scrollData.Offset;
        if (AreClose(currentOffset.Y, targetOffsetY))
        {
            return;
        }

        _syncingAncestorScrollViewerOffset = true;
        try
        {
            containingScrollViewer.Offset = new Vector(currentOffset.X, targetOffsetY);
        }
        finally
        {
            _syncingAncestorScrollViewerOffset = false;
        }
    }

    private ScrollViewer? FindContainingScrollViewer()
    {
        if (_containingScrollViewer is { Content: var cachedContent } cachedScrollViewer &&
            ReferenceEquals(cachedContent, this))
        {
            return cachedScrollViewer;
        }

        foreach (Visual ancestor in this.GetVisualAncestors())
        {
            if (ancestor is ScrollViewer { Content: var content } scrollViewer &&
                ReferenceEquals(content, this))
            {
                _containingScrollViewer = scrollViewer;
                return scrollViewer;
            }
        }

        _containingScrollViewer = null;
        return null;
    }

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
        ApplySixelGraphicsSettingToProcessor(_vtProcessor);
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
            change.Property == TerminalFontSizeProperty ||
            change.Property == FontSubpixelPositioningProperty ||
            change.Property == FontEdgingProperty ||
            change.Property == FontHintingProperty ||
            change.Property == FontBaselineSnapProperty ||
            change.Property == FontEmbeddedBitmapsProperty ||
            change.Property == FontEmboldenProperty ||
            change.Property == FontForceAutoHintingProperty ||
            change.Property == FontLinearMetricsProperty)
        {
            ApplyFontSettings();
            return;
        }

        if (change.Property == VtProcessorPreferenceProperty)
        {
            ApplyVtProcessorPreference();
            return;
        }

        if (change.Property == SixelGraphicsEnabledProperty)
        {
            ApplySixelGraphicsSetting();
            return;
        }

        if (change.Property == ScrollbackLimitProperty)
        {
            ApplyScrollbackLimit();
            return;
        }

        if (change.Property == TextRenderPipelineProperty)
        {
            ApplyTextRenderPipeline();
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

        if (change.Property == PaddingProperty)
        {
            ApplyPaddingSettings();
            return;
        }

        if (change.Property == AutoScrollProperty)
        {
            ApplyAutoScrollSetting();
        }
    }

    private void ApplyPaddingSettings()
    {
        InvalidateMeasure();
        InvalidateArrange();
        _presenter?.Invalidate(fullRedraw: true);
        RaiseScrollInvalidated();
    }

    private void ApplyFontSettings()
    {
        if (_screen is null)
        {
            return;
        }

        bool wasAtBottom = _scrollData is { CanScroll: true, IsAtBottom: true };
        _preserveNativeViewportBottomOnNextResize |= wasAtBottom && AutoScroll;
        SkiaTerminalRenderer nextRenderer = CreateRenderer(_renderer);
        _renderer = nextRenderer;

        if (_scrollData is not null)
        {
            _scrollData.CellHeight = nextRenderer.CellHeight;
            _scrollData.Viewport = GetLogicalViewportHeight(Rows, nextRenderer.CellHeight);
        }

        if (_scrollData is not null)
        {
            _scrollViewer?.UpdateViewport(_scrollData.Viewport, nextRenderer.CellHeight);

            lock (_screen.SyncRoot)
            {
                if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
                {
                    SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
                    UpdateRendererCursorForViewportLocked();
                }
                else
                {
                    _scrollData.UpdateExtent(_screen.TotalRows, AutoScroll);
                    int nextOffset = _scrollData.ToScreenScrollOffsetRows(_screen.MaxScrollOffset);
                    if (_screen.ScrollOffset != nextOffset)
                    {
                        _screen.ScrollOffset = nextOffset;
                        _screen.InvalidateViewport();
                    }

                    UpdateRendererCursorForViewportLocked();
                }
            }
        }

        lock (_screen.SyncRoot)
        {
            _screen.InvalidateAll();
            UpdateRendererParityStateLocked();
        }

        _presenter?.SetRenderState(nextRenderer, _screen);
        _presenter?.NotifyResize(Bounds.Size);
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
            fontFilePath,
            CreateFontRenderingSettings());
        renderer.BackgroundOpacityEnabled = _backgroundOpacityEnabled;
        renderer.BackgroundOpacityCells = RendererBackgroundOpacityCells;
        renderer.BackgroundOpacity = RendererBackgroundOpacity;
        renderer.TextHighlightingMode = GetEffectiveTextHighlightingMode();
        renderer.SetTextHighlightRules(_textHighlightRules);
        renderer.TextRenderPipeline = TextRenderPipeline;

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
        renderer.SelectionIsRectangle = previous.SelectionIsRectangle;
        renderer.SetSelectionSpans(previous.GetSelectionSpans());
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

    private TerminalFontRenderingSettings CreateFontRenderingSettings()
    {
        return new TerminalFontRenderingSettings
        {
            SubpixelPositioning = FontSubpixelPositioning,
            Edging = FontEdging,
            Hinting = FontHinting,
            BaselineSnap = FontBaselineSnap,
            EmbeddedBitmaps = FontEmbeddedBitmaps,
            Embolden = FontEmbolden,
            ForceAutoHinting = FontForceAutoHinting,
            LinearMetrics = FontLinearMetrics,
        }.Normalize();
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

        RefreshPresenterRenderState(fullRedraw: true);
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
        RefreshPresenterRenderState(fullRedraw: true);
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

    private void ApplyScrollbackLimit()
    {
        if (_screen is null)
        {
            return;
        }

        lock (_screen.SyncRoot)
        {
            _screen.ScrollbackLimit = ScrollbackLimit;
        }

        // libghostty-vt accepts max_scrollback only at terminal creation,
        // so idle processors must be recreated to pick up the new limit.
        if (!TerminalSessionService.HasActiveTransport)
        {
            EnsureVtProcessorPreferenceApplied(force: true);
        }

        if (_scrollData is not null)
        {
            lock (_screen.SyncRoot)
            {
                if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
                {
                    viewportScrollSource.SetViewportOffsetRows(viewportScrollSource.ViewportScrollState.OffsetRows);
                    SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
                }
                else
                {
                    _scrollData.UpdateExtent(_screen.TotalRows, AutoScroll);
                    int nextOffset = _scrollData.ToScreenScrollOffsetRows(_screen.MaxScrollOffset);
                    if (_screen.ScrollOffset != nextOffset)
                    {
                        _screen.ScrollOffset = nextOffset;
                        _screen.InvalidateViewport();
                    }
                }

                UpdateRendererCursorForViewportLocked();
                UpdateRendererParityStateLocked();
            }
        }

        _presenter?.Invalidate();
        RaiseScrollInvalidated();
    }

    private void ApplyTextRenderPipeline()
    {
        if (_renderer is null)
        {
            return;
        }

        _renderer.TextRenderPipeline = TextRenderPipeline;
        if (_screen is not null)
        {
            lock (_screen.SyncRoot)
            {
                _screen.InvalidateAll();
            }
        }

        _presenter?.Invalidate();
    }

    private void ApplyVtProcessorPreference()
    {
        if (_screen is null || TerminalSessionService.HasActiveTransport)
        {
            return;
        }

        EnsureVtProcessorPreferenceApplied();
    }

    private void EnsureVtProcessorPreferenceApplied(bool force = false)
    {
        if (_screen is null)
        {
            return;
        }

        if (!force && _vtProcessor is not null && _appliedVtProcessorPreference == VtProcessorPreference)
        {
            return;
        }

        IVtProcessor nextProcessor = VtProcessorFactory.Create(_screen, VtProcessorPreference);
        ApplySixelGraphicsSettingToProcessor(nextProcessor);
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

    private void ApplySixelGraphicsSetting()
    {
        ApplySixelGraphicsSettingToProcessor(_vtProcessor);
        _presenter?.Invalidate();
    }

    private void ApplySixelGraphicsSettingToProcessor(IVtProcessor? processor)
    {
        if (processor is ITerminalSixelOptionsSink sixelOptions)
        {
            sixelOptions.SixelGraphicsEnabled = SixelGraphicsEnabled;
        }
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
        Size contentSize = GetTerminalContentSize(Bounds.Size);
        ApplyTerminalSize(Columns, Rows, contentSize.Width, contentSize.Height, raiseTerminalResized: true, invalidateMeasure: true);
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

        (int widthPx, int heightPx) = CalculateRenderedGridPixelSize(safeColumns, safeRows);
        double layoutWidth = width > 0 ? width : widthPx;
        double layoutHeight = height > 0 ? height : heightPx;

        bool gridChanged = safeColumns != _lastAppliedColumns || safeRows != _lastAppliedRows;
        bool pixelSizeChanged = widthPx != _lastAppliedWidthPx || heightPx != _lastAppliedHeightPx;
        bool layoutSizeChanged =
            !AreClose(layoutWidth, _lastAppliedLayoutWidth) ||
            !AreClose(layoutHeight, _lastAppliedLayoutHeight);

        if (!force && !gridChanged && !pixelSizeChanged && !layoutSizeChanged)
        {
            return;
        }

        if (force || gridChanged)
        {
            FlushPendingTransportOutputBeforeResize();
        }

        bool hasActiveSelection = _hasAnchoredSelection || HasRendererSelection();
        bool hasNativeViewportScrollSource = TryGetViewportScrollSource(out _);
        bool suppressManagedBottomPreservationForSelection = hasActiveSelection && !hasNativeViewportScrollSource;
        int previousViewportRows = _screen?.ViewportRows ?? _lastAppliedRows;
        bool rowsDecreased = previousViewportRows > 0 && safeRows < previousViewportRows;
        bool screenAtLiveBottom = _screen is null || _screen.ScrollOffset == 0;
        bool wasAtBottom = !suppressManagedBottomPreservationForSelection &&
            (_autoScrollPinnedToBottom ||
             screenAtLiveBottom ||
             IsScrollDataAtLiveBottom(treatNonScrollableAsBottom: rowsDecreased));
        bool preserveNativeViewportBottom = AutoScroll && (wasAtBottom || _preserveNativeViewportBottomOnNextResize);
        bool windowsPtyNormalBufferResize = ShouldPreserveWindowsPtyViewportTopOnResize();
        bool columnsChangedForWindowsPty = _lastAppliedColumns > 0 && safeColumns != _lastAppliedColumns;
        bool preserveWindowsPtyViewportTop = windowsPtyNormalBufferResize && columnsChangedForWindowsPty;
        if ((force || gridChanged) && windowsPtyNormalBufferResize)
        {
            _suppressNextWindowsPtyResizeRepaint = true;
        }
        NativeSelectionResizeContext? nativeSelectionResizeContext = null;
        if (_screen is not null && (force || gridChanged || pixelSizeChanged))
        {
            lock (_screen.SyncRoot)
            {
                TerminalGridPosition[]? selectionResizeAnchors = null;
                bool selectionResizeAnchorsAreSpans = false;
                SelectionResizeAnchorSpace selectionResizeAnchorSpace = SelectionResizeAnchorSpace.Absolute;
                NativeSelectionResizeContext? selectionResizeContext = null;
                if (force || gridChanged)
                {
                    CaptureRendererSelectionForCurrentViewportLocked();
                    if (_vtProcessor is BasicVtProcessor || !TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
                    {
                        selectionResizeAnchors = CreateSelectionResizeAnchorsLocked(out selectionResizeAnchorsAreSpans);
                    }
                    else
                    {
                        selectionResizeContext = CreateSelectionNativeViewportResizeContextLocked(
                            safeColumns,
                            safeRows,
                            ReflowOnResize && _vtProcessor?.AlternateScreen != true,
                            GetViewportTopAbsoluteRowLocked(viewportScrollSource));
                        selectionResizeAnchors = selectionResizeContext?.Anchors;
                        selectionResizeAnchorsAreSpans = selectionResizeContext?.AnchorsAreSpans == true;
                        selectionResizeAnchorSpace = SelectionResizeAnchorSpace.NativeViewport;
                    }
                }

                if (_vtProcessor is BasicVtProcessor basicVtProcessor)
                {
                    if (force || gridChanged)
                    {
                        bool managedReflowOnResize = ShouldUseLocalResizeReflow();
                        basicVtProcessor.ResizeScreen(
                            safeColumns,
                            safeRows,
                            widthPx,
                            heightPx,
                            managedReflowOnResize,
                            selectionResizeAnchors.AsSpan(),
                            preserveWindowsPtyViewportTop);
                    }
                    else
                    {
                        basicVtProcessor.NotifyResize(safeColumns, safeRows, widthPx, heightPx);
                    }
                }
                else
                {
                    if (force || gridChanged)
                    {
                        bool resizeMirrorWithReflow = ShouldUseLocalResizeReflow() &&
                            _vtProcessor?.AlternateScreen != true &&
                            selectionResizeAnchorSpace != SelectionResizeAnchorSpace.NativeViewport;
                        // Native processors own scrollback reflow. Keep the mirror dimensions current,
                        // but use a compact viewport snapshot for selection anchors and fallback paint.
                        _screen.Resize(
                            safeColumns,
                            safeRows,
                            resizeMirrorWithReflow,
                            trackedViewportPosition: null,
                            selectionResizeAnchorSpace == SelectionResizeAnchorSpace.NativeViewport
                                ? Span<TerminalGridPosition>.Empty
                                : selectionResizeAnchors.AsSpan(),
                            preserveWindowsPtyViewportTop);
                        if (selectionResizeContext is not null)
                        {
                            CopyViewportRows(selectionResizeContext.Snapshot, _screen);
                        }
                    }

                    ApplyResizeReflowPolicyToProcessor(ShouldUseLocalResizeReflow());
                    _vtProcessor?.NotifyResize(safeColumns, safeRows, widthPx, heightPx);
                }

                if (selectionResizeAnchorSpace == SelectionResizeAnchorSpace.NativeViewport)
                {
                    ApplyNativeSelectionResizeContextLocked(selectionResizeContext);
                    nativeSelectionResizeContext = selectionResizeContext;
                }
                else
                {
                    ApplySelectionResizeAnchorsLocked(selectionResizeAnchors, selectionResizeAnchorsAreSpans);
                }
            }
        }

        bool alternateScreenActive = _vtProcessor?.AlternateScreen == true;

        if (_scrollData is not null)
        {
            _scrollData.Viewport = GetLogicalViewportHeight(safeRows, _renderer.CellHeight);
            _scrollViewer?.UpdateViewport(_scrollData.Viewport, _renderer.CellHeight);
            if (force || gridChanged)
            {
                if (!TryGetViewportScrollSource(out _))
                {
                    _scrollData.UpdateExtent(GetScrollableRowCount(), preserveNativeViewportBottom || alternateScreenActive);
                }
            }

            if (alternateScreenActive)
            {
                SyncAlternateScreenScrollState();
                _preserveNativeViewportBottomOnNextResize = false;
            }
            else if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
            {
                if (preserveNativeViewportBottom)
                {
                    viewportScrollSource.ScrollViewportToBottom();
                }

                lock (_screen!.SyncRoot)
                {
                    SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
                    ApplyNativeSelectionResizeContextLocked(nativeSelectionResizeContext);
                }

                _preserveNativeViewportBottomOnNextResize = false;
            }
            else
            {
                if (preserveNativeViewportBottom || alternateScreenActive)
                {
                    _scrollData.ScrollToBottom();
                    if (preserveNativeViewportBottom)
                    {
                        _preserveAutoScrollBottomDuringAncestorOffsetSync = true;
                    }
                }

                SyncScreenScrollOffsetFromScrollData();
                _preserveNativeViewportBottomOnNextResize = false;
            }

            UpdateRendererCursorForViewport();
            UpdateRendererParityStateFromScreen();
            UpdateAutoScrollPinnedToBottom();
        }

        if (force || gridChanged)
        {
            Endpoint?.SetSize(widthPx, heightPx);
            ResizeTransportSession(safeColumns, safeRows, widthPx, heightPx);
        }

        _lastAppliedColumns = safeColumns;
        _lastAppliedRows = safeRows;
        _lastAppliedWidthPx = widthPx;
        _lastAppliedHeightPx = heightPx;
        _lastAppliedLayoutWidth = layoutWidth;
        _lastAppliedLayoutHeight = layoutHeight;

        RaiseScrollInvalidated();
        if (raiseTerminalResized && gridChanged)
        {
            TerminalResized?.Invoke(this, new TerminalSizeEventArgs(safeColumns, safeRows));
        }

        _presenter?.NotifyResize(new Size(layoutWidth, layoutHeight));
        _presenter?.Invalidate();

        if (invalidateMeasure)
        {
            InvalidateMeasure();
        }
    }

    private static bool AreClose(double left, double right)
    {
        return double.IsFinite(left) &&
               double.IsFinite(right) &&
               Math.Abs(left - right) < 0.001;
    }

    private bool ShouldUseLocalResizeReflow()
    {
        return ReflowOnResize;
    }

    private bool ShouldPreserveWindowsPtyViewportTopOnResize()
    {
        return OperatingSystem.IsWindows() &&
               _vtProcessor?.AlternateScreen != true &&
               TerminalSessionService.HasActiveTransport &&
               string.Equals(_activeTransportId, TerminalTransportIds.Pty, StringComparison.Ordinal);
    }

    private bool ShouldDebounceWindowsPtyTransportResize()
    {
        return OperatingSystem.IsWindows() &&
               TerminalSessionService.HasActiveTransport &&
               string.Equals(_activeTransportId, TerminalTransportIds.Pty, StringComparison.Ordinal);
    }

    private void ResizeTransportSession(int columns, int rows, int widthPixels, int heightPixels)
    {
        TerminalSessionDimensions dimensions = new(columns, rows, widthPixels, heightPixels);
        if (!ShouldDebounceWindowsPtyTransportResize())
        {
            CancelPendingTransportResize();
            TerminalSessionService.ResizeSession(columns, rows, widthPixels, heightPixels);
            return;
        }

        _pendingTransportResize = dimensions;
        EnsureTransportResizeDebounceTimer().Stop();
        _transportResizeDebounceTimer!.Start();
    }

    private DispatcherTimer EnsureTransportResizeDebounceTimer()
    {
        if (_transportResizeDebounceTimer is not null)
        {
            return _transportResizeDebounceTimer;
        }

        DispatcherTimer timer = new(DispatcherPriority.Background)
        {
            Interval = WindowsPtyTransportResizeDebounceInterval,
        };
        timer.Tick += OnTransportResizeDebounceTimerTick;
        _transportResizeDebounceTimer = timer;
        return timer;
    }

    private void OnTransportResizeDebounceTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        FlushPendingTransportResize();
    }

    private void FlushPendingTransportResize()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.InvokeAsync(FlushPendingTransportResize).GetAwaiter().GetResult();
            return;
        }

        _transportResizeDebounceTimer?.Stop();
        if (_pendingTransportResize is not { } dimensions)
        {
            return;
        }

        _pendingTransportResize = null;
        TerminalSessionService.ResizeSession(
            dimensions.Columns,
            dimensions.Rows,
            dimensions.WidthPixels,
            dimensions.HeightPixels);
    }

    private void CancelPendingTransportResize()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.InvokeAsync(CancelPendingTransportResize).GetAwaiter().GetResult();
            return;
        }

        _transportResizeDebounceTimer?.Stop();
        _pendingTransportResize = null;
    }

    private void ApplyResizeReflowPolicyToProcessor(bool localReflowOnResize)
    {
        if (_vtProcessor is ITerminalResizeReflowPolicySink reflowPolicySink)
        {
            reflowPolicySink.LocalReflowOnResize = localReflowOnResize;
        }
    }

    private (int WidthPx, int HeightPx) CalculateRenderedGridPixelSize(int columns, int rows)
    {
        if (_renderer is null)
        {
            return (Math.Max(1, columns), Math.Max(1, rows));
        }

        int safeColumns = Math.Max(1, columns);
        int safeRows = Math.Max(1, rows);
        int widthPx = Math.Max(1, (int)Math.Round(safeColumns * _renderer.CellWidth));
        int heightPx = Math.Max(1, (int)Math.Round(safeRows * _renderer.CellHeight));
        return (widthPx, heightPx);
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

        _containingScrollViewer = null;
        AttachTopLevelScaling();

        // TemplatedControl without a template never fires OnApplyTemplate.
        // Create the presenter here as a fallback so rendering always works.
        EnsurePresenter();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _containingScrollViewer = null;
        CancelPendingTransportResize();
        StopMouseSelectionDrag();
        EnsureCursorBlinkTimerRunning(false);
        RestoreReservedAncestorKeyBindings();
        DetachTopLevelScaling();
    }

    private void EnsurePresenter()
    {
        if (_presenter is not null)
        {
            _presenter.IsHitTestVisible = true;
            if (_renderer is not null && _screen is not null)
            {
                _presenter.SetRenderState(_renderer, _screen);
            }

            UpdatePresenterShaderState();
            return;
        }

        _presenter = new TerminalPresenter();
        _presenter.IsHitTestVisible = true;
        ((ISetLogicalParent)_presenter).SetParent(this);
        VisualChildren.Add(_presenter);

        if (_renderer is not null && _screen is not null)
        {
            _presenter.SetRenderState(_renderer, _screen);
        }

        UpdatePresenterShaderState();
    }

    private void UpdatePresenterShaderState()
    {
        _presenter?.SetShaderState(
            _shaderSources,
            _shaderAnimationEnabled);
        _presenter?.Invalidate(fullRedraw: true);
    }

    private static IReadOnlyList<TerminalShaderSource>? NormalizeShaderSources(
        IReadOnlyList<TerminalShaderSource>? sources)
    {
        if (sources is null || sources.Count == 0)
        {
            return null;
        }

        TerminalShaderSource[] copy = new TerminalShaderSource[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            copy[i] = sources[i] ?? throw new ArgumentException(
                "Shader source collection cannot contain null entries.",
                nameof(sources));
        }

        return copy;
    }

    private static IReadOnlyList<TerminalTextHighlightRule>? NormalizeTextHighlightRules(
        IReadOnlyList<TerminalTextHighlightRule>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            return null;
        }

        TerminalTextHighlightRule[] copy = new TerminalTextHighlightRule[rules.Count];
        for (int i = 0; i < rules.Count; i++)
        {
            copy[i] = rules[i] ?? throw new ArgumentException(
                "Text highlight rule collection cannot contain null entries.",
                nameof(rules));
        }

        return copy;
    }

    private static TerminalTextHighlightingMode NormalizeTextHighlightingMode(TerminalTextHighlightingMode mode)
    {
        return Enum.IsDefined(mode)
            ? mode
            : TerminalTextHighlightingMode.Static;
    }

    private static bool AreTextHighlightRulesEqual(
        IReadOnlyList<TerminalTextHighlightRule>? left,
        IReadOnlyList<TerminalTextHighlightRule>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        int leftCount = left?.Count ?? 0;
        int rightCount = right?.Count ?? 0;
        if (leftCount != rightCount)
        {
            return false;
        }

        for (int i = 0; i < leftCount; i++)
        {
            if (!left![i].Equals(right![i]))
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyTextHighlightRules()
    {
        if (_renderer is not null)
        {
            _renderer.SetTextHighlightRules(_textHighlightRules);
        }

        if (_screen is not null)
        {
            lock (_screen.SyncRoot)
            {
                _screen.InvalidateAll();
            }
        }

        _presenter?.Invalidate(fullRedraw: true);
    }

    private void ApplyTextHighlightingMode()
    {
        if (_renderer is not null)
        {
            _renderer.TextHighlightingMode = GetEffectiveTextHighlightingMode();
        }

        if (_screen is not null)
        {
            lock (_screen.SyncRoot)
            {
                _screen.InvalidateAll();
            }
        }

        _presenter?.Invalidate(fullRedraw: true);
    }

    private void RefreshPresenterRenderState(bool fullRedraw)
    {
        if (_presenter is null)
        {
            return;
        }

        if (_renderer is not null && _screen is not null)
        {
            _presenter.SetRenderState(_renderer, _screen);
            return;
        }

        _presenter.Invalidate(fullRedraw);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_renderer is null)
            return base.MeasureOverride(availableSize);

        Thickness padding = GetEffectivePadding();

        // Calculate desired size based on columns/rows
        double desiredWidth = Columns * _renderer.CellWidth + padding.Left + padding.Right;
        double desiredHeight = Rows * _renderer.CellHeight + padding.Top + padding.Bottom;

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
        Rect contentRect = GetTerminalContentRect(finalSize);

        if (_presenter is not null)
        {
            _presenter.Arrange(contentRect);
        }

        // Recalculate grid dimensions based on actual size
        if (_renderer is not null && _renderer.CellWidth > 0 && _renderer.CellHeight > 0)
        {
            if (contentRect.Width < _renderer.CellWidth || contentRect.Height < _renderer.CellHeight)
            {
                // Avoid collapsing terminal state to a destructive 1x1 grid when a full cell
                // cannot be displayed during transient tiny layout bounds.
                _presenter?.NotifyResize(contentRect.Size);
                return finalSize;
            }

            int newCols = Math.Max(1, (int)(contentRect.Width / _renderer.CellWidth));
            int newRows = Math.Max(1, (int)(contentRect.Height / _renderer.CellHeight));

            if (newCols != Columns || newRows != Rows)
            {
                ApplyGridFromLayout(newCols, newRows, contentRect.Size);
            }
            else
            {
                ApplyTerminalSize(newCols, newRows, contentRect.Width, contentRect.Height, raiseTerminalResized: false, invalidateMeasure: false);
            }
        }

        return finalSize;
    }

    private Rect GetTerminalContentRect(Size availableSize)
    {
        Thickness padding = GetEffectivePadding();
        double width = Math.Max(0d, availableSize.Width - padding.Left - padding.Right);
        double height = Math.Max(0d, availableSize.Height - padding.Top - padding.Bottom);
        return new Rect(padding.Left, padding.Top, width, height);
    }

    private Size GetTerminalContentSize(Size availableSize)
    {
        return GetTerminalContentRect(availableSize).Size;
    }

    private Thickness GetEffectivePadding()
    {
        Thickness padding = Padding;
        return new Thickness(
            NormalizePaddingValue(padding.Left),
            NormalizePaddingValue(padding.Top),
            NormalizePaddingValue(padding.Right),
            NormalizePaddingValue(padding.Bottom));
    }

    private static double NormalizePaddingValue(double value)
    {
        return double.IsFinite(value) && value > 0d ? value : 0d;
    }

    private bool TryTranslatePointToTerminalContent(Point controlPoint, out Point contentPoint)
    {
        Rect contentRect = GetTerminalContentRect(Bounds.Size);
        if (contentRect.Width <= 0d || contentRect.Height <= 0d ||
            controlPoint.X < contentRect.X ||
            controlPoint.Y < contentRect.Y ||
            controlPoint.X >= contentRect.Right ||
            controlPoint.Y >= contentRect.Bottom)
        {
            contentPoint = default;
            return false;
        }

        contentPoint = new Point(controlPoint.X - contentRect.X, controlPoint.Y - contentRect.Y);
        return true;
    }

    private Point ClampPointToTerminalContent(Point controlPoint)
    {
        Rect contentRect = GetTerminalContentRect(Bounds.Size);
        double x = Math.Clamp(controlPoint.X - contentRect.X, 0d, GetMaxContentCoordinate(contentRect.Width));
        double y = Math.Clamp(controlPoint.Y - contentRect.Y, 0d, GetMaxContentCoordinate(contentRect.Height));
        return new Point(x, y);
    }

    private static double GetMaxContentCoordinate(double length)
    {
        return length > 0d ? Math.BitDecrement(length) : 0d;
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
        ApplyContentScaleToEndpoint(endpoint);

        if (_renderer is not null)
        {
            (int widthPx, int heightPx) = CalculateRenderedGridPixelSize(Columns, Rows);
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
            if (_vtProcessor?.AlternateScreen == true)
            {
                SyncAlternateScreenScrollStateLocked();
            }
            else if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
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
            ApplyAnchoredSelectionToRendererLocked();
        }

        UpdateAutoScrollPinnedToBottom();
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
            ulong? restoreViewportOffsetRows = null;
            if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
            {
                TerminalViewportScrollState viewportState = viewportScrollSource.ViewportScrollState;
                ulong clampedOffsetRows = Math.Min(viewportState.OffsetRows, viewportState.MaxOffsetRows);
                if (_vtProcessor?.AlternateScreen != true && clampedOffsetRows < viewportState.MaxOffsetRows)
                {
                    restoreViewportOffsetRows = clampedOffsetRows;
                }
            }
            else if (_screen.ScrollOffset != 0)
            {
                // The managed VT processor writes through TerminalScreen.GetViewportRow.
                // Keep those writes anchored to the live terminal viewport, not the
                // user's scrollback viewport.
                restoreScrollOffset = _screen.ScrollOffset;
                _screen.ScrollOffset = 0;
            }

            try
            {
                if (TrySuppressWindowsPtyResizeRepaint(data))
                {
                    return resetMouseSelection;
                }

                _vtProcessor?.Process(data);
            }
            finally
            {
                if (restoreScrollOffset >= 0)
                {
                    _screen.ScrollOffset = restoreScrollOffset;
                }

                if (restoreViewportOffsetRows is { } offsetRows && viewportScrollSource is not null)
                {
                    viewportScrollSource.SetViewportOffsetRows(offsetRows);
                }
            }

            TryUpdateHoveredLinkFromPointerLocked();
            UpdateRendererParityStateLocked();
        }

        return resetMouseSelection;
    }

    private bool ProcessOutputBatchCore(IReadOnlyList<byte[]> chunks)
    {
        if (_screen is null)
        {
            return false;
        }

        bool resetMouseSelection = false;
        lock (_screen.SyncRoot)
        {
            int restoreScrollOffset = -1;
            ulong? restoreViewportOffsetRows = null;
            if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
            {
                TerminalViewportScrollState viewportState = viewportScrollSource.ViewportScrollState;
                ulong clampedOffsetRows = Math.Min(viewportState.OffsetRows, viewportState.MaxOffsetRows);
                if (_vtProcessor?.AlternateScreen != true && clampedOffsetRows < viewportState.MaxOffsetRows)
                {
                    restoreViewportOffsetRows = clampedOffsetRows;
                }
            }
            else if (_screen.ScrollOffset != 0)
            {
                restoreScrollOffset = _screen.ScrollOffset;
                _screen.ScrollOffset = 0;
            }

            try
            {
                for (int i = 0; i < chunks.Count; i++)
                {
                    byte[] chunk = chunks[i];
                    bool mouseModeChanged = _mouseModeTracker.Process(chunk);
                    if (mouseModeChanged && IsMouseReportingActiveForInput())
                    {
                        resetMouseSelection = true;
                    }

                    if (TrySuppressWindowsPtyResizeRepaint(chunk))
                    {
                        continue;
                    }

                    _vtProcessor?.Process(chunk);
                }
            }
            finally
            {
                if (restoreScrollOffset >= 0)
                {
                    _screen.ScrollOffset = restoreScrollOffset;
                }

                if (restoreViewportOffsetRows is { } offsetRows && viewportScrollSource is not null)
                {
                    viewportScrollSource.SetViewportOffsetRows(offsetRows);
                }
            }

            TryUpdateHoveredLinkFromPointerLocked();
            UpdateRendererParityStateLocked();
        }

        return resetMouseSelection;
    }

    private bool TrySuppressWindowsPtyResizeRepaint(ReadOnlySpan<byte> data)
    {
        if (!_suppressNextWindowsPtyResizeRepaint)
        {
            return false;
        }

        _suppressNextWindowsPtyResizeRepaint = false;

        if (_vtProcessor?.AlternateScreen == true ||
            !LooksLikeWindowsPtyResizeRepaint(data))
        {
            return false;
        }

        // ConPTY repaints the current viewport after a resize. During a
        // height-only resize that repaint contains only the temporarily visible
        // rows, so accepting it as VT output drops older rows from our local
        // managed buffer.
        _screen?.InvalidateViewport();
        return true;
    }

    private static bool LooksLikeWindowsPtyResizeRepaint(ReadOnlySpan<byte> data)
    {
        int index = 0;

        _ = TryConsumeCsiLiteral(data, ref index, "?25l"u8);
        _ = TryConsumeCsiWindowResize(data, ref index);
        ConsumeWindowsPtyResizeRepaintPrefixSequences(data, ref index);

        if (!TryConsumeCsiCursorHome(data, ref index))
        {
            return false;
        }

        ReadOnlySpan<byte> tail = data[index..];
        return ContainsCsiLiteral(tail, "K"u8) &&
               ContainsCsiLiteral(tail, "?25h"u8);
    }

    private static void ConsumeWindowsPtyResizeRepaintPrefixSequences(ReadOnlySpan<byte> data, ref int index)
    {
        while (TryConsumeCsiSgr(data, ref index) ||
               TryConsumeCsiLiteral(data, ref index, "?25l"u8) ||
               TryConsumeCsiWindowResize(data, ref index))
        {
        }
    }

    private static bool TryConsumeCsiSgr(ReadOnlySpan<byte> data, ref int index)
    {
        int start = index;
        if (index + 3 > data.Length ||
            data[index] != 0x1B ||
            data[index + 1] != (byte)'[')
        {
            return false;
        }

        index += 2;
        while (index < data.Length)
        {
            byte value = data[index];
            if (value == (byte)'m')
            {
                index++;
                return true;
            }

            if ((value >= 0x30 && value <= 0x3F) ||
                (value >= 0x20 && value <= 0x2F))
            {
                index++;
                continue;
            }

            index = start;
            return false;
        }

        index = start;
        return false;
    }

    private static bool TryConsumeCsiLiteral(
        ReadOnlySpan<byte> data,
        ref int index,
        ReadOnlySpan<byte> literal)
    {
        if (index + 2 + literal.Length > data.Length ||
            data[index] != 0x1B ||
            data[index + 1] != (byte)'[' ||
            !data.Slice(index + 2, literal.Length).SequenceEqual(literal))
        {
            return false;
        }

        index += 2 + literal.Length;
        return true;
    }

    private static bool TryConsumeCsiWindowResize(ReadOnlySpan<byte> data, ref int index)
    {
        int start = index;
        if (index + 4 > data.Length ||
            data[index] != 0x1B ||
            data[index + 1] != (byte)'[' ||
            data[index + 2] != (byte)'8' ||
            data[index + 3] != (byte)';')
        {
            return false;
        }

        index += 4;
        if (!TryConsumeDigits(data, ref index) ||
            index >= data.Length ||
            data[index] != (byte)';')
        {
            index = start;
            return false;
        }

        index++;
        if (!TryConsumeDigits(data, ref index) ||
            index >= data.Length ||
            data[index] != (byte)'t')
        {
            index = start;
            return false;
        }

        index++;
        return true;
    }

    private static bool TryConsumeCsiCursorHome(ReadOnlySpan<byte> data, ref int index)
    {
        int start = index;
        if (index + 3 > data.Length ||
            data[index] != 0x1B ||
            data[index + 1] != (byte)'[')
        {
            return false;
        }

        index += 2;
        while (index < data.Length)
        {
            byte value = data[index];
            if (value == (byte)'H' || value == (byte)'f')
            {
                index++;
                return true;
            }

            if (value != (byte)'1' && value != (byte)';')
            {
                index = start;
                return false;
            }

            index++;
        }

        index = start;
        return false;
    }

    private static bool TryConsumeDigits(ReadOnlySpan<byte> data, ref int index)
    {
        int start = index;
        while (index < data.Length)
        {
            byte value = data[index];
            if (value < (byte)'0' || value > (byte)'9')
            {
                break;
            }

            index++;
        }

        return index > start;
    }

    private static bool ContainsCsiLiteral(ReadOnlySpan<byte> data, ReadOnlySpan<byte> literal)
    {
        int lastStart = data.Length - literal.Length - 2;
        for (int index = 0; index <= lastStart; index++)
        {
            if (data[index] == 0x1B &&
                data[index + 1] == (byte)'[' &&
                data.Slice(index + 2, literal.Length).SequenceEqual(literal))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyMouseModeSelectionResetOnUiThread()
    {
        if (_screen is null)
        {
            return;
        }

        ResetPointerButtons();
        _isMouseSelecting = false;
        ClearAnchoredSelection();
        TerminalSelectionService.ClearSelection(_screen, _renderer, _presenter);
    }

    /// <summary>
    /// Sends input text to the active terminal endpoint or PTY.
    /// </summary>
    public void SendInput(string text)
    {
        FlushPendingTransportResize();
        TerminalSessionService.SendInput(text);
    }

    /// <summary>
    /// Sends input bytes to the active terminal endpoint or PTY.
    /// </summary>
    public void SendInput(ReadOnlySpan<byte> data)
    {
        FlushPendingTransportResize();
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
        if (e.Key == Key.Escape && HasRendererSelection())
        {
            ClearSelection();
            e.Handled = true;
            return;
        }

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

        if (TryHandleScrollbackEscapeKeyDown(e))
        {
            return;
        }

        FlushPendingTransportResize();
        if (TerminalInputAdapter.HandleKeyDown(e, TerminalSessionService, _vtProcessor))
        {
            ScrollToBottomForAcceptedKeyboardInput();
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

        FlushPendingTransportResize();
        if (!TerminalInputAdapter.HandleKeyDown(e, TerminalSessionService, _vtProcessor))
        {
            return false;
        }

        ScrollToBottomForAcceptedKeyboardInput();
        e.Handled = true;
        return true;
    }

    private bool TryHandleScrollbackEscapeKeyDown(KeyEventArgs e)
    {
        if (e.Key != Key.Escape ||
            e.KeyModifiers != KeyModifiers.None ||
            !ScrollToBottomOnInput ||
            _vtProcessor?.AlternateScreen == true ||
            !IsScrolledBackFromLiveBottom())
        {
            return false;
        }

        ScrollToBottom();
        _suppressNextScrollbackEscapeKeyUp = true;
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
        if (TryHandleSuppressedScrollbackEscapeKeyUp(e))
        {
            return;
        }

        FlushPendingTransportResize();
        if (TerminalInputAdapter.HandleKeyUp(e, TerminalSessionService))
        {
            e.Handled = true;
        }
    }

    private bool TryHandleSuppressedScrollbackEscapeKeyUp(KeyEventArgs e)
    {
        if (!_suppressNextScrollbackEscapeKeyUp)
        {
            return false;
        }

        _suppressNextScrollbackEscapeKeyUp = false;
        if (e.Key != Key.Escape)
        {
            return false;
        }

        e.Handled = true;
        return true;
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
        FlushPendingTransportResize();
        if (TerminalInputAdapter.HandleTextInput(e, TerminalSessionService))
        {
            ScrollToBottomForAcceptedKeyboardInput();
            e.Handled = true;
        }
    }

    private void ScrollToBottomForAcceptedKeyboardInput()
    {
        if (!ScrollToBottomOnInput || _vtProcessor?.AlternateScreen == true)
        {
            return;
        }

        ScrollToBottom();
    }

    private bool IsScrolledBackFromLiveBottom()
    {
        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            TerminalViewportScrollState viewportState = viewportScrollSource.ViewportScrollState;
            return Math.Min(viewportState.OffsetRows, viewportState.MaxOffsetRows) < viewportState.MaxOffsetRows;
        }

        return _screen?.ScrollOffset > 0;
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

        Point controlPoint = e.GetPosition(this);
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        if (!TryTranslatePointToTerminalContent(controlPoint, out Point point))
        {
            ResetPointerCell();
            ResetPointerButtons();
            _pointerInputStartedInContent = false;
            e.Handled = true;
            return;
        }

        UpdatePointerCell(point);
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
            _pointerInputStartedInContent = true;
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
            _renderer is not null &&
            _screen is not null)
        {
            _isMouseSelecting = true;
            e.Pointer.Capture(this);

            UpdateMouseSelectionFromPointer(point, e.KeyModifiers, resetAnchor: true);
            _renderer.SelectionIsRectangle = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            StopSelectionAutoScroll();
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

        Point controlPoint = e.GetPosition(this);
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        bool inContent = TryTranslatePointToTerminalContent(controlPoint, out Point point);
        if (!inContent)
        {
            if (!_isMouseSelecting && !_pointerInputStartedInContent)
            {
                ResetPointerCell();
                SyncPointerButtonState(props, preserveWhenNoButtons: true);
                return;
            }

            point = ClampPointToTerminalContent(controlPoint);
        }

        UpdatePointerCell(point);
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

        if (_isMouseSelecting && _renderer is not null && _screen is not null)
        {
            UpdateMouseSelectionFromPointer(point, e.KeyModifiers, resetAnchor: false);
            _renderer.SelectionIsRectangle = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            UpdateSelectionAutoScroll(point);
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
        Point controlPoint = e.GetPosition(this);
        bool inContent = TryTranslatePointToTerminalContent(controlPoint, out Point point);
        bool useContentPoint = inContent || _isMouseSelecting || _pointerInputStartedInContent;
        if (useContentPoint)
        {
            if (!inContent)
            {
                point = ClampPointToTerminalContent(controlPoint);
            }

            UpdatePointerCell(point);
        }
        else
        {
            ResetPointerCell();
        }

        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
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

        if (useContentPoint)
        {
            SendPointerEvent(new TerminalPointerEvent(
                Kind: TerminalPointerEventKind.Button,
                X: point.X,
                Y: point.Y,
                Button: button,
                Action: TerminalInputAction.Release,
                Modifiers: ConvertTerminalModifiers(e.KeyModifiers)));
        }

        if (button != TerminalMouseButton.None)
        {
            SetPointerButtonState(button, isDown: false);
        }
        else
        {
            SyncPointerButtonState(props);
        }

        bool wasMouseSelecting = _isMouseSelecting;
        if (wasMouseSelecting)
        {
            StopSelectionAutoScroll();
            if (_renderer is not null && _screen is not null)
            {
                UpdateMouseSelectionFromPointer(point, e.KeyModifiers, resetAnchor: false);
                _renderer.SelectionIsRectangle = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            }
        }

        _isMouseSelecting = false;
        _pointerInputStartedInContent = false;
        if (wasMouseSelecting)
        {
            e.Pointer.Capture(null);
        }
        e.Handled = true;
    }

    private void UpdateMouseSelectionFromPointer(Point point, KeyModifiers keyModifiers, bool resetAnchor)
    {
        if (_renderer is null || _screen is null)
        {
            return;
        }

        _lastSelectionPointerPoint = point;
        _lastSelectionKeyModifiers = keyModifiers;

        int column = (int)(point.X / _renderer.CellWidth);
        int topRow;
        int absoluteRow;
        lock (_screen.SyncRoot)
        {
            topRow = GetViewportTopAbsoluteRowLocked();
            long unclampedAbsoluteRow = (long)topRow + GetClampedSelectionViewportRow(point);
            int maxAbsoluteRow = GetSelectionMaxAbsoluteRowLocked();
            absoluteRow = (int)Math.Clamp(unclampedAbsoluteRow, 0, maxAbsoluteRow);
        }

        if (resetAnchor)
        {
            _selectionAnchorColumn = column;
            _selectionAnchorAbsoluteRow = absoluteRow;
        }

        _selectionActiveColumn = column;
        _selectionActiveAbsoluteRow = absoluteRow;
        _hasAnchoredSelection = true;
        SetSelectionAnchorSpans(Array.Empty<TerminalHighlightSpan>());
        ApplyMouseSelectionToRenderer(topRow);
    }

    private int GetClampedSelectionViewportRow(Point point)
    {
        if (_renderer is null || _screen is null || _screen.ViewportRows <= 0)
        {
            return 0;
        }

        int row = (int)(point.Y / _renderer.CellHeight);
        return Math.Clamp(row, 0, _screen.ViewportRows - 1);
    }

    private int GetSelectionMaxAbsoluteRowLocked()
    {
        if (_screen is null)
        {
            return 0;
        }

        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            ulong totalRows = viewportScrollSource.ViewportScrollState.TotalRows;
            if (totalRows == 0)
            {
                return 0;
            }

            ulong maxAbsoluteRow = totalRows - 1;
            return maxAbsoluteRow > int.MaxValue
                ? int.MaxValue
                : (int)maxAbsoluteRow;
        }

        return Math.Max(0, _screen.TotalRows - 1);
    }

    private void ApplyMouseSelectionToRenderer(int topRow)
    {
        if (_renderer is null || _screen is null)
        {
            return;
        }

        ApplyAnchoredSelectionToRenderer(topRow);
        InvalidateScreen();
        _presenter?.Invalidate();
    }

    private void CaptureRendererSelectionForCurrentViewport()
    {
        if (_screen is null)
        {
            return;
        }

        lock (_screen.SyncRoot)
        {
            CaptureRendererSelectionForCurrentViewportLocked();
        }
    }

    private void CaptureRendererSelectionForCurrentViewportLocked()
    {
        if (_hasAnchoredSelection ||
            _renderer?.SelectionStart is not { } start ||
            _renderer.SelectionEnd is not { } end)
        {
            return;
        }

        int topRow = GetViewportTopAbsoluteRowLocked();
        _selectionAnchorColumn = start.Column;
        _selectionAnchorAbsoluteRow = topRow + start.Row;
        _selectionActiveColumn = end.Column;
        _selectionActiveAbsoluteRow = topRow + end.Row;
        _hasAnchoredSelection = true;
    }

    private void ApplyAnchoredSelectionToRendererLocked()
    {
        if (!_hasAnchoredSelection || _screen is null)
        {
            return;
        }

        ApplyAnchoredSelectionToRenderer(GetViewportTopAbsoluteRowLocked());
    }

    private void ApplyAnchoredSelectionToRenderer(int topRow)
    {
        if (!_hasAnchoredSelection || _renderer is null)
        {
            return;
        }

        _renderer.SelectionStart = (_selectionAnchorColumn, _selectionAnchorAbsoluteRow - topRow);
        _renderer.SelectionEnd = (_selectionActiveColumn, _selectionActiveAbsoluteRow - topRow);
        ApplySelectionSpansToRenderer(topRow);
    }

    private void ApplySelectionSpansToRenderer(int topRow)
    {
        if (_renderer is null)
        {
            ResetSelectionViewportSpanCache();
            return;
        }

        if (_selectionAnchorSpans.Length == 0)
        {
            ResetSelectionViewportSpanCache();
            _renderer.SetSelectionSpans(ReadOnlySpan<TerminalHighlightSpan>.Empty);
            return;
        }

        if (ReferenceEquals(_selectionViewportSpansSource, _selectionAnchorSpans) &&
            ReferenceEquals(_selectionViewportSpansRenderer, _renderer) &&
            _selectionViewportSpansTopRow == topRow)
        {
            return;
        }

        TerminalHighlightSpan[] visibleSpans = new TerminalHighlightSpan[_selectionAnchorSpans.Length];
        int visibleSpanCount = 0;
        for (int i = 0; i < _selectionAnchorSpans.Length; i++)
        {
            TerminalHighlightSpan span = _selectionAnchorSpans[i];
            int viewportRow = span.Row - topRow;
            if (_screen is not null && (viewportRow < 0 || viewportRow >= _screen.ViewportRows))
            {
                continue;
            }

            visibleSpans[visibleSpanCount++] = new TerminalHighlightSpan(
                viewportRow,
                span.StartColumn,
                span.EndColumn,
                span.Kind);
        }

        if (visibleSpanCount == 0)
        {
            visibleSpans = Array.Empty<TerminalHighlightSpan>();
        }
        else if (visibleSpanCount != visibleSpans.Length)
        {
            Array.Resize(ref visibleSpans, visibleSpanCount);
        }

        _selectionViewportSpansSource = _selectionAnchorSpans;
        _selectionViewportSpansRenderer = _renderer;
        _selectionViewportSpansTopRow = topRow;
        _renderer.SetSelectionSpans(visibleSpans);
    }

    private void SetSelectionAnchorSpans(TerminalHighlightSpan[] spans)
    {
        _selectionAnchorSpans = spans;
        ResetSelectionViewportSpanCache();
    }

    private void ResetSelectionViewportSpanCache()
    {
        _selectionViewportSpansSource = null;
        _selectionViewportSpansRenderer = null;
        _selectionViewportSpansTopRow = int.MinValue;
    }

    private TerminalGridPosition[]? CreateSelectionResizeAnchorsLocked(out bool anchorsAreSpans)
    {
        anchorsAreSpans = false;
        if (!_hasAnchoredSelection)
        {
            return null;
        }

        TerminalHighlightSpan[] spans = CreateCurrentSelectionAbsoluteSpansLocked();
        if (spans.Length > 0)
        {
            anchorsAreSpans = true;
            return CreateSelectionSpanResizeAnchors(spans);
        }

        return
        [
            new TerminalGridPosition(_selectionAnchorColumn, _selectionAnchorAbsoluteRow),
            new TerminalGridPosition(_selectionActiveColumn, _selectionActiveAbsoluteRow),
        ];
    }

    private TerminalHighlightSpan[] CreateCurrentSelectionAbsoluteSpansLocked()
    {
        if (_selectionAnchorSpans.Length > 0)
        {
            return _selectionAnchorSpans;
        }

        if (_renderer?.SelectionIsRectangle != true)
        {
            return Array.Empty<TerminalHighlightSpan>();
        }

        int left = Math.Min(_selectionAnchorColumn, _selectionActiveColumn);
        int rightExclusive = Math.Max(_selectionAnchorColumn, _selectionActiveColumn);
        if (rightExclusive <= left)
        {
            return Array.Empty<TerminalHighlightSpan>();
        }

        int top = Math.Min(_selectionAnchorAbsoluteRow, _selectionActiveAbsoluteRow);
        int bottom = Math.Max(_selectionAnchorAbsoluteRow, _selectionActiveAbsoluteRow);
        int rowCount = checked(bottom - top + 1);
        TerminalHighlightSpan[] spans = new TerminalHighlightSpan[rowCount];
        for (int i = 0; i < spans.Length; i++)
        {
            spans[i] = new TerminalHighlightSpan(
                top + i,
                left,
                rightExclusive - 1,
                TerminalHighlightKind.Selection);
        }

        return spans;
    }

    private static TerminalGridPosition[] CreateSelectionSpanResizeAnchors(ReadOnlySpan<TerminalHighlightSpan> spans)
    {
        TerminalGridPosition[] anchors = new TerminalGridPosition[checked(spans.Length * 2)];
        for (int i = 0; i < spans.Length; i++)
        {
            TerminalHighlightSpan span = spans[i];
            int anchorIndex = i * 2;
            anchors[anchorIndex] = new TerminalGridPosition(span.StartColumn, span.Row);
            anchors[anchorIndex + 1] = new TerminalGridPosition(span.EndColumn + 1, span.Row);
        }

        return anchors;
    }

    private NativeSelectionResizeContext? CreateSelectionNativeViewportResizeContextLocked(
        int columns,
        int viewportRows,
        bool reflowOnResize,
        int topRow)
    {
        if (!_hasAnchoredSelection || _screen is null)
        {
            return null;
        }

        TerminalHighlightSpan[] absoluteSpans = CreateCurrentSelectionAbsoluteSpansLocked();
        bool anchorsAreSpans = absoluteSpans.Length > 0;
        int selectionTopRow = Math.Min(_selectionAnchorAbsoluteRow, _selectionActiveAbsoluteRow);
        int selectionBottomRow = Math.Max(_selectionAnchorAbsoluteRow, _selectionActiveAbsoluteRow);
        if (anchorsAreSpans)
        {
            for (int i = 0; i < absoluteSpans.Length; i++)
            {
                TerminalHighlightSpan span = absoluteSpans[i];
                selectionTopRow = Math.Min(selectionTopRow, span.Row);
                selectionBottomRow = Math.Max(selectionBottomRow, span.Row);
            }
        }

        bool selectionFitsViewport = selectionTopRow >= topRow &&
            selectionBottomRow < topRow + _screen.ViewportRows;
        int snapshotFirstAbsoluteRow = topRow;
        TerminalScreen viewportSnapshot;
        if (selectionFitsViewport)
        {
            // Native mirrors can contain padded scrollback rows and style-only blank cells.
            // Selection resize only needs the visible text cells that can affect anchor rows.
                viewportSnapshot = new(
                    _screen.Columns,
                    _screen.ViewportRows,
                    CalculateSnapshotScrollbackLimit(_screen.Columns, _screen.ViewportRows, viewportRows))
            {
                DefaultForeground = _screen.DefaultForeground,
                DefaultBackground = _screen.DefaultBackground,
            };

            for (int row = 0; row < _screen.ViewportRows; row++)
            {
                TerminalRow snapshotRow = viewportSnapshot.GetViewportRow(row);
                snapshotRow.CopyFrom(
                    _screen.GetViewportRow(row),
                    _screen.DefaultForeground,
                    _screen.DefaultBackground);
                ClearStyleOnlyTrailingCells(
                    snapshotRow,
                    _screen.DefaultForeground,
                    _screen.DefaultBackground);
            }
        }
        else if (_vtProcessor is ITerminalScreenSnapshotSource snapshotSource)
        {
            int maxAbsoluteRow = GetSelectionMaxAbsoluteRowLocked();
            int paddingRows = Math.Max(1, _screen.ViewportRows);
            int snapshotLastAbsoluteRow = Math.Min(
                maxAbsoluteRow,
                Math.Max(selectionBottomRow, topRow + _screen.ViewportRows - 1) + paddingRows);
            snapshotFirstAbsoluteRow = Math.Max(0, Math.Min(selectionTopRow, topRow) - paddingRows);
            int snapshotRowCount = checked(snapshotLastAbsoluteRow - snapshotFirstAbsoluteRow + 1);
            int snapshotScrollbackLimit = CalculateSnapshotScrollbackLimit(
                _screen.Columns,
                snapshotRowCount,
                viewportRows);
            if (!snapshotSource.TryCreateScreenSnapshot(
                    snapshotFirstAbsoluteRow,
                    snapshotRowCount,
                    snapshotScrollbackLimit,
                    out viewportSnapshot))
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        TerminalGridPosition[] anchors;
        if (anchorsAreSpans)
        {
            TerminalHighlightSpan[] viewportSpans = new TerminalHighlightSpan[absoluteSpans.Length];
            for (int i = 0; i < absoluteSpans.Length; i++)
            {
                TerminalHighlightSpan span = absoluteSpans[i];
                viewportSpans[i] = new TerminalHighlightSpan(
                    span.Row - snapshotFirstAbsoluteRow,
                    span.StartColumn,
                    span.EndColumn,
                    span.Kind);
            }

            anchors = CreateSelectionSpanResizeAnchors(viewportSpans);
        }
        else
        {
            anchors =
            [
                new TerminalGridPosition(_selectionAnchorColumn, _selectionAnchorAbsoluteRow - snapshotFirstAbsoluteRow),
                new TerminalGridPosition(_selectionActiveColumn, _selectionActiveAbsoluteRow - snapshotFirstAbsoluteRow),
            ];
        }

        viewportSnapshot.Resize(
            columns,
            viewportRows,
            reflowOnResize,
            trackedViewportPosition: null,
            anchors);
        return new NativeSelectionResizeContext(viewportSnapshot, anchors, anchorsAreSpans);
    }

    private static int CalculateSnapshotScrollbackLimit(
        int columns,
        int snapshotRows,
        int resizedViewportRows)
    {
        long maxReflowedRows = (long)Math.Max(1, columns) * Math.Max(1, snapshotRows);
        long neededScrollback = maxReflowedRows - Math.Max(1, resizedViewportRows);
        return neededScrollback <= 0
            ? 0
            : neededScrollback >= int.MaxValue
                ? int.MaxValue
                : (int)neededScrollback;
    }

    private static void CopyViewportRows(TerminalScreen source, TerminalScreen destination)
    {
        int rowCount = Math.Min(source.ViewportRows, destination.ViewportRows);
        for (int row = 0; row < rowCount; row++)
        {
            destination.GetViewportRow(row).CopyFrom(
                source.GetViewportRow(row),
                destination.DefaultForeground,
                destination.DefaultBackground);
        }
    }

    private static void ClearStyleOnlyTrailingCells(TerminalRow row, uint foreground, uint background)
    {
        int lastContentColumn = -1;
        for (int column = 0; column < row.Columns; column++)
        {
            TerminalCell cell = row[column];
            if (!cell.HasContent)
            {
                continue;
            }

            lastContentColumn = cell.Width == 2
                ? Math.Min(row.Columns - 1, column + 1)
                : column;
        }

        for (int column = lastContentColumn + 1; column < row.Columns; column++)
        {
            row[column] = TerminalCell.Empty(foreground, background);
        }
    }

    private void ApplySelectionResizeAnchorsLocked(
        TerminalGridPosition[]? selectionResizeAnchors,
        bool anchorsAreSpans)
    {
        if (!_hasAnchoredSelection ||
            selectionResizeAnchors is not { Length: >= 2 })
        {
            return;
        }

        if (anchorsAreSpans)
        {
            SetSelectionAnchorSpans(CreateSelectionSpansFromResizeAnchors(
                selectionResizeAnchors,
                rowOffset: 0,
                _screen?.Columns ?? 1));
            UpdateSelectionEndpointsFromSpans();
            ApplyAnchoredSelectionToRendererLocked();
            return;
        }

        SetSelectionAnchorSpans(Array.Empty<TerminalHighlightSpan>());
        _selectionAnchorColumn = selectionResizeAnchors[0].Column;
        _selectionActiveColumn = selectionResizeAnchors[1].Column;
        _selectionAnchorAbsoluteRow = selectionResizeAnchors[0].Row;
        _selectionActiveAbsoluteRow = selectionResizeAnchors[1].Row;

        ApplyAnchoredSelectionToRendererLocked();
    }

    private void ApplyNativeSelectionResizeContextLocked(NativeSelectionResizeContext? context)
    {
        if (!_hasAnchoredSelection ||
            _screen is null ||
            context?.Anchors is not { Length: >= 2 } anchors)
        {
            return;
        }

        int rowOffset = FindNativeSelectionSnapshotViewportRowOffset(context, _screen);
        int topRow = GetViewportTopAbsoluteRowLocked();
        int absoluteRowOffset = topRow + rowOffset;
        if (context.AnchorsAreSpans)
        {
            SetSelectionAnchorSpans(CreateSelectionSpansFromResizeAnchors(
                anchors,
                absoluteRowOffset,
                _screen.Columns));
            UpdateSelectionEndpointsFromSpans();
            ApplyAnchoredSelectionToRendererLocked();
            return;
        }

        SetSelectionAnchorSpans(Array.Empty<TerminalHighlightSpan>());
        _selectionAnchorColumn = anchors[0].Column;
        _selectionAnchorAbsoluteRow = anchors[0].Row + absoluteRowOffset;
        _selectionActiveColumn = anchors[1].Column;
        _selectionActiveAbsoluteRow = anchors[1].Row + absoluteRowOffset;

        ApplyAnchoredSelectionToRendererLocked();
    }

    private static TerminalHighlightSpan[] CreateSelectionSpansFromResizeAnchors(
        ReadOnlySpan<TerminalGridPosition> anchors,
        int rowOffset,
        int columns)
    {
        List<TerminalHighlightSpan> spans = new(anchors.Length);
        int clampedColumns = Math.Max(1, columns);
        for (int i = 0; i + 1 < anchors.Length; i += 2)
        {
            TerminalGridPosition start = anchors[i];
            TerminalGridPosition end = anchors[i + 1];
            if (start.Row > end.Row || (start.Row == end.Row && start.Column > end.Column))
            {
                (start, end) = (end, start);
            }

            for (int row = start.Row; row <= end.Row; row++)
            {
                int left = row == start.Row ? start.Column : 0;
                int rightExclusive = row == end.Row ? end.Column : clampedColumns;
                left = Math.Clamp(left, 0, clampedColumns);
                rightExclusive = Math.Clamp(rightExclusive, 0, clampedColumns);
                if (rightExclusive <= left)
                {
                    continue;
                }

                spans.Add(new TerminalHighlightSpan(
                    row + rowOffset,
                    left,
                    rightExclusive - 1,
                    TerminalHighlightKind.Selection));
            }
        }

        return spans.ToArray();
    }

    private void UpdateSelectionEndpointsFromSpans()
    {
        if (_selectionAnchorSpans.Length == 0)
        {
            return;
        }

        TerminalHighlightSpan first = _selectionAnchorSpans[0];
        TerminalHighlightSpan last = _selectionAnchorSpans[^1];
        _selectionAnchorColumn = first.StartColumn;
        _selectionAnchorAbsoluteRow = first.Row;
        _selectionActiveColumn = last.EndColumn + 1;
        _selectionActiveAbsoluteRow = last.Row;
    }

    private static int FindNativeSelectionSnapshotViewportRowOffset(
        NativeSelectionResizeContext context,
        TerminalScreen destination)
    {
        int snapshotRowCount = context.Snapshot.TotalRows;
        int destinationRowCount = destination.ViewportRows;
        if (snapshotRowCount <= 0 || destinationRowCount <= 0)
        {
            return 0;
        }

        ComparableRowKey[] snapshotRows = CreateComparableScreenRowKeys(context.Snapshot);
        ComparableRowKey[] destinationRows = CreateComparableViewportRowKeys(destination);
        GetNativeSelectionSnapshotAnchorRange(context.Anchors, out int anchorTop, out int anchorBottom);

        Dictionary<int, NativeSelectionOffsetScore>? offsetScores = null;
        for (int snapshotRow = 0; snapshotRow < snapshotRowCount; snapshotRow++)
        {
            ComparableRowKey rowKey = snapshotRows[snapshotRow];
            if (rowKey.TextLength == 0)
            {
                continue;
            }

            for (int destinationRow = 0; destinationRow < destinationRowCount; destinationRow++)
            {
                if (rowKey != destinationRows[destinationRow] ||
                    !ComparableRowsEqual(
                        context.Snapshot.GetRow(snapshotRow),
                        destination.GetViewportRow(destinationRow),
                        rowKey.TextLength))
                {
                    continue;
                }

                int offset = destinationRow - snapshotRow;
                long scoreDelta = 1024 - Math.Min(1024, GetDistanceFromRange(snapshotRow, anchorTop, anchorBottom));
                offsetScores ??= [];
                offsetScores.TryGetValue(offset, out NativeSelectionOffsetScore score);
                offsetScores[offset] = new NativeSelectionOffsetScore(
                    score.Matches + 1,
                    score.Score + scoreDelta);
            }
        }

        int bestOffset = 0;
        int bestMatches = 0;
        long bestScore = long.MinValue;
        if (offsetScores is null)
        {
            return 0;
        }

        foreach (KeyValuePair<int, NativeSelectionOffsetScore> offsetScore in offsetScores)
        {
            int offset = offsetScore.Key;
            NativeSelectionOffsetScore score = offsetScore.Value;
            if (score.Matches > bestMatches ||
                (score.Matches == bestMatches &&
                 (score.Score > bestScore ||
                  (score.Score == bestScore && offset < bestOffset))))
            {
                bestMatches = score.Matches;
                bestScore = score.Score;
                bestOffset = offset;
            }
        }

        return bestMatches > 0 ? bestOffset : 0;
    }

    private static void GetNativeSelectionSnapshotAnchorRange(
        ReadOnlySpan<TerminalGridPosition> anchors,
        out int anchorTop,
        out int anchorBottom)
    {
        anchorTop = int.MaxValue;
        anchorBottom = int.MinValue;
        for (int i = 0; i < anchors.Length; i++)
        {
            int row = anchors[i].Row;
            anchorTop = Math.Min(anchorTop, row);
            anchorBottom = Math.Max(anchorBottom, row);
        }

        if (anchorTop == int.MaxValue)
        {
            anchorTop = 0;
            anchorBottom = 0;
        }
    }

    private static int GetDistanceFromRange(int value, int start, int end)
    {
        if (value < start)
        {
            return start - value;
        }

        return value > end ? value - end : 0;
    }

    private static ComparableRowKey[] CreateComparableViewportRowKeys(TerminalScreen screen)
    {
        ComparableRowKey[] rows = new ComparableRowKey[screen.ViewportRows];
        for (int row = 0; row < rows.Length; row++)
        {
            rows[row] = CreateComparableRowKey(screen.GetViewportRow(row));
        }

        return rows;
    }

    private static ComparableRowKey[] CreateComparableScreenRowKeys(TerminalScreen screen)
    {
        ComparableRowKey[] rows = new ComparableRowKey[screen.TotalRows];
        for (int row = 0; row < rows.Length; row++)
        {
            rows[row] = CreateComparableRowKey(screen.GetRow(row));
        }

        return rows;
    }

    private static ComparableRowKey CreateComparableRowKey(TerminalRow row)
    {
        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        int lastContentColumn = FindComparableLastContentColumn(cells);

        if (lastContentColumn < 0)
        {
            return default;
        }

        ulong hash = ComparableRowFnvOffsetBasis;
        int textLength = 0;
        Span<char> runeChars = stackalloc char[2];
        for (int column = 0; column <= lastContentColumn; column++)
        {
            ref readonly TerminalCell cell = ref cells[column];
            if (cell.Width == 0 || (cell.Attributes & CellAttributes.Hidden) != 0)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                string grapheme = cell.Grapheme;
                for (int i = 0; i < grapheme.Length; i++)
                {
                    hash = AddComparableRowChar(hash, grapheme[i]);
                }

                textLength += grapheme.Length;
            }
            else if (cell.Codepoint > 0 && Rune.IsValid(cell.Codepoint))
            {
                Rune rune = new(cell.Codepoint);
                int charsWritten = rune.EncodeToUtf16(runeChars);
                for (int i = 0; i < charsWritten; i++)
                {
                    hash = AddComparableRowChar(hash, runeChars[i]);
                }

                textLength += charsWritten;
            }
            else
            {
                hash = AddComparableRowChar(hash, ' ');
                textLength++;
            }
        }

        return new ComparableRowKey(textLength, hash);
    }

    private static bool ComparableRowsEqual(TerminalRow left, TerminalRow right, int textLength)
    {
        if (textLength <= ComparableRowStackCharLimit)
        {
            Span<char> leftText = stackalloc char[textLength];
            Span<char> rightText = stackalloc char[textLength];
            WriteComparableRowText(left, leftText);
            WriteComparableRowText(right, rightText);
            return leftText.SequenceEqual(rightText);
        }

        char[] leftRented = ArrayPool<char>.Shared.Rent(textLength);
        char[] rightRented = ArrayPool<char>.Shared.Rent(textLength);
        try
        {
            Span<char> leftText = leftRented.AsSpan(0, textLength);
            Span<char> rightText = rightRented.AsSpan(0, textLength);
            WriteComparableRowText(left, leftText);
            WriteComparableRowText(right, rightText);
            return leftText.SequenceEqual(rightText);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(leftRented);
            ArrayPool<char>.Shared.Return(rightRented);
        }
    }

    private static int FindComparableLastContentColumn(ReadOnlySpan<TerminalCell> cells)
    {
        int lastContentColumn = -1;
        for (int column = 0; column < cells.Length; column++)
        {
            ref readonly TerminalCell cell = ref cells[column];
            if (cell.Width == 0 ||
                (cell.Attributes & CellAttributes.Hidden) != 0 ||
                !cell.HasContent)
            {
                continue;
            }

            lastContentColumn = column;
        }

        return lastContentColumn;
    }

    private static void WriteComparableRowText(TerminalRow row, Span<char> destination)
    {
        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        int lastContentColumn = FindComparableLastContentColumn(cells);
        int written = 0;
        Span<char> runeChars = stackalloc char[2];
        for (int column = 0; column <= lastContentColumn; column++)
        {
            ref readonly TerminalCell cell = ref cells[column];
            if (cell.Width == 0 || (cell.Attributes & CellAttributes.Hidden) != 0)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                string grapheme = cell.Grapheme;
                grapheme.AsSpan().CopyTo(destination[written..]);
                written += grapheme.Length;
            }
            else if (cell.Codepoint > 0 && Rune.IsValid(cell.Codepoint))
            {
                Rune rune = new(cell.Codepoint);
                int charsWritten = rune.EncodeToUtf16(runeChars);
                runeChars[..charsWritten].CopyTo(destination[written..]);
                written += charsWritten;
            }
            else
            {
                destination[written++] = ' ';
            }
        }

        Debug.Assert(written == destination.Length);
    }

    private static ulong AddComparableRowChar(ulong hash, char value)
    {
        hash ^= value;
        return hash * ComparableRowFnvPrime;
    }

    private void ClearAnchoredSelection()
    {
        _hasAnchoredSelection = false;
        SetSelectionAnchorSpans(Array.Empty<TerminalHighlightSpan>());
        _renderer?.SetSelectionSpans(ReadOnlySpan<TerminalHighlightSpan>.Empty);
    }

    private void UpdateSelectionAutoScroll(Point point)
    {
        if (GetSelectionAutoScrollRows(point) == 0)
        {
            StopSelectionAutoScroll();
            return;
        }

        _selectionAutoScrollTimer ??= CreateSelectionAutoScrollTimer();
        if (!_selectionAutoScrollTimer.IsEnabled)
        {
            _selectionAutoScrollTimer.Start();
            OnSelectionAutoScrollTick(null, EventArgs.Empty);
        }
    }

    private DispatcherTimer CreateSelectionAutoScrollTimer()
    {
        DispatcherTimer timer = new()
        {
            Interval = SelectionAutoScrollInterval,
        };
        timer.Tick += OnSelectionAutoScrollTick;
        return timer;
    }

    private void OnSelectionAutoScrollTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (!_isMouseSelecting)
        {
            StopSelectionAutoScroll();
            return;
        }

        int rows = GetSelectionAutoScrollRows(_lastSelectionPointerPoint);
        if (rows == 0)
        {
            StopSelectionAutoScroll();
            return;
        }

        if (!TryScrollSelectionViewportByRows(rows))
        {
            StopSelectionAutoScroll();
            return;
        }

        UpdateMouseSelectionFromPointer(_lastSelectionPointerPoint, _lastSelectionKeyModifiers, resetAnchor: false);
    }

    private bool TryScrollSelectionViewportByRows(int rows)
    {
        if (_screen is null || rows == 0)
        {
            return false;
        }

        int beforeTopRow;
        bool canScroll;
        lock (_screen.SyncRoot)
        {
            beforeTopRow = GetViewportTopAbsoluteRowLocked();
            canScroll = CanScrollSelectionViewportByRowsLocked(rows, beforeTopRow);
        }

        if (!canScroll)
        {
            return false;
        }

        ScrollByRows(rows);

        int afterTopRow;
        lock (_screen.SyncRoot)
        {
            afterTopRow = GetViewportTopAbsoluteRowLocked();
        }

        return beforeTopRow != afterTopRow;
    }

    private bool CanScrollSelectionViewportByRowsLocked(int rows, int topRow)
    {
        if (rows < 0)
        {
            return topRow > 0;
        }

        int maxTopRow = GetSelectionMaxTopAbsoluteRowLocked();
        return topRow < maxTopRow;
    }

    private int GetSelectionMaxTopAbsoluteRowLocked()
    {
        if (_screen is null)
        {
            return 0;
        }

        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            return GetViewportMaxTopAbsoluteRow(viewportScrollSource.ViewportScrollState);
        }

        return Math.Max(0, _screen.TotalRows - _screen.ViewportRows);
    }

    private int GetSelectionAutoScrollRows(Point point)
    {
        Size contentSize = GetTerminalContentSize(Bounds.Size);
        if (_renderer is null || _screen is null || contentSize.Height <= 0)
        {
            return 0;
        }

        double autoScrollMargin = Math.Min(SelectionAutoScrollMargin, contentSize.Height * 0.5d);
        if (point.Y < autoScrollMargin)
        {
            return -1;
        }

        if (point.Y > contentSize.Height - autoScrollMargin)
        {
            return 1;
        }

        return 0;
    }

    private void StopMouseSelectionDrag()
    {
        StopSelectionAutoScroll();
        _isMouseSelecting = false;
        _pointerInputStartedInContent = false;
    }

    private void StopSelectionAutoScroll()
    {
        if (_selectionAutoScrollTimer is not null && _selectionAutoScrollTimer.IsEnabled)
        {
            _selectionAutoScrollTimer.Stop();
        }
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

        ResetPointerCell();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        StopMouseSelectionDrag();
    }

    private void HandlePointerWheelChangedCore(PointerWheelEventArgs e)
    {
        Point controlPoint = e.GetPosition(this);
        bool insideContent = TryTranslatePointToTerminalContent(controlPoint, out Point point);
        if (IsMouseReportingActiveForInput())
        {
            if (insideContent)
            {
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
        }

        HandlePointerWheelScroll(e, forwardToInputSink: insideContent);
    }

    private void HandlePointerWheelScroll(PointerWheelEventArgs e, bool forwardToInputSink)
    {
        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            int deltaRows = e.Delta.Y > 0
                ? -3
                : e.Delta.Y < 0
                    ? 3
                    : 0;
            if (deltaRows != 0)
            {
                CaptureRendererSelectionForCurrentViewport();
                viewportScrollSource.ScrollViewportByRows(deltaRows);
                lock (_screen!.SyncRoot)
                {
                    SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
                    UpdateRendererCursorForViewportLocked();
                    ApplyAnchoredSelectionToRendererLocked();
                }

                _presenter?.Invalidate();
                RaiseScrollInvalidated();
            }

            e.Handled = true;
            return;
        }

        CaptureRendererSelectionForCurrentViewport();
        if (forwardToInputSink)
        {
            TerminalScrollService.HandlePointerWheel(
                e,
                _scrollViewer,
                TerminalSessionService,
                _presenter,
                RaiseScrollInvalidated);
        }
        else
        {
            _scrollViewer?.HandleWheel(e.Delta.Y);
            _presenter?.Invalidate();
            RaiseScrollInvalidated();
        }

        UpdateRendererCursorForViewport();
        UpdateRendererParityStateFromScreen();
        UpdateAutoScrollPinnedToBottom();
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
        _suppressNextScrollbackEscapeKeyUp = false;
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

        if (_screen is null)
        {
            return snapshotExporter.TryExportSnapshot(format, options, out snapshot);
        }

        lock (_screen.SyncRoot)
        {
            return snapshotExporter.TryExportSnapshot(format, options, out snapshot);
        }
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

        int topRow;
        lock (_screen.SyncRoot)
        {
            topRow = GetViewportTopAbsoluteRowLocked();
        }

        _selectionAnchorColumn = 0;
        _selectionAnchorAbsoluteRow = topRow;
        _selectionActiveColumn = _screen.Columns;
        _selectionActiveAbsoluteRow = topRow + _screen.ViewportRows - 1;
        _hasAnchoredSelection = true;
        SetSelectionAnchorSpans(Array.Empty<TerminalHighlightSpan>());
        _renderer.SelectionStart = (0, 0);
        _renderer.SelectionEnd = (_screen.Columns, _screen.ViewportRows - 1);
        _renderer.SelectionIsRectangle = false;
        _renderer.SetSelectionSpans(ReadOnlySpan<TerminalHighlightSpan>.Empty);
        InvalidateScreen();
        _presenter?.Invalidate();
    }

    /// <summary>
    /// Clears the current text selection.
    /// </summary>
    public void ClearSelection()
    {
        ClearAnchoredSelection();
        TerminalSelectionService.ClearSelection(_screen, _renderer, _presenter);
    }

    private bool HasRendererSelection() =>
        _renderer is not null &&
        ((_renderer.SelectionStart is not null && _renderer.SelectionEnd is not null) ||
         !_renderer.GetSelectionSpans().IsEmpty);

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
        if (ReferenceEquals(_theme, theme))
        {
            ApplyThemeCore(theme, updateStyledDefaults: true);
            return;
        }

        SetCurrentValue(ThemeProperty, theme);
    }

    /// <summary>
    /// Forces a full re-render of the terminal.
    /// </summary>
    public void InvalidateTerminal()
    {
        InvalidateScreen();
        RefreshPresenterRenderState(fullRedraw: true);
    }

    /// <summary>
    /// Scrolls the terminal by the given number of rows.
    /// </summary>
    public void ScrollByRows(int rows)
    {
        CaptureRendererSelectionForCurrentViewport();
        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            viewportScrollSource.ScrollViewportByRows(rows);
            if (_screen is not null)
            {
                lock (_screen.SyncRoot)
                {
                    SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
                    ApplyAnchoredSelectionToRendererLocked();
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
        UpdateAutoScrollPinnedToBottom();
        RaiseScrollInvalidated();
    }

    /// <summary>
    /// Scrolls to the bottom of the terminal output.
    /// </summary>
    public void ScrollToBottom()
    {
        CaptureRendererSelectionForCurrentViewport();
        if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            viewportScrollSource.ScrollViewportToBottom();
            if (_screen is not null)
            {
                lock (_screen.SyncRoot)
                {
                    SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
                    ApplyAnchoredSelectionToRendererLocked();
                }
            }

            _presenter?.Invalidate();
        }
        else
        {
            TerminalScrollService.ScrollToBottom(_scrollData, _screen, _presenter);
        }

        _autoScrollPinnedToBottom = true;
        UpdateRendererCursorForViewport();
        UpdateRendererParityStateFromScreen();
        RaiseScrollInvalidated();
    }

    /// <summary>
    /// Clears scrollback/history while preserving the active terminal viewport.
    /// </summary>
    public void ClearScrollback()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.InvokeAsync(ClearScrollback).GetAwaiter().GetResult();
            return;
        }

        if (_screen is null)
        {
            return;
        }

        FlushPendingTransportOutputBeforeResize();
        ClearSelection();
        ResetCursorBlinkPhase();

        lock (_screen.SyncRoot)
        {
            if (_vtProcessor is ITerminalSessionHistoryController historyController)
            {
                historyController.ClearScrollback();
            }
            else
            {
                _screen.ClearScrollback();
            }

            SyncHistoryMutationScrollStateLocked();
        }

        UpdateAutoScrollPinnedToBottom();
        UpdateRendererCursorForViewport();
        UpdateRendererParityStateFromScreen();
        _presenter?.Invalidate(fullRedraw: true);
        RaiseScrollInvalidated();
    }

    /// <summary>
    /// Updates the content scale for DPI changes.
    /// </summary>
    public void SetContentScale(double scaleX, double scaleY)
    {
        double safeScaleX = NormalizeContentScale(scaleX);
        double safeScaleY = NormalizeContentScale(scaleY);
        _contentScaleX = safeScaleX;
        _contentScaleY = safeScaleY;

        if (Endpoint is ITerminalScaleSink scaleSink)
        {
            scaleSink.SetContentScale(safeScaleX, safeScaleY);
        }
    }

    private void AttachTopLevelScaling()
    {
        TopLevel? nextTopLevel = TopLevel.GetTopLevel(this);
        if (ReferenceEquals(_scalingTopLevel, nextTopLevel))
        {
            ApplyContentScaleFromTopLevel();
            return;
        }

        DetachTopLevelScaling();
        _scalingTopLevel = nextTopLevel;
        if (_scalingTopLevel is not null)
        {
            _scalingTopLevel.ScalingChanged += OnTopLevelScalingChanged;
        }

        ApplyContentScaleFromTopLevel();
    }

    private void DetachTopLevelScaling()
    {
        if (_scalingTopLevel is not null)
        {
            _scalingTopLevel.ScalingChanged -= OnTopLevelScalingChanged;
            _scalingTopLevel = null;
        }
    }

    private void OnTopLevelScalingChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyContentScaleFromTopLevel();
    }

    private void ApplyContentScaleFromTopLevel()
    {
        if (_scalingTopLevel is null)
        {
            return;
        }

        double scaling = NormalizeContentScale(_scalingTopLevel.RenderScaling);
        SetContentScale(scaling, scaling);
    }

    private void ApplyContentScaleToEndpoint(ITerminalEndpoint endpoint)
    {
        if (endpoint is ITerminalScaleSink scaleSink)
        {
            scaleSink.SetContentScale(_contentScaleX, _contentScaleY);
        }
    }

    private static double NormalizeContentScale(double scale)
    {
        return double.IsFinite(scale) && scale > 0d ? scale : 1d;
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
        (int widthPx, int heightPx) = CalculateRenderedGridPixelSize(Columns, Rows);

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
    public ValueTask StartSessionAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, PreserveScrollbackOnSessionStart, cancellationToken);
    }

    /// <summary>
    /// Starts a terminal session using a transport-specific options payload.
    /// </summary>
    /// <param name="options">Transport-specific session options.</param>
    /// <param name="preserveScrollback">Whether the previous completed session's scrollback should be preserved.</param>
    /// <param name="cancellationToken">Cancellation token used while starting the transport.</param>
    public async ValueTask StartSessionAsync(
        ITerminalTransportOptions options,
        bool preserveScrollback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureVtProcessorPreferenceApplied();
        if (TerminalSessionService.HasActiveTransport)
        {
            throw new InvalidOperationException("A terminal transport session is already active.");
        }

        CancelPendingTransportResize();
        SetPendingTransportOutputAcceptance(acceptOutput: false);
        try
        {
            ResetPendingTransportOutputQueue();
            if (Dispatcher.UIThread.CheckAccess())
            {
                PrepareTerminalForSessionStart(preserveScrollback);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => PrepareTerminalForSessionStart(preserveScrollback));
            }
        }
        catch
        {
            SetPendingTransportOutputAcceptance(acceptOutput: true);
            throw;
        }

        SetPendingTransportOutputAcceptance(acceptOutput: true);
        _mouseModeTracker.Reset();
        ResetPointerButtons();
        _isMouseSelecting = false;
        EnsureCursorBlinkTimerRunning(false);
        unchecked
        {
            _transportSessionGeneration++;
        }

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

    private void PrepareTerminalForSessionStart(bool preserveScrollback)
    {
        if (_screen is null || _vtProcessor is null)
        {
            return;
        }

        FlushPendingTransportOutputBeforeResize();
        ClearSelection();
        ResetCursorBlinkPhase();

        int previousTotalRows = _screen.TotalRows;
        int previousMaxScrollOffset = _screen.MaxScrollOffset;
        double previousScrollExtent = _scrollData?.Extent ?? 0d;
        double previousScrollOffset = _scrollData?.Offset ?? 0d;

        lock (_screen.SyncRoot)
        {
            if (_vtProcessor is ITerminalSessionHistoryController historyController)
            {
                historyController.PrepareForNewSession(preserveScrollback);
            }
            else if (preserveScrollback)
            {
                _screen.MoveViewportToScrollbackAndClear();
                _vtProcessor.Reset();
            }
            else
            {
                _screen.ClearAll();
                _vtProcessor.Reset();
            }

            SyncHistoryMutationScrollStateLocked();
        }

        UpdateAutoScrollPinnedToBottom();
        UpdateRendererCursorForViewport();
        UpdateRendererParityStateFromScreen();
        _presenter?.Invalidate(fullRedraw: true);
        if (_screen.TotalRows != previousTotalRows ||
            _screen.MaxScrollOffset != previousMaxScrollOffset ||
            _scrollData is not null &&
            (Math.Abs(_scrollData.Extent - previousScrollExtent) > double.Epsilon ||
             Math.Abs(_scrollData.Offset - previousScrollOffset) > double.Epsilon))
        {
            RaiseScrollInvalidated();
        }
    }

    /// <summary>
    /// Starts a pipe transport-backed terminal session.
    /// </summary>
    public ValueTask StartPipeAsync(PipeTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Starts a pipe transport-backed terminal session.
    /// </summary>
    public ValueTask StartPipeAsync(
        PipeTransportOptions options,
        bool preserveScrollback,
        CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, preserveScrollback, cancellationToken);
    }

    /// <summary>
    /// Starts an SSH transport-backed terminal session.
    /// </summary>
    public ValueTask StartSshAsync(SshTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Starts an SSH transport-backed terminal session.
    /// </summary>
    public ValueTask StartSshAsync(
        SshTransportOptions options,
        bool preserveScrollback,
        CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, preserveScrollback, cancellationToken);
    }

    /// <summary>
    /// Starts a raw TCP transport-backed terminal session.
    /// </summary>
    public ValueTask StartRawTcpAsync(RawTcpTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Starts a raw TCP transport-backed terminal session.
    /// </summary>
    public ValueTask StartRawTcpAsync(
        RawTcpTransportOptions options,
        bool preserveScrollback,
        CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, preserveScrollback, cancellationToken);
    }

    /// <summary>
    /// Starts a Telnet transport-backed terminal session.
    /// </summary>
    public ValueTask StartTelnetAsync(TelnetTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Starts a Telnet transport-backed terminal session.
    /// </summary>
    public ValueTask StartTelnetAsync(
        TelnetTransportOptions options,
        bool preserveScrollback,
        CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, preserveScrollback, cancellationToken);
    }

    /// <summary>
    /// Starts a serial transport-backed terminal session.
    /// </summary>
    public ValueTask StartSerialAsync(SerialTransportOptions options, CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Starts a serial transport-backed terminal session.
    /// </summary>
    public ValueTask StartSerialAsync(
        SerialTransportOptions options,
        bool preserveScrollback,
        CancellationToken cancellationToken = default)
    {
        return StartSessionAsync(options, preserveScrollback, cancellationToken);
    }

    /// <summary>
    /// Stops the PTY and kills the child shell process.
    /// </summary>
    public void StopPty()
    {
        FlushPendingTransportResize();
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
        StopMouseSelectionDrag();
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
        int sessionGeneration = _transportSessionGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (sessionGeneration != _transportSessionGeneration)
            {
                return;
            }

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

    private void FlushPendingTransportOutputBeforeResize()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return;
        }

        DrainPendingTransportOutput(flushAll: true);
        DrainPendingTransportOutputUiBatches(flushAll: true);
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
        bool resetMouseSelection = chunks.Count == 1
            ? ProcessOutputCore(chunks[0])
            : ProcessOutputBatchCore(chunks);
        return new PendingTransportUiBatch(chunks, totalBytes, resetMouseSelection);
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

        bool resetMouseSelection = ProcessOutputBatchCore(chunks);
        if (resetMouseSelection)
        {
            ApplyMouseModeSelectionResetOnUiThread();
        }

        for (int i = 0; i < chunks.Count; i++)
        {
            DataReceived?.Invoke(this, new TerminalDataEventArgs(chunks[i]));
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
        _presenter?.Invalidate(dirtyRowsOnly: true);
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

        FlushPendingTransportResize();

        if (_vtProcessor is ITerminalPointerSequenceEncoderSource nativeEncoder &&
            TryEncodePointerWithNativeEncoder(nativeEncoder, pointerEvent, out byte[] nativeEncoded))
        {
            TerminalSessionService.SendInput(nativeEncoded);
            return true;
        }

        if (!TryEncodePointerFromTrackedMouseMode(pointerEvent, out byte[] encoded))
        {
            return false;
        }

        TerminalSessionService.SendInput(encoded);
        return true;
    }

    private bool TryEncodePointerWithNativeEncoder(
        ITerminalPointerSequenceEncoderSource nativeEncoder,
        TerminalPointerEvent pointerEvent,
        out byte[] encoded)
    {
        encoded = [];

        if (_renderer is null)
        {
            return false;
        }

        float cellWidth = _renderer.CellWidth;
        float cellHeight = _renderer.CellHeight;

        if (cellWidth <= 0 || cellHeight <= 0)
        {
            return false;
        }

        int cellWidthPx = Math.Max(1, (int)Math.Ceiling(cellWidth));
        int cellHeightPx = Math.Max(1, (int)Math.Ceiling(cellHeight));
        Size contentSize = GetTerminalContentSize(Bounds.Size);
        int screenWidthPx = Math.Max(1, (int)Math.Round(contentSize.Width));
        int screenHeightPx = Math.Max(1, (int)Math.Round(contentSize.Height));
        TerminalPointerEvent nativePointerEvent = pointerEvent;

        if (ShouldScalePointerForNativeMouseEncoding())
        {
            double widthScale = cellWidthPx / cellWidth;
            double heightScale = cellHeightPx / cellHeight;

            nativePointerEvent = pointerEvent with
            {
                X = pointerEvent.X * widthScale,
                Y = pointerEvent.Y * heightScale
            };

            screenWidthPx = Math.Max(1, (int)Math.Round(contentSize.Width * widthScale));
            screenHeightPx = Math.Max(1, (int)Math.Round(contentSize.Height * heightScale));
        }

        TerminalPointerEncodingContext context = new(
            ScreenWidthPx: screenWidthPx,
            ScreenHeightPx: screenHeightPx,
            CellWidthPx: cellWidthPx,
            CellHeightPx: cellHeightPx);

        return nativeEncoder.TryEncodePointer(nativePointerEvent, context, out encoded);
    }

    private bool TryEncodePointerFromTrackedMouseMode(TerminalPointerEvent pointerEvent, out byte[] encoded)
    {
        encoded = [];

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
                Math.Max(1, (int)Math.Floor(pointerEvent.X) + 1),
                Math.Max(1, (int)Math.Floor(pointerEvent.Y) + 1),
                out encoded))
        {
            return false;
        }

        return true;
    }

    private bool HasFractionalRendererCellMetrics()
    {
        if (_renderer is null)
        {
            return false;
        }

        // Reference terminals derive mouse cells from the same grid metrics used
        // for rendering. Avalonia cell metrics can be fractional DIPs; rounding
        // them before encoding accumulates row drift near the bottom edge.
        return !IsWholePixelMetric(_renderer.CellWidth) ||
               !IsWholePixelMetric(_renderer.CellHeight);
    }

    private bool ShouldScalePointerForNativeMouseEncoding()
    {
        return HasFractionalRendererCellMetrics() &&
               _mouseModeTracker.ModeState.Encoding != TerminalMouseEncoding.SgrPixels;
    }

    private static bool IsWholePixelMetric(float value)
    {
        return Math.Abs(value - MathF.Round(value)) < 0.001f;
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

        FlushPendingTransportResize();
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

    private void ResetPointerCell()
    {
        if (_lastPointerColumn < 0 && _lastPointerRow < 0 && _hoveredLinkUrl is null)
        {
            return;
        }

        bool hadHover = _hoveredLinkUrl is not null;
        _lastPointerColumn = -1;
        _lastPointerRow = -1;
        _hoveredLinkUrl = null;

        if (hadHover)
        {
            UpdateRendererParityStateFromScreen();
        }
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
        Span<char> runeChars = stackalloc char[2];
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
                Rune rune = new(cell.Codepoint);
                int charsWritten = rune.EncodeToUtf16(runeChars);
                _linkTokenScratch.Append(runeChars[..charsWritten]);
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
        ApplyAnchoredSelectionToRendererLocked();
        _renderer.BackgroundOpacityEnabled = _backgroundOpacityEnabled;
        _renderer.BackgroundOpacityCells = RendererBackgroundOpacityCells;
        _renderer.BackgroundOpacity = RendererBackgroundOpacity;
        _renderer.TextHighlightingMode = GetEffectiveTextHighlightingMode();
        _renderer.SetHighlightSpans(BuildHighlightSpansLocked());
    }

    private TerminalTextHighlightingMode GetEffectiveTextHighlightingMode()
    {
        return _vtProcessor?.AlternateScreen == true
            ? TerminalTextHighlightingMode.Disabled
            : _textHighlightingMode;
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

        ReadOnlySpan<char> needleText = needle.AsSpan();
        for (int absoluteRow = 0; absoluteRow < _screen.TotalRows; absoluteRow++)
        {
            TerminalRow terminalRow = _screen.GetRow(absoluteRow);
            if (!TryBuildRowTextColumnMap(terminalRow, out int rowTextLength))
            {
                continue;
            }

            ReadOnlySpan<char> rowText = _rowTextScratch.AsSpan(0, rowTextLength);
            int searchFrom = 0;
            while (searchFrom <= rowText.Length - needleText.Length)
            {
                int relativeFound = rowText[searchFrom..].IndexOf(needleText, StringComparison.Ordinal);
                if (relativeFound < 0)
                {
                    break;
                }

                int found = searchFrom + relativeFound;
                int mapEnd = found + needleText.Length - 1;
                if ((uint)found < (uint)rowTextLength &&
                    (uint)mapEnd < (uint)rowTextLength)
                {
                    int startColumn = _rowColumnMapScratch[found];
                    int endColumn = _rowColumnMapScratch[mapEnd];
                    _searchMatchScratch.Add(new TerminalSearchMatch(absoluteRow, startColumn, endColumn));
                }

                searchFrom = found + Math.Max(needleText.Length, 1);
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
        if (TryBuildRowTextColumnMap(terminalRow, out int rowTextLength))
        {
            ReadOnlySpan<char> rowText = _rowTextScratch.AsSpan(0, rowTextLength);
            ReadOnlySpan<char> urlText = url.AsSpan();
            int searchFrom = 0;
            while (searchFrom <= rowText.Length - urlText.Length)
            {
                int relativeFound = rowText[searchFrom..].IndexOf(urlText, StringComparison.Ordinal);
                if (relativeFound < 0)
                {
                    break;
                }

                int found = searchFrom + relativeFound;
                int mapEnd = found + urlText.Length - 1;
                if ((uint)found < (uint)rowTextLength &&
                    (uint)mapEnd < (uint)rowTextLength)
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

                searchFrom = found + Math.Max(urlText.Length, 1);
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

    private bool TryBuildRowTextColumnMap(TerminalRow row, out int rowTextLength)
    {
        rowTextLength = 0;

        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        for (int col = 0; col < cells.Length; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (cell.Width == 0 || (cell.Attributes & CellAttributes.Hidden) != 0)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                ReadOnlySpan<char> text = cell.Grapheme.AsSpan();
                EnsureRowTextScratchCapacity(rowTextLength + text.Length);
                text.CopyTo(_rowTextScratch.AsSpan(rowTextLength));
                _rowColumnMapScratch.AsSpan(rowTextLength, text.Length).Fill(col);
                rowTextLength += text.Length;
            }
            else if (cell.Codepoint > 0 && Rune.IsValid(cell.Codepoint))
            {
                EnsureRowTextScratchCapacity(rowTextLength + 2);
                Rune rune = new(cell.Codepoint);
                int charsWritten = rune.EncodeToUtf16(_rowTextScratch.AsSpan(rowTextLength, 2));
                _rowColumnMapScratch.AsSpan(rowTextLength, charsWritten).Fill(col);
                rowTextLength += charsWritten;
            }
        }

        return rowTextLength > 0;
    }

    private void EnsureRowTextScratchCapacity(int capacity)
    {
        if (_rowTextScratch.Length >= capacity &&
            _rowColumnMapScratch.Length >= capacity)
        {
            return;
        }

        int nextCapacity = GetRowTextScratchCapacity(capacity);
        if (_rowTextScratch.Length < capacity)
        {
            Array.Resize(ref _rowTextScratch, nextCapacity);
        }

        if (_rowColumnMapScratch.Length < capacity)
        {
            Array.Resize(ref _rowColumnMapScratch, nextCapacity);
        }
    }

    private int GetRowTextScratchCapacity(int requiredCapacity)
    {
        int currentCapacity = Math.Max(_rowTextScratch.Length, _rowColumnMapScratch.Length);
        int nextCapacity = currentCapacity == 0
            ? InitialRowTextScratchCapacity
            : currentCapacity;
        while (nextCapacity < requiredCapacity)
        {
            int doubled = nextCapacity * 2;
            if (doubled <= nextCapacity)
            {
                return requiredCapacity;
            }

            nextCapacity = doubled;
        }

        return nextCapacity;
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
        _pointerInputStartedInContent = false;
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

    private int GetScrollableRowCount()
    {
        if (_screen is null)
        {
            return 0;
        }

        return _vtProcessor?.AlternateScreen == true
            ? _screen.ViewportRows
            : _screen.TotalRows;
    }

    private bool IsScrollDataAtLiveBottom(bool treatNonScrollableAsBottom)
    {
        if (_scrollData is null)
        {
            return true;
        }

        return (_scrollData.CanScroll && _scrollData.IsAtBottom) ||
               (treatNonScrollableAsBottom && !_scrollData.CanScroll);
    }

    private void UpdateAutoScrollPinnedToBottom()
    {
        if (_scrollData is null || _screen is null)
        {
            _autoScrollPinnedToBottom = true;
            return;
        }

        _autoScrollPinnedToBottom = _scrollData.IsAtBottom && _screen.ScrollOffset == 0;
    }

    private static double GetLogicalViewportHeight(int rows, double cellHeight)
    {
        double safeCellHeight = Math.Max(1d, cellHeight);
        return Math.Max(1, rows) * safeCellHeight;
    }

    private void SyncAlternateScreenScrollState()
    {
        if (_scrollData is null || _screen is null || _vtProcessor?.AlternateScreen != true)
        {
            return;
        }

        lock (_screen.SyncRoot)
        {
            SyncAlternateScreenScrollStateLocked();
        }
    }

    private void SyncAlternateScreenScrollStateLocked()
    {
        if (_scrollData is null || _screen is null || _vtProcessor?.AlternateScreen != true)
        {
            return;
        }

        if (_vtProcessor is ITerminalViewportScrollSource viewportScrollSource
            && viewportScrollSource.ViewportScrollState.OffsetRows != 0)
        {
            viewportScrollSource.SetViewportOffsetRows(0);
        }

        double alternateExtent = _screen.ViewportRows * Math.Max(1d, _scrollData.CellHeight);
        if (Math.Abs(_scrollData.Extent - alternateExtent) > double.Epsilon)
        {
            _scrollData.Extent = alternateExtent;
        }

        _scrollData.ScrollToBottom();

        if (_screen.ScrollOffset != 0)
        {
            _screen.ScrollOffset = 0;
            _screen.InvalidateViewport();
        }
    }

    private void SyncHistoryMutationScrollStateLocked()
    {
        if (_scrollData is null || _screen is null)
        {
            return;
        }

        bool pinnedToLiveBottom = false;
        if (_vtProcessor?.AlternateScreen == true)
        {
            SyncAlternateScreenScrollStateLocked();
            pinnedToLiveBottom = true;
        }
        else if (TryGetViewportScrollSource(out ITerminalViewportScrollSource? viewportScrollSource))
        {
            viewportScrollSource.SetViewportOffsetRows(viewportScrollSource.ViewportScrollState.MaxOffsetRows);
            SyncScrollDataFromNativeViewportLocked(viewportScrollSource);
            pinnedToLiveBottom = true;
        }
        else
        {
            _screen.ScrollOffset = 0;
            _scrollData.UpdateExtent(_screen.TotalRows, true);
            _scrollData.ScrollToBottom();
            _screen.InvalidateViewport();
            pinnedToLiveBottom = true;
        }

        if (pinnedToLiveBottom && AutoScroll)
        {
            _preserveAutoScrollBottomDuringAncestorOffsetSync = true;
        }

        UpdateRendererCursorForViewportLocked();
        ApplyAnchoredSelectionToRendererLocked();
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
        double scrollableRows = viewportState.MaxOffsetRows;
        _scrollData.Extent = (scrollableRows * cellHeight) + _scrollData.Viewport;

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
        if (!TryGetViewportScrollSource(out _))
        {
            cursorRow += _screen.ScrollOffset;
        }

        _renderer.CursorColumn = cursorColumn;
        _renderer.CursorRow = cursorRow;
        bool blinkEnabled = UpdateRendererCursorStyleFromVtProcessor();

        bool rowVisible = (uint)cursorRow < (uint)_screen.ViewportRows;
        bool columnVisible = (uint)cursorColumn < (uint)_screen.Columns;
        bool baseVisible = _vtProcessor.CursorVisible && rowVisible && columnVisible;
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

        ResetCursorBlinkPhase();
    }

    private void ResetCursorBlinkPhase()
    {
        _cursorBlinkVisiblePhase = true;
        _lastBlinkCursorColumn = -1;
        _lastBlinkCursorRow = -1;
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
