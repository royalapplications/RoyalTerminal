// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Native terminal control using full libghostty Metal rendering.

using System;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Diagnostics;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Internal.Theme;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal.Theming;

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
    private TerminalTheme? _theme;
    private GhosttyConfig? _appliedThemeConfig;
    private bool _suppressThemePropertyApply;
    private Func<GhosttyConfig>? _themeConfigFactory;
    private IGhosttyLogger _logger = NullGhosttyLogger.Instance;
    private readonly GhosttyClipboardAdapter _clipboardAdapter;
    private readonly GhosttySurfaceLifecycle _surfaceLifecycle;

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

    public static new readonly DirectProperty<GhosttyNativeTerminalControl, TerminalTheme?> ThemeProperty =
        AvaloniaProperty.RegisterDirect<GhosttyNativeTerminalControl, TerminalTheme?>(
            nameof(Theme),
            control => control.Theme,
            (control, value) => control.Theme = value);

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

    static GhosttyNativeTerminalControl()
    {
        FocusableProperty.OverrideDefaultValue<GhosttyNativeTerminalControl>(true);
        ThemeProperty.Changed.AddClassHandler<GhosttyNativeTerminalControl>(
            static (control, _) => control.OnThemePropertyChanged());
    }

    public GhosttyNativeTerminalControl()
    {
        _theme = TerminalTheme.Dark;
        _clipboardAdapter = new GhosttyClipboardAdapter(this, () => Logger);
        GhosttyActionDispatcher actionDispatcher = new(
            () => _surface,
            () => _surface?.Draw(),
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
    }

    /// <summary>
    /// Initializes the terminal with the given Ghostty app.
    /// Must be called before the control is attached to the visual tree.
    /// </summary>
    public void Initialize(GhosttyApp app)
    {
        _app = app;
        _surfaceLifecycle.Attach(app);
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
            ApplyThemeCore(ResolveActiveTheme(), updateThemeProperty: false, updateGhosttyRuntime: true);

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
        DisposeAppliedThemeConfig();
        if (_surface is not null)
        {
            _surface.Dispose();
            _surface = null;
        }
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

    private static GhosttyColorScheme ToLegacyColorScheme(TerminalTheme theme)
    {
        return IsPerceivedLightColor(theme.DefaultBackground)
            ? GhosttyColorScheme.Light
            : GhosttyColorScheme.Dark;
    }

    private static bool IsPerceivedLightColor(uint argb)
    {
        int red = (int)((argb >> 16) & 0xFF);
        int green = (int)((argb >> 8) & 0xFF);
        int blue = (int)(argb & 0xFF);
        int luminance = ((red * 299) + (green * 587) + (blue * 114)) / 1000;
        return luminance >= 128;
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

        if (updateGhosttyRuntime && _surface is not null)
        {
            _surface.SetColorScheme(ToLegacyColorScheme(theme));
            ApplyThemeToGhosttySurface(theme);
            _surface.Refresh();
        }
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
            Logger.Error("Failed to apply Ghostty native theme config.", ex);
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
        uint color = 0xFF000000u | ((uint)change.R << 16) | ((uint)change.G << 8) | change.B;
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
        Logger.Debug($"Ghostty native config change action received (handle=0x{configHandle:X}).");
    }

    private void OnRuntimeReloadConfig(bool soft)
    {
        Logger.Debug($"Ghostty native reload config action received (soft={soft}).");
        _surface?.Refresh();
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (GhosttyInputPipeline.HandleKeyDown(_surface, e))
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

    #region Public API

    /// <summary>Refreshes the terminal rendering.</summary>
    public void Refresh() => _surface?.Refresh();

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

        GC.SuppressFinalize(this);
    }

    #endregion
}
