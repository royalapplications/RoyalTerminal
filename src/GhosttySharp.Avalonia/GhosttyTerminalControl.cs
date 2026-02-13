// Licensed under the MIT License.
// GhosttySharp.Avalonia - Main terminal control.

using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using GhosttySharp.Avalonia.Rendering;
using GhosttySharp.Avalonia.Scrolling;
using GhosttySharp.Avalonia.Terminal;
using GhosttySharp.Native;
using SkiaSharp;

namespace GhosttySharp.Avalonia;

/// <summary>
/// Full-featured Avalonia terminal control backed by Ghostty.
/// Features:
/// - SkiaSharp rendering via Avalonia CustomVisual composition
/// - Keyboard input with IME support
/// - Mouse input (clicks, selection, scrolling)
/// - Virtualized scrolling with large buffer support
/// - Focus management
/// - Content scaling (DPI awareness)
/// </summary>
public class GhosttyTerminalControl : TemplatedControl, ILogicalScrollable
{
    #region Styled Properties

    /// <summary>Platform-appropriate default monospace font.</summary>
    private static readonly string DefaultMonoFont =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
        "Consolas";

    /// <summary>The font family used for terminal text.</summary>
    public static readonly StyledProperty<string> FontFamilyNameProperty =
        AvaloniaProperty.Register<GhosttyTerminalControl, string>(nameof(FontFamilyName), DefaultMonoFont);

    /// <summary>The font size for terminal text.</summary>
    public static readonly StyledProperty<double> TerminalFontSizeProperty =
        AvaloniaProperty.Register<GhosttyTerminalControl, double>(nameof(TerminalFontSize), 14.0);

    /// <summary>Number of columns in the terminal grid.</summary>
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<GhosttyTerminalControl, int>(nameof(Columns), 80);

    /// <summary>Number of rows in the terminal viewport.</summary>
    public static readonly StyledProperty<int> RowsProperty =
        AvaloniaProperty.Register<GhosttyTerminalControl, int>(nameof(Rows), 24);

    /// <summary>Maximum number of scrollback rows.</summary>
    public static readonly StyledProperty<int> ScrollbackLimitProperty =
        AvaloniaProperty.Register<GhosttyTerminalControl, int>(nameof(ScrollbackLimit), 10_000);

    /// <summary>Default foreground color.</summary>
    public static readonly StyledProperty<Color> DefaultForegroundProperty =
        AvaloniaProperty.Register<GhosttyTerminalControl, Color>(nameof(DefaultForeground),
            Color.FromRgb(0xD4, 0xD4, 0xD4));

    /// <summary>Default background color.</summary>
    public static readonly StyledProperty<Color> DefaultBackgroundProperty =
        AvaloniaProperty.Register<GhosttyTerminalControl, Color>(nameof(DefaultBackground),
            Color.FromRgb(0x1E, 0x1E, 0x1E));

    /// <summary>Whether to auto-scroll to bottom on new output.</summary>
    public static readonly StyledProperty<bool> AutoScrollProperty =
        AvaloniaProperty.Register<GhosttyTerminalControl, bool>(nameof(AutoScroll), true);

    /// <summary>When true, forces use of <see cref="GhosttyVtProcessor"/> (native libghostty-terminal).
    /// When false (default), auto-detects the best available VT processor.</summary>
    public static readonly StyledProperty<bool?> UseNativeVtProcessorProperty =
        AvaloniaProperty.Register<GhosttyTerminalControl, bool?>(nameof(UseNativeVtProcessor));

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
    /// Gets or sets whether to force use of the native Ghostty VT processor
    /// (<c>libghostty-terminal</c>). When <c>null</c> (default), auto-detects.
    /// Set to <c>true</c> to require it, or <c>false</c> to force <see cref="BasicVtProcessor"/>.
    /// </summary>
    public bool? UseNativeVtProcessor
    {
        get => GetValue(UseNativeVtProcessorProperty);
        set => SetValue(UseNativeVtProcessorProperty, value);
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

    private GhosttyTerminalPresenter? _presenter;
    private SkiaTerminalRenderer? _renderer;
    private TerminalScreen? _screen;
    private TerminalScrollData? _scrollData;
    private VirtualizedTerminalScrollViewer? _scrollViewer;

    private GhosttySurface? _surface;
    private IVtProcessor? _vtProcessor;
    private IPty? _pty;
    private bool _isMouseSelecting;
    private Point _selectionStartPoint;

    /// <summary>Gets the underlying Ghostty surface, if connected.</summary>
    public GhosttySurface? Surface => _surface;

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

    static GhosttyTerminalControl()
    {
        FocusableProperty.OverrideDefaultValue<GhosttyTerminalControl>(true);
    }

    public GhosttyTerminalControl()
    {
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

        _vtProcessor = CreateVtProcessor(_screen);

        _renderer = new SkiaTerminalRenderer(FontFamilyName, (float)TerminalFontSize);

        _scrollData = new TerminalScrollData
        {
            CellHeight = _renderer.CellHeight,
            Viewport = Rows * _renderer.CellHeight,
        };
        _scrollData.UpdateExtent(_screen.TotalRows, true);

        _scrollViewer = new VirtualizedTerminalScrollViewer(_screen, _scrollData);
    }

    /// <summary>
    /// Creates the best available VT processor. Respects <see cref="UseNativeVtProcessor"/>:
    /// <c>true</c> = force native, <c>false</c> = force basic, <c>null</c> = auto-detect.
    /// </summary>
    private IVtProcessor CreateVtProcessor(TerminalScreen screen)
    {
        var preference = UseNativeVtProcessor;

        // If explicitly disabled, skip native detection
        if (preference == false)
            return new BasicVtProcessor(screen);

        // Try native processor
        if (GhosttyVtProcessor.IsAvailable())
        {
            try
            {
                return new GhosttyVtProcessor(screen);
            }
            catch
            {
                if (preference == true)
                    throw; // Caller explicitly requested native — propagate
            }
        }
        else if (preference == true)
        {
            throw new InvalidOperationException(
                "Native VT processor was requested but libghostty-terminal is not available.");
        }

        return new BasicVtProcessor(screen);
    }

    /// <summary>
    /// Gets whether the native Ghostty VT processor is being used
    /// instead of the fallback BasicVtProcessor.
    /// </summary>
    public bool IsUsingNativeVtProcessor => _vtProcessor is GhosttyVtProcessor;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // Look for the presenter in the template, or create one
        _presenter = e.NameScope.Find<GhosttyTerminalPresenter>("PART_Presenter");
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

        _presenter = new GhosttyTerminalPresenter();
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
                Columns = newCols;
                Rows = newRows;

                if (_screen is not null)
                {
                    lock (_screen.SyncRoot)
                    {
                        _screen.Resize(newCols, newRows);
                        _vtProcessor?.NotifyResize(newCols, newRows,
                            (int)finalSize.Width, (int)finalSize.Height);
                    }
                }

                _scrollData?.UpdateExtent(_screen?.TotalRows ?? 0, AutoScroll);
                _scrollViewer?.UpdateViewport(finalSize.Height, _renderer.CellHeight);
                RaiseScrollInvalidated();

                // Notify the native surface about the resize
                if (_surface is not null)
                {
                    _surface.SetSize((uint)finalSize.Width, (uint)finalSize.Height);
                }

                // Notify the PTY about the resize (standalone mode)
                _pty?.Resize(newCols, newRows, (int)finalSize.Width, (int)finalSize.Height);

                TerminalResized?.Invoke(this, new TerminalSizeEventArgs(newCols, newRows));
                _presenter?.NotifyResize(finalSize);
                _presenter?.Invalidate();

                // Columns/Rows changed — next measure must reflect the new cell grid
                InvalidateMeasure();
            }
        }

        return finalSize;
    }

    #region Ghostty Surface Integration

    /// <summary>
    /// Connects this control to a Ghostty surface for terminal I/O.
    /// </summary>
    public void AttachSurface(GhosttySurface surface)
    {
        DetachSurface();
        _surface = surface;
    }

    /// <summary>
    /// Disconnects the current Ghostty surface.
    /// </summary>
    public void DetachSurface()
    {
        _surface = null;
    }

    /// <summary>
    /// Writes data to the terminal screen model.
    /// Called when output is received from the PTY/Ghostty surface.
    /// </summary>
    public void WriteOutput(ReadOnlySpan<byte> data)
    {
        if (_screen is null) return;

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

        // Raise event
        DataReceived?.Invoke(this, new TerminalDataEventArgs(data.ToArray()));

        // Scroll to bottom on new output
        if (AutoScroll)
            _scrollData?.ScrollToBottom();

        // Request re-render
        _presenter?.Invalidate();
        RaiseScrollInvalidated();
    }

    /// <summary>
    /// Sends input text to the Ghostty surface.
    /// </summary>
    public void SendInput(string text)
    {
        _surface?.SendText(text);
    }

    /// <summary>
    /// Sends input bytes to the Ghostty surface.
    /// </summary>
    public void SendInput(ReadOnlySpan<byte> data)
    {
        _surface?.SendText(data);
    }

    #endregion

    #region Keyboard Input

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_surface is not null)
        {
            // Native Ghostty surface handles input
            var mods = ConvertModifiers(e.KeyModifiers);
            var key = ConvertKey(e.Key);

            var inputKey = new GhosttyInputKey
            {
                Action = GhosttyInputAction.Press,
                Keycode = (uint)key,
                Mods = mods,
                Composing = false,
            };

            _surface.SendKey(inputKey);
            e.Handled = true;
        }
        else if (_pty is not null)
        {
            // Standalone mode — send special keys to PTY
            var seq = KeyToAnsiSequence(e.Key, e.KeyModifiers);
            if (seq is not null)
            {
                _pty.Write(seq);
                e.Handled = true;
            }
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (_surface is null) return;

        var mods = ConvertModifiers(e.KeyModifiers);
        var key = ConvertKey(e.Key);

        var inputKey = new GhosttyInputKey
        {
            Action = GhosttyInputAction.Release,
            Keycode = (uint)key,
            Mods = mods,
            Composing = false,
        };

        _surface.SendKey(inputKey);
        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (string.IsNullOrEmpty(e.Text)) return;

        if (_surface is not null)
        {
            _surface.SendText(e.Text);
        }
        else if (_pty is not null)
        {
            _pty.Write(e.Text);
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    #endregion

    #region Mouse Input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        if (_renderer is null) return;

        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (_surface is not null)
        {
            var button = props.IsLeftButtonPressed ? GhosttyMouseButton.Left
                : props.IsRightButtonPressed ? GhosttyMouseButton.Right
                : props.IsMiddleButtonPressed ? GhosttyMouseButton.Middle
                : GhosttyMouseButton.Left;

            var mods = ConvertModifiers(e.KeyModifiers);
            _surface.SendMouseButton(GhosttyMouseState.Press, button, mods);
        }

        // Start text selection on left click
        if (props.IsLeftButtonPressed)
        {
            _isMouseSelecting = true;
            _selectionStartPoint = point;

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

        if (_renderer is null) return;

        var point = e.GetPosition(this);
        var col = (int)(point.X / _renderer.CellWidth);
        var row = (int)(point.Y / _renderer.CellHeight);

        if (_surface is not null)
        {
            var moveMods = ConvertModifiers(e.KeyModifiers);
            _surface.SendMousePos(point.X, point.Y, moveMods);
        }

        if (_isMouseSelecting)
        {
            _renderer.SelectionEnd = (col, row);
            _screen?.InvalidateAll();
            _presenter?.Invalidate();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_surface is not null)
        {
            var button = e.InitialPressMouseButton switch
            {
                MouseButton.Left => GhosttyMouseButton.Left,
                MouseButton.Right => GhosttyMouseButton.Right,
                MouseButton.Middle => GhosttyMouseButton.Middle,
                _ => GhosttyMouseButton.Left,
            };

            var mods = ConvertModifiers(e.KeyModifiers);
            _surface.SendMouseButton(GhosttyMouseState.Release, button, mods);
        }

        _isMouseSelecting = false;
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        _scrollViewer?.HandleWheel(e.Delta.Y);

        if (_surface is not null)
        {
            var mods = ConvertModifiers(e.KeyModifiers);
            _surface.SendMouseScroll(e.Delta.X, e.Delta.Y, (int)mods);
        }

        _presenter?.Invalidate();
        RaiseScrollInvalidated();
        e.Handled = true;
    }

    #endregion

    #region Focus

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _surface?.SetFocus(true);
        _renderer?.SetCursorVisible(true);
        _presenter?.Invalidate();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _surface?.SetFocus(false);
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
        string? text = null;

        if (_surface is not null)
        {
            text = _surface.ReadSelection();
        }
        else if (_screen is not null && _renderer is not null)
        {
            text = GetSelectedText();
        }

        if (string.IsNullOrEmpty(text)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    /// <summary>
    /// Pastes text from the clipboard into the terminal.
    /// </summary>
    public async Task PasteAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        var text = await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text))
            SendInput(text);
    }

    /// <summary>
    /// Clears the current text selection.
    /// </summary>
    public void ClearSelection()
    {
        if (_renderer is null) return;
        _renderer.SelectionStart = null;
        _renderer.SelectionEnd = null;
        _screen?.InvalidateAll();
        _presenter?.Invalidate();
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
        _scrollData?.ScrollByRows(rows);
        _screen?.InvalidateAll();
        _presenter?.Invalidate();
    }

    /// <summary>
    /// Scrolls to the bottom of the terminal output.
    /// </summary>
    public void ScrollToBottom()
    {
        _scrollData?.ScrollToBottom();
        _screen?.InvalidateAll();
        _presenter?.Invalidate();
    }

    /// <summary>
    /// Updates the content scale for DPI changes.
    /// </summary>
    public void SetContentScale(double scaleX, double scaleY)
    {
        _surface?.SetContentScale(scaleX, scaleY);
    }

    #endregion

    #region Standalone PTY Mode

    /// <summary>
    /// Starts a shell process with a PTY in standalone mode (without Ghostty native library).
    /// On macOS/Linux uses POSIX forkpty(); on Windows uses ConPTY.
    /// </summary>
    /// <param name="shell">Shell path, or null for auto-detect.</param>
    /// <param name="workingDirectory">Working directory, or null for home.</param>
    public void StartPty(string? shell = null, string? workingDirectory = null)
    {
        if (_pty is not null) return;

        IPty pty;
        if (OperatingSystem.IsWindows())
        {
            var winPty = new WindowsPty();
            winPty.Start(shell, Columns, Rows, workingDirectory);
            pty = winPty;
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var unixPty = new UnixPty();
            unixPty.Start(shell, Columns, Rows, workingDirectory);
            pty = unixPty;
        }
        else
        {
            throw new PlatformNotSupportedException("No PTY implementation available for this platform.");
        }

        pty.DataReceived += OnPtyDataReceived;
        pty.ProcessExited += OnPtyProcessExited;
        _pty = pty;

        // Wire up VT processor responses (DSR, DA, etc.) to write back to the PTY
        if (_vtProcessor is not null)
        {
            _vtProcessor.ResponseCallback = OnVtProcessorResponse;
            _vtProcessor.BellCallback = OnVtProcessorBell;
            _vtProcessor.TitleCallback = OnVtProcessorTitleChanged;
        }
    }

    /// <summary>
    /// Stops the PTY and kills the child shell process.
    /// </summary>
    public void StopPty()
    {
        if (_pty is null) return;

        if (_vtProcessor is not null)
        {
            _vtProcessor.ResponseCallback = null;
            _vtProcessor.BellCallback = null;
            _vtProcessor.TitleCallback = null;
        }

        _pty.DataReceived -= OnPtyDataReceived;
        _pty.ProcessExited -= OnPtyProcessExited;
        _pty.Dispose();
        _pty = null;

        _vtProcessor?.Dispose();
    }

    /// <summary>Whether a standalone PTY is active.</summary>
    public bool HasPty => _pty is not null;

    /// <summary>Gets the managed PTY, if started.</summary>
    public IPty? Pty => _pty;

    /// <summary>
    /// Handles terminal query responses (DSR, DA, etc.) by writing them back to the PTY.
    /// Called from the VT processor when it detects a query sequence in the output stream.
    /// </summary>
    private void OnVtProcessorResponse(byte[] data)
    {
        _pty?.Write(data, 0, data.Length);
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
            // Write exit message to screen
            var msg = $"\r\n[Process exited with code {exitCode}]\r\n";
            var bytes = Encoding.UTF8.GetBytes(msg);
            WriteOutput(bytes);
        });
    }

    #endregion

    #region Helpers — Key and Modifier Conversion

    private static GhosttyMods ConvertModifiers(KeyModifiers keyModifiers)
    {
        var mods = GhosttyMods.None;
        if (keyModifiers.HasFlag(KeyModifiers.Shift)) mods |= GhosttyMods.Shift;
        if (keyModifiers.HasFlag(KeyModifiers.Control)) mods |= GhosttyMods.Ctrl;
        if (keyModifiers.HasFlag(KeyModifiers.Alt)) mods |= GhosttyMods.Alt;
        if (keyModifiers.HasFlag(KeyModifiers.Meta)) mods |= GhosttyMods.Super;
        return mods;
    }

    private static GhosttyKey ConvertKey(Key key) => key switch
    {
        Key.A => GhosttyKey.A,
        Key.B => GhosttyKey.B,
        Key.C => GhosttyKey.C,
        Key.D => GhosttyKey.D,
        Key.E => GhosttyKey.E,
        Key.F => GhosttyKey.F,
        Key.G => GhosttyKey.G,
        Key.H => GhosttyKey.H,
        Key.I => GhosttyKey.I,
        Key.J => GhosttyKey.J,
        Key.K => GhosttyKey.K,
        Key.L => GhosttyKey.L,
        Key.M => GhosttyKey.M,
        Key.N => GhosttyKey.N,
        Key.O => GhosttyKey.O,
        Key.P => GhosttyKey.P,
        Key.Q => GhosttyKey.Q,
        Key.R => GhosttyKey.R,
        Key.S => GhosttyKey.S,
        Key.T => GhosttyKey.T,
        Key.U => GhosttyKey.U,
        Key.V => GhosttyKey.V,
        Key.W => GhosttyKey.W,
        Key.X => GhosttyKey.X,
        Key.Y => GhosttyKey.Y,
        Key.Z => GhosttyKey.Z,
        Key.D0 => GhosttyKey.Digit0,
        Key.D1 => GhosttyKey.Digit1,
        Key.D2 => GhosttyKey.Digit2,
        Key.D3 => GhosttyKey.Digit3,
        Key.D4 => GhosttyKey.Digit4,
        Key.D5 => GhosttyKey.Digit5,
        Key.D6 => GhosttyKey.Digit6,
        Key.D7 => GhosttyKey.Digit7,
        Key.D8 => GhosttyKey.Digit8,
        Key.D9 => GhosttyKey.Digit9,
        Key.Return => GhosttyKey.Enter,
        Key.Escape => GhosttyKey.Escape,
        Key.Back => GhosttyKey.Backspace,
        Key.Tab => GhosttyKey.Tab,
        Key.Space => GhosttyKey.Space,
        Key.OemMinus => GhosttyKey.Minus,
        Key.OemPlus => GhosttyKey.Equal,
        Key.OemOpenBrackets => GhosttyKey.BracketLeft,
        Key.OemCloseBrackets => GhosttyKey.BracketRight,
        Key.OemBackslash => GhosttyKey.Backslash,
        Key.OemSemicolon => GhosttyKey.Semicolon,
        Key.OemQuotes => GhosttyKey.Quote,
        Key.OemTilde => GhosttyKey.Backquote,
        Key.OemComma => GhosttyKey.Comma,
        Key.OemPeriod => GhosttyKey.Period,
        Key.Oem2 => GhosttyKey.Slash,
        Key.F1 => GhosttyKey.F1,
        Key.F2 => GhosttyKey.F2,
        Key.F3 => GhosttyKey.F3,
        Key.F4 => GhosttyKey.F4,
        Key.F5 => GhosttyKey.F5,
        Key.F6 => GhosttyKey.F6,
        Key.F7 => GhosttyKey.F7,
        Key.F8 => GhosttyKey.F8,
        Key.F9 => GhosttyKey.F9,
        Key.F10 => GhosttyKey.F10,
        Key.F11 => GhosttyKey.F11,
        Key.F12 => GhosttyKey.F12,
        Key.Insert => GhosttyKey.Insert,
        Key.Home => GhosttyKey.Home,
        Key.PageUp => GhosttyKey.PageUp,
        Key.Delete => GhosttyKey.Delete,
        Key.End => GhosttyKey.End,
        Key.PageDown => GhosttyKey.PageDown,
        Key.Right => GhosttyKey.ArrowRight,
        Key.Left => GhosttyKey.ArrowLeft,
        Key.Down => GhosttyKey.ArrowDown,
        Key.Up => GhosttyKey.ArrowUp,
        Key.NumPad0 => GhosttyKey.Numpad0,
        Key.NumPad1 => GhosttyKey.Numpad1,
        Key.NumPad2 => GhosttyKey.Numpad2,
        Key.NumPad3 => GhosttyKey.Numpad3,
        Key.NumPad4 => GhosttyKey.Numpad4,
        Key.NumPad5 => GhosttyKey.Numpad5,
        Key.NumPad6 => GhosttyKey.Numpad6,
        Key.NumPad7 => GhosttyKey.Numpad7,
        Key.NumPad8 => GhosttyKey.Numpad8,
        Key.NumPad9 => GhosttyKey.Numpad9,
        Key.Decimal => GhosttyKey.NumpadDecimal,
        Key.Divide => GhosttyKey.NumpadDivide,
        Key.Multiply => GhosttyKey.NumpadMultiply,
        Key.Subtract => GhosttyKey.NumpadSubtract,
        Key.Add => GhosttyKey.NumpadAdd,
        Key.LeftShift => GhosttyKey.ShiftLeft,
        Key.LeftCtrl => GhosttyKey.ControlLeft,
        Key.LeftAlt => GhosttyKey.AltLeft,
        Key.LWin => GhosttyKey.MetaLeft,
        Key.RightShift => GhosttyKey.ShiftRight,
        Key.RightCtrl => GhosttyKey.ControlRight,
        Key.RightAlt => GhosttyKey.AltRight,
        Key.RWin => GhosttyKey.MetaRight,
        _ => GhosttyKey.Unidentified,
    };

    private static uint ColorToArgb(Color c) =>
        ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    /// <summary>
    /// Converts special keys to ANSI escape sequences for standalone PTY mode.
    /// Returns null for keys that should be handled by TextInput instead.
    /// </summary>
    private string? KeyToAnsiSequence(Key key, KeyModifiers mods)
    {
        var ctrl = mods.HasFlag(KeyModifiers.Control);

        // Ctrl+key combinations
        if (ctrl)
        {
            return key switch
            {
                Key.C => "\x03",   // ETX (Ctrl+C)
                Key.D => "\x04",   // EOT (Ctrl+D)
                Key.Z => "\x1A",   // SUB (Ctrl+Z)
                Key.L => "\x0C",   // FF  (Ctrl+L)
                Key.A => "\x01",   // SOH (Ctrl+A)
                Key.E => "\x05",   // ENQ (Ctrl+E)
                Key.K => "\x0B",   // VT  (Ctrl+K)
                Key.U => "\x15",   // NAK (Ctrl+U)
                Key.W => "\x17",   // ETB (Ctrl+W)
                Key.R => "\x12",   // DC2 (Ctrl+R)
                _ => null,
            };
        }

        // Application cursor key mode: ESC O x instead of ESC [ x
        var appCursor = _vtProcessor?.ApplicationCursorKeys ?? false;
        var csi = appCursor ? "\x1BO" : "\x1B[";

        return key switch
        {
            Key.Return => "\r",
            Key.Back => "\x7F",       // DEL
            Key.Escape => "\x1B",
            Key.Tab => "\t",
            Key.Up => csi + "A",
            Key.Down => csi + "B",
            Key.Right => csi + "C",
            Key.Left => csi + "D",
            Key.Home => appCursor ? "\x1BOH" : "\x1B[H",
            Key.End => appCursor ? "\x1BOF" : "\x1B[F",
            Key.Insert => "\x1B[2~",
            Key.Delete => "\x1B[3~",
            Key.PageUp => "\x1B[5~",
            Key.PageDown => "\x1B[6~",
            Key.F1 => "\x1BOP",
            Key.F2 => "\x1BOQ",
            Key.F3 => "\x1BOR",
            Key.F4 => "\x1BOS",
            Key.F5 => "\x1B[15~",
            Key.F6 => "\x1B[17~",
            Key.F7 => "\x1B[18~",
            Key.F8 => "\x1B[19~",
            Key.F9 => "\x1B[20~",
            Key.F10 => "\x1B[21~",
            Key.F11 => "\x1B[23~",
            Key.F12 => "\x1B[24~",
            _ => null, // Let TextInput handle printable characters
        };
    }

    /// <summary>
    /// Extracts selected text from the screen buffer (standalone mode).
    /// </summary>
    private string? GetSelectedText()
    {
        if (_renderer?.SelectionStart is null || _renderer?.SelectionEnd is null || _screen is null)
            return null;

        var (startCol, startRow) = _renderer.SelectionStart.Value;
        var (endCol, endRow) = _renderer.SelectionEnd.Value;

        // Normalize direction
        if (startRow > endRow || (startRow == endRow && startCol > endCol))
        {
            (startCol, startRow, endCol, endRow) = (endCol, endRow, startCol, startRow);
        }

        var sb = new StringBuilder();
        for (var row = startRow; row <= endRow; row++)
        {
            if (row < 0 || row >= _screen.ViewportRows) continue;
            var termRow = _screen.GetViewportRow(row);

            var colStart = row == startRow ? startCol : 0;
            var colEnd = row == endRow ? endCol : _screen.Columns - 1;

            for (var col = colStart; col <= colEnd && col < _screen.Columns; col++)
            {
                ref var cell = ref termRow[col];
                if (cell.Codepoint > 0)
                    sb.Append(char.ConvertFromUtf32(cell.Codepoint));
                else
                    sb.Append(' ');
            }

            if (row < endRow)
                sb.AppendLine();
        }

        return sb.ToString();
    }

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

/// <summary>
/// Event args for terminal data events.
/// </summary>
public class TerminalDataEventArgs : EventArgs
{
    /// <summary>The raw terminal data.</summary>
    public byte[] Data { get; }

    public TerminalDataEventArgs(byte[] data) => Data = data;
}

/// <summary>
/// Event args for terminal resize events.
/// </summary>
public class TerminalSizeEventArgs : EventArgs
{
    /// <summary>New column count.</summary>
    public int Columns { get; }

    /// <summary>New row count.</summary>
    public int Rows { get; }

    public TerminalSizeEventArgs(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
    }
}
