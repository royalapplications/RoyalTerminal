// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Terminal control using Ghostty VT processing + custom SkiaSharp rendering.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using SkiaSharp;
using RoyalTerminal.Avalonia.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Internal.Theme;
using RoyalTerminal.Rendering.Contracts;
using RoyalTerminal.Rendering.Interop.Ghostty;
using RoyalTerminal.Rendering.Interop.Ghostty.Skia;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Terminal control that uses Ghostty's full VT processing and PTY management
/// but renders with the custom SkiaSharp rendering engine instead of Ghostty's
/// embedded Metal renderer. This gives you Ghostty's battle-tested terminal
/// emulation with full control over the rendering pipeline.
///
/// Architecture:
/// - Ghostty handles: VT parsing, screen state, PTY, input encoding
/// - This control handles: Reading screen state via C API, rendering via SkiaSharp
/// - In <see cref="GhosttyRenderedTerminalRenderingMode.CpuCellRenderer"/>, a hidden NSView backs the Ghostty surface.
/// - In <see cref="GhosttyRenderedTerminalRenderingMode.TextureInterop"/>, rendering runs through renderer interop APIs.
/// </summary>
[SupportedOSPlatform("macos")]
public class GhosttyRenderedTerminalControl : Control, IDisposable
{
    #region Fields

    private GhosttyApp? _app;
    private GhosttySurface? _surface;
    private nint _nsView;
    private nint _nsWindow;
    private GhosttyRenderContext? _interopContext;
    private GhosttyRenderSurface? _interopSurface;
    private SkiaInteropRenderer? _interopRenderer;
    private bool _disposed;
    private TerminalTheme? _theme;
    private GhosttyConfig? _appliedThemeConfig;
    private bool _suppressThemePropertyApply;
    private Func<GhosttyConfig>? _themeConfigFactory;

    // Rendering
    private CompositionCustomVisual? _compositionVisual;
    private SkiaTerminalRenderer? _renderer;
    private TerminalScreen? _screen;

    // Cell buffer for reading from Ghostty
    private GhosttyCellInfo[]? _cellBuffer;
    private GhosttyCellGraphemeSpan[]? _graphemeSpanBuffer;
    private uint[]? _graphemeCodepointBuffer;
    private int _lastCols;
    private int _lastRows;

    // Polling timer: the embedded apprt's renderer thread draws directly
    // without dispatching the Render action, so we poll screen state on a timer.
    private DispatcherTimer? _renderTimer;

    // Set to true after a fatal error in SyncScreenFromGhostty to stop retrying.
    private bool _syncFailed;
    private IGhosttyLogger _logger = NullGhosttyLogger.Instance;
    private IAvaloniaSkiaRenderTargetProvider _interopRenderTargetProvider = new AvaloniaSkiaRenderTargetProvider();
    private readonly GhosttyClipboardAdapter _clipboardAdapter;
    private readonly GhosttySurfaceLifecycle _surfaceLifecycle;

    #endregion

    #region Events

    /// <summary>Fired when the surface title changes.</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>Fired when the terminal process exits.</summary>
    public event EventHandler<int>? ProcessExited;

    /// <summary>Fired when Ghostty requests a surface close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Fired when the terminal grid dimensions change (columns/rows).</summary>
    public event EventHandler<TerminalSizeEventArgs>? TerminalResized;

    #endregion

    #region Styled Properties

    public static readonly StyledProperty<float> TerminalFontSizeProperty =
        AvaloniaProperty.Register<GhosttyRenderedTerminalControl, float>(nameof(TerminalFontSize), 14.0f);

    public static readonly StyledProperty<string> FontFamilyNameProperty =
        AvaloniaProperty.Register<GhosttyRenderedTerminalControl, string>(nameof(FontFamilyName), TerminalDefaults.DefaultMonoFont);

    public static readonly StyledProperty<string?> WorkingDirectoryProperty =
        AvaloniaProperty.Register<GhosttyRenderedTerminalControl, string?>(nameof(WorkingDirectory));

    public static readonly StyledProperty<string?> CommandProperty =
        AvaloniaProperty.Register<GhosttyRenderedTerminalControl, string?>(nameof(Command));

    public static readonly StyledProperty<GhosttyRenderedTerminalRenderingMode> RenderingModeProperty =
        AvaloniaProperty.Register<GhosttyRenderedTerminalControl, GhosttyRenderedTerminalRenderingMode>(
            nameof(RenderingMode),
            GhosttyRenderedTerminalRenderingMode.CpuCellRenderer);

    public static new readonly DirectProperty<GhosttyRenderedTerminalControl, TerminalTheme?> ThemeProperty =
        AvaloniaProperty.RegisterDirect<GhosttyRenderedTerminalControl, TerminalTheme?>(
            nameof(Theme),
            control => control.Theme,
            (control, value) => control.Theme = value);

    public float TerminalFontSize
    {
        get => GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    public string FontFamilyName
    {
        get => GetValue(FontFamilyNameProperty);
        set => SetValue(FontFamilyNameProperty, value);
    }

    public string? WorkingDirectory
    {
        get => GetValue(WorkingDirectoryProperty);
        set => SetValue(WorkingDirectoryProperty, value);
    }

    public string? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the rendering mode used by this control.
    /// </summary>
    public GhosttyRenderedTerminalRenderingMode RenderingMode
    {
        get => GetValue(RenderingModeProperty);
        set => SetValue(RenderingModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the active neutral terminal theme.
    /// </summary>
    public new TerminalTheme? Theme
    {
        get => _theme;
        set => SetAndRaise(ThemeProperty, ref _theme, value);
    }

    /// <summary>
    /// Optional factory that provides a base Ghostty config used when adapting neutral themes.
    /// </summary>
    public Func<GhosttyConfig>? ThemeConfigFactory
    {
        get => _themeConfigFactory;
        set
        {
            if (ReferenceEquals(_themeConfigFactory, value))
            {
                return;
            }

            _themeConfigFactory = value;
            if (_surface is not null)
            {
                ApplyThemeCore(ResolveActiveTheme(), updateThemeProperty: false, updateGhosttyRuntime: true);
            }
        }
    }

    /// <summary>
    /// Gets or sets the logger used for control diagnostics.
    /// Defaults to a no-op logger.
    /// </summary>
    public IGhosttyLogger Logger
    {
        get => _logger;
        set => _logger = value ?? NullGhosttyLogger.Instance;
    }

    /// <summary>Gets the underlying Ghostty surface.</summary>
    public GhosttySurface? Surface => _surface;

    /// <summary>
    /// Gets the managed Skia renderer used in <see cref="GhosttyRenderedTerminalRenderingMode.CpuCellRenderer"/>.
    /// Available after <see cref="Initialize"/> is called.
    /// </summary>
    public SkiaTerminalRenderer? Renderer => _renderer;

    /// <summary>
    /// Gets or sets the render-target adapter used in <see cref="GhosttyRenderedTerminalRenderingMode.TextureInterop"/> mode.
    /// </summary>
    public IAvaloniaSkiaRenderTargetProvider InteropRenderTargetProvider
    {
        get => _interopRenderTargetProvider;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_interopRenderTargetProvider, value))
            {
                return;
            }

            _interopRenderTargetProvider.DiagnosticReported -= OnInteropRenderTargetProviderDiagnosticReported;
            _interopRenderTargetProvider = value;
            _interopRenderTargetProvider.DiagnosticReported += OnInteropRenderTargetProviderDiagnosticReported;
        }
    }

    #endregion

    public GhosttyRenderedTerminalControl()
    {
        _theme = TerminalTheme.Dark;
        _clipboardAdapter = new GhosttyClipboardAdapter(this, () => Logger);
        GhosttyActionDispatcher actionDispatcher = new(
            () => _surface,
            OnRenderRequested,
            title => TitleChanged?.Invoke(this, title),
            exitCode => ProcessExited?.Invoke(this, exitCode),
            () => CloseRequested?.Invoke(this, EventArgs.Empty),
            OnRuntimeColorChanged,
            OnRuntimeConfigChanged,
            OnRuntimeReloadConfig);
        _surfaceLifecycle = new GhosttySurfaceLifecycle(
            () => _surface,
            actionDispatcher,
            _clipboardAdapter,
            () => _app?.Tick(),
            () => CloseRequested?.Invoke(this, EventArgs.Empty));
        _interopRenderTargetProvider.DiagnosticReported += OnInteropRenderTargetProviderDiagnosticReported;
    }

    static GhosttyRenderedTerminalControl()
    {
        FocusableProperty.OverrideDefaultValue<GhosttyRenderedTerminalControl>(true);
        RenderingModeProperty.Changed.AddClassHandler<GhosttyRenderedTerminalControl>(
            static (control, _) => control.OnRenderingModeChanged());
        ThemeProperty.Changed.AddClassHandler<GhosttyRenderedTerminalControl>(
            static (control, _) => control.OnThemePropertyChanged());
    }

    /// <summary>
    /// Initializes the terminal with the given Ghostty app.
    /// Call this before attaching to the visual tree.
    /// </summary>
    public void Initialize(GhosttyApp app)
    {
        _app = app;
        _surfaceLifecycle.Attach(app);

        // Initialize rendering pipeline
        _renderer = new SkiaTerminalRenderer(FontFamilyName, TerminalFontSize);
        _screen = new TerminalScreen(80, 24);
        ApplyThemeCore(ResolveActiveTheme(), updateThemeProperty: false, updateGhosttyRuntime: false);
    }

    #region Lifecycle Overrides

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CreateGhosttySurface();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DestroyGhosttySurface();
        ElementComposition.SetElementChildVisual(this, null);
        _compositionVisual = null;
    }

    private void OnRenderingModeChanged()
    {
        if (VisualRoot is null)
        {
            return;
        }

        DestroyGhosttySurface();
        ElementComposition.SetElementChildVisual(this, null);
        _compositionVisual = null;
        _syncFailed = false;
        CreateGhosttySurface();
        TryInitializeComposition();
    }

    #endregion

    #region Surface Management

    private unsafe void CreateGhosttySurface()
    {
        if (_app is null) return;

        try
        {
            // Create a hidden NSView for Ghostty's surface (required by embedding API)
            // It must be hosted in an off-screen NSWindow so Metal can initialise
            // its CAMetalLayer with a valid drawable.
            _nsView = ObjCRuntime.CreateNSView();
            if (_nsView == nint.Zero)
                throw new InvalidOperationException("Failed to create NSView for Ghostty");

            _nsWindow = ObjCRuntime.CreateOffscreenWindowForView(_nsView);

            var config = GhosttyNative.SurfaceConfigNew();
            config.PlatformTag = GhosttyPlatform.MacOS;
            config.Platform = new GhosttyPlatformUnion
            {
                MacOS = new GhosttyPlatformMacOS { NSView = _nsView }
            };

            var scale = VisualRoot?.RenderScaling ?? 1.0;
            config.ScaleFactor = scale;
            config.FontSize = TerminalFontSize;
            config.Context = GhosttySurfaceContext.Window;

            byte[]? wdBytes = null;
            byte[]? cmdBytes = null;

            if (WorkingDirectory is not null)
                wdBytes = Encoding.UTF8.GetBytes(WorkingDirectory + '\0');
            if (Command is not null)
                cmdBytes = Encoding.UTF8.GetBytes(Command + '\0');

            fixed (byte* wdPtr = wdBytes)
            fixed (byte* cmdPtr = cmdBytes)
            {
                config.WorkingDirectory = wdPtr;
                config.Command = cmdPtr;
                _surface = new GhosttySurface(_app, ref config);
            }

            // Set initial size based on renderer cell metrics
            UpdateSurfaceSize(Bounds.Width > 0 ? Bounds : new Rect(0, 0, 640, 480));

            _surface.SetContentScale(scale, scale);
            _surface.SetFocus(IsFocused);

            if (RenderingMode == GhosttyRenderedTerminalRenderingMode.TextureInterop)
            {
                // Keep the embedded Ghostty surface active in interop mode so screen state,
                // input, title, clipboard, and process events remain wired.
                CreateTextureInteropSurface();
            }

            ApplyThemeCore(ResolveActiveTheme(), updateThemeProperty: false, updateGhosttyRuntime: true);

            if (_renderTimer is null)
            {
                // Start polling timer — the embedded renderer thread draws directly
                // without dispatching the Render action to our callback, so we poll
                // Ghostty's screen state on a timer (~60 fps).
                _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _renderTimer.Tick += OnRenderTimerTick;
                _renderTimer.Start();
            }

            Logger.Debug(
                $"[GhosttyRendered] Surface created: NSView=0x{_nsView:X}, NSWindow=0x{_nsWindow:X}, " +
                $"scale={scale:F2}, fontSize={TerminalFontSize}, bounds={Bounds}");
        }
        catch (Exception ex)
        {
            Logger.Error("[GhosttyRendered] Failed to create Ghostty surface.", ex);
            DestroyGhosttySurface();
        }
    }

    private void CreateTextureInteropSurface()
    {
        try
        {
            _interopContext = new GhosttyRenderContext();
            _interopSurface = CreateTextureInteropSurfaceWithBackendFallback(
                _interopContext,
                out RenderBackendKind backendKind);
            _interopRenderer = new SkiaInteropRenderer(
                _interopSurface,
                new GhosttyRenderSurfaceRgbaFallbackRenderer(_interopSurface));

            UpdateSurfaceSize(Bounds.Width > 0 ? Bounds : new Rect(0, 0, 640, 480));

            double scale = VisualRoot?.RenderScaling ?? 1.0;
            _interopSurface.SetScale(scale, scale);
            _interopSurface.SetFocus(IsFocused);

            Logger.Debug(
                $"[GhosttyRendered] TextureInterop initialized: backend={backendKind}, " +
                $"scale={scale:F2}, bounds={Bounds}");
        }
        catch (Exception ex)
        {
            Logger.Error("[GhosttyRendered] Failed to initialize texture interop renderer.", ex);
            DestroyGhosttySurface();
        }
    }

    private GhosttyRenderSurface CreateTextureInteropSurfaceWithBackendFallback(
        GhosttyRenderContext context,
        out RenderBackendKind backendKind)
    {
        Exception? lastException = null;

        RenderBackendKind[] candidates = GetTextureInteropBackendCandidates();
        for (int i = 0; i < candidates.Length; i++)
        {
            RenderBackendKind candidate = candidates[i];
            try
            {
                GhosttyRenderSurface surface = context.CreateSurface(candidate);
                backendKind = candidate;
                return surface;
            }
            catch (Exception ex)
            {
                lastException = ex;
                Logger.Debug(
                    $"[GhosttyRendered] TextureInterop backend candidate '{candidate}' unavailable: {ex.Message}");
            }
        }

        backendKind = RenderBackendKind.Unknown;
        throw new InvalidOperationException(
            "Failed to create a texture-interop render surface for any backend candidate.",
            lastException);
    }

    private static RenderBackendKind[] GetTextureInteropBackendCandidates()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return [RenderBackendKind.Metal, RenderBackendKind.Software];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return [RenderBackendKind.Vulkan, RenderBackendKind.Software];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return [RenderBackendKind.D3D11, RenderBackendKind.D3D12, RenderBackendKind.Software];
        }

        return [RenderBackendKind.Software];
    }

    private void DestroyGhosttySurface()
    {
        if (_renderTimer is not null)
        {
            _renderTimer.Tick -= OnRenderTimerTick;
            _renderTimer.Stop();
            _renderTimer = null;
        }

        DisposeAppliedThemeConfig();

        _interopRenderer = null;

        if (_interopSurface is not null)
        {
            _interopSurface.Dispose();
            _interopSurface = null;
        }

        if (_interopContext is not null)
        {
            _interopContext.Dispose();
            _interopContext = null;
        }

        if (_surface is not null)
        {
            _surface.Dispose();
            _surface = null;
        }

        if (_nsView != nint.Zero)
        {
            ObjCRuntime.ReleaseNSView(_nsView);
            _nsView = nint.Zero;
        }

        if (_nsWindow != nint.Zero)
        {
            ObjCRuntime.ReleaseNSWindow(_nsWindow);
            _nsWindow = nint.Zero;
        }
    }

    private void OnRenderTimerTick(object? sender, EventArgs e)
    {
        if (RenderingMode == GhosttyRenderedTerminalRenderingMode.TextureInterop)
        {
            SyncScreenFromGhostty();
            SyncTextureInteropFrame();
            return;
        }

        SyncScreenFromGhostty();
    }

    private void UpdateSurfaceSize(Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        double scale = VisualRoot?.RenderScaling ?? 1.0;
        PixelSize pixelSize = GetPixelSize(bounds, scale);
        double pointWidth = Math.Max(1.0, bounds.Width);
        double pointHeight = Math.Max(1.0, bounds.Height);

        if (_nsView != nint.Zero)
        {
            ObjCRuntime.SetNSViewSize(_nsView, pointWidth, pointHeight);
        }

        if (_nsWindow != nint.Zero)
        {
            ObjCRuntime.SetNSWindowContentSize(_nsWindow, pointWidth, pointHeight);
        }

        if (_surface is not null && _renderer is not null)
        {
            _surface.SetContentScale(scale, scale);
            _surface.SetSize((uint)pixelSize.Width, (uint)pixelSize.Height);
        }

        if (RenderingMode == GhosttyRenderedTerminalRenderingMode.TextureInterop)
        {
            _interopSurface?.SetSize(pixelSize.Width, pixelSize.Height);
            _compositionVisual?.SendHandlerMessage(new TerminalTextureInteropDrawHandler.ResizeMessage(pixelSize));
            return;
        }
    }

    private static PixelSize GetPixelSize(Rect bounds, double scale)
    {
        int width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        return new PixelSize(width, height);
    }

    #endregion

    #region Layout

    protected override Size MeasureOverride(Size availableSize)
    {
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Initialize composition visual on first arrange with valid size
        if (_compositionVisual is null && finalSize.Width > 0 && finalSize.Height > 0)
            TryInitializeComposition();

        if (_compositionVisual is not null)
            _compositionVisual.Size = new Vector(finalSize.Width, finalSize.Height);

        UpdateSurfaceSize(new Rect(0, 0, finalSize.Width, finalSize.Height));

        if (RenderingMode == GhosttyRenderedTerminalRenderingMode.TextureInterop)
            SyncTextureInteropFrame();

        return finalSize;
    }

    /// <summary>
    /// Creates a CompositionCustomVisual directly on this control.
    /// Called from ArrangeOverride and the polling timer until it succeeds.
    /// </summary>
    private void TryInitializeComposition()
    {
        if (_compositionVisual is not null) return;

        var elementVisual = ElementComposition.GetElementVisual(this);
        if (elementVisual is null) return;

        var compositor = elementVisual.Compositor;
        _compositionVisual = RenderingMode == GhosttyRenderedTerminalRenderingMode.TextureInterop
            ? compositor.CreateCustomVisual(new TerminalTextureInteropDrawHandler())
            : compositor.CreateCustomVisual(new TerminalDrawHandler());
        ElementComposition.SetElementChildVisual(this, _compositionVisual);
        _compositionVisual.Size = new Vector(Bounds.Width, Bounds.Height);

        Logger.Debug(
            $"[GhosttyRendered] Composition initialized: bounds={Bounds.Width:F0}x{Bounds.Height:F0}");

        // Send initial render state
        if (RenderingMode == GhosttyRenderedTerminalRenderingMode.TextureInterop)
        {
            SyncTextureInteropFrame();
        }
        else if (_renderer is not null && _screen is not null)
        {
            _compositionVisual.SendHandlerMessage(
                new TerminalDrawHandler.UpdateMessage(_renderer, _screen));
        }
    }

    #endregion

    #region Screen State Reading

    private void SyncTextureInteropFrame()
    {
        if (RenderingMode != GhosttyRenderedTerminalRenderingMode.TextureInterop ||
            _interopRenderer is null)
        {
            return;
        }

        if (_compositionVisual is null)
        {
            TryInitializeComposition();
            if (_compositionVisual is null)
            {
                return;
            }
        }

        Rect bounds = Bounds.Width > 0 && Bounds.Height > 0
            ? Bounds
            : new Rect(0, 0, 1, 1);

        double scale = VisualRoot?.RenderScaling ?? 1.0;
        PixelSize pixelSize = GetPixelSize(bounds, scale);

        _interopSurface?.SetScale(scale, scale);
        _interopSurface?.SetSize(pixelSize.Width, pixelSize.Height);

        _compositionVisual.SendHandlerMessage(
            new TerminalTextureInteropDrawHandler.UpdateMessage(
                _interopRenderer,
                _interopRenderTargetProvider,
                pixelSize,
                _renderer,
                _screen));
    }

    private void OnInteropRenderTargetProviderDiagnosticReported(object? sender, string diagnostic)
    {
        Logger.Debug($"[GhosttyRendered] TextureInterop target provider diagnostic: {diagnostic}");
    }

    /// <summary>
    /// Reads the current screen state from Ghostty and updates the TerminalScreen.
    /// Called in response to Ghostty's Render action.
    /// </summary>
    private int _syncLogCounter;

    private unsafe void SyncScreenFromGhostty()
    {
        if (_surface is null || _screen is null || _renderer is null || _syncFailed) return;

        try
        {
            // Get grid dimensions from Ghostty
            var size = _surface.Size;
            var cols = (int)size.Columns;
            var rows = (int)size.Rows;
            UpdateRendererCellMetrics(size, cols, rows);

            // Periodic diagnostics
            if (++_syncLogCounter % 300 == 1) // ~every 5 seconds at 60fps
            {
                Logger.Debug(
                    $"[GhosttyRendered] SyncScreen: grid={cols}x{rows}, px={size.WidthPx}x{size.HeightPx}, cell={size.CellWidthPx}x{size.CellHeightPx}");
            }

            if (cols <= 0 || rows <= 0) return;

            // Resize screen if needed
            if (cols != _lastCols || rows != _lastRows)
            {
                lock (_screen.SyncRoot)
                {
                    _screen.Resize(cols, rows);
                }
                _lastCols = cols;
                _lastRows = rows;
                TerminalResized?.Invoke(this, new TerminalSizeEventArgs(cols, rows));
            }

            // Ensure cell buffer is large enough
            if (_cellBuffer is null || _cellBuffer.Length < cols)
                _cellBuffer = new GhosttyCellInfo[cols];
            bool supportsSurfaceRowCellGraphemes = GhosttyNative.SupportsSurfaceRowCellGraphemes;
            if (supportsSurfaceRowCellGraphemes)
            {
                if (_graphemeSpanBuffer is null || _graphemeSpanBuffer.Length < cols)
                    _graphemeSpanBuffer = new GhosttyCellGraphemeSpan[cols];

                int graphemeCapacity = Math.Max(cols * 8, cols);
                if (_graphemeCodepointBuffer is null || _graphemeCodepointBuffer.Length < graphemeCapacity)
                    _graphemeCodepointBuffer = new uint[graphemeCapacity];
            }

            GhosttyCellInfo[] cellBuffer = _cellBuffer;
            GhosttyCellGraphemeSpan[] graphemeSpanBuffer =
                supportsSurfaceRowCellGraphemes && _graphemeSpanBuffer is not null
                    ? _graphemeSpanBuffer
                    : Array.Empty<GhosttyCellGraphemeSpan>();
            uint[] graphemeCodepointBuffer =
                supportsSurfaceRowCellGraphemes && _graphemeCodepointBuffer is not null
                    ? _graphemeCodepointBuffer
                    : Array.Empty<uint>();

            var defaultBg = _screen.DefaultBackground;

            // Lock Ghostty's screen state and read cells
            _surface.ScreenLock();
            try
            {
                // Read cursor info and apply to renderer
                var cursor = _surface.GetCursorInfo();
                _renderer.CursorColumn = cursor.X;
                _renderer.CursorRow = cursor.Y;
                _renderer.CursorVisible = cursor.Visible != 0;
                _renderer.CursorStyle = cursor.Style switch
                {
                    0 => CursorStyle.Block,         // block / default
                    1 => CursorStyle.Bar,           // bar
                    2 => CursorStyle.Underline,     // underline
                    3 => CursorStyle.Block,         // block_hollow (fallback to block)
                    _ => CursorStyle.Block,
                };

                lock (_screen.SyncRoot)
                {
                    // Read each viewport row
                    for (var row = 0; row < rows && row < _screen.ViewportRows; row++)
                    {
                        uint cellCount;
                        ReadOnlySpan<uint> graphemeCodepoints = ReadOnlySpan<uint>.Empty;
                        if (supportsSurfaceRowCellGraphemes)
                        {
                            cellCount = _surface.GetRowCellsWithGraphemes(
                                (uint)row,
                                cellBuffer,
                                graphemeSpanBuffer,
                                graphemeCodepointBuffer,
                                out uint graphemeCodepointsWritten);

                            int graphemeLength = (int)Math.Min(graphemeCodepointsWritten, (uint)graphemeCodepointBuffer.Length);
                            graphemeCodepoints = graphemeCodepointBuffer.AsSpan(0, graphemeLength);
                        }
                        else
                        {
                            cellCount = _surface.GetRowCells((uint)row, cellBuffer);
                        }
                        var termRow = _screen.GetViewportRow(row);

                        for (var col = 0; col < (int)cellCount && col < termRow.Columns; col++)
                        {
                            ref var src = ref cellBuffer[col];
                            ref var dst = ref termRow.Cells[col];

                            // Wide cell mapping:
                            // 0=narrow, 1=wide, 2=spacer_tail, 3=spacer_head
                            dst.Width = src.Wide switch
                            {
                                0 => 1, // narrow — single-column character
                                1 => 2, // wide — double-column character
                                2 => 0, // spacer_tail — continuation of wide char, skip rendering
                                3 => 0, // spacer_head — pad before wide char, skip rendering
                                _ => 1,
                            };

                            // For spacer cells, clear content so they aren't rendered
                            if (dst.Width == 0)
                            {
                                dst.Codepoint = 0;
                                dst.Grapheme = null;
                                dst.Foreground = PackArgb(src.FgR, src.FgG, src.FgB);
                                dst.Background = src.HasBg != 0
                                    ? PackArgb(src.BgR, src.BgG, src.BgB)
                                    : defaultBg;
                                dst.Attributes = CellAttributes.None;
                                dst.UnderlineStyle = TerminalUnderlineStyle.None;
                                dst.Decorations = CellDecorations.None;
                                continue;
                            }

                            dst.Codepoint = (int)src.Codepoint;
                            dst.Grapheme = supportsSurfaceRowCellGraphemes
                                ? TryBuildCellGrapheme(
                                    src.Codepoint,
                                    graphemeSpanBuffer[col],
                                    graphemeCodepoints)
                                : null;
                            dst.Foreground = PackArgb(src.FgR, src.FgG, src.FgB);
                            dst.Background = src.HasBg != 0
                                ? PackArgb(src.BgR, src.BgG, src.BgB)
                                : defaultBg;
                            dst.Attributes = ConvertAttributes(src.Attrs);
                            dst.UnderlineStyle = ConvertUnderlineStyle(src.Attrs);
                            dst.Decorations = ConvertDecorations(src.Attrs);
                        }

                        for (var col = (int)cellCount; col < termRow.Columns; col++)
                        {
                            ref var dst = ref termRow.Cells[col];
                            dst.Codepoint = 0;
                            dst.Grapheme = null;
                            dst.Foreground = _screen.DefaultForeground;
                            dst.Background = _screen.DefaultBackground;
                            dst.Attributes = CellAttributes.None;
                            dst.UnderlineStyle = TerminalUnderlineStyle.None;
                            dst.Decorations = CellDecorations.None;
                            dst.Width = 1;
                        }

                        termRow.IsDirty = true;
                    }
                }
            }
            finally
            {
                _surface.ScreenUnlock();
            }

            // Trigger re-render — retry composition init if needed,
            // then send update to the draw handler.
            if (_compositionVisual is null)
                TryInitializeComposition();

            if (_compositionVisual is not null)
            {
                _compositionVisual.SendHandlerMessage(
                    new TerminalDrawHandler.UpdateMessage(_renderer, _screen));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[GhosttyRendered] SyncScreenFromGhostty FATAL: {ex.GetType().Name}: {ex.Message}", ex);
            // Stop retrying — the error is likely permanent (e.g. missing native symbol)
            _syncFailed = true;
        }
    }

    private void UpdateRendererCellMetrics(GhosttySurfaceSize size, int cols, int rows)
    {
        if (_renderer is null || cols <= 0 || rows <= 0)
        {
            return;
        }

        double scale = VisualRoot?.RenderScaling ?? 1.0;
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            scale = 1.0;
        }

        float cellWidth;
        float cellHeight;

        if (size.CellWidthPx > 0 && size.CellHeightPx > 0)
        {
            cellWidth = (float)(size.CellWidthPx / scale);
            cellHeight = (float)(size.CellHeightPx / scale);
        }
        else
        {
            cellWidth = (float)(Bounds.Width / cols);
            cellHeight = (float)(Bounds.Height / rows);
        }

        if (cellWidth <= 0 || cellHeight <= 0 || float.IsNaN(cellWidth) || float.IsNaN(cellHeight))
        {
            return;
        }

        _renderer.SetCellSize(cellWidth, cellHeight);
    }

    private static uint PackArgb(byte r, byte g, byte b) => 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;

    private static string? TryBuildCellGrapheme(
        uint primaryCodepoint,
        GhosttyCellGraphemeSpan span,
        ReadOnlySpan<uint> graphemeCodepoints)
    {
        if (span.Length == 0 || span.Offset > int.MaxValue || span.Length > int.MaxValue)
        {
            return null;
        }

        int offset = (int)span.Offset;
        int length = (int)span.Length;
        if (offset < 0 || length < 0 || offset > graphemeCodepoints.Length - length)
        {
            return null;
        }

        if (!Rune.IsValid((int)primaryCodepoint))
        {
            return null;
        }

        StringBuilder sb = new();
        sb.Append(char.ConvertFromUtf32((int)primaryCodepoint));
        ReadOnlySpan<uint> trailing = graphemeCodepoints.Slice(offset, length);
        for (int i = 0; i < trailing.Length; i++)
        {
            uint cp = trailing[i];
            if (!Rune.IsValid((int)cp))
            {
                return null;
            }

            sb.Append(char.ConvertFromUtf32((int)cp));
        }

        return sb.ToString();
    }

    private static CellAttributes ConvertAttributes(ushort attrs)
    {
        var result = CellAttributes.None;
        if ((attrs & (1 << 0)) != 0) result |= CellAttributes.Bold;
        if ((attrs & (1 << 1)) != 0) result |= CellAttributes.Italic;
        if ((attrs & (1 << 2)) != 0) result |= CellAttributes.Dim;
        if ((attrs & (1 << 3)) != 0) result |= CellAttributes.Inverse;
        if ((attrs & (1 << 4)) != 0) result |= CellAttributes.Hidden;
        if ((attrs & (1 << 5)) != 0) result |= CellAttributes.Strikethrough;
        if (ConvertUnderlineStyle(attrs) != TerminalUnderlineStyle.None) result |= CellAttributes.Underline;
        if ((attrs & (1 << 7)) != 0) result |= CellAttributes.Blink;
        return result;
    }

    private static TerminalUnderlineStyle ConvertUnderlineStyle(ushort attrs)
    {
        return ((attrs >> 8) & 0x7) switch
        {
            1 => TerminalUnderlineStyle.Single,
            2 => TerminalUnderlineStyle.Double,
            3 => TerminalUnderlineStyle.Curly,
            4 => TerminalUnderlineStyle.Dotted,
            5 => TerminalUnderlineStyle.Dashed,
            _ => TerminalUnderlineStyle.None,
        };
    }

    private static CellDecorations ConvertDecorations(ushort attrs)
    {
        CellDecorations decorations = CellDecorations.None;
        if ((attrs & (1 << 6)) != 0)
        {
            decorations |= CellDecorations.Overline;
        }

        return decorations;
    }

    private void OnThemePropertyChanged()
    {
        if (_suppressThemePropertyApply)
        {
            return;
        }

        ApplyThemeCore(ResolveActiveTheme(), updateThemeProperty: false, updateGhosttyRuntime: true);
    }

    private TerminalTheme ResolveActiveTheme()
    {
        return Theme ?? _theme ?? TerminalTheme.Dark;
    }

    private static RenderTheme ToRenderTheme(TerminalTheme theme)
    {
        return new RenderTheme(
            theme.DefaultForeground,
            theme.DefaultBackground,
            theme.CursorColor);
    }

    private static GhosttyColorScheme ToLegacyColorScheme(TerminalTheme theme)
    {
        return IsPerceivedLightColor(theme.DefaultBackground)
            ? GhosttyColorScheme.Light
            : GhosttyColorScheme.Dark;
    }

    private static uint ToLegacyColorSchemeId(TerminalTheme theme)
    {
        return ToLegacyColorScheme(theme) == GhosttyColorScheme.Light ? 0u : 1u;
    }

    private static bool IsPerceivedLightColor(uint argb)
    {
        int red = (int)((argb >> 16) & 0xFF);
        int green = (int)((argb >> 8) & 0xFF);
        int blue = (int)(argb & 0xFF);
        int luminance = ((red * 299) + (green * 587) + (blue * 114)) / 1000;
        return luminance >= 128;
    }

    private static void ApplyThemeToRenderer(TerminalTheme theme, SkiaTerminalRenderer renderer)
    {
        renderer.CursorColor = new SKColor(
            (byte)((theme.CursorColor >> 16) & 0xFF),
            (byte)((theme.CursorColor >> 8) & 0xFF),
            (byte)(theme.CursorColor & 0xFF),
            (byte)((theme.CursorColor >> 24) & 0xFF));

        if (theme.SelectionBackground is uint selectionBg)
        {
            uint translucent = (selectionBg & 0x00FFFFFFu) | 0x80000000u;
            renderer.SelectionColor = new SKColor(
                (byte)((translucent >> 16) & 0xFF),
                (byte)((translucent >> 8) & 0xFF),
                (byte)(translucent & 0xFF),
                (byte)((translucent >> 24) & 0xFF));
        }
    }

    private void ApplyThemeCore(TerminalTheme theme, bool updateThemeProperty, bool updateGhosttyRuntime)
    {
        _theme = theme;

        if (updateThemeProperty)
        {
            _suppressThemePropertyApply = true;
            try
            {
                SetCurrentValue(ThemeProperty, theme);
            }
            finally
            {
                _suppressThemePropertyApply = false;
            }
        }

        if (_screen is not null)
        {
            lock (_screen.SyncRoot)
            {
                _screen.ApplyTheme(theme, invalidateRows: true);
            }
        }

        if (_renderer is not null)
        {
            ApplyThemeToRenderer(theme, _renderer);
        }

        if (_interopSurface is not null)
        {
            _interopSurface.SetColorScheme(ToLegacyColorSchemeId(theme));
            _interopSurface.SetTheme(ToRenderTheme(theme));
        }

        if (updateGhosttyRuntime && _surface is not null)
        {
            _surface.SetColorScheme(ToLegacyColorScheme(theme));
            ApplyThemeToGhosttySurface(theme);
        }

        if (_compositionVisual is not null && _renderer is not null && _screen is not null)
        {
            if (RenderingMode == GhosttyRenderedTerminalRenderingMode.TextureInterop)
            {
                SyncTextureInteropFrame();
            }
            else
            {
                _compositionVisual.SendHandlerMessage(
                    new TerminalDrawHandler.UpdateMessage(_renderer, _screen));
            }
        }

        InvalidateVisual();
    }

    private void ApplyThemeToGhosttySurface(TerminalTheme theme)
    {
        if (_surface is null)
        {
            return;
        }

        GhosttyConfig? previousConfig = _appliedThemeConfig;
        GhosttyConfig? nextConfig = null;
        try
        {
            nextConfig = GhosttyThemeCompatibilityAdapter.CreateConfigForTheme(theme, _themeConfigFactory);
            _surface.UpdateConfig(nextConfig);
            _appliedThemeConfig = nextConfig;
            nextConfig = null;
            previousConfig?.Dispose();
        }
        catch (Exception ex)
        {
            nextConfig?.Dispose();
            Logger.Error("[GhosttyRendered] Failed to apply theme config.", ex);
        }
    }

    private void DisposeAppliedThemeConfig()
    {
        _appliedThemeConfig?.Dispose();
        _appliedThemeConfig = null;
    }

    private void OnRuntimeColorChanged(GhosttyColorChange change)
    {
        TerminalTheme current = ResolveActiveTheme();
        uint color = PackArgb(change.R, change.G, change.B);
        int kind = (int)change.Kind;

        TerminalTheme? next = null;
        if (kind == (int)GhosttyColorKind.Foreground)
        {
            if (current.DefaultForeground != color)
            {
                next = current.WithDefaultForeground(color);
            }
        }
        else if (kind == (int)GhosttyColorKind.Background)
        {
            if (current.DefaultBackground != color)
            {
                next = current.WithDefaultBackground(color);
            }
        }
        else if (kind == (int)GhosttyColorKind.Cursor)
        {
            if (current.CursorColor != color)
            {
                next = current.WithCursorColor(color);
            }
        }
        else if ((uint)kind < 256u)
        {
            if (current.Palette[kind] != color)
            {
                next = current.WithPaletteColor(kind, color, explicitOverride: true);
            }
        }

        if (next is not null)
        {
            ApplyThemeCore(next, updateThemeProperty: true, updateGhosttyRuntime: false);
        }
    }

    private void OnRuntimeConfigChanged(nint configHandle)
    {
        // Ghostty runtime config-change actions can carry transient pointers.
        // Avoid dereferencing the native config handle from managed code here.
        // We keep runtime stable and let explicit theme application drive state.
        Logger.Debug($"[GhosttyRendered] ConfigChange action received (handle=0x{configHandle:X}).");
    }

    private void OnRuntimeReloadConfig(bool soft)
    {
        Logger.Debug($"[GhosttyRendered] ReloadConfig action received (soft={soft}).");
        if (RenderingMode == GhosttyRenderedTerminalRenderingMode.TextureInterop)
        {
            SyncTextureInteropFrame();
        }
        else
        {
            SyncScreenFromGhostty();
        }
    }

    #endregion

    #region Input Handling

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool handled = GhosttyInputPipeline.HandleKeyDown(
            _surface,
            e,
            dispatch =>
            {
                string text = dispatch.HasText ? dispatch.KeySymbol ?? string.Empty : "(none)";
                Logger.Debug(
                    $"[GhosttyRendered] KeyDown: key={e.Key}, keySymbol={dispatch.KeySymbol ?? "(null)"}, " +
                    $"macKeycode=0x{dispatch.MacKeycode:X2}, text={text}, accepted={dispatch.Accepted}");
            });

        if (handled)
        {
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (GhosttyInputPipeline.HandleKeyUp(_surface, e))
        {
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (GhosttyInputPipeline.HandleTextInput(_surface, e))
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (GhosttyInputPipeline.HandlePointerPressed(this, _surface, e))
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        GhosttyInputPipeline.HandlePointerMoved(this, _surface, e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (GhosttyInputPipeline.HandlePointerReleased(_surface, e))
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (GhosttyInputPipeline.HandlePointerWheelChanged(_surface, e))
        {
            e.Handled = true;
        }
    }

    #endregion

    #region Focus

    public override void Render(DrawingContext context)
    {
        // Draw background — ensures this control is hit-testable for pointer
        // events so clicking anywhere on the terminal area grants focus. The
        // composition visual renders the actual terminal content on top.
        var bg = _screen?.DefaultBackground ?? 0xFF1E1E1Eu;
        var color = Color.FromArgb(
            (byte)(bg >> 24), (byte)(bg >> 16), (byte)(bg >> 8), (byte)bg);
        context.FillRectangle(new SolidColorBrush(color), new Rect(Bounds.Size));
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        Logger.Debug("[GhosttyRendered] GotFocus");
        _surface?.SetFocus(true);
        _interopSurface?.SetFocus(true);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        Logger.Debug("[GhosttyRendered] LostFocus");
        _surface?.SetFocus(false);
        _interopSurface?.SetFocus(false);
    }

    #endregion

    #region Runtime Callbacks

    private void OnRenderRequested()
    {
        // Instead of calling _surface.Draw() (Metal), we read screen state
        // and render with SkiaSharp or texture interop mode.
        if (RenderingMode == GhosttyRenderedTerminalRenderingMode.TextureInterop)
        {
            SyncScreenFromGhostty();
            SyncTextureInteropFrame();
            return;
        }

        SyncScreenFromGhostty();
    }

    #endregion

    #region Public API

    /// <summary>Sets the color scheme.</summary>
    public void SetColorScheme(GhosttyColorScheme scheme)
    {
        TerminalTheme mappedTheme = scheme == GhosttyColorScheme.Light
            ? TerminalTheme.Light
            : TerminalTheme.Dark;
        ApplyTheme(mappedTheme);
    }

    /// <summary>
    /// Applies a neutral terminal theme at runtime.
    /// </summary>
    public void ApplyTheme(TerminalTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        SetCurrentValue(ThemeProperty, theme);
    }

    /// <summary>Checks if the terminal has a selection.</summary>
    public bool HasSelection => _surface?.HasSelection ?? false;

    /// <summary>Copies the selection to clipboard.</summary>
    public async Task CopySelectionAsync()
    {
        await _clipboardAdapter.CopySelectionAsync(_surface);
    }

    /// <summary>Pastes from clipboard.</summary>
    public async Task PasteAsync()
    {
        await _clipboardAdapter.PasteAsync(_surface);
    }

    /// <summary>Sends text to the terminal.</summary>
    public void SendInput(string text) => _surface?.SendText(text);

    /// <summary>Requests the surface to close.</summary>
    public void RequestClose() => _surface?.RequestClose();

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DestroyGhosttySurface();
        _surfaceLifecycle.Dispose();
        _interopRenderTargetProvider.DiagnosticReported -= OnInteropRenderTargetProviderDiagnosticReported;

        _renderer?.Dispose();
        _renderer = null;

        GC.SuppressFinalize(this);
    }

    #endregion
}
