// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Native terminal control using full libghostty Metal rendering.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Input;
using RoyalTerminal.Avalonia.Diagnostics;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Avalonia terminal control that uses the full libghostty embedding API.
/// Ghostty handles everything: VT parsing, screen state, PTY management, and Metal rendering.
/// On macOS, an NSView is created and hosted via NativeControlHost; Ghostty renders into it.
/// </summary>
[SupportedOSPlatform("macos")]
public class GhosttyNativeTerminalControl : NativeControlHost, IDisposable
{
    private GhosttyApp? _app;
    private GhosttySurface? _surface;
    private nint _nsView;
    private bool _disposed;
    private IGhosttyLogger _logger = NullGhosttyLogger.Instance;

    /// <summary>Fired when the surface title changes.</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>Fired when the terminal process exits.</summary>
    public event EventHandler<int>? ProcessExited;

    /// <summary>Fired when Ghostty requests a surface close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Fired when the terminal grid dimensions change (columns/rows).</summary>
    public event EventHandler<TerminalSizeEventArgs>? TerminalResized;

    private int _lastCols;
    private int _lastRows;

    /// <summary>Gets the underlying Ghostty surface.</summary>
    public GhosttySurface? Surface => _surface;

    // Styled properties
    public static readonly StyledProperty<float> TerminalFontSizeProperty =
        AvaloniaProperty.Register<GhosttyNativeTerminalControl, float>(nameof(TerminalFontSize), 14.0f);

    public static readonly StyledProperty<string?> WorkingDirectoryProperty =
        AvaloniaProperty.Register<GhosttyNativeTerminalControl, string?>(nameof(WorkingDirectory));

    public static readonly StyledProperty<string?> CommandProperty =
        AvaloniaProperty.Register<GhosttyNativeTerminalControl, string?>(nameof(Command));

    public float TerminalFontSize
    {
        get => GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
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

    static GhosttyNativeTerminalControl()
    {
        FocusableProperty.OverrideDefaultValue<GhosttyNativeTerminalControl>(true);
    }

    /// <summary>
    /// Initializes the terminal with the given Ghostty app.
    /// Must be called before the control is attached to the visual tree.
    /// </summary>
    public void Initialize(GhosttyApp app)
    {
        _app = app;

        _app.WakeupRequested += OnWakeupRequested;
        _app.ActionRequested += OnActionRequested;
        _app.ClipboardReadRequested += OnClipboardReadRequested;
        _app.ClipboardWriteRequested += OnClipboardWriteRequested;
        _app.SurfaceCloseRequested += OnSurfaceCloseRequested;
    }

    #region NativeControlHost overrides

    /// <summary>
    /// Called by Avalonia to create the native control. Creates an NSView and Ghostty surface.
    /// </summary>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _nsView = ObjCRuntime.CreateNSView();
        if (_nsView == nint.Zero)
            throw new InvalidOperationException("Failed to create NSView for Ghostty terminal");

        // Create the Ghostty surface targeting this NSView
        CreateGhosttySurface();

        return new NativeViewHandle(_nsView, "NSView");
    }

    /// <summary>
    /// Called by Avalonia to destroy the native control.
    /// </summary>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        DestroyGhosttySurface();
        if (_nsView != nint.Zero)
        {
            ObjCRuntime.ReleaseNSView(_nsView);
            _nsView = nint.Zero;
        }
    }

    #endregion

    #region Ghostty Surface Management

    private unsafe void CreateGhosttySurface()
    {
        if (_app is null || _nsView == nint.Zero) return;

        try
        {
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

            // Set initial size
            var bounds = Bounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                _surface.SetSize(
                    (uint)(bounds.Width * scale),
                    (uint)(bounds.Height * scale));
            }

            _surface.SetContentScale(scale, scale);
            _surface.SetFocus(IsFocused);

            Logger.Debug(
                $"Ghostty surface created: NSView=0x{_nsView:X}, size={bounds.Width:F0}x{bounds.Height:F0}");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to create Ghostty surface.", ex);
        }
    }

    private void DestroyGhosttySurface()
    {
        if (_surface is not null)
        {
            _surface.Dispose();
            _surface = null;
        }
    }

    #endregion

    #region Layout

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);

        if (_surface is not null && finalSize.Width > 0 && finalSize.Height > 0)
        {
            var scale = VisualRoot?.RenderScaling ?? 1.0;
            _surface.SetSize(
                (uint)(finalSize.Width * scale),
                (uint)(finalSize.Height * scale));

            // Check if grid dimensions changed and fire event
            var size = _surface.Size;
            var cols = (int)size.Columns;
            var rows = (int)size.Rows;
            if (cols > 0 && rows > 0 && (cols != _lastCols || rows != _lastRows))
            {
                _lastCols = cols;
                _lastRows = rows;
                TerminalResized?.Invoke(this, new TerminalSizeEventArgs(cols, rows));
            }
        }

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

            if (_surface.SendKey(inputKey))
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

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _surface?.SetFocus(true);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
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
                    _surface?.Draw();
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
        GhosttyClipboardBridge.HandleReadRequest(this, _surface, state, Logger);
    }

    private void OnClipboardWriteRequested(GhosttyClipboard clipboard, nint contentPtr, nuint len, bool confirm)
    {
        GhosttyClipboardBridge.HandleWriteRequest(this, contentPtr, len, Logger);
    }

    private void OnSurfaceCloseRequested(bool processAlive)
    {
        Dispatcher.UIThread.Post(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    #endregion

    #region Public API

    /// <summary>Refreshes the terminal rendering.</summary>
    public void Refresh() => _surface?.Refresh();

    /// <summary>Sets the color scheme.</summary>
    public void SetColorScheme(GhosttyColorScheme scheme) => _surface?.SetColorScheme(scheme);

    /// <summary>Checks if the terminal has a selection.</summary>
    public bool HasSelection => _surface?.HasSelection ?? false;

    /// <summary>Copies the selection to clipboard.</summary>
    public async Task CopySelectionAsync()
    {
        await GhosttyClipboardBridge.CopySelectionAsync(this, _surface);
    }

    /// <summary>Pastes from clipboard.</summary>
    public async Task PasteAsync()
    {
        await GhosttyClipboardBridge.PasteAsync(this, _surface);
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

        GC.SuppressFinalize(this);
    }

    #endregion
}
