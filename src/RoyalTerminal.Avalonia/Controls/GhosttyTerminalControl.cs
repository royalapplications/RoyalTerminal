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
using RoyalTerminal.Avalonia.Terminal;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Terminal.Services;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Avalonia.Controls;

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

    /// <summary>The font family used for terminal text.</summary>
    public static readonly StyledProperty<string> FontFamilyNameProperty =
        AvaloniaProperty.Register<GhosttyTerminalControl, string>(nameof(FontFamilyName), TerminalDefaults.DefaultMonoFont);

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
    /// When null (default), auto-detects the best available VT processor.</summary>
    public static readonly DirectProperty<GhosttyTerminalControl, bool?> UseNativeVtProcessorProperty =
        AvaloniaProperty.RegisterDirect<GhosttyTerminalControl, bool?>(
            nameof(UseNativeVtProcessor),
            o => o.UseNativeVtProcessor,
            (o, v) => o.UseNativeVtProcessor = v);

    private bool? _useNativeVtProcessor;

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
        get => _useNativeVtProcessor;
        set => SetAndRaise(UseNativeVtProcessorProperty, ref _useNativeVtProcessor, value);
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

    private IVtProcessor? _vtProcessor;
    private bool _isMouseSelecting;

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

    /// <summary>Gets the underlying Ghostty surface, if connected.</summary>
    public GhosttySurface? Surface => TerminalSessionService.Surface;

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
        : this(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory(),
            new DefaultPtyFactory())
    {
    }

    /// <summary>
    /// Initializes a terminal control with explicit service/factory dependencies.
    /// </summary>
    public GhosttyTerminalControl(
        ITerminalSessionService terminalSessionService,
        ITerminalInputAdapter terminalInputAdapter,
        ITerminalSelectionService terminalSelectionService,
        ITerminalScrollService terminalScrollService,
        IVtProcessorFactory vtProcessorFactory,
        IPtyFactory ptyFactory)
    {
        TerminalSessionService = terminalSessionService ?? throw new ArgumentNullException(nameof(terminalSessionService));
        TerminalInputAdapter = terminalInputAdapter ?? throw new ArgumentNullException(nameof(terminalInputAdapter));
        TerminalSelectionService = terminalSelectionService ?? throw new ArgumentNullException(nameof(terminalSelectionService));
        TerminalScrollService = terminalScrollService ?? throw new ArgumentNullException(nameof(terminalScrollService));
        VtProcessorFactory = vtProcessorFactory ?? throw new ArgumentNullException(nameof(vtProcessorFactory));
        PtyFactory = ptyFactory ?? throw new ArgumentNullException(nameof(ptyFactory));

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

        _vtProcessor = VtProcessorFactory.Create(_screen, UseNativeVtProcessor);

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
                if (Surface is not null)
                {
                    Surface.SetSize((uint)finalSize.Width, (uint)finalSize.Height);
                }

                // Notify the PTY about the resize (standalone mode)
                TerminalSessionService.ResizePty(newCols, newRows, (int)finalSize.Width, (int)finalSize.Height);

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
        TerminalSessionService.AttachSurface(surface);
    }

    /// <summary>
    /// Disconnects the current Ghostty surface.
    /// </summary>
    public void DetachSurface()
    {
        TerminalSessionService.DetachSurface();
    }

    /// <summary>
    /// Writes data to the terminal screen model.
    /// Called when output is received from the PTY/Ghostty surface.
    /// </summary>
    public void WriteOutput(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        WriteOutput((ReadOnlyMemory<byte>)data);
    }

    /// <summary>
    /// Writes data to the terminal screen model.
    /// Called when output is received from the PTY/Ghostty surface.
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
    /// Called when output is received from the PTY/Ghostty surface.
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
    /// Sends input text to the Ghostty surface.
    /// </summary>
    public void SendInput(string text)
    {
        TerminalSessionService.SendInput(text);
    }

    /// <summary>
    /// Sends input bytes to the Ghostty surface.
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

        if (_renderer is null) return;

        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (Surface is not null)
        {
            GhosttyMouseButton button = TerminalInputAdapter.ConvertPressedMouseButton(props);
            GhosttyMods mods = TerminalInputAdapter.ConvertModifiers(e.KeyModifiers);
            Surface.SendMouseButton(GhosttyMouseState.Press, button, mods);
        }

        // Start text selection on left click
        if (props.IsLeftButtonPressed)
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

        if (_renderer is null) return;

        var point = e.GetPosition(this);
        var col = (int)(point.X / _renderer.CellWidth);
        var row = (int)(point.Y / _renderer.CellHeight);

        if (Surface is not null)
        {
            GhosttyMods moveMods = TerminalInputAdapter.ConvertModifiers(e.KeyModifiers);
            Surface.SendMousePos(point.X, point.Y, moveMods);
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

        if (Surface is not null)
        {
            GhosttyMouseButton button = TerminalInputAdapter.ConvertMouseButton(e.InitialPressMouseButton);
            GhosttyMods mods = TerminalInputAdapter.ConvertModifiers(e.KeyModifiers);
            Surface.SendMouseButton(GhosttyMouseState.Release, button, mods);
        }

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
            TerminalInputAdapter,
            _presenter,
            RaiseScrollInvalidated);
        e.Handled = true;
    }

    #endregion

    #region Focus

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        Surface?.SetFocus(true);
        _renderer?.SetCursorVisible(true);
        _presenter?.Invalidate();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        Surface?.SetFocus(false);
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
        Surface?.SetContentScale(scaleX, scaleY);
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
        TerminalSessionService.StartPty(
            PtyFactory,
            shell,
            Columns,
            Rows,
            workingDirectory,
            _vtProcessor,
            OnPtyDataReceived,
            OnPtyProcessExited,
            OnVtProcessorResponse,
            OnVtProcessorBell,
            OnVtProcessorTitleChanged);
    }

    /// <summary>
    /// Stops the PTY and kills the child shell process.
    /// </summary>
    public void StopPty()
    {
        TerminalSessionService.StopPty(_vtProcessor, OnPtyDataReceived, OnPtyProcessExited);
    }

    /// <summary>Whether a standalone PTY is active.</summary>
    public bool HasPty => TerminalSessionService.HasPty;

    /// <summary>Gets the managed PTY, if started.</summary>
    public IPty? Pty => TerminalSessionService.Pty;

    /// <summary>
    /// Handles terminal query responses (DSR, DA, etc.) by writing them back to the PTY.
    /// Called from the VT processor when it detects a query sequence in the output stream.
    /// </summary>
    private void OnVtProcessorResponse(byte[] data)
    {
        TerminalSessionService.Pty?.Write(data, 0, data.Length);
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

    #region Helpers

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
