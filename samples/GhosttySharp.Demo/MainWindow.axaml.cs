// Licensed under the MIT License.
// GhosttySharp.Demo — Main window with multi-tab terminal management.
// Uses a visibility-toggled panel instead of TabControl so terminal controls
// stay attached to the visual tree across tab switches (no re-init).

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GhosttySharp.Avalonia;
using GhosttySharp.Native;

namespace GhosttySharp.Demo;

public partial class MainWindow : Window
{
    private readonly List<TerminalTab> _tabs = [];
    private TerminalTab? _activeTab;
    private int _tabCounter;
    private double _fontSize = 14.0;
    private bool _isDarkTheme = true;
    private bool _useNativeControl;
    private bool _useRenderedControl;
    private bool _useNativeVtControl;
    private bool _ghosttyAvailable;
    private bool _nativeVtAvailable;

    private static readonly string MonoFont =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
        "Consolas";

    // Ghostty state
    private GhosttyApp? _ghosttyApp;
    private GhosttyConfig? _ghosttyConfig;
    private DispatcherTimer? _tickTimer;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void InitializeComponent()
    {
        global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var newTabButton = this.FindControl<Button>("NewTabButton");
        var fontDecreaseButton = this.FindControl<Button>("FontDecreaseButton");
        var fontIncreaseButton = this.FindControl<Button>("FontIncreaseButton");
        var themeToggleButton = this.FindControl<Button>("ThemeToggleButton");
        var modeToggleButton = this.FindControl<Button>("ModeToggleButton");
        var copyMenuItem = this.FindControl<MenuItem>("CopyMenuItem");
        var pasteMenuItem = this.FindControl<MenuItem>("PasteMenuItem");
        var selectAllMenuItem = this.FindControl<MenuItem>("SelectAllMenuItem");
        var newTabMenuItem = this.FindControl<MenuItem>("NewTabMenuItem");
        var closeTabMenuItem = this.FindControl<MenuItem>("CloseTabMenuItem");

        if (newTabButton is not null) newTabButton.Click += (_, _) => CreateNewTab();
        if (fontDecreaseButton is not null) fontDecreaseButton.Click += (_, _) => AdjustFontSize(-1);
        if (fontIncreaseButton is not null) fontIncreaseButton.Click += (_, _) => AdjustFontSize(1);
        if (themeToggleButton is not null) themeToggleButton.Click += (_, _) => ToggleTheme();
        if (modeToggleButton is not null) modeToggleButton.Click += (_, _) => CycleRenderMode();
        if (copyMenuItem is not null) copyMenuItem.Click += async (_, _) => await CopySelection();
        if (pasteMenuItem is not null) pasteMenuItem.Click += async (_, _) => await PasteClipboard();
        if (selectAllMenuItem is not null) selectAllMenuItem.Click += (_, _) => { };
        if (newTabMenuItem is not null) newTabMenuItem.Click += (_, _) => CreateNewTab();
        if (closeTabMenuItem is not null) closeTabMenuItem.Click += (_, _) => CloseCurrentTab();

        KeyDown += OnWindowKeyDown;

        // Try to initialize full Ghostty (libghostty with Metal rendering)
        TryInitializeGhostty();

        // Check if libghostty-terminal is available (separate from full Ghostty)
        _nativeVtAvailable = GhosttySharp.Avalonia.Terminal.GhosttyVtProcessor.IsAvailable();

        CreateNewTab();
        UpdateStatus(_useRenderedControl
            ? "Ghostty VT + SkiaSharp rendering"
            : _useNativeControl
                ? "Ghostty native terminal ready"
                : _useNativeVtControl
                    ? "Native VT (libghostty-terminal) ready"
                    : "Terminal ready — Rendered (Custom PTY) mode");
    }

    private void TryInitializeGhostty()
    {
        // The Ghostty embedded API (Ghostty Rendered/Native modes) currently only supports macOS.
        // On Windows and Linux, we fall through to Rendered (Custom PTY) mode.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _useNativeControl = false;
            _useRenderedControl = false;
            _ghosttyAvailable = false;
            UpdateModeButton();
            return;
        }

        try
        {
            if (!Ghostty.Initialize())
            {
                return;
            }

            _ghosttyConfig = new GhosttyConfig();
            _ghosttyConfig.LoadDefaultFiles();
            _ghosttyConfig.Finalize_();

            _ghosttyApp = new GhosttyApp(_ghosttyConfig);

            // Start tick timer for Ghostty event processing
            _tickTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };
            _tickTimer.Tick += (_, _) => _ghosttyApp?.Tick();
            _tickTimer.Start();

            _useNativeControl = true;
            _useRenderedControl = true; // Default to custom rendering mode with Ghostty VT
            _ghosttyAvailable = true;

            var info = Ghostty.GetInfo();
            UpdateStatus($"Ghostty {info.Version} — custom rendered mode");
            UpdateModeButton();
        }
        catch (Exception)
        {
            _useNativeControl = false;
            _useRenderedControl = false;
            _ghosttyAvailable = false;
            UpdateModeButton();
        }
    }

    #region Tab Management

    private void CreateNewTab()
    {
        _tabCounter++;
        var tabName = $"Terminal {_tabCounter}";

        Control terminal;

        if (_useRenderedControl && _ghosttyApp is not null)
        {
            // Mode: Ghostty VT + SkiaSharp rendering
            var renderedControl = new GhosttyRenderedTerminalControl
            {
                TerminalFontSize = (float)_fontSize,
                FontFamilyName = MonoFont,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            renderedControl.Initialize(_ghosttyApp);

            renderedControl.TitleChanged += (_, title) =>
            {
                var tab = _tabs.Find(t => t.Control == renderedControl);
                tab?.UpdateTitle(title);
            };
            renderedControl.ProcessExited += (_, code) =>
            {
                UpdateStatus($"Process exited: {code}");
            };
            renderedControl.CloseRequested += (_, _) =>
            {
                var tab = _tabs.Find(t => t.Control == renderedControl);
                if (tab is not null) CloseTab(tab);
            };
            renderedControl.TerminalResized += (_, args) =>
            {
                if (_activeTab?.Control == renderedControl)
                    UpdateDimensions(args.Columns, args.Rows);
            };

            terminal = renderedControl;
        }
        else if (_useNativeControl && _ghosttyApp is not null)
        {
            var nativeControl = new GhosttyNativeTerminalControl
            {
                TerminalFontSize = (float)_fontSize,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            nativeControl.Initialize(_ghosttyApp);

            nativeControl.TitleChanged += (_, title) =>
            {
                var tab = _tabs.Find(t => t.Control == nativeControl);
                tab?.UpdateTitle(title);
            };
            nativeControl.ProcessExited += (_, code) =>
            {
                UpdateStatus($"Process exited: {code}");
            };
            nativeControl.CloseRequested += (_, _) =>
            {
                var tab = _tabs.Find(t => t.Control == nativeControl);
                if (tab is not null) CloseTab(tab);
            };
            nativeControl.TerminalResized += (_, args) =>
            {
                if (_activeTab?.Control == nativeControl)
                    UpdateDimensions(args.Columns, args.Rows);
            };

            terminal = nativeControl;
        }
        else
        {
            var standaloneControl = new GhosttyTerminalControl
            {
                FontFamilyName = MonoFont,
                TerminalFontSize = _fontSize,
                Columns = 80,
                Rows = 24,
                ScrollbackLimit = 10_000,
                UseNativeVtProcessor = _useNativeVtControl ? true : null,
                DefaultForeground = _isDarkTheme
                    ? Color.FromRgb(0xD4, 0xD4, 0xD4)
                    : Color.FromRgb(0x1E, 0x1E, 0x1E),
                DefaultBackground = _isDarkTheme
                    ? Color.FromRgb(0x1E, 0x1E, 0x1E)
                    : Color.FromRgb(0xFF, 0xFF, 0xFF),
            };

            standaloneControl.DataReceived += (_, args) =>
            {
                UpdateStatus($"Received {args.Data.Length} bytes");
            };
            standaloneControl.TerminalResized += (_, args) =>
            {
                UpdateDimensions(args.Columns, args.Rows);
            };

            var needsPty = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                         || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                         || RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            terminal = standaloneControl;

            if (needsPty)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        standaloneControl.StartPty(workingDirectory: home);
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"Failed to start PTY: {ex.Message}");
                    }
                }, DispatcherPriority.Background);
            }
        }

        // Determine mode label, glyph, and color for the tab header
        string modeName;
        string modeGlyph;
        IBrush glyphColor;
        if (_useRenderedControl && _ghosttyApp is not null)
        {
            modeName = "Rendered (Ghostty VT + SkiaSharp)";
            modeGlyph = "\u25CF"; // filled circle
            glyphColor = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6)); // blue
        }
        else if (_useNativeControl && _ghosttyApp is not null)
        {
            modeName = "Native (Ghostty Metal)";
            modeGlyph = "\u25C6"; // diamond
            glyphColor = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xAA)); // yellow
        }
        else if (_useNativeVtControl)
        {
            modeName = "Native VT (libghostty-terminal)";
            modeGlyph = "\u25B2"; // triangle
            glyphColor = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78)); // orange
        }
        else
        {
            var vtLabel = terminal is GhosttyTerminalControl gtc && gtc.IsUsingNativeVtProcessor
                ? "Ghostty VT" : "Basic VT";
            modeName = $"Rendered (Custom PTY — {vtLabel})";
            modeGlyph = "\u25A0"; // filled square
            glyphColor = new SolidColorBrush(Color.FromRgb(0x6A, 0x99, 0x55)); // green
        }

        // Build the tab header button (lives in the TabStrip)
        var modeIndicator = new TextBlock
        {
            Text = modeGlyph,
            Foreground = glyphColor,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10,
        };
        var titleText = new TextBlock
        {
            Text = tabName,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        };
        var closeButton = new Button
        {
            Content = "\u00d7",
            FontSize = 14,
            Padding = new Thickness(2, 0),
            MinWidth = 20,
            MinHeight = 20,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var headerContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        headerContent.Children.Add(modeIndicator);
        headerContent.Children.Add(titleText);
        headerContent.Children.Add(closeButton);

        var headerButton = new Button
        {
            Content = headerContent,
            Padding = new Thickness(12, 6),
            BorderThickness = new Thickness(0),
        };
        headerButton.Classes.Add("tabHeader");
        ToolTip.SetTip(headerButton, modeName);

        // Wrap standalone control in a ScrollViewer for auto-hiding scrollbar
        Control container;
        if (terminal is GhosttyTerminalControl)
        {
            container = new ScrollViewer
            {
                Content = terminal,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            };
        }
        else
        {
            container = terminal;
        }

        var tab = new TerminalTab(headerButton, terminal, container, titleText, _tabCounter, modeName);
        closeButton.Click += (_, _) => CloseTab(tab);
        headerButton.Click += (_, _) =>
        {
            var idx = _tabs.IndexOf(tab);
            if (idx >= 0) SwitchToTab(idx);
        };

        _tabs.Add(tab);

        // Add container to the persistent host (stays in visual tree)
        var terminalHost = this.FindControl<Grid>("TerminalHost")!;
        container.IsVisible = false; // hidden until SwitchToTab
        terminalHost.Children.Add(container);

        // Add header button to the tab strip
        var tabStrip = this.FindControl<StackPanel>("TabStrip")!;
        tabStrip.Children.Add(headerButton);

        // Activate the new tab
        SwitchToTab(_tabs.Count - 1);

        UpdateStatus($"Opened {tabName}");
    }

    private void CloseTab(TerminalTab tab)
    {
        var tabIndex = _tabs.IndexOf(tab);
        if (tabIndex < 0) return;

        _tabs.Remove(tab);

        // Remove from visual tree
        var terminalHost = this.FindControl<Grid>("TerminalHost")!;
        terminalHost.Children.Remove(tab.Container);

        var tabStrip = this.FindControl<StackPanel>("TabStrip")!;
        tabStrip.Children.Remove(tab.HeaderButton);

        // Dispose the terminal
        if (tab.Control is GhosttyTerminalControl standaloneControl)
        {
            standaloneControl.StopPty();
            standaloneControl.DetachSurface();
        }
        else if (tab.Control is GhosttyRenderedTerminalControl renderedControl)
        {
            renderedControl.Dispose();
        }
        else if (tab.Control is GhosttyNativeTerminalControl nativeControl)
        {
            nativeControl.Dispose();
        }

        if (_tabs.Count == 0)
        {
            _activeTab = null;
            CreateNewTab();
        }
        else
        {
            // Activate adjacent tab
            var newIndex = Math.Min(tabIndex, _tabs.Count - 1);
            SwitchToTab(newIndex);
        }

        UpdateStatus($"Closed {tab.Title}");
    }

    private void CloseCurrentTab()
    {
        if (_activeTab is not null)
            CloseTab(_activeTab);
    }

    private TerminalTab? GetActiveTab() => _activeTab;

    private void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;

        // Hide all terminals & deactivate all header buttons
        foreach (var tab in _tabs)
        {
            tab.Container.IsVisible = false;
            tab.HeaderButton.Classes.Remove("active");
        }

        // Show the selected terminal & activate its header
        var target = _tabs[index];
        target.Container.IsVisible = true;
        target.HeaderButton.Classes.Add("active");
        _activeTab = target;

        Dispatcher.UIThread.Post(() => target.Control.Focus(), DispatcherPriority.Input);
    }

    #endregion

    #region Keyboard Shortcuts

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl && !shift)
        {
            switch (e.Key)
            {
                case Key.T:
                    CreateNewTab();
                    e.Handled = true;
                    return;
                case Key.W:
                    CloseCurrentTab();
                    e.Handled = true;
                    return;
                case Key.Tab:
                    CycleTab(forward: true);
                    e.Handled = true;
                    return;
                case Key.OemPlus or Key.Add:
                    AdjustFontSize(1);
                    e.Handled = true;
                    return;
                case Key.OemMinus or Key.Subtract:
                    AdjustFontSize(-1);
                    e.Handled = true;
                    return;
                case Key.D0:
                    ResetFontSize();
                    e.Handled = true;
                    return;
            }

            if (e.Key >= Key.D1 && e.Key <= Key.D9)
            {
                SwitchToTab(e.Key - Key.D1);
                e.Handled = true;
                return;
            }
        }

        if (ctrl && shift)
        {
            switch (e.Key)
            {
                case Key.C:
                    _ = CopySelection();
                    e.Handled = true;
                    return;
                case Key.V:
                    _ = PasteClipboard();
                    e.Handled = true;
                    return;
                case Key.Tab:
                    CycleTab(forward: false);
                    e.Handled = true;
                    return;
            }
        }
    }

    private void CycleTab(bool forward)
    {
        if (_tabs.Count <= 1) return;

        var currentIndex = _activeTab is not null ? _tabs.IndexOf(_activeTab) : 0;
        var next = forward
            ? (currentIndex + 1) % _tabs.Count
            : (currentIndex - 1 + _tabs.Count) % _tabs.Count;
        SwitchToTab(next);
    }

    #endregion

    #region Clipboard

    private async System.Threading.Tasks.Task CopySelection()
    {
        var tab = GetActiveTab();
        if (tab is null) return;

        if (tab.Control is GhosttyNativeTerminalControl native)
            await native.CopySelectionAsync();
        else if (tab.Control is GhosttyRenderedTerminalControl rendered)
            await rendered.CopySelectionAsync();
        else if (tab.Control is GhosttyTerminalControl standalone)
            await standalone.CopySelectionAsync();

        UpdateStatus("Copied to clipboard");
    }

    private async System.Threading.Tasks.Task PasteClipboard()
    {
        var tab = GetActiveTab();
        if (tab is null) return;

        if (tab.Control is GhosttyNativeTerminalControl native)
            await native.PasteAsync();
        else if (tab.Control is GhosttyRenderedTerminalControl rendered)
            await rendered.PasteAsync();
        else if (tab.Control is GhosttyTerminalControl standalone)
            await standalone.PasteAsync();

        UpdateStatus("Pasted from clipboard");
    }

    #endregion

    #region Font & Theme

    private void AdjustFontSize(int delta)
    {
        _fontSize = Math.Clamp(_fontSize + delta, 8, 32);

        foreach (var tab in _tabs)
        {
            if (tab.Control is GhosttyTerminalControl standalone)
            {
                standalone.TerminalFontSize = _fontSize;
                standalone.InvalidateTerminal();
            }
            else if (tab.Control is GhosttyRenderedTerminalControl rendered)
            {
                rendered.TerminalFontSize = (float)_fontSize;
            }
        }

        var display = this.FindControl<TextBlock>("FontSizeDisplay");
        if (display is not null) display.Text = _fontSize.ToString("0");

        UpdateStatus($"Font size: {_fontSize}");
    }

    private void ResetFontSize()
    {
        _fontSize = 14;
        AdjustFontSize(0);
    }

    private void ToggleTheme()
    {
        _isDarkTheme = !_isDarkTheme;

        var fg = _isDarkTheme ? Color.FromRgb(0xD4, 0xD4, 0xD4) : Color.FromRgb(0x1E, 0x1E, 0x1E);
        var bg = _isDarkTheme ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Color.FromRgb(0xFF, 0xFF, 0xFF);

        foreach (var tab in _tabs)
        {
            if (tab.Control is GhosttyTerminalControl standalone)
            {
                standalone.DefaultForeground = fg;
                standalone.DefaultBackground = bg;
                standalone.InvalidateTerminal();
            }
            else if (tab.Control is GhosttyRenderedTerminalControl rendered)
            {
                rendered.SetColorScheme(
                    _isDarkTheme ? GhosttyColorScheme.Dark : GhosttyColorScheme.Light);
            }
            else if (tab.Control is GhosttyNativeTerminalControl native)
            {
                native.SetColorScheme(
                    _isDarkTheme ? GhosttyColorScheme.Dark : GhosttyColorScheme.Light);
            }
        }

        Background = new SolidColorBrush(bg);

        var themeButton = this.FindControl<Button>("ThemeToggleButton");
        if (themeButton is not null) themeButton.Content = _isDarkTheme ? "\u2600" : "\U0001F319";

        UpdateStatus(_isDarkTheme ? "Dark theme" : "Light theme");
    }

    private void CycleRenderMode()
    {
        if (_ghosttyAvailable)
        {
            // Cycle: Rendered → Native → Native VT → Standalone → Rendered
            if (_useRenderedControl)
            {
                _useRenderedControl = false;
                _useNativeControl = true;
                _useNativeVtControl = false;
            }
            else if (_useNativeControl)
            {
                _useNativeControl = false;
                if (_nativeVtAvailable)
                    _useNativeVtControl = true;
                // else skip to standalone
            }
            else if (_useNativeVtControl)
            {
                _useNativeVtControl = false;
            }
            else
            {
                _useRenderedControl = true;
                _useNativeControl = true;
                _useNativeVtControl = false;
            }
        }
        else if (_nativeVtAvailable)
        {
            // No full Ghostty, but libghostty-terminal is available
            // Cycle: Native VT → Standalone → Native VT
            _useNativeVtControl = !_useNativeVtControl;
        }
        else
        {
            UpdateStatus("Only Rendered (Custom PTY) mode is available on this platform");
        }

        UpdateModeButton();
        var modeName = _useRenderedControl ? "Rendered (Ghostty VT + SkiaSharp)"
            : _useNativeControl ? "Native (Ghostty Metal)"
            : _useNativeVtControl ? "Native VT (libghostty-terminal)"
            : "Rendered (Custom PTY)";
        UpdateStatus($"New tabs will use: {modeName}");
    }

    private void UpdateModeButton()
    {
        var btn = this.FindControl<Button>("ModeToggleButton");
        if (btn is null) return;
        btn.Content = _useRenderedControl ? "Ghostty Rendered"
            : _useNativeControl ? "Ghostty Native"
            : _useNativeVtControl ? "Native VT"
            : "Rendered";
    }

    #endregion

    #region Status Bar

    private void UpdateStatus(string text)
    {
        var statusText = this.FindControl<TextBlock>("StatusText");
        if (statusText is not null) statusText.Text = text;
    }

    private void UpdateDimensions(int cols, int rows)
    {
        var display = this.FindControl<TextBlock>("DimensionsDisplay");
        if (display is not null) display.Text = $"{cols}x{rows}";
    }

    #endregion

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _tickTimer?.Stop();

        foreach (var tab in _tabs)
        {
            if (tab.Control is GhosttyTerminalControl standalone)
            {
                standalone.StopPty();
                standalone.DetachSurface();
            }
            else if (tab.Control is GhosttyRenderedTerminalControl rendered)
            {
                rendered.Dispose();
            }
            else if (tab.Control is GhosttyNativeTerminalControl native)
            {
                native.Dispose();
            }
        }
        _tabs.Clear();

        _ghosttyApp?.Dispose();
        _ghosttyConfig?.Dispose();
    }
}

/// <summary>
/// Tracks state for a single terminal tab.
/// </summary>
internal sealed class TerminalTab
{
    public Button HeaderButton { get; }
    public Control Control { get; }
    /// <summary>The element added to TerminalHost — either the Control itself or a ScrollViewer wrapping it.</summary>
    public Control Container { get; }
    public TextBlock TitleText { get; }
    public int Index { get; }
    /// <summary>The rendering mode this tab was created with.</summary>
    public string ModeName { get; }
    public string Title => TitleText.Text ?? $"Terminal {Index}";

    public TerminalTab(Button headerButton, Control control, Control container, TextBlock titleText, int index, string modeName)
    {
        HeaderButton = headerButton;
        Control = control;
        Container = container;
        TitleText = titleText;
        Index = index;
        ModeName = modeName;
    }

    public void UpdateTitle(string title)
    {
        TitleText.Text = title;
    }
}
