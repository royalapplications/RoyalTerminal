// Licensed under the MIT License.
// GhosttySharp.Avalonia - Terminal control using Ghostty VT processing + custom SkiaSharp rendering.

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
using GhosttySharp.Avalonia.Diagnostics;
using GhosttySharp.Avalonia.Input;
using GhosttySharp.Avalonia.Rendering;
using GhosttySharp.Native;

namespace GhosttySharp.Avalonia.Controls;

/// <summary>
/// Terminal control that uses Ghostty's full VT processing and PTY management
/// but renders with the custom SkiaSharp rendering engine instead of Ghostty's
/// embedded Metal renderer. This gives you Ghostty's battle-tested terminal
/// emulation with full control over the rendering pipeline.
///
/// Architecture:
/// - Ghostty handles: VT parsing, screen state, PTY, input encoding
/// - This control handles: Reading screen state via C API, rendering via SkiaSharp
/// - A hidden NSView backs the Ghostty surface (required by the embedding API)
/// </summary>
[SupportedOSPlatform("macos")]
public class GhosttyRenderedTerminalControl : Control, IDisposable
{
    #region Fields

    private GhosttyApp? _app;
    private GhosttySurface? _surface;
    private nint _nsView;
    private nint _nsWindow;
    private bool _disposed;

    // Rendering
    private CompositionCustomVisual? _compositionVisual;
    private SkiaTerminalRenderer? _renderer;
    private TerminalScreen? _screen;

    // Cell buffer for reading from Ghostty
    private GhosttyCellInfo[]? _cellBuffer;
    private int _lastCols;
    private int _lastRows;

    // Polling timer: the embedded apprt's renderer thread draws directly
    // without dispatching the Render action, so we poll screen state on a timer.
    private DispatcherTimer? _renderTimer;

    // Set to true after a fatal error in SyncScreenFromGhostty to stop retrying.
    private bool _syncFailed;
    private IGhosttyLogger _logger = NullGhosttyLogger.Instance;

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

    private static readonly string DefaultMonoFont =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
        "Consolas";

    public static readonly StyledProperty<float> TerminalFontSizeProperty =
        AvaloniaProperty.Register<GhosttyRenderedTerminalControl, float>(nameof(TerminalFontSize), 14.0f);

    public static readonly StyledProperty<string> FontFamilyNameProperty =
        AvaloniaProperty.Register<GhosttyRenderedTerminalControl, string>(nameof(FontFamilyName), DefaultMonoFont);

    public static readonly StyledProperty<string?> WorkingDirectoryProperty =
        AvaloniaProperty.Register<GhosttyRenderedTerminalControl, string?>(nameof(WorkingDirectory));

    public static readonly StyledProperty<string?> CommandProperty =
        AvaloniaProperty.Register<GhosttyRenderedTerminalControl, string?>(nameof(Command));

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

    #endregion

    static GhosttyRenderedTerminalControl()
    {
        FocusableProperty.OverrideDefaultValue<GhosttyRenderedTerminalControl>(true);
    }

    /// <summary>
    /// Initializes the terminal with the given Ghostty app.
    /// Call this before attaching to the visual tree.
    /// </summary>
    public void Initialize(GhosttyApp app)
    {
        _app = app;

        _app.WakeupRequested += OnWakeupRequested;
        _app.ActionRequested += OnActionRequested;
        _app.ClipboardReadRequested += OnClipboardReadRequested;
        _app.ClipboardWriteRequested += OnClipboardWriteRequested;
        _app.SurfaceCloseRequested += OnSurfaceCloseRequested;

        // Initialize rendering pipeline
        _renderer = new SkiaTerminalRenderer(FontFamilyName, TerminalFontSize);
        _screen = new TerminalScreen(80, 24);
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
        _compositionVisual = null;
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

            // Start polling timer — the embedded renderer thread draws directly
            // without dispatching the Render action to our callback, so we poll
            // Ghostty's screen state on a timer (~60 fps).
            _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _renderTimer.Tick += (_, _) => SyncScreenFromGhostty();
            _renderTimer.Start();

            Logger.Debug(
                $"[GhosttyRendered] Surface created: NSView=0x{_nsView:X}, NSWindow=0x{_nsWindow:X}, " +
                $"scale={scale:F2}, fontSize={TerminalFontSize}, bounds={Bounds}");
        }
        catch (Exception ex)
        {
            Logger.Error("[GhosttyRendered] Failed to create Ghostty surface.", ex);
        }
    }

    private void DestroyGhosttySurface()
    {
        if (_renderTimer is not null)
        {
            _renderTimer.Stop();
            _renderTimer = null;
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

    private void UpdateSurfaceSize(Rect bounds)
    {
        if (_surface is null || _renderer is null || bounds.Width <= 0 || bounds.Height <= 0) return;

        var scale = VisualRoot?.RenderScaling ?? 1.0;
        _surface.SetSize(
            (uint)(bounds.Width * scale),
            (uint)(bounds.Height * scale));
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
        _compositionVisual = compositor.CreateCustomVisual(new TerminalDrawHandler());
        ElementComposition.SetElementChildVisual(this, _compositionVisual);
        _compositionVisual.Size = new Vector(Bounds.Width, Bounds.Height);

        Logger.Debug(
            $"[GhosttyRendered] Composition initialized: bounds={Bounds.Width:F0}x{Bounds.Height:F0}");

        // Send initial render state
        if (_renderer is not null && _screen is not null)
        {
            _compositionVisual.SendHandlerMessage(
                new TerminalDrawHandler.UpdateMessage(_renderer, _screen));
        }
    }

    #endregion

    #region Screen State Reading

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
                        var cellCount = _surface.GetRowCells((uint)row, _cellBuffer);
                        var termRow = _screen.GetViewportRow(row);

                        for (var col = 0; col < (int)cellCount && col < termRow.Columns; col++)
                        {
                            ref var src = ref _cellBuffer[col];
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
                                dst.Foreground = PackArgb(src.FgR, src.FgG, src.FgB);
                                dst.Background = src.HasBg != 0
                                    ? PackArgb(src.BgR, src.BgG, src.BgB)
                                    : defaultBg;
                                dst.Attributes = CellAttributes.None;
                                continue;
                            }

                            dst.Codepoint = (int)src.Codepoint;
                            dst.Foreground = PackArgb(src.FgR, src.FgG, src.FgB);
                            dst.Background = src.HasBg != 0
                                ? PackArgb(src.BgR, src.BgG, src.BgB)
                                : defaultBg;
                            dst.Attributes = ConvertAttributes(src.Attrs);
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

    private static uint PackArgb(byte r, byte g, byte b) => 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;

    private static CellAttributes ConvertAttributes(ushort attrs)
    {
        var result = CellAttributes.None;
        if ((attrs & (1 << 0)) != 0) result |= CellAttributes.Bold;
        if ((attrs & (1 << 1)) != 0) result |= CellAttributes.Italic;
        if ((attrs & (1 << 2)) != 0) result |= CellAttributes.Dim;       // faint
        if ((attrs & (1 << 3)) != 0) result |= CellAttributes.Blink;
        if ((attrs & (1 << 4)) != 0) result |= CellAttributes.Inverse;
        if ((attrs & (1 << 5)) != 0) result |= CellAttributes.Hidden;    // invisible
        if ((attrs & (1 << 6)) != 0) result |= CellAttributes.Strikethrough;
        // bit 7: overline (no mapping in CellAttributes)
        if ((attrs & 0xFF00) != 0) result |= CellAttributes.Underline;   // underline enum in upper bits
        return result;
    }

    #endregion

    #region Input Handling

    protected override unsafe void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_surface is null) return;

        var macKeycode = MacKeyMapping.ConvertKeyToMacKeycode(e.Key);
        if (macKeycode == MacKeyMapping.Unmapped) return;

        // Get the character text for this key event (layout-aware).
        // Only set text for printable characters (codepoint >= 0x20).
        // Control characters (Enter, Tab, Escape, etc.) are encoded by Ghostty
        // from the physical key identity.
        var keySymbol = e.KeySymbol;
        byte[]? textBytes = null;
        if (!string.IsNullOrEmpty(keySymbol))
        {
            var firstCodepoint = char.ConvertToUtf32(keySymbol, 0);
            if (firstCodepoint >= 0x20)
                textBytes = Encoding.UTF8.GetBytes(keySymbol + '\0');
        }

        fixed (byte* textPtr = textBytes)
        {
            var inputKey = new GhosttyInputKey
            {
                Action = GhosttyInputAction.Press,
                Keycode = macKeycode,
                Mods = MacKeyMapping.ConvertModifiers(e.KeyModifiers),
                Text = textPtr,
                Composing = false,
            };

            var accepted = _surface.SendKey(inputKey);
            Logger.Debug(
                $"[GhosttyRendered] KeyDown: key={e.Key}, keySymbol={keySymbol ?? "(null)"}, " +
                $"macKeycode=0x{macKeycode:X2}, text={(textBytes is not null ? keySymbol : "(none)")}, " +
                $"accepted={accepted}");

            if (accepted)
                e.Handled = true;
        }
    }

    protected override unsafe void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_surface is null) return;

        var macKeycode = MacKeyMapping.ConvertKeyToMacKeycode(e.Key);
        if (macKeycode == MacKeyMapping.Unmapped) return;

        fixed (byte* _ = (byte[]?)null)
        {
            var inputKey = new GhosttyInputKey
            {
                Action = GhosttyInputAction.Release,
                Keycode = macKeycode,
                Mods = MacKeyMapping.ConvertModifiers(e.KeyModifiers),
                Composing = false,
            };

            _surface.SendKey(inputKey);
            e.Handled = true;
        }
    }

    protected override unsafe void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_surface is null || string.IsNullOrEmpty(e.Text)) return;

        // For composed/IME text that wasn't handled by OnKeyDown,
        // send each character as a key event with text (matching
        // macOS native Ghostty behavior).
        var textBytes = Encoding.UTF8.GetBytes(e.Text + '\0');
        fixed (byte* textPtr = textBytes)
        {
            var inputKey = new GhosttyInputKey
            {
                Action = GhosttyInputAction.Press,
                Keycode = 0, // no physical key for composed text
                Mods = GhosttyMods.None,
                Text = textPtr,
                Composing = false,
            };

            _surface.SendKey(inputKey);
        }
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_surface is null) return;

        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        var button = props.IsLeftButtonPressed ? GhosttyMouseButton.Left
            : props.IsRightButtonPressed ? GhosttyMouseButton.Right
            : props.IsMiddleButtonPressed ? GhosttyMouseButton.Middle
            : GhosttyMouseButton.Left;

        _surface.SendMouseButton(GhosttyMouseState.Press, button, MacKeyMapping.ConvertModifiers(e.KeyModifiers));
        _surface.SendMousePos(point.X, point.Y, MacKeyMapping.ConvertModifiers(e.KeyModifiers));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_surface is null) return;

        var point = e.GetPosition(this);
        _surface.SendMousePos(point.X, point.Y, MacKeyMapping.ConvertModifiers(e.KeyModifiers));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_surface is null) return;

        var button = e.InitialPressMouseButton switch
        {
            MouseButton.Left => GhosttyMouseButton.Left,
            MouseButton.Right => GhosttyMouseButton.Right,
            MouseButton.Middle => GhosttyMouseButton.Middle,
            _ => GhosttyMouseButton.Left,
        };

        _surface.SendMouseButton(GhosttyMouseState.Release, button, MacKeyMapping.ConvertModifiers(e.KeyModifiers));
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_surface is null) return;

        _surface.SendMouseScroll(e.Delta.X, e.Delta.Y, (int)MacKeyMapping.ConvertModifiers(e.KeyModifiers));
        e.Handled = true;
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
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        Logger.Debug("[GhosttyRendered] LostFocus");
        _surface?.SetFocus(false);
    }

    #endregion

    #region Runtime Callbacks

    private void OnWakeupRequested()
    {
        Dispatcher.UIThread.Post(() => _app?.Tick());
    }

    private bool OnActionRequested(GhosttyTarget target, GhosttyAction action)
    {
        // Filter: only handle surface-targeted actions for our surface
        if (target.Tag == GhosttyTargetTag.Surface && _surface is not null
            && target.Target.Surface != _surface.Handle)
        {
            return false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            switch (action.Tag)
            {
                case GhosttyActionTag.SetTitle:
                    unsafe
                    {
                        var titlePtr = action.Action.SetTitle.Title;
                        if (titlePtr != null)
                        {
                            var title = Marshal.PtrToStringUTF8((nint)titlePtr);
                            if (title is not null)
                                TitleChanged?.Invoke(this, title);
                        }
                    }
                    break;

                case GhosttyActionTag.Render:
                    // Instead of calling _surface.Draw() (Metal), we read screen state
                    // and render with SkiaSharp
                    SyncScreenFromGhostty();
                    break;

                case GhosttyActionTag.ShowChildExited:
                    ProcessExited?.Invoke(this, (int)action.Action.ChildExited.ExitCode);
                    break;

                case GhosttyActionTag.CloseWindow:
                case GhosttyActionTag.Quit:
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        });

        return false;
    }

    private void OnClipboardReadRequested(GhosttyClipboard clipboard, nint state)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                var avClipboard = topLevel?.Clipboard;
                if (avClipboard is null || _surface is null) return;

                var text = await avClipboard.GetTextAsync();
                if (text is not null)
                    _surface.CompleteClipboardRequest(text, state, true);
            }
            catch (Exception ex)
            {
                Logger.Error($"Clipboard read error: {ex.Message}", ex);
            }
        });
    }

    private void OnClipboardWriteRequested(GhosttyClipboard clipboard, nint contentPtr, nuint len, bool confirm)
    {
        string? clipText = null;
        unsafe
        {
            for (nuint i = 0; i < len; i++)
            {
                var content = (GhosttyClipboardContent*)((byte*)contentPtr +
                    (nint)(i * (nuint)sizeof(GhosttyClipboardContent)));
                if (content->Data != null)
                    clipText = Marshal.PtrToStringUTF8((nint)content->Data);
            }
        }

        if (clipText is null) return;

        var textToWrite = clipText;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                var avClipboard = topLevel?.Clipboard;
                if (avClipboard is null) return;

                await avClipboard.SetTextAsync(textToWrite);
            }
            catch (Exception ex)
            {
                Logger.Error($"Clipboard write error: {ex.Message}", ex);
            }
        });
    }

    private void OnSurfaceCloseRequested(bool processAlive)
    {
        Dispatcher.UIThread.Post(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    #endregion

    #region Public API

    /// <summary>Sets the color scheme.</summary>
    public void SetColorScheme(GhosttyColorScheme scheme) => _surface?.SetColorScheme(scheme);

    /// <summary>Checks if the terminal has a selection.</summary>
    public bool HasSelection => _surface?.HasSelection ?? false;

    /// <summary>Copies the selection to clipboard.</summary>
    public async Task CopySelectionAsync()
    {
        if (_surface is null) return;
        var text = _surface.ReadSelection();
        if (string.IsNullOrEmpty(text)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    /// <summary>Pastes from clipboard.</summary>
    public async Task PasteAsync()
    {
        if (_surface is null) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        var text = await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text))
            _surface.SendText(text);
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

        if (_app is not null)
        {
            _app.WakeupRequested -= OnWakeupRequested;
            _app.ActionRequested -= OnActionRequested;
            _app.ClipboardReadRequested -= OnClipboardReadRequested;
            _app.ClipboardWriteRequested -= OnClipboardWriteRequested;
            _app.SurfaceCloseRequested -= OnSurfaceCloseRequested;
        }

        _renderer?.Dispose();
        _renderer = null;

        GC.SuppressFinalize(this);
    }

    #endregion
}
