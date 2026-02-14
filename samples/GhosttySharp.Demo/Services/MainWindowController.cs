// Licensed under the MIT License.
// GhosttySharp.Demo — Runtime controller for terminal tab orchestration.

using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GhosttySharp.Avalonia;
using GhosttySharp.Demo.ViewModels;
using GhosttySharp.Native;
using ReactiveUI;

namespace GhosttySharp.Demo.Services;

internal sealed class MainWindowController
{
    private static readonly string MonoFont =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
        "Consolas";

    private static readonly ThemePalette DarkPalette = new(
        Color.FromRgb(0x1E, 0x1E, 0x1E),
        Color.FromRgb(0x33, 0x33, 0x33),
        Color.FromRgb(0x55, 0x55, 0x55),
        Color.FromRgb(0xCC, 0xCC, 0xCC),
        Color.FromRgb(0x25, 0x25, 0x26),
        Color.FromRgb(0x00, 0x7A, 0xCC),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0x2D, 0x2D, 0x2D),
        Color.FromRgb(0x96, 0x96, 0x96),
        Color.FromRgb(0x1E, 0x1E, 0x1E),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xD4, 0xD4, 0xD4),
        Color.FromRgb(0x1E, 0x1E, 0x1E));

    private static readonly ThemePalette LightPalette = new(
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xE5, 0xE5, 0xE5),
        Color.FromRgb(0xC4, 0xC4, 0xC4),
        Color.FromRgb(0x33, 0x33, 0x33),
        Color.FromRgb(0xF0, 0xF0, 0xF0),
        Color.FromRgb(0x00, 0x7A, 0xCC),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xDC, 0xDC, 0xDC),
        Color.FromRgb(0x33, 0x33, 0x33),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0x11, 0x11, 0x11),
        Color.FromRgb(0x1E, 0x1E, 0x1E),
        Color.FromRgb(0xFF, 0xFF, 0xFF));

    private readonly MainWindowViewModel _viewModel;
    private readonly Grid _terminalHost;
    private readonly StackPanel _tabStrip;
    private readonly List<TerminalTab> _tabs = [];

    private TerminalTab? _activeTab;
    private int _tabCounter;
    private GhosttyApp? _ghosttyApp;
    private GhosttyConfig? _ghosttyConfig;
    private DispatcherTimer? _tickTimer;

    public MainWindowController(Window window, MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        _terminalHost = window.FindControl<Grid>("TerminalHost")
            ?? throw new InvalidOperationException("TerminalHost was not found in MainWindow.");
        _tabStrip = window.FindControl<StackPanel>("TabStrip")
            ?? throw new InvalidOperationException("TabStrip was not found in MainWindow.");
    }

    public IDisposable Activate()
    {
        CompositeDisposable lifetime = new();
        RegisterInteractionHandlers(lifetime);

        bool ghosttyAvailable = TryInitializeGhostty();
        bool nativeVtAvailable = GhosttySharp.Avalonia.Terminal.GhosttyVtProcessor.IsAvailable();
        _viewModel.SetTerminalCapabilities(ghosttyAvailable, nativeVtAvailable);

        if (!ghosttyAvailable)
        {
            _viewModel.SetRenderMode(useRenderedControl: false, useNativeControl: false, useNativeVtControl: false);
        }

        ApplyThemeResources(_viewModel.IsDarkTheme);

        CreateNewTab();
        UpdateStatus(_viewModel.UseRenderedControl
            ? "Ghostty VT + SkiaSharp rendering"
            : _viewModel.UseNativeControl
                ? "Ghostty native terminal ready"
                : _viewModel.UseNativeVtControl
                    ? "Native VT (libghostty-terminal) ready"
                    : "Terminal ready - Rendered (Custom PTY) mode");

        lifetime.Add(Disposable.Create(DisposeResources));
        return lifetime;
    }

    private void RegisterInteractionHandlers(CompositeDisposable disposables)
    {
        disposables.Add(_viewModel.CreateNewTabInteraction.RegisterHandler(context =>
        {
            CreateNewTab();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.CloseCurrentTabInteraction.RegisterHandler(context =>
        {
            CloseCurrentTab();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ActivateTabInteraction.RegisterHandler(context =>
        {
            ActivateTabById(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.CloseTabInteraction.RegisterHandler(context =>
        {
            CloseTabById(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.SwitchToTabByIndexInteraction.RegisterHandler(context =>
        {
            SwitchToTab(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.CycleTabInteraction.RegisterHandler(context =>
        {
            CycleTab(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.CopySelectionInteraction.RegisterHandler(async context =>
        {
            await CopySelection();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.PasteClipboardInteraction.RegisterHandler(async context =>
        {
            await PasteClipboard();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ApplyFontSizeInteraction.RegisterHandler(context =>
        {
            ApplyFontSize(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ApplyThemeInteraction.RegisterHandler(context =>
        {
            ApplyTheme(context.Input);
            context.SetOutput(Unit.Default);
        }));
    }

    private bool TryInitializeGhostty()
    {
        // The Ghostty embedded API (Ghostty Rendered/Native modes) currently only supports macOS.
        // On Windows and Linux, the app falls through to Rendered (Custom PTY) mode.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _viewModel.SetRenderMode(useRenderedControl: false, useNativeControl: false, useNativeVtControl: false);
            return false;
        }

        try
        {
            if (!Ghostty.Initialize())
            {
                _viewModel.SetRenderMode(useRenderedControl: false, useNativeControl: false, useNativeVtControl: false);
                return false;
            }

            _ghosttyConfig = new GhosttyConfig();
            _ghosttyConfig.LoadDefaultFiles();
            _ghosttyConfig.Finalize_();

            _ghosttyApp = new GhosttyApp(_ghosttyConfig);

            _tickTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };
            _tickTimer.Tick += (_, _) => _ghosttyApp?.Tick();
            _tickTimer.Start();

            _viewModel.SetRenderMode(useRenderedControl: true, useNativeControl: true, useNativeVtControl: false);

            GhosttyLibraryInfo info = Ghostty.GetInfo();
            UpdateStatus($"Ghostty {info.Version} - custom rendered mode");
            return true;
        }
        catch (Exception)
        {
            _viewModel.SetRenderMode(useRenderedControl: false, useNativeControl: false, useNativeVtControl: false);
            return false;
        }
    }

    #region Tab Management

    private void CreateNewTab()
    {
        _tabCounter++;
        string tabName = $"Terminal {_tabCounter}";
        Control terminal;

        if (_viewModel.UseRenderedControl && _ghosttyApp is not null)
        {
            GhosttyRenderedTerminalControl renderedControl = new()
            {
                TerminalFontSize = (float)_viewModel.FontSize,
                FontFamilyName = MonoFont,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            renderedControl.Initialize(_ghosttyApp);

            renderedControl.TitleChanged += (_, title) =>
            {
                TerminalTab? tab = _tabs.Find(t => t.Control == renderedControl);
                tab?.UpdateTitle(title);
            };
            renderedControl.ProcessExited += (_, code) => UpdateStatus($"Process exited: {code}");
            renderedControl.CloseRequested += (_, _) =>
            {
                TerminalTab? tab = _tabs.Find(t => t.Control == renderedControl);
                if (tab is not null)
                {
                    CloseTab(tab);
                }
            };
            renderedControl.TerminalResized += (_, args) =>
            {
                if (_activeTab?.Control == renderedControl)
                {
                    UpdateDimensions(args.Columns, args.Rows);
                }
            };

            terminal = renderedControl;
        }
        else if (_viewModel.UseNativeControl && _ghosttyApp is not null)
        {
            GhosttyNativeTerminalControl nativeControl = new()
            {
                TerminalFontSize = (float)_viewModel.FontSize,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            nativeControl.Initialize(_ghosttyApp);

            nativeControl.TitleChanged += (_, title) =>
            {
                TerminalTab? tab = _tabs.Find(t => t.Control == nativeControl);
                tab?.UpdateTitle(title);
            };
            nativeControl.ProcessExited += (_, code) => UpdateStatus($"Process exited: {code}");
            nativeControl.CloseRequested += (_, _) =>
            {
                TerminalTab? tab = _tabs.Find(t => t.Control == nativeControl);
                if (tab is not null)
                {
                    CloseTab(tab);
                }
            };
            nativeControl.TerminalResized += (_, args) =>
            {
                if (_activeTab?.Control == nativeControl)
                {
                    UpdateDimensions(args.Columns, args.Rows);
                }
            };

            terminal = nativeControl;
        }
        else
        {
            ThemePalette palette = GetPalette(_viewModel.IsDarkTheme);

            GhosttyTerminalControl standaloneControl = new()
            {
                FontFamilyName = MonoFont,
                TerminalFontSize = _viewModel.FontSize,
                Columns = 80,
                Rows = 24,
                ScrollbackLimit = 10_000,
                UseNativeVtProcessor = _viewModel.UseNativeVtControl ? true : null,
                DefaultForeground = palette.TerminalForeground,
                DefaultBackground = palette.TerminalBackground,
            };

            standaloneControl.DataReceived += (_, args) =>
            {
                UpdateStatus($"Received {args.Data.Length} bytes");
            };
            standaloneControl.TerminalResized += (_, args) =>
            {
                UpdateDimensions(args.Columns, args.Rows);
            };

            bool needsPty = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                            || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                            || RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            terminal = standaloneControl;

            if (needsPty)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        standaloneControl.StartPty(workingDirectory: home);
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"Failed to start PTY: {ex.Message}");
                    }
                }, DispatcherPriority.Background);
            }
        }

        TabVisualMode tabMode = ResolveTabMode(terminal);
        Button headerButton = CreateTabHeader(tabName, tabMode);

        Control container = terminal is GhosttyTerminalControl
            ? new ScrollViewer
            {
                Content = terminal,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            }
            : terminal;

        TerminalTab tab = new(headerButton, terminal, container, _tabCounter, tabMode.Name);
        tab.CloseButton.Command = _viewModel.CloseTabCommand;
        tab.CloseButton.CommandParameter = tab.Index;
        headerButton.Command = _viewModel.ActivateTabCommand;
        headerButton.CommandParameter = tab.Index;

        _tabs.Add(tab);

        container.IsVisible = false;
        _terminalHost.Children.Add(container);
        _tabStrip.Children.Add(headerButton);

        SwitchToTab(_tabs.Count - 1);
        UpdateStatus($"Opened {tabName}");
    }

    private TabVisualMode ResolveTabMode(Control terminal)
    {
        if (_viewModel.UseRenderedControl && _ghosttyApp is not null)
        {
            return new TabVisualMode(
                "Rendered (Ghostty VT + SkiaSharp)",
                "\u25CF",
                new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6)));
        }

        if (_viewModel.UseNativeControl && _ghosttyApp is not null)
        {
            return new TabVisualMode(
                "Native (Ghostty Metal)",
                "\u25C6",
                new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xAA)));
        }

        if (_viewModel.UseNativeVtControl)
        {
            return new TabVisualMode(
                "Native VT (libghostty-terminal)",
                "\u25B2",
                new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78)));
        }

        string vtLabel = terminal is GhosttyTerminalControl gtc && gtc.IsUsingNativeVtProcessor
            ? "Ghostty VT"
            : "Basic VT";
        return new TabVisualMode(
            $"Rendered (Custom PTY - {vtLabel})",
            "\u25A0",
            new SolidColorBrush(Color.FromRgb(0x6A, 0x99, 0x55)));
    }

    private static Button CreateTabHeader(string title, TabVisualMode mode)
    {
        TextBlock modeIndicator = new()
        {
            Text = mode.Glyph,
            Foreground = mode.GlyphBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10,
        };

        TextBlock titleText = new()
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        };

        Button closeButton = new()
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

        StackPanel headerContent = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        headerContent.Children.Add(modeIndicator);
        headerContent.Children.Add(titleText);
        headerContent.Children.Add(closeButton);

        Button headerButton = new()
        {
            Content = headerContent,
            Padding = new Thickness(12, 6),
            BorderThickness = new Thickness(0),
        };
        headerButton.Classes.Add("tabHeader");
        ToolTip.SetTip(headerButton, mode.Name);

        headerButton.Tag = closeButton;
        return headerButton;
    }

    private void ActivateTabById(int tabId)
    {
        int tabIndex = _tabs.FindIndex(tab => tab.Index == tabId);
        if (tabIndex >= 0)
        {
            SwitchToTab(tabIndex);
        }
    }

    private void CloseTabById(int tabId)
    {
        TerminalTab? tab = _tabs.Find(candidate => candidate.Index == tabId);
        if (tab is not null)
        {
            CloseTab(tab);
        }
    }

    private void CloseTab(TerminalTab tab)
    {
        int tabIndex = _tabs.IndexOf(tab);
        if (tabIndex < 0)
        {
            return;
        }

        _tabs.Remove(tab);

        _terminalHost.Children.Remove(tab.Container);
        _tabStrip.Children.Remove(tab.HeaderButton);

        DisposeTerminal(tab.Control);

        if (_tabs.Count == 0)
        {
            _activeTab = null;
            CreateNewTab();
        }
        else
        {
            int newIndex = Math.Min(tabIndex, _tabs.Count - 1);
            SwitchToTab(newIndex);
        }

        UpdateStatus($"Closed {tab.Title}");
    }

    private void CloseCurrentTab()
    {
        if (_activeTab is not null)
        {
            CloseTab(_activeTab);
        }
    }

    private TerminalTab? GetActiveTab() => _activeTab;

    private void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            return;
        }

        foreach (TerminalTab tab in _tabs)
        {
            tab.Container.IsVisible = false;
            tab.HeaderButton.Classes.Remove("active");
        }

        TerminalTab target = _tabs[index];
        target.Container.IsVisible = true;
        target.HeaderButton.Classes.Add("active");
        _activeTab = target;

        Dispatcher.UIThread.Post(() => target.Control.Focus(), DispatcherPriority.Input);
    }

    private void CycleTab(bool forward)
    {
        if (_tabs.Count <= 1)
        {
            return;
        }

        int currentIndex = _activeTab is not null ? _tabs.IndexOf(_activeTab) : 0;
        int next = forward
            ? (currentIndex + 1) % _tabs.Count
            : (currentIndex - 1 + _tabs.Count) % _tabs.Count;
        SwitchToTab(next);
    }

    #endregion

    #region Clipboard

    private async Task CopySelection()
    {
        TerminalTab? tab = GetActiveTab();
        if (tab is null)
        {
            return;
        }

        if (tab.Control is GhosttyNativeTerminalControl native)
        {
            await native.CopySelectionAsync();
        }
        else if (tab.Control is GhosttyRenderedTerminalControl rendered)
        {
            await rendered.CopySelectionAsync();
        }
        else if (tab.Control is GhosttyTerminalControl standalone)
        {
            await standalone.CopySelectionAsync();
        }
    }

    private async Task PasteClipboard()
    {
        TerminalTab? tab = GetActiveTab();
        if (tab is null)
        {
            return;
        }

        if (tab.Control is GhosttyNativeTerminalControl native)
        {
            await native.PasteAsync();
        }
        else if (tab.Control is GhosttyRenderedTerminalControl rendered)
        {
            await rendered.PasteAsync();
        }
        else if (tab.Control is GhosttyTerminalControl standalone)
        {
            await standalone.PasteAsync();
        }
    }

    #endregion

    #region Font And Theme

    private void ApplyFontSize(double fontSize)
    {
        foreach (TerminalTab tab in _tabs)
        {
            if (tab.Control is GhosttyTerminalControl standalone)
            {
                standalone.TerminalFontSize = fontSize;
                standalone.InvalidateTerminal();
            }
            else if (tab.Control is GhosttyRenderedTerminalControl rendered)
            {
                rendered.TerminalFontSize = (float)fontSize;
            }
            else if (tab.Control is GhosttyNativeTerminalControl native)
            {
                native.TerminalFontSize = (float)fontSize;
            }
        }
    }

    private void ApplyTheme(bool isDarkTheme)
    {
        ThemePalette palette = GetPalette(isDarkTheme);

        foreach (TerminalTab tab in _tabs)
        {
            if (tab.Control is GhosttyTerminalControl standalone)
            {
                standalone.DefaultForeground = palette.TerminalForeground;
                standalone.DefaultBackground = palette.TerminalBackground;
                standalone.InvalidateTerminal();
            }
            else if (tab.Control is GhosttyRenderedTerminalControl rendered)
            {
                rendered.SetColorScheme(isDarkTheme ? GhosttyColorScheme.Dark : GhosttyColorScheme.Light);
            }
            else if (tab.Control is GhosttyNativeTerminalControl native)
            {
                native.SetColorScheme(isDarkTheme ? GhosttyColorScheme.Dark : GhosttyColorScheme.Light);
            }
        }

        ApplyThemeResources(isDarkTheme);
    }

    private static ThemePalette GetPalette(bool isDarkTheme)
        => isDarkTheme ? DarkPalette : LightPalette;

    private static void ApplyThemeResources(bool isDarkTheme)
    {
        ThemePalette palette = GetPalette(isDarkTheme);
        UpdateBrushResource("WindowBackgroundBrush", palette.WindowBackground);
        UpdateBrushResource("ToolbarBackgroundBrush", palette.ToolbarBackground);
        UpdateBrushResource("ToolbarDividerBrush", palette.ToolbarDivider);
        UpdateBrushResource("ToolbarForegroundBrush", palette.ToolbarForeground);
        UpdateBrushResource("TabStripBackgroundBrush", palette.TabStripBackground);
        UpdateBrushResource("StatusBarBackgroundBrush", palette.StatusBarBackground);
        UpdateBrushResource("StatusBarForegroundBrush", palette.StatusBarForeground);
        UpdateBrushResource("TabHeaderBackgroundBrush", palette.TabHeaderBackground);
        UpdateBrushResource("TabHeaderForegroundBrush", palette.TabHeaderForeground);
        UpdateBrushResource("TabHeaderActiveBackgroundBrush", palette.TabHeaderActiveBackground);
        UpdateBrushResource("TabHeaderActiveForegroundBrush", palette.TabHeaderActiveForeground);
    }

    private static void UpdateBrushResource(string key, Color color)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    #endregion

    #region Status

    private void UpdateStatus(string text)
    {
        _viewModel.SetStatus(text);
    }

    private void UpdateDimensions(int columns, int rows)
    {
        _viewModel.SetDimensions(columns, rows);
    }

    #endregion

    private void DisposeResources()
    {
        _tickTimer?.Stop();

        foreach (TerminalTab tab in _tabs)
        {
            DisposeTerminal(tab.Control);
        }
        _tabs.Clear();

        _ghosttyApp?.Dispose();
        _ghosttyConfig?.Dispose();
    }

    private static void DisposeTerminal(Control control)
    {
        if (control is GhosttyTerminalControl standaloneControl)
        {
            standaloneControl.StopPty();
            standaloneControl.DetachSurface();
            return;
        }

        if (control is GhosttyRenderedTerminalControl renderedControl)
        {
            renderedControl.Dispose();
            return;
        }

        if (control is GhosttyNativeTerminalControl nativeControl)
        {
            nativeControl.Dispose();
        }
    }

    private sealed class TerminalTab
    {
        public TerminalTab(Button headerButton, Control control, Control container, int index, string modeName)
        {
            HeaderButton = headerButton;
            Control = control;
            Container = container;
            Index = index;
            ModeName = modeName;
            CloseButton = headerButton.Tag as Button
                ?? throw new InvalidOperationException("Tab header close button is missing.");
            TitleText = ((headerButton.Content as StackPanel)?.Children[1] as TextBlock)
                ?? throw new InvalidOperationException("Tab header title text is missing.");
        }

        public Button HeaderButton { get; }
        public Button CloseButton { get; }
        public Control Control { get; }
        public Control Container { get; }
        public TextBlock TitleText { get; }
        public int Index { get; }
        public string ModeName { get; }
        public string Title => TitleText.Text ?? $"Terminal {Index}";

        public void UpdateTitle(string title)
        {
            TitleText.Text = title;
        }
    }

    private readonly record struct TabVisualMode(string Name, string Glyph, IBrush GlyphBrush);

    private readonly record struct ThemePalette(
        Color WindowBackground,
        Color ToolbarBackground,
        Color ToolbarDivider,
        Color ToolbarForeground,
        Color TabStripBackground,
        Color StatusBarBackground,
        Color StatusBarForeground,
        Color TabHeaderBackground,
        Color TabHeaderForeground,
        Color TabHeaderActiveBackground,
        Color TabHeaderActiveForeground,
        Color TerminalForeground,
        Color TerminalBackground);
}
