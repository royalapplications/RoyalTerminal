// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — UI flow tests for demo ViewModel command surface.

using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Demo;
using RoyalTerminal.Demo.Services;
using RoyalTerminal.Demo.ViewModels;
using RoyalTerminal.Demo.Views;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using ReactiveUI;
using Xunit;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace RoyalTerminal.Tests;

[Collection("MainWindowControllerHeadlessTests")]
public class MainWindowViewModelFlowTests
{
    [Fact]
    public void DemoAssembly_UsesRoyalTerminalProductTitle()
    {
        Assembly assembly = typeof(MainWindow).Assembly;

        Assert.Equal("RoyalTerminal", assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
        Assert.Equal("RoyalTerminal", assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
    }

    [AvaloniaFact]
    public void DemoApp_UsesRoyalTerminalApplicationName()
    {
        App app = new();

        app.Initialize();

        Assert.Equal("RoyalTerminal", app.Name);

        NativeMenu applicationMenu = NativeMenu.GetMenu(app)
            ?? throw new InvalidOperationException("Application native menu was not initialized from App.axaml.");

        Assert.Equal("_About RoyalTerminal", Assert.IsType<NativeMenuItem>(applicationMenu.Items[0]).Header);
        Assert.False(ContainsNativeMenuItem(applicationMenu, "About Avalonia"));
    }

    [AvaloniaFact]
    public void DemoApp_ProvidesRoyalTerminalLogoGeometry()
    {
        App app = new();

        app.Initialize();

        Assert.True(app.Resources.TryGetResource("Icon.RoyalTerminalLogo", null, out object? resource));

        Geometry geometry = Assert.IsAssignableFrom<Geometry>(resource);

        Assert.True(geometry.Bounds.Width > 0);
        Assert.True(geometry.Bounds.Height > 0);
    }

    [AvaloniaFact]
    public void MainWindow_ClearScrollbackMenuItem_IsBoundToClearActiveScrollbackCommand()
    {
        MainWindow window = new();

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            NativeMenu menu = NativeMenu.GetMenu(window)
                ?? throw new InvalidOperationException("MainWindow native menu was not found.");
            NativeMenuItem clearScrollbackItem = FindNativeMenuItem(menu, "_Clear Scrollback");

            Assert.Same(viewModel.ClearActiveScrollbackCommand, clearScrollbackItem.Command);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_NativeMenu_DoesNotDuplicateMacOSApplicationMenu()
    {
        MainWindow window = new();

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            NativeMenu menu = NativeMenu.GetMenu(window)
                ?? throw new InvalidOperationException("MainWindow native menu was not found.");
            NativeMenuItem commandHistoryItem = FindNativeMenuItem(menu, "_Command History");

            Assert.DoesNotContain(
                menu.Items.OfType<NativeMenuItem>(),
                item => string.Equals(
                    Convert.ToString(item.Header, CultureInfo.InvariantCulture),
                    "_RoyalTerminal",
                    StringComparison.Ordinal));
            Assert.False(ContainsNativeMenuItem(menu, "_About RoyalTerminal"));
            Assert.False(ContainsNativeMenuItem(menu, "_Quit RoyalTerminal"));
            Assert.Same(viewModel.OpenCommandHistoryOverlayCommand, commandHistoryItem.Command);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ApplicationNativeMenu_ReplacesAvaloniaDefaultApplicationItems()
    {
        MainWindowViewModel viewModel = new();
        NativeMenu menu = ApplicationNativeMenuFactory.Create(viewModel);

        NativeMenuItem aboutItem = FindNativeMenuItem(menu, "_About RoyalTerminal");
        NativeMenuItem preferencesItem = FindNativeMenuItem(menu, "_Preferences...");
        NativeMenuItem quitItem = FindNativeMenuItem(menu, "_Quit RoyalTerminal");

        Assert.Equal(5, menu.Items.Count);
        Assert.Equal("_About RoyalTerminal", Assert.IsType<NativeMenuItem>(menu.Items[0]).Header);
        Assert.IsType<NativeMenuItemSeparator>(menu.Items[1]);
        Assert.Equal("_Preferences...", Assert.IsType<NativeMenuItem>(menu.Items[2]).Header);
        Assert.IsType<NativeMenuItemSeparator>(menu.Items[3]);
        Assert.Equal("_Quit RoyalTerminal", Assert.IsType<NativeMenuItem>(menu.Items[4]).Header);
        Assert.Same(viewModel.ShowAboutCommand, aboutItem.Command);
        Assert.Same(viewModel.PrepareSettingsPanelCommand, preferencesItem.Command);
        Assert.Same(viewModel.QuitApplicationCommand, quitItem.Command);
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), preferencesItem.Gesture);
        Assert.Equal(new KeyGesture(Key.Q, KeyModifiers.Meta), quitItem.Gesture);
        Assert.False(ContainsNativeMenuItem(menu, "_RoyalTerminal"));
        Assert.False(ContainsNativeMenuItem(menu, "About Avalonia"));
    }

    [Fact]
    public void ApplicationNativeMenu_BindsDeclaredApplicationMenuShell()
    {
        MainWindowViewModel viewModel = new();
        NativeMenu menu = ApplicationNativeMenuFactory.CreateShell();

        ApplicationNativeMenuFactory.Bind(menu, viewModel);

        NativeMenuItem aboutItem = FindNativeMenuItem(menu, "_About RoyalTerminal");
        NativeMenuItem preferencesItem = FindNativeMenuItem(menu, "_Preferences...");
        NativeMenuItem quitItem = FindNativeMenuItem(menu, "_Quit RoyalTerminal");

        Assert.Same(viewModel.ShowAboutCommand, aboutItem.Command);
        Assert.Same(viewModel.PrepareSettingsPanelCommand, preferencesItem.Command);
        Assert.Same(viewModel.QuitApplicationCommand, quitItem.Command);
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), preferencesItem.Gesture);
        Assert.Equal(new KeyGesture(Key.Q, KeyModifiers.Meta), quitItem.Gesture);
        Assert.False(ContainsNativeMenuItem(menu, "About Avalonia"));
    }

    [Fact]
    public void ApplicationNativeMenu_LeafItemsAreCommandBackedAfterBinding()
    {
        MainWindowViewModel viewModel = new();
        NativeMenu menu = ApplicationNativeMenuFactory.Create(viewModel);

        AssertNativeMenuLeafItemsAreCommandBacked(menu);
    }

    [AvaloniaFact]
    public void ApplicationNativeMenu_DoesNotInstallIntoWindowMenu()
    {
        MainWindow window = new();

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            NativeMenu menu = NativeMenu.GetMenu(window)
                ?? throw new InvalidOperationException("MainWindow native menu was not found.");

            NativeMenuItem shellItem = FindNativeMenuItem(menu, "_Shell");

            Assert.Same(viewModel.NewTabCommand, FindNativeMenuItem(shellItem.Menu!, "_New Tab").Command);
            Assert.False(ContainsNativeMenuItem(menu, "_RoyalTerminal"));
            Assert.False(ContainsNativeMenuItem(menu, "_About RoyalTerminal"));
            Assert.False(ContainsNativeMenuItem(menu, "_Quit RoyalTerminal"));
            Assert.False(ContainsNativeMenuItem(menu, "About Avalonia"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_NativeMenu_LeafItemsAndKeyBindingsAreCommandBacked()
    {
        MainWindow window = new();

        try
        {
            NativeMenu menu = NativeMenu.GetMenu(window)
                ?? throw new InvalidOperationException("MainWindow native menu was not found.");

            AssertNativeMenuLeafItemsAreCommandBacked(menu);

            foreach (KeyBinding keyBinding in window.KeyBindings)
            {
                Assert.NotNull(keyBinding.Command);
                Assert.True(
                    ContainsNativeMenuCommand(menu, keyBinding.Command),
                    $"Expected key binding '{keyBinding.Gesture}' command to be mirrored in the native menu.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_NativeMenu_ExposesDemoCommandSurface()
    {
        MainWindow window = new();

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            NativeMenu menu = NativeMenu.GetMenu(window)
                ?? throw new InvalidOperationException("MainWindow native menu was not found.");

            AssertNativeMenuCommand(menu, viewModel.NewTabCommand, "_New Tab");
            AssertNativeMenuCommand(menu, viewModel.CloseCurrentTabCommand, "_Close Tab");
            AssertNativeMenuCommand(menu, viewModel.OpenCommandHistoryOverlayCommand, "_Command History");
            AssertNativeMenuCommand(menu, viewModel.RefreshCommandSuggestionsCommand, "_Refresh Command Suggestions");
            AssertNativeMenuCommand(menu, viewModel.AcceptCommandSuggestionCommand, "_Insert Selected Suggestion");
            AssertNativeMenuCommand(menu, viewModel.CloseCommandHistoryOverlayCommand, "Close Command _History");
            AssertNativeMenuCommand(menu, viewModel.SplitPaneRightCommand, "Split Pane _Right");
            AssertNativeMenuCommand(menu, viewModel.SplitPaneDownCommand, "Split Pane _Down");
            AssertNativeMenuCommand(menu, viewModel.RefreshSessionLauncherCommand, "_Refresh Profiles");
            AssertNativeMenuCommand(menu, viewModel.LaunchSelectedSessionProfileCommand, "_Launch Selected Profile");
            AssertNativeMenuCommand(menu, viewModel.RestartActiveSessionCommand, "_Restart Session");
            AssertNativeMenuCommand(menu, viewModel.ClearActiveScrollbackCommand, "_Clear Scrollback");
            AssertNativeMenuCommand(menu, viewModel.CopySelectionCommand, "_Copy");
            AssertNativeMenuCommand(menu, viewModel.PasteClipboardCommand, "_Paste");
            AssertNativeMenuCommand(menu, viewModel.SelectAllCommand, "Select _All");
            AssertNativeMenuCommand(menu, viewModel.ApplySearchCommand, "_Find");
            AssertNativeMenuCommand(menu, viewModel.NextSearchCommand, "Find _Next");
            AssertNativeMenuCommand(menu, viewModel.PreviousSearchCommand, "Find _Previous");
            AssertNativeMenuCommand(menu, viewModel.ClearSearchCommand, "_Clear Search");
            AssertNativeMenuCommand(menu, viewModel.IncreaseFontSizeCommand, "_Increase Font Size");
            AssertNativeMenuCommand(menu, viewModel.DecreaseFontSizeCommand, "_Decrease Font Size");
            AssertNativeMenuCommand(menu, viewModel.ResetFontSizeCommand, "_Reset Font Size");
            AssertNativeMenuCommand(menu, viewModel.ToggleThemeCommand, "_Toggle Light Theme");
            AssertNativeMenuCommand(menu, viewModel.GenerateThemeCommand, "_Generate Theme");
            AssertNativeMenuCommand(menu, viewModel.PrepareSettingsPanelCommand, "_Preferences...");
            AssertNativeMenuCommand(menu, viewModel.CloseSettingsPanelCommand, "Close Preferences");
            Assert.Equal(viewModel.IsSettingsPanelOpen, FindNativeMenuItem(menu, "Preferences _Actions").IsEnabled);
            AssertNativeMenuCommand(menu, viewModel.SettingsPanelState.NewProfileCommand, "_New Profile");
            AssertNativeMenuCommand(menu, viewModel.SettingsPanelState.DuplicateProfileCommand, "_Duplicate Profile");
            AssertNativeMenuCommand(menu, viewModel.SettingsPanelState.DeleteProfileCommand, "_Delete Profile");
            AssertNativeMenuCommand(menu, viewModel.SettingsPanelState.SetDefaultProfileCommand, "Set _Default Profile");
            AssertNativeMenuCommand(menu, viewModel.SettingsPanelState.ApplyCommand, "_Apply Settings");
            AssertNativeMenuCommand(menu, viewModel.SettingsPanelState.SaveCommand, "_Save Settings");
            AssertNativeMenuCommand(menu, viewModel.SettingsPanelState.BrowseFontFileCommand, "_Browse Font File");
            AssertNativeMenuCommand(menu, viewModel.SettingsPanelState.AddTextHighlightRuleCommand, "Add Text Highlight _Rule");
            AssertNativeMenuCommand(menu, viewModel.ToggleGhosttyDiagnosticsCommand, "_Diagnostics");
            AssertNativeMenuCommand(menu, viewModel.ClearEventLogCommand, "_Clear Event Log");
            AssertNativeMenuCommand(menu, viewModel.TogglePreserveScrollbackOnRestartCommand, "_Preserve Scrollback on Restart");
            AssertNativeMenuCommand(menu, viewModel.ToggleSixelGraphicsCommand, "_Sixel Graphics");
            AssertNativeMenuCommand(menu, viewModel.SelectCaptureFormatCommand, "RoyalTerminal JSON");
            AssertNativeMenuCommand(menu, viewModel.SelectCaptureFormatCommand, "Asciicast v3");
            AssertNativeMenuCommand(menu, viewModel.SaveCaptureCommand, "_Save Capture");
            AssertNativeMenuCommand(menu, viewModel.LoadReplayCommand, "_Load Replay");
            AssertNativeMenuCommand(menu, viewModel.ToggleReplayPlaybackCommand, "Replay _Play/Pause");
            AssertNativeMenuCommand(menu, viewModel.StopReplayCommand, "_Stop Replay");
            Assert.Equal(
                TerminalCaptureSessionFormats.RoyalTerminalJsonId,
                FindNativeMenuItem(menu, "RoyalTerminal JSON").CommandParameter);
            Assert.Equal(
                TerminalCaptureSessionFormats.AsciicastV3Id,
                FindNativeMenuItem(menu, "Asciicast v3").CommandParameter);
            Assert.Equal(MenuItemToggleType.Radio, FindNativeMenuItem(menu, "RoyalTerminal JSON").ToggleType);
            Assert.Equal(MenuItemToggleType.Radio, FindNativeMenuItem(menu, "Asciicast v3").ToggleType);
            Assert.Equal(
                viewModel.PreserveScrollbackOnRestart,
                FindNativeMenuItem(menu, "_Preserve Scrollback on Restart").IsChecked);
            Assert.Equal(
                viewModel.SixelGraphicsEnabled,
                FindNativeMenuItem(menu, "_Sixel Graphics").IsChecked);
            Assert.Equal(
                viewModel.IsRoyalTerminalJsonCaptureFormatSelected,
                FindNativeMenuItem(menu, "RoyalTerminal JSON").IsChecked);
            Assert.Equal(
                viewModel.IsAsciicastV3CaptureFormatSelected,
                FindNativeMenuItem(menu, "Asciicast v3").IsChecked);
            AssertNativeMenuCommand(menu, viewModel.CycleTabForwardCommand, "_Next Tab");
            AssertNativeMenuCommand(menu, viewModel.CycleTabBackwardCommand, "_Previous Tab");
            AssertNativeMenuCommand(menu, viewModel.FocusPaneLeftCommand, "Focus Pane _Left");
            AssertNativeMenuCommand(menu, viewModel.FocusPaneRightCommand, "Focus Pane _Right");
            AssertNativeMenuCommand(menu, viewModel.FocusPaneUpCommand, "Focus Pane _Up");
            AssertNativeMenuCommand(menu, viewModel.FocusPaneDownCommand, "Focus Pane _Down");
            AssertNativeMenuCommand(menu, viewModel.ResizePaneLeftCommand, "Resize Pane Left");
            AssertNativeMenuCommand(menu, viewModel.ResizePaneRightCommand, "Resize Pane Right");
            AssertNativeMenuCommand(menu, viewModel.ResizePaneUpCommand, "Resize Pane Up");
            AssertNativeMenuCommand(menu, viewModel.ResizePaneDownCommand, "Resize Pane Down");
            AssertNativeMenuCommand(menu, viewModel.ShowHyperlinkSampleCommand, "_Hyperlink Sample");
            AssertNativeMenuCommand(menu, viewModel.ShowKittyGraphicsSampleCommand, "_Kitty Graphics Sample");
            AssertNativeMenuCommand(menu, viewModel.CopyPlainSnapshotCommand, "_Copy Plain Snapshot");
            AssertNativeMenuCommand(menu, viewModel.CopyStyledVtSnapshotCommand, "Copy _VT Snapshot");
            AssertNativeMenuCommand(menu, viewModel.CopyHtmlSnapshotCommand, "Copy _HTML Snapshot");
            Assert.True(ContainsNativeMenuCommand(menu, viewModel.CycleRenderModeCommand));
            Assert.True(ContainsNativeMenuCommand(menu, viewModel.CycleThemePresetCommand));
            Assert.True(ContainsNativeMenuCommand(menu, viewModel.ToggleCaptureCommand));
            Assert.True(ContainsNativeMenuCommand(menu, viewModel.SwitchToTabByIndexCommand));
            Assert.True(ContainsNativeMenuCommand(menu, viewModel.CycleShaderSampleCommand));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_SessionNativeMenu_UsesCommandStateForActiveItems()
    {
        MainWindow window = new();

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            NativeMenu menu = NativeMenu.GetMenu(window)
                ?? throw new InvalidOperationException("MainWindow native menu was not found.");

            AssertCanExecuteNativeMenuCommand(menu, "_Preserve Scrollback on Restart");
            AssertCanExecuteNativeMenuCommand(menu, "_Sixel Graphics");
            AssertCanExecuteNativeMenuCommand(menu, "_Load Replay");
            AssertCanExecuteNativeMenuCommand(viewModel.ToggleCaptureCommand);
            AssertCannotExecuteNativeMenuCommand(menu, "_Save Capture");
            AssertCannotExecuteNativeMenuCommand(menu, "Replay _Play/Pause");
            AssertCannotExecuteNativeMenuCommand(menu, "_Stop Replay");

            viewModel.SetCaptureState(isCaptureActive: false, hasCapture: true);
            AssertCanExecuteNativeMenuCommand(menu, "_Save Capture");

            viewModel.SetReplayState(
                isReplayEnabled: true,
                isReplayPlaying: false,
                replayPositionSeconds: 0,
                replayDurationSeconds: 30,
                replaySourceLabel: "capture.rtcap.json");
            AssertCanExecuteNativeMenuCommand(menu, "Replay _Play/Pause");
            AssertCanExecuteNativeMenuCommand(menu, "_Stop Replay");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void MainWindowViewModel_MenuCommandsUseCommandCanExecuteState()
    {
        MainWindowViewModel viewModel = new();

        AssertCannotExecuteNativeMenuCommand(viewModel.ApplySearchCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.NextSearchCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.PreviousSearchCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.ClearSearchCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.AcceptCommandSuggestionCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.LaunchSelectedSessionProfileCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.ClearEventLogCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.CloseCommandHistoryOverlayCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.CloseSettingsPanelCommand);

        viewModel.SearchQuery = "error";
        AssertCanExecuteNativeMenuCommand(viewModel.ApplySearchCommand);
        AssertCanExecuteNativeMenuCommand(viewModel.ClearSearchCommand);

        viewModel.SetSearchState("error", total: 2, selected: 0, usesNativeScrollback: true);
        AssertCanExecuteNativeMenuCommand(viewModel.NextSearchCommand);
        AssertCanExecuteNativeMenuCommand(viewModel.PreviousSearchCommand);

        viewModel.SetCommandSuggestions(
        [
            new TerminalCommandSuggestion("git status", "/repo", DateTimeOffset.UtcNow, 1),
        ]);
        AssertCanExecuteNativeMenuCommand(viewModel.AcceptCommandSuggestionCommand);

        viewModel.SetSessionLaunchOptions(
        [
            new SessionLaunchOption("shell:zsh", "Zsh", TerminalTransportIds.Pty, "PTY / zsh", "/tmp"),
        ]);
        AssertCanExecuteNativeMenuCommand(viewModel.LaunchSelectedSessionProfileCommand);

        viewModel.EventLogEnabled = true;
        viewModel.AppendEventLogEntry("Connected.");
        AssertCanExecuteNativeMenuCommand(viewModel.ClearEventLogCommand);

        using IDisposable refreshRegistration = viewModel.RefreshCommandSuggestionsInteraction.RegisterHandler(context =>
        {
            context.SetOutput(Unit.Default);
        });
        using IDisposable preparationRegistration = viewModel.PrepareSettingsPanelInteraction.RegisterHandler(context =>
        {
            context.SetOutput(Unit.Default);
        });

        viewModel.OpenCommandHistoryOverlayCommand.Execute().Wait();
        AssertCanExecuteNativeMenuCommand(viewModel.CloseCommandHistoryOverlayCommand);

        viewModel.PrepareSettingsPanelCommand.Execute().Wait();
        AssertCanExecuteNativeMenuCommand(viewModel.CloseSettingsPanelCommand);
    }

    [Fact]
    public void MainWindow_ShowAboutCommand_RoutesThroughInteraction()
    {
        MainWindowViewModel viewModel = new();
        bool handled = false;
        using IDisposable registration = viewModel.ShowAboutInteraction.RegisterHandler(context =>
        {
            handled = true;
            context.SetOutput(Unit.Default);
        });

        viewModel.ShowAboutCommand.Execute().Wait();

        Assert.True(handled);
    }

    [AvaloniaFact]
    public void AboutRoyalTerminalWindow_UsesRoyalTerminalTitleAndLogo()
    {
        AboutRoyalTerminalViewModel viewModel = AboutRoyalTerminalViewModel.CreateDefault();
        AboutRoyalTerminalWindow window = new()
        {
            DataContext = viewModel,
        };

        try
        {
            AvaloniaPath logo = window.FindControl<AvaloniaPath>("AboutLogoGlyph")
                ?? throw new InvalidOperationException("AboutLogoGlyph was not found.");
            TextBlock productName = window.FindControl<TextBlock>("AboutProductName")
                ?? throw new InvalidOperationException("AboutProductName was not found.");

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));

            Assert.Equal("About RoyalTerminal", window.Title);
            Assert.Equal("RoyalTerminal", productName.Text);
            Assert.NotNull(logo.Data);
            Assert.True(logo.Bounds.Width > 0);
            Assert.True(logo.Bounds.Height > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_ProfileAndHistoryButtons_AreBoundToCommands()
    {
        MainWindow window = new();

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            Button profileLauncherButton = window.FindControl<Button>("ProfileLauncherButton")
                ?? throw new InvalidOperationException("ProfileLauncherButton was not found.");
            Button commandHistoryButton = window.FindControl<Button>("CommandHistoryButton")
                ?? throw new InvalidOperationException("CommandHistoryButton was not found.");

            Assert.Same(viewModel.RefreshSessionLauncherCommand, profileLauncherButton.Command);
            Assert.Same(viewModel.OpenCommandHistoryOverlayCommand, commandHistoryButton.Command);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_TopCommandBar_ArrangesPrimaryControlsWithoutScrollbarAtMinimumWidth()
    {
        MainWindow window = new()
        {
            Width = 720,
            Height = 460,
        };

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            NativeMenuBar mainMenuBar = window.FindControl<NativeMenuBar>("MainMenuBar")
                ?? throw new InvalidOperationException("MainMenuBar was not found.");
            Grid topCommandBar = window.FindControl<Grid>("TopCommandBar")
                ?? throw new InvalidOperationException("TopCommandBar was not found.");
            Grid topSearchPanel = window.FindControl<Grid>("TopSearchPanel")
                ?? throw new InvalidOperationException("TopSearchPanel was not found.");
            Grid tabStripLayout = window.FindControl<Grid>("TabStripLayout")
                ?? throw new InvalidOperationException("TabStripLayout was not found.");
            TextBox topSearchBox = window.FindControl<TextBox>("TopSearchBox")
                ?? throw new InvalidOperationException("TopSearchBox was not found.");
            Button topSearchApplyButton = window.FindControl<Button>("TopSearchApplyButton")
                ?? throw new InvalidOperationException("TopSearchApplyButton was not found.");
            Button topSearchPreviousButton = window.FindControl<Button>("TopSearchPreviousButton")
                ?? throw new InvalidOperationException("TopSearchPreviousButton was not found.");
            Border topSearchStatusIdleChip = window.FindControl<Border>("TopSearchStatusIdleChip")
                ?? throw new InvalidOperationException("TopSearchStatusIdleChip was not found.");
            Border topSearchStatusMatchesChip = window.FindControl<Border>("TopSearchStatusMatchesChip")
                ?? throw new InvalidOperationException("TopSearchStatusMatchesChip was not found.");
            Border topSearchStatusNoMatchesChip = window.FindControl<Border>("TopSearchStatusNoMatchesChip")
                ?? throw new InvalidOperationException("TopSearchStatusNoMatchesChip was not found.");
            PathIcon topSearchStatusIdleIcon = window.FindControl<PathIcon>("TopSearchStatusIdleIcon")
                ?? throw new InvalidOperationException("TopSearchStatusIdleIcon was not found.");
            TextBlock topSearchStatusIdleText = window.FindControl<TextBlock>("TopSearchStatusIdleText")
                ?? throw new InvalidOperationException("TopSearchStatusIdleText was not found.");
            TextBlock topSearchStatusMatchesText = window.FindControl<TextBlock>("TopSearchStatusMatchesText")
                ?? throw new InvalidOperationException("TopSearchStatusMatchesText was not found.");
            TextBlock topSearchStatusNoMatchesText = window.FindControl<TextBlock>("TopSearchStatusNoMatchesText")
                ?? throw new InvalidOperationException("TopSearchStatusNoMatchesText was not found.");
            Button tabStripNewTabButton = window.FindControl<Button>("TabStripNewTabButton")
                ?? throw new InvalidOperationException("TabStripNewTabButton was not found.");
            PathIcon tabStripNewTabIcon = window.FindControl<PathIcon>("TabStripNewTabIcon")
                ?? throw new InvalidOperationException("TabStripNewTabIcon was not found.");

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));

            Assert.NotNull(mainMenuBar);
            Assert.Null(window.FindControl<Button>("TopNewTabButton"));
            Assert.True(topCommandBar.ClipToBounds);
            Assert.True(topSearchPanel.ClipToBounds);
            Assert.True(tabStripLayout.ClipToBounds);
            Assert.Empty(topCommandBar.Children.OfType<ScrollViewer>());
            Assert.Same(topSearchPanel, topSearchBox.Parent);
            Assert.Contains("searchField", topSearchBox.Classes);
            Assert.Contains("iconButton", topSearchApplyButton.Classes);
            Assert.Contains("iconButton", topSearchPreviousButton.Classes);
            Assert.Contains("searchStatusChip", topSearchStatusIdleChip.Classes);
            Assert.Contains("searchStatusIdle", topSearchStatusIdleChip.Classes);
            Assert.Contains("searchStatusIcon", topSearchStatusIdleIcon.Classes);
            Assert.Contains("searchStatusText", topSearchStatusIdleText.Classes);
            Assert.True(topSearchStatusIdleChip.IsVisible);
            Assert.False(topSearchStatusMatchesChip.IsVisible);
            Assert.False(topSearchStatusNoMatchesChip.IsVisible);
            Assert.False(topSearchStatusIdleChip.IsHitTestVisible);
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(topSearchStatusIdleChip));
            Assert.Equal("Idle", topSearchStatusIdleText.Text);
            Assert.Equal(viewModel.SearchResultText, ToolTip.GetTip(topSearchStatusIdleChip));
            Assert.True(
                topSearchStatusIdleChip.Opacity < 0.8,
                $"Expected idle search status to be visually muted. Opacity={topSearchStatusIdleChip.Opacity}.");
            Assert.Contains("tabStripNewTab", tabStripNewTabButton.Classes);
            Assert.Contains("tabStripNewTabIcon", tabStripNewTabIcon.Classes);
            Assert.Same(viewModel.NewTabCommand, tabStripNewTabButton.Command);

            viewModel.SetSearchState("needle", total: 2, selected: 0, usesNativeScrollback: true);
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));

            Assert.False(topSearchStatusIdleChip.IsVisible);
            Assert.True(topSearchStatusMatchesChip.IsVisible);
            Assert.False(topSearchStatusNoMatchesChip.IsVisible);
            Assert.Contains("searchStatusMatches", topSearchStatusMatchesChip.Classes);
            Assert.Equal("1/2", topSearchStatusMatchesText.Text);
            Assert.Equal(viewModel.SearchResultText, ToolTip.GetTip(topSearchStatusMatchesChip));

            viewModel.SetSearchState("missing", total: 0, selected: 0, usesNativeScrollback: false);
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));

            Assert.False(topSearchStatusIdleChip.IsVisible);
            Assert.False(topSearchStatusMatchesChip.IsVisible);
            Assert.True(topSearchStatusNoMatchesChip.IsVisible);
            Assert.Contains("searchStatusNoMatches", topSearchStatusNoMatchesChip.Classes);
            Assert.Equal("No matches", topSearchStatusNoMatchesText.Text);
            Assert.Equal(viewModel.SearchResultText, ToolTip.GetTip(topSearchStatusNoMatchesChip));

            Assert.True(topSearchPanel.Bounds.Width > 0);
            Assert.True(tabStripNewTabButton.Bounds.Width > 0);
            Assert.True(
                Math.Abs(topSearchApplyButton.Bounds.Width - 32) <= 0.5,
                $"Expected compact search icon button width. Button={topSearchApplyButton.Bounds}.");
            Assert.True(
                Math.Abs(topSearchApplyButton.Bounds.Height - 32) <= 0.5,
                $"Expected compact search icon button height. Button={topSearchApplyButton.Bounds}.");
            Assert.True(
                topSearchApplyButton.Bounds.Left <= topSearchBox.Bounds.Right + 4.5,
                $"Search action button should sit directly after input. Search={topSearchBox.Bounds}, Button={topSearchApplyButton.Bounds}.");
            Assert.True(
                topSearchPreviousButton.Bounds.Left <= topSearchApplyButton.Bounds.Right + 4.5,
                $"Search navigation buttons should use compact spacing. Apply={topSearchApplyButton.Bounds}, Previous={topSearchPreviousButton.Bounds}.");
            Assert.True(
                topSearchPanel.Bounds.Right <= topCommandBar.Bounds.Width + 0.5,
                $"Top search panel escapes the command bar. Search={topSearchPanel.Bounds}, TopBar={topCommandBar.Bounds}.");
            Assert.True(
                Math.Abs(tabStripNewTabButton.Bounds.Width - 30) <= 0.5,
                $"Expected compact tab strip add button width. Button={tabStripNewTabButton.Bounds}.");
            Assert.True(
                Math.Abs(tabStripNewTabButton.Bounds.Height - 30) <= 0.5,
                $"Expected compact tab strip add button height. Button={tabStripNewTabButton.Bounds}.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_UsesExtendedClientTitleBarLayout()
    {
        MainWindow window = new()
        {
            Width = 720,
            Height = 460,
        };

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            Grid topCommandBar = window.FindControl<Grid>("TopCommandBar")
                ?? throw new InvalidOperationException("TopCommandBar was not found.");
            Grid titleBarLayout = window.FindControl<Grid>("TitleBarLayout")
                ?? throw new InvalidOperationException("TitleBarLayout was not found.");
            Border titleBarDragSurface = window.FindControl<Border>("TitleBarDragSurface")
                ?? throw new InvalidOperationException("TitleBarDragSurface was not found.");
            Border titleBarBrandDragZone = window.FindControl<Border>("TitleBarBrandDragZone")
                ?? throw new InvalidOperationException("TitleBarBrandDragZone was not found.");
            StackPanel titleBarBrandContent = window.FindControl<StackPanel>("TitleBarBrandContent")
                ?? throw new InvalidOperationException("TitleBarBrandContent was not found.");
            Border titleBarBrandIcon = window.FindControl<Border>("TitleBarBrandIcon")
                ?? throw new InvalidOperationException("TitleBarBrandIcon was not found.");
            PathIcon titleBarBrandPathIcon = window.FindControl<PathIcon>("TitleBarBrandPathIcon")
                ?? throw new InvalidOperationException("TitleBarBrandPathIcon was not found.");
            Border macTrafficLightReserve = window.FindControl<Border>("MacTrafficLightReserve")
                ?? throw new InvalidOperationException("MacTrafficLightReserve was not found.");
            TextBox topSearchBox = window.FindControl<TextBox>("TopSearchBox")
                ?? throw new InvalidOperationException("TopSearchBox was not found.");
            Grid statusBarLayout = window.FindControl<Grid>("StatusBarLayout")
                ?? throw new InvalidOperationException("StatusBarLayout was not found.");
            Border statusRenderChip = window.FindControl<Border>("StatusRenderChip")
                ?? throw new InvalidOperationException("StatusRenderChip was not found.");
            Border statusSessionChip = window.FindControl<Border>("StatusSessionChip")
                ?? throw new InvalidOperationException("StatusSessionChip was not found.");
            TextBlock statusSessionTextBlock = window.FindControl<TextBlock>("StatusSessionTextBlock")
                ?? throw new InvalidOperationException("StatusSessionTextBlock was not found.");
            Border statusDimensionsChip = window.FindControl<Border>("StatusDimensionsChip")
                ?? throw new InvalidOperationException("StatusDimensionsChip was not found.");

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));

            Border titleBar = Assert.Single(
                topCommandBar.GetVisualAncestors().OfType<Border>(),
                border => border.Classes.Contains("titleBarArea"));
            Border shellRail = Assert.Single(
                window.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("shellRail"));

            Point titleBarOrigin = titleBar.TranslatePoint(new Point(0, 0), window)
                ?? throw new InvalidOperationException("Title bar is not attached to the window visual tree.");
            Point shellRailOrigin = shellRail.TranslatePoint(new Point(0, 0), window)
                ?? throw new InvalidOperationException("Shell rail is not attached to the window visual tree.");

            Assert.True(window.ExtendClientAreaToDecorationsHint);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
            Assert.Equal(44d, window.ExtendClientAreaTitleBarHeightHint);
            Assert.Contains("titleBarArea", titleBar.Classes);
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(titleBar));
            Assert.True(
                titleBarLayout.Bounds.Height <= 48,
                $"Expected compact titlebar layout. Layout={titleBarLayout.Bounds}.");
            Assert.True(
                macTrafficLightReserve.Bounds.Width >= 88 - 0.5,
                $"Expected titlebar to reserve macOS traffic-light space. Reserve={macTrafficLightReserve.Bounds}.");
            Assert.True(
                titleBarDragSurface.Bounds.Height >= titleBarLayout.Bounds.Height - 0.5,
                $"Expected titlebar drag surface to cover the titlebar height. Drag={titleBarDragSurface.Bounds}, Layout={titleBarLayout.Bounds}.");
            Assert.True(
                titleBarBrandDragZone.Bounds.Width > 0,
                $"Expected brand icon area to be a drag zone. BrandDrag={titleBarBrandDragZone.Bounds}.");
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(titleBarDragSurface));
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(titleBarBrandDragZone));
            Assert.True(titleBarBrandDragZone.IsHitTestVisible);
            Assert.False(titleBarBrandContent.IsHitTestVisible);
            Assert.Contains("titleBrandIcon", titleBarBrandIcon.Classes);
            Assert.Contains("titleBrandIconGlyph", titleBarBrandPathIcon.Classes);
            Assert.DoesNotContain(
                titleBarBrandDragZone.GetVisualDescendants().OfType<TextBlock>(),
                textBlock => string.Equals(textBlock.Text, "RoyalTerminal", StringComparison.Ordinal));
            Assert.DoesNotContain(
                titleBarBrandDragZone.GetVisualDescendants().OfType<TextBlock>(),
                textBlock => string.Equals(textBlock.Text, viewModel.ActiveSessionDisplay, StringComparison.Ordinal));
            Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(topSearchBox));
            Assert.Null(window.FindControl<Button>("TopNewTabButton"));
            Assert.True(statusBarLayout.ClipToBounds);
            Assert.Contains("statusChip", statusRenderChip.Classes);
            Assert.Contains("statusRenderChip", statusRenderChip.Classes);
            Assert.Contains("statusChip", statusSessionChip.Classes);
            Assert.Contains("statusSessionChip", statusSessionChip.Classes);
            Assert.Contains("statusChip", statusDimensionsChip.Classes);
            Assert.Contains("statusDimensionsChip", statusDimensionsChip.Classes);
            Assert.Equal(viewModel.ActiveSessionDisplay, statusSessionTextBlock.Text);
            Assert.Equal(viewModel.ActiveSessionDisplay, ToolTip.GetTip(statusSessionChip));
            Assert.True(
                titleBar.Bounds.Width >= topCommandBar.Bounds.Width,
                $"Expected titlebar surface to host the top command bar. TitleBar={titleBar.Bounds}, TopBar={topCommandBar.Bounds}.");
            Assert.True(
                shellRailOrigin.Y >= titleBarOrigin.Y + titleBar.Bounds.Height - 0.5,
                $"Expected shell rail to start below titlebar. RailY={shellRailOrigin.Y}, TitleBar={titleBarOrigin.Y}+{titleBar.Bounds.Height}.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_SettingsOverlay_ConstrainsSettingsPanelToAvailableHost()
    {
        MainWindow window = new()
        {
            Width = 720,
            Height = 520,
        };

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            using IDisposable preparationRegistration = viewModel.PrepareSettingsPanelInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
            });

            viewModel.PrepareSettingsPanelCommand.Execute().Wait();

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));

            Border settingsOverlay = window.FindControl<Border>("SettingsOverlay")
                ?? throw new InvalidOperationException("SettingsOverlay was not found.");
            TerminalSettingsPanel settingsPanel = Assert.Single(
                settingsOverlay.GetVisualDescendants().OfType<TerminalSettingsPanel>());
            Border settingsRoot = Assert.Single(
                settingsPanel.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("settings-panel-root"));
            Grid overlayHost = settingsOverlay.Parent as Grid
                ?? throw new InvalidOperationException("SettingsOverlay host grid was not found.");

            Assert.True(settingsOverlay.IsVisible);
            Assert.True(settingsOverlay.ClipToBounds);
            Assert.Contains("settingsOverlayPanel", settingsOverlay.Classes);
            Assert.True(
                settingsOverlay.Bounds.Width <= overlayHost.Bounds.Width + 0.5,
                $"Expected settings overlay to fit its host. Overlay={settingsOverlay.Bounds}, Host={overlayHost.Bounds}.");
            Assert.True(
                settingsOverlay.Bounds.Height <= overlayHost.Bounds.Height + 0.5,
                $"Expected settings overlay to fit its host. Overlay={settingsOverlay.Bounds}, Host={overlayHost.Bounds}.");
            Assert.True(
                settingsPanel.Bounds.Width <= settingsOverlay.Bounds.Width + 0.5,
                $"Expected settings panel to fit inside overlay. Panel={settingsPanel.Bounds}, Overlay={settingsOverlay.Bounds}.");
            Assert.True(
                settingsRoot.Bounds.Width <= settingsPanel.Bounds.Width + 0.5,
                $"Expected settings root to fit inside panel. Root={settingsRoot.Bounds}, Panel={settingsPanel.Bounds}.");
            Assert.True(
                settingsRoot.Bounds.Height <= settingsPanel.Bounds.Height + 0.5,
                $"Expected settings root to fit inside panel. Root={settingsRoot.Bounds}, Panel={settingsPanel.Bounds}.");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SessionLauncher_SelectAndLaunch_RoutesSelectedProfileId()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetSessionLaunchOptions(
        [
            new SessionLaunchOption("profile:stored", "Stored", TerminalTransportIds.Pipe, "Pipe / echo", "/tmp"),
        ]);

        string? launched = null;
        using IDisposable registration = viewModel.LaunchSessionProfileInteraction.RegisterHandler(context =>
        {
            launched = context.Input;
            context.SetOutput(Unit.Default);
        });

        viewModel.LaunchSelectedSessionProfileCommand.Execute().Wait();

        Assert.Equal("profile:stored", launched);
    }

    [Fact]
    public void SessionLauncher_LaunchSessionProfileCommand_RoutesParameterizedProfile()
    {
        MainWindowViewModel viewModel = new();
        SessionLaunchOption launchOption = new(
            "profile:row",
            "Row",
            TerminalTransportIds.Pipe,
            "Pipe / row",
            "/tmp");
        string? launched = null;
        using IDisposable registration = viewModel.LaunchSessionProfileInteraction.RegisterHandler(context =>
        {
            launched = context.Input;
            context.SetOutput(Unit.Default);
        });

        viewModel.LaunchSessionProfileCommand.Execute(launchOption).Wait();

        Assert.Equal("profile:row", launched);

        viewModel.LaunchSessionProfileCommand.Execute("shell:zsh").Wait();

        Assert.Equal("shell:zsh", launched);
    }

    [Fact]
    public void CommandHistoryOverlay_OpenRefreshAccept_RoutesSelectedSuggestion()
    {
        MainWindowViewModel viewModel = new();
        List<string?> refreshQueries = [];
        string? accepted = null;
        using IDisposable refreshRegistration = viewModel.RefreshCommandSuggestionsInteraction.RegisterHandler(context =>
        {
            refreshQueries.Add(context.Input);
            context.SetOutput(Unit.Default);
        });
        using IDisposable acceptRegistration = viewModel.AcceptCommandSuggestionInteraction.RegisterHandler(context =>
        {
            accepted = context.Input;
            context.SetOutput(Unit.Default);
        });

        viewModel.CommandSuggestionQuery = "git";
        viewModel.SetCommandSuggestions(
        [
            new TerminalCommandSuggestion("git status", "/repo", DateTimeOffset.UtcNow, 3),
        ]);

        viewModel.OpenCommandHistoryOverlayCommand.Execute().Wait();
        viewModel.AcceptCommandSuggestionCommand.Execute().Wait();

        Assert.Contains("git", refreshQueries);
        Assert.Equal("git status", accepted);
        Assert.False(viewModel.IsCommandHistoryOverlayOpen);
    }

    [Fact]
    public void PaneCommands_RouteSplitFocusAndResizeRequests()
    {
        MainWindowViewModel viewModel = new();
        List<TerminalPaneSplitRequest> splitRequests = [];
        List<TerminalPaneDirection> focusRequests = [];
        List<TerminalPaneDirection> resizeRequests = [];
        using IDisposable splitRegistration = viewModel.SplitPaneInteraction.RegisterHandler(context =>
        {
            splitRequests.Add(context.Input);
            context.SetOutput(Unit.Default);
        });
        using IDisposable focusRegistration = viewModel.FocusPaneInteraction.RegisterHandler(context =>
        {
            focusRequests.Add(context.Input);
            context.SetOutput(Unit.Default);
        });
        using IDisposable resizeRegistration = viewModel.ResizePaneInteraction.RegisterHandler(context =>
        {
            resizeRequests.Add(context.Input);
            context.SetOutput(Unit.Default);
        });

        viewModel.SplitPaneRightCommand.Execute().Wait();
        viewModel.SplitPaneDownCommand.Execute().Wait();
        viewModel.FocusPaneLeftCommand.Execute().Wait();
        viewModel.FocusPaneRightCommand.Execute().Wait();
        viewModel.FocusPaneUpCommand.Execute().Wait();
        viewModel.FocusPaneDownCommand.Execute().Wait();
        viewModel.ResizePaneLeftCommand.Execute().Wait();
        viewModel.ResizePaneRightCommand.Execute().Wait();
        viewModel.ResizePaneUpCommand.Execute().Wait();
        viewModel.ResizePaneDownCommand.Execute().Wait();

        Assert.Equal([TerminalPaneSplitRequest.Right, TerminalPaneSplitRequest.Down], splitRequests);
        Assert.Equal(
            [TerminalPaneDirection.Left, TerminalPaneDirection.Right, TerminalPaneDirection.Up, TerminalPaneDirection.Down],
            focusRequests);
        Assert.Equal(
            [TerminalPaneDirection.Left, TerminalPaneDirection.Right, TerminalPaneDirection.Up, TerminalPaneDirection.Down],
            resizeRequests);
    }

    [Fact]
    public void SettingsPanel_OpenClose_TogglesOverlayAfterPreparation()
    {
        MainWindowViewModel viewModel = new();
        using IDisposable preparationRegistration = viewModel.PrepareSettingsPanelInteraction.RegisterHandler(context =>
        {
            context.SetOutput(Unit.Default);
        });

        viewModel.PrepareSettingsPanelCommand.Execute().Wait();

        Assert.True(viewModel.IsSettingsPanelOpen);

        viewModel.CloseSettingsPanelCommand.Execute().Wait();

        Assert.False(viewModel.IsSettingsPanelOpen);
    }

    [AvaloniaFact]
    public async Task SettingsPanel_PreparationCompletedOffUiThread_UpdatesBoundCommandsOnUiThread()
    {
        MainWindow window = new()
        {
            Width = 720,
            Height = 520,
        };

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            Button closeSettingsPanelButton = new()
            {
                Command = viewModel.CloseSettingsPanelCommand,
            };
            using IDisposable preparationRegistration = viewModel.PrepareSettingsPanelInteraction.RegisterHandler(async context =>
            {
                await Task
                    .Run(() => context.SetOutput(Unit.Default))
                    .ConfigureAwait(false);
            });

            await viewModel.PrepareSettingsPanelCommand.Execute().ToTask();
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.True(viewModel.IsSettingsPanelOpen);
            Assert.True(closeSettingsPanelButton.Command?.CanExecute(null));
            Assert.Equal(
                Dispatcher.UIThread.CheckAccess(),
                closeSettingsPanelButton.CheckAccess());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcut_CtrlT_TriggersNewTabFlow()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var called = new ManualResetEventSlim(false);

        try
        {
            using var registration = viewModel.CreateNewTabInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                called.Set();
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.T, KeyModifiers.Control),
                Command = viewModel.NewTabCommand,
            });

            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);
            Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcut_CtrlD2_TriggersSwitchTabFlow()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var called = new ManualResetEventSlim(false);
        int capturedIndex = -1;

        try
        {
            using var registration = viewModel.SwitchToTabByIndexInteraction.RegisterHandler(context =>
            {
                capturedIndex = context.Input;
                context.SetOutput(Unit.Default);
                called.Set();
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.D2, KeyModifiers.Control),
                Command = viewModel.SwitchToTabByIndexCommand,
                CommandParameter = 1,
            });

            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control);

            Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, capturedIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_CopyPaste_TriggerClipboardFlowAndStatus()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var copyCalled = new ManualResetEventSlim(false);
        using var pasteCalled = new ManualResetEventSlim(false);
        using var selectAllCalled = new ManualResetEventSlim(false);

        try
        {
            using var copyRegistration = viewModel.CopySelectionInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                copyCalled.Set();
            });
            using var pasteRegistration = viewModel.PasteClipboardInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                pasteCalled.Set();
            });
            using var selectAllRegistration = viewModel.SelectAllInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                selectAllCalled.Set();
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.CopySelectionCommand,
            });
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.V, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.PasteClipboardCommand,
            });
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.A, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.SelectAllCommand,
            });

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(copyCalled.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal("Copied to clipboard", viewModel.StatusText);

            window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(pasteCalled.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal("Pasted from clipboard", viewModel.StatusText);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(selectAllCalled.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal("Selected all text", viewModel.StatusText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_CtrlTabAndCtrlShiftTab_TriggerCycleFlow()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var called = new ManualResetEventSlim(false);
        List<bool> directions = [];

        try
        {
            using var registration = viewModel.CycleTabInteraction.RegisterHandler(context =>
            {
                directions.Add(context.Input);
                context.SetOutput(Unit.Default);
                if (directions.Count >= 2)
                {
                    called.Set();
                }
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Tab, KeyModifiers.Control),
                Command = viewModel.CycleTabForwardCommand,
            });
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.CycleTabBackwardCommand,
            });

            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);

            Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal([true, false], directions);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ModeSwitching_CycleRenderMode_UpdatesState()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: true, useNativeVtControl: false);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseRenderedControl);
        Assert.True(viewModel.UseNativeVtControl);
        Assert.False(viewModel.UseManagedVtControl);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.True(viewModel.UseManagedVtControl);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.True(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.False(viewModel.UseManagedVtControl);
    }

    [Fact]
    public void ModeSwitching_NoNative_CyclesManagedAndRendered()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: false);
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseNativeVtControl);
        Assert.True(viewModel.UseManagedVtControl);
        Assert.Equal("Managed VT", viewModel.ModeButtonText);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.True(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.False(viewModel.UseManagedVtControl);
        Assert.Equal("Rendered", viewModel.ModeButtonText);
    }

    [Fact]
    public void ModeSwitching_NativeVtOnly_CyclesNativeManagedAndRendered()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.True(viewModel.UseNativeVtControl);
        Assert.False(viewModel.UseManagedVtControl);
        Assert.Equal("Native VT", viewModel.ModeButtonText);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseNativeVtControl);
        Assert.True(viewModel.UseManagedVtControl);
        Assert.Equal("Managed VT", viewModel.ModeButtonText);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.True(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.False(viewModel.UseManagedVtControl);
        Assert.Equal("Rendered", viewModel.ModeButtonText);
    }

    [Fact]
    public void ModeSwitching_RequestRendered_WithNativeVtOnly_PreservesRenderedMode()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);

        viewModel.SetRenderMode(useRenderedControl: true, useNativeVtControl: false);

        Assert.True(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.Equal("Rendered", viewModel.ModeButtonText);
    }

    [Fact]
    public void ModeSwitching_RequestUnavailableNativeVt_FallsBackToManagedVt()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: false);

        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: true);

        Assert.False(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.True(viewModel.UseManagedVtControl);
        Assert.Equal("Managed VT", viewModel.ModeButtonText);
    }

    [Fact]
    public void ThemeSwitching_CycleThemePreset_UpdatesActiveThemeForCurrentMode()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: true, useNativeVtControl: false);

        string initialButtonText = viewModel.ThemePresetButtonText;
        string? appliedThemeName = null;
        int applyCount = 0;
        using var registration = viewModel.ApplyThemeModelInteraction.RegisterHandler(context =>
        {
            appliedThemeName = context.Input.ThemeName;
            applyCount++;
            context.SetOutput(Unit.Default);
        });

        viewModel.CycleThemePresetCommand.Execute().Wait();

        Assert.Equal(1, applyCount);
        Assert.NotNull(appliedThemeName);
        Assert.Equal($"Theme: {appliedThemeName}", viewModel.ThemePresetButtonText);
        Assert.NotEqual(initialButtonText, viewModel.ThemePresetButtonText);
        Assert.Contains("Theme preset:", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeSwitching_GeneratedTheme_IsTrackedPerMode()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: true);

        TerminalTheme nativeBefore = viewModel.ActiveTheme;
        int applyCount = 0;
        using var registration = viewModel.ApplyThemeModelInteraction.RegisterHandler(context =>
        {
            applyCount++;
            context.SetOutput(Unit.Default);
        });

        viewModel.GenerateThemeCommand.Execute().Wait();
        TerminalTheme nativeGenerated = viewModel.ActiveTheme;
        Assert.NotSame(nativeBefore, nativeGenerated);

        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false, useManagedVtControl: true);
        TerminalTheme managedBefore = viewModel.ActiveTheme;

        viewModel.GenerateThemeCommand.Execute().Wait();
        TerminalTheme managedGenerated = viewModel.ActiveTheme;
        Assert.NotSame(managedBefore, managedGenerated);

        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: true);
        Assert.Same(nativeGenerated, viewModel.ActiveTheme);
        Assert.Equal(2, applyCount);
    }

    [Fact]
    public void ThemeSwitching_ToggleTheme_SwitchesBetweenLightAndModeDefaultPreset()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: true);

        using var registration = viewModel.ApplyThemeModelInteraction.RegisterHandler(context =>
        {
            context.SetOutput(Unit.Default);
        });

        viewModel.ToggleThemeCommand.Execute().Wait();
        Assert.False(viewModel.IsDarkTheme);
        Assert.Equal("Theme: Solarized Light", viewModel.ThemePresetButtonText);

        viewModel.ToggleThemeCommand.Execute().Wait();
        Assert.True(viewModel.IsDarkTheme);
        Assert.Equal("Theme: Gruvbox Dark", viewModel.ThemePresetButtonText);
    }

    [Fact]
    public void SessionTransport_DefaultsToPtyAndShowsLocalConfig()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);

        Assert.Equal(TerminalTransportIds.Pty, viewModel.SelectedTransportMode.Id);
        Assert.True(viewModel.ShowSessionTransportPicker);
        Assert.True(viewModel.IsSessionTransportConfigEnabled);
        Assert.False(viewModel.ShowSessionTransportHint);
        Assert.True(viewModel.ShowLocalSessionFields);
        Assert.False(viewModel.ShowSshSessionFields);
    }

    [Fact]
    public void SessionTransport_RenderedMode_KeepsTransportConfigEnabled()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: true, useNativeVtControl: false);

        Assert.True(viewModel.ShowSessionTransportPicker);
        Assert.True(viewModel.IsSessionTransportConfigEnabled);
        Assert.False(viewModel.ShowSessionTransportHint);
    }

    [Fact]
    public void SessionTransport_SwitchToSsh_UpdatesVisibilityFlags()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);

        TransportModeOption sshMode = FindTransportMode(viewModel, TerminalTransportIds.Ssh);
        viewModel.SelectedTransportMode = sshMode;

        Assert.True(viewModel.ShowSessionTransportPicker);
        Assert.False(viewModel.ShowLocalSessionFields);
        Assert.True(viewModel.ShowSshSessionFields);
        Assert.True(viewModel.IsSshTransportSelected);
    }

    [Fact]
    public void SessionTransport_SshAuthMode_Password_ShowsPasswordFieldOnly()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Ssh);
        viewModel.SelectedSshAuthMode = FindSshAuthMode(viewModel, SshAuthModeOption.PasswordModeId);

        Assert.True(viewModel.ShowSshSessionFields);
        Assert.True(viewModel.ShowSshPasswordField);
        Assert.False(viewModel.ShowSshPrivateKeyField);
        Assert.False(viewModel.ShowSshAgentHint);
    }

    [Fact]
    public void SessionTransport_SshAuthMode_Agent_ShowsAgentHintOnly()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Ssh);
        viewModel.SelectedSshAuthMode = FindSshAuthMode(viewModel, SshAuthModeOption.AgentModeId);

        Assert.True(viewModel.ShowSshSessionFields);
        Assert.False(viewModel.ShowSshPasswordField);
        Assert.False(viewModel.ShowSshPrivateKeyField);
        Assert.True(viewModel.ShowSshAgentHint);
    }

    [Fact]
    public void SessionTransport_IncludesRawTelnetAndSerialModes()
    {
        MainWindowViewModel viewModel = new();

        Assert.Equal(TerminalTransportIds.RawTcp, FindTransportMode(viewModel, TerminalTransportIds.RawTcp).Id);
        Assert.Equal(TerminalTransportIds.Telnet, FindTransportMode(viewModel, TerminalTransportIds.Telnet).Id);
        Assert.Equal(TerminalTransportIds.Serial, FindTransportMode(viewModel, TerminalTransportIds.Serial).Id);
    }

    [Fact]
    public void SettingsCategories_DefaultsToSessionCategory()
    {
        MainWindowViewModel viewModel = new();

        Assert.Equal(SettingsCategoryOption.SessionCategoryId, viewModel.SelectedSettingsCategory.Id);
        Assert.True(viewModel.ShowSessionSettingsCategory);
        Assert.False(viewModel.ShowConnectionSettingsCategory);
        Assert.False(viewModel.ShowTerminalSettingsCategory);
        Assert.False(viewModel.ShowAppearanceSettingsCategory);
        Assert.False(viewModel.ShowSshSettingsCategory);
        Assert.False(viewModel.ShowLoggingSettingsCategory);
    }

    [Fact]
    public void SettingsCategories_SshCategory_ShowsSshFieldsOnlyForSshTransport()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedSettingsCategory = FindSettingsCategory(viewModel, SettingsCategoryOption.SshCategoryId);

        Assert.True(viewModel.ShowSshSettingsCategory);
        Assert.True(viewModel.ShowSshSettingsUnavailableHint);
        Assert.False(viewModel.ShowSshSettingsFields);

        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Ssh);
        viewModel.SelectedSshProxyType = SshProxyType.Socks5;

        Assert.False(viewModel.ShowSshSettingsUnavailableHint);
        Assert.True(viewModel.ShowSshSettingsFields);
        Assert.True(viewModel.ShowSshProxyFields);
    }

    [Fact]
    public void SettingsCategories_LoggingCategory_ExposesLoggingSurface()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedSettingsCategory = FindSettingsCategory(viewModel, SettingsCategoryOption.LoggingCategoryId);

        Assert.True(viewModel.ShowLoggingSettingsCategory);
        Assert.Contains(TerminalSessionLogFormat.PlainText, viewModel.SessionLogFormats);
        Assert.Contains(TerminalSessionLogFormat.RawBytes, viewModel.SessionLogFormats);
        Assert.False(viewModel.SessionLoggingEnabled);
    }

    [Fact]
    public void SettingsCategories_TerminalCategory_ExposesBehaviorOptions()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedSettingsCategory = FindSettingsCategory(viewModel, SettingsCategoryOption.TerminalCategoryId);

        Assert.True(viewModel.ShowTerminalSettingsCategory);
        Assert.False(viewModel.CopyOnSelectEnabled);
        Assert.True(viewModel.EnableBellNotifications);
        Assert.False(viewModel.BackspaceSendsControlH);
        Assert.True(viewModel.EnableTextShaping);
        Assert.True(viewModel.ReflowOnResize);
        Assert.True(viewModel.PreserveScrollbackOnRestart);
        Assert.True(viewModel.SixelGraphicsEnabled);
        Assert.Equal("Sixel: On", viewModel.SixelButtonText);
        viewModel.PreserveScrollbackOnRestart = false;
        Assert.False(viewModel.PreserveScrollbackOnRestart);
        viewModel.SixelGraphicsEnabled = false;
        Assert.Equal("Sixel: Off", viewModel.SixelButtonText);
        Assert.False(viewModel.EnableLigatures);
        Assert.Equal(TerminalPasteSafetyPolicy.None, viewModel.SelectedPasteSafetyPolicy);
        Assert.Contains(TerminalPasteSafetyPolicy.BlockUnsafe, viewModel.PasteSafetyPolicies);
    }

    [Fact]
    public void FontSettings_NormalizeNullAndWhitespaceValues()
    {
        MainWindowViewModel viewModel = new();

        viewModel.FontFamilyName = "  Custom Mono  ";
        viewModel.FontFilePath = "  /tmp/custom-font.ttf  ";

        Assert.Equal("Custom Mono", viewModel.FontFamilyName);
        Assert.Equal("/tmp/custom-font.ttf", viewModel.FontFilePath);

        viewModel.FontFamilyName = null!;
        viewModel.FontFilePath = null!;

        Assert.False(string.IsNullOrWhiteSpace(viewModel.FontFamilyName));
        Assert.Equal(string.Empty, viewModel.FontFilePath);
    }

    [Fact]
    public void Logging_EventLogAppendAndClear_UpdatesSurface()
    {
        MainWindowViewModel viewModel = new();
        viewModel.AppendEventLogEntry("Connected.");
        viewModel.AppendEventLogEntry("Host key accepted.");

        Assert.True(viewModel.HasEventLogEntries);
        Assert.Equal("2 event(s)", viewModel.EventLogEntryCountText);
        Assert.Contains("Connected.", viewModel.EventLogText, StringComparison.Ordinal);

        viewModel.ClearEventLogCommand.Execute().Wait();

        Assert.False(viewModel.HasEventLogEntries);
        Assert.Equal("0 event(s)", viewModel.EventLogEntryCountText);
        Assert.Equal(string.Empty, viewModel.EventLogText);
    }

    [Fact]
    public async Task SshHostKeyPrompt_AcceptCommand_CompletesPrompt()
    {
        MainWindowViewModel viewModel = new();

        Task<bool> promptTask = viewModel.ShowSshHostKeyPromptAsync(CreateSshHostKeyPromptRequest());

        Assert.True(viewModel.IsSshHostKeyPromptVisible);
        Assert.Equal("alice@example.com:22", viewModel.SshHostKeyPromptEndpoint);
        Assert.Equal("ssh-ed25519 (256 bits)", viewModel.SshHostKeyPromptAlgorithm);

        viewModel.AcceptSshHostKeyCommand.Execute().Wait();

        Assert.True(await promptTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(viewModel.IsSshHostKeyPromptVisible);
    }

    [Fact]
    public async Task SshHostKeyPrompt_DeclineCommand_CompletesPrompt()
    {
        MainWindowViewModel viewModel = new();

        Task<bool> promptTask = viewModel.ShowSshHostKeyPromptAsync(CreateSshHostKeyPromptRequest());
        viewModel.DeclineSshHostKeyCommand.Execute().Wait();

        Assert.False(await promptTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(viewModel.IsSshHostKeyPromptVisible);
    }

    [Fact]
    public void SetShellProfiles_UpdatesSelection()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetShellProfiles(
        [
            new ShellProfileOption("zsh", "Zsh", "/bin/zsh"),
            new ShellProfileOption("bash", "Bash", "/bin/bash"),
        ]);

        Assert.NotNull(viewModel.SelectedShellProfile);
        Assert.Equal("zsh", viewModel.SelectedShellProfile!.Id);
        Assert.Equal(2, viewModel.ShellProfiles.Count);
    }

    [Fact]
    public void CaptureReplay_ToggleCaptureCommand_UsesCurrentCaptureState()
    {
        MainWindowViewModel viewModel = new();
        List<bool> toggles = [];
        using IDisposable registration = viewModel.ToggleCaptureInteraction.RegisterHandler(context =>
        {
            toggles.Add(context.Input);
            context.SetOutput(Unit.Default);
        });

        viewModel.ToggleCaptureCommand.Execute().Wait();
        viewModel.SetCaptureState(isCaptureActive: true, hasCapture: false);
        viewModel.ToggleCaptureCommand.Execute().Wait();

        Assert.Equal([true, false], toggles);
    }

    [Fact]
    public void SessionOptions_ToggleCommands_UpdateMenuBackedSettings()
    {
        MainWindowViewModel viewModel = new();

        viewModel.TogglePreserveScrollbackOnRestartCommand.Execute().Wait();
        viewModel.ToggleSixelGraphicsCommand.Execute().Wait();

        Assert.False(viewModel.PreserveScrollbackOnRestart);
        Assert.False(viewModel.SixelGraphicsEnabled);
        Assert.Equal("Sixel: Off", viewModel.SixelButtonText);

        viewModel.TogglePreserveScrollbackOnRestartCommand.Execute().Wait();
        viewModel.ToggleSixelGraphicsCommand.Execute().Wait();

        Assert.True(viewModel.PreserveScrollbackOnRestart);
        Assert.True(viewModel.SixelGraphicsEnabled);
        Assert.Equal("Sixel: On", viewModel.SixelButtonText);
    }

    [Fact]
    public void CaptureReplay_SelectCaptureFormatCommand_UpdatesSelectedFormat()
    {
        MainWindowViewModel viewModel = new();

        Assert.Equal(TerminalCaptureSessionFormats.RoyalTerminalJsonId, viewModel.SelectedCaptureFormat.FormatId);
        Assert.True(viewModel.IsRoyalTerminalJsonCaptureFormatSelected);
        Assert.False(viewModel.IsAsciicastV3CaptureFormatSelected);

        viewModel.SelectCaptureFormatCommand.Execute(TerminalCaptureSessionFormats.AsciicastV3Id).Wait();

        Assert.Equal(TerminalCaptureSessionFormats.AsciicastV3Id, viewModel.SelectedCaptureFormat.FormatId);
        Assert.False(viewModel.IsRoyalTerminalJsonCaptureFormatSelected);
        Assert.True(viewModel.IsAsciicastV3CaptureFormatSelected);

        viewModel.SelectCaptureFormatCommand.Execute("unknown").Wait();

        Assert.Equal(TerminalCaptureSessionFormats.AsciicastV3Id, viewModel.SelectedCaptureFormat.FormatId);
    }

    [Fact]
    public void CaptureReplay_CommandCanExecuteReflectsCaptureAndReplayState()
    {
        MainWindowViewModel viewModel = new();

        AssertCannotExecuteNativeMenuCommand(viewModel.SaveCaptureCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.ToggleReplayPlaybackCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.StopReplayCommand);

        viewModel.SetCaptureState(isCaptureActive: false, hasCapture: true);
        AssertCanExecuteNativeMenuCommand(viewModel.SaveCaptureCommand);

        viewModel.SetReplayState(
            isReplayEnabled: true,
            isReplayPlaying: false,
            replayPositionSeconds: 0,
            replayDurationSeconds: 30,
            replaySourceLabel: "capture.rtcap.json");
        AssertCanExecuteNativeMenuCommand(viewModel.ToggleReplayPlaybackCommand);
        AssertCanExecuteNativeMenuCommand(viewModel.StopReplayCommand);

        viewModel.SetCaptureState(isCaptureActive: false, hasCapture: false);
        viewModel.SetReplayState(
            isReplayEnabled: false,
            isReplayPlaying: false,
            replayPositionSeconds: 0,
            replayDurationSeconds: 0,
            replaySourceLabel: string.Empty);
        AssertCannotExecuteNativeMenuCommand(viewModel.SaveCaptureCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.ToggleReplayPlaybackCommand);
        AssertCannotExecuteNativeMenuCommand(viewModel.StopReplayCommand);
    }

    [Fact]
    public void CaptureReplay_ToggleReplayPlaybackCommand_UsesCurrentPlaybackState()
    {
        MainWindowViewModel viewModel = new();
        List<bool> requests = [];
        using IDisposable registration = viewModel.SetReplayPlayingInteraction.RegisterHandler(context =>
        {
            requests.Add(context.Input);
            context.SetOutput(Unit.Default);
        });

        viewModel.SetReplayState(
            isReplayEnabled: true,
            isReplayPlaying: false,
            replayPositionSeconds: 0,
            replayDurationSeconds: 30,
            replaySourceLabel: "capture.rtcap.json");
        viewModel.ToggleReplayPlaybackCommand.Execute().Wait();

        viewModel.SetReplayState(
            isReplayEnabled: true,
            isReplayPlaying: true,
            replayPositionSeconds: 10,
            replayDurationSeconds: 30,
            replaySourceLabel: "capture.rtcap.json");
        viewModel.ToggleReplayPlaybackCommand.Execute().Wait();

        Assert.Equal([true, false], requests);
    }

    [Fact]
    public void CaptureReplay_SetReplayState_UpdatesTimelineSurface()
    {
        MainWindowViewModel viewModel = new();

        viewModel.SetReplayState(
            isReplayEnabled: true,
            isReplayPlaying: false,
            replayPositionSeconds: 65,
            replayDurationSeconds: 125,
            replaySourceLabel: "session.rtcap.json");

        Assert.True(viewModel.IsReplayEnabled);
        Assert.Equal(125, viewModel.ReplayDurationSeconds);
        Assert.Equal(65, viewModel.ReplayTimelineValue);
        Assert.Equal("01:05 / 02:05", viewModel.ReplayTimelineText);
        Assert.Equal("session.rtcap.json", viewModel.ReplaySourceLabel);
        Assert.True(viewModel.CanSeekReplay);
    }

    [AvaloniaFact]
    public void MainWindow_ReplayStatusControls_AreBoundToReplayCommandsAndSeekState()
    {
        MainWindow window = new()
        {
            Width = 900,
            Height = 520,
        };
        window.Show();

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            Border replayStatusControls = window.FindControl<Border>("ReplayStatusControls")
                ?? throw new InvalidOperationException("ReplayStatusControls was not found.");
            Button replayPlayPauseButton = window.FindControl<Button>("ReplayPlayPauseButton")
                ?? throw new InvalidOperationException("ReplayPlayPauseButton was not found.");
            Button replayStopButton = window.FindControl<Button>("ReplayStopButton")
                ?? throw new InvalidOperationException("ReplayStopButton was not found.");
            Slider replayTimelineSlider = window.FindControl<Slider>("ReplayTimelineSlider")
                ?? throw new InvalidOperationException("ReplayTimelineSlider was not found.");
            TextBlock replayTransportLabelTextBlock = window.FindControl<TextBlock>("ReplayTransportLabelTextBlock")
                ?? throw new InvalidOperationException("ReplayTransportLabelTextBlock was not found.");
            TextBlock replayTimelineTextBlock = window.FindControl<TextBlock>("ReplayTimelineTextBlock")
                ?? throw new InvalidOperationException("ReplayTimelineTextBlock was not found.");

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));

            Assert.Same(viewModel.ToggleReplayPlaybackCommand, replayPlayPauseButton.Command);
            Assert.Same(viewModel.StopReplayCommand, replayStopButton.Command);
            Assert.False(replayStatusControls.IsVisible);

            viewModel.SetReplayState(
                isReplayEnabled: true,
                isReplayPlaying: false,
                replayPositionSeconds: 65,
                replayDurationSeconds: 125,
                replaySourceLabel: "session.rtcap.json");

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));

            Assert.True(replayStatusControls.IsVisible);
            Assert.Contains("statusChip", replayStatusControls.Classes);
            Assert.Contains("replayTransport", replayStatusControls.Classes);
            Assert.Contains("statusIconButton", replayPlayPauseButton.Classes);
            Assert.Contains("statusIconButton", replayStopButton.Classes);
            Assert.Contains("replayLabel", replayTransportLabelTextBlock.Classes);
            Assert.Contains("replayTime", replayTimelineTextBlock.Classes);
            Assert.Equal("Replay", replayTransportLabelTextBlock.Text);
            Assert.Equal("session.rtcap.json", ToolTip.GetTip(replayStatusControls));
            Assert.Equal(125, replayTimelineSlider.Maximum);
            Assert.Equal(65, replayTimelineSlider.Value);
            Assert.True(replayTimelineSlider.IsEnabled);
            Assert.True(
                replayStatusControls.Bounds.Width <= 382.5,
                $"Expected bounded replay controls. Bounds={replayStatusControls.Bounds}.");
            Assert.True(
                replayTimelineSlider.Bounds.Width <= 150.5,
                $"Expected compact replay timeline. Bounds={replayTimelineSlider.Bounds}.");
            Assert.True(
                replayTimelineSlider.Bounds.Height <= 16.5,
                $"Expected compact replay timeline height. Bounds={replayTimelineSlider.Bounds}.");
            Assert.NotNull(replayTimelineSlider.Template);
            Assert.True(
                replayTransportLabelTextBlock.Bounds.Right <= replayPlayPauseButton.Bounds.Left + 0.5,
                $"Expected replay label to stay before controls. Label={replayTransportLabelTextBlock.Bounds}, Play={replayPlayPauseButton.Bounds}.");

            replayTimelineSlider.Value = 42;

            Assert.Equal(42, viewModel.ReplayTimelineValue);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Showcase_SetSearchStateAndDiagnostics_UpdatesSurface()
    {
        MainWindowViewModel viewModel = new();

        viewModel.SetSearchState("ghostty", total: 3, selected: 1, usesNativeScrollback: true);
        viewModel.SetGhosttyDiagnostics(show: true, text: "SIMD: yes");

        Assert.Equal("ghostty", viewModel.SearchQuery);
        Assert.True(viewModel.CanApplySearch);
        Assert.True(viewModel.CanAdvanceSearch);
        Assert.True(viewModel.CanClearSearch);
        Assert.False(viewModel.IsSearchIdle);
        Assert.True(viewModel.HasSearchMatches);
        Assert.False(viewModel.HasSearchNoMatches);
        Assert.Equal("2/3", viewModel.SearchStatusSummaryText);
        Assert.Equal("2/3 matches · native scrollback", viewModel.SearchResultText);
        Assert.True(viewModel.ShowGhosttyDiagnostics);
        Assert.Equal("Hide Diagnostics", viewModel.GhosttyDiagnosticsButtonText);
        Assert.Equal("SIMD: yes", viewModel.GhosttyDiagnosticsText);

        viewModel.SetSearchState("ghostty", total: 0, selected: 0, usesNativeScrollback: true);

        Assert.False(viewModel.IsSearchIdle);
        Assert.False(viewModel.HasSearchMatches);
        Assert.True(viewModel.HasSearchNoMatches);
        Assert.Equal("No matches", viewModel.SearchStatusSummaryText);

        viewModel.ClearSearchState();
        viewModel.SetGhosttyDiagnostics(show: false, text: string.Empty);

        Assert.False(viewModel.CanAdvanceSearch);
        Assert.False(viewModel.CanClearSearch);
        Assert.True(viewModel.IsSearchIdle);
        Assert.False(viewModel.HasSearchMatches);
        Assert.False(viewModel.HasSearchNoMatches);
        Assert.Equal("Idle", viewModel.SearchStatusSummaryText);
        Assert.Equal("Search idle", viewModel.SearchResultText);
        Assert.False(viewModel.ShowGhosttyDiagnostics);
        Assert.Equal("Native Diagnostics", viewModel.GhosttyDiagnosticsButtonText);
        Assert.Contains("unavailable", viewModel.GhosttyDiagnosticsText, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void MainWindow_DiagnosticsPanel_ExposesSelectableDiagnosticsAndEventLog()
    {
        MainWindow window = new()
        {
            Width = 900,
            Height = 520,
        };

        try
        {
            MainWindowViewModel viewModel = window.ViewModel
                ?? throw new InvalidOperationException("MainWindow view model was not initialized.");
            TextBox diagnosticsTextBox = window.FindControl<TextBox>("GhosttyDiagnosticsTextBox")
                ?? throw new InvalidOperationException("GhosttyDiagnosticsTextBox was not found.");
            TextBox eventLogTextBox = window.FindControl<TextBox>("EventLogTextBox")
                ?? throw new InvalidOperationException("EventLogTextBox was not found.");
            Button clearEventLogButton = window.FindControl<Button>("DiagnosticsClearEventLogButton")
                ?? throw new InvalidOperationException("DiagnosticsClearEventLogButton was not found.");

            viewModel.SetGhosttyDiagnostics(show: true, text: "SIMD: yes");
            viewModel.EventLogEnabled = true;
            viewModel.AppendEventLogEntry("Connected.");

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));

            Assert.True(diagnosticsTextBox.IsReadOnly);
            Assert.True(diagnosticsTextBox.AcceptsReturn);
            Assert.Contains("diagnosticsTextBox", diagnosticsTextBox.Classes);
            Assert.Equal("SIMD: yes", diagnosticsTextBox.Text);
            Assert.True(eventLogTextBox.IsReadOnly);
            Assert.True(eventLogTextBox.AcceptsReturn);
            Assert.Contains("diagnosticsTextBox", eventLogTextBox.Classes);
            Assert.Contains("Connected.", eventLogTextBox.Text, StringComparison.Ordinal);
            Assert.Same(viewModel.ClearEventLogCommand, clearEventLogButton.Command);
            Assert.True(clearEventLogButton.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Showcase_Commands_RouteThroughInteractions()
    {
        MainWindowViewModel viewModel = new();
        List<string> calls = [];
        List<bool> diagnosticsToggles = [];
        List<TerminalSnapshotExportFormat> snapshotFormats = [];

        using IDisposable applySearch = viewModel.ApplySearchInteraction.RegisterHandler(context =>
        {
            calls.Add($"search:{context.Input}");
            context.SetOutput(Unit.Default);
        });
        using IDisposable nextSearch = viewModel.NextSearchInteraction.RegisterHandler(context =>
        {
            calls.Add("next");
            context.SetOutput(Unit.Default);
        });
        using IDisposable previousSearch = viewModel.PreviousSearchInteraction.RegisterHandler(context =>
        {
            calls.Add("previous");
            context.SetOutput(Unit.Default);
        });
        using IDisposable clearSearch = viewModel.ClearSearchInteraction.RegisterHandler(context =>
        {
            calls.Add("clear");
            context.SetOutput(Unit.Default);
        });
        using IDisposable restartSession = viewModel.RestartActiveSessionInteraction.RegisterHandler(context =>
        {
            calls.Add("restart");
            context.SetOutput(Unit.Default);
        });
        using IDisposable clearHistory = viewModel.ClearActiveScrollbackInteraction.RegisterHandler(context =>
        {
            calls.Add("clear-history");
            context.SetOutput(Unit.Default);
        });
        using IDisposable hyperlinkSample = viewModel.ShowHyperlinkSampleInteraction.RegisterHandler(context =>
        {
            calls.Add("hyperlink");
            context.SetOutput(Unit.Default);
        });
        using IDisposable kittySample = viewModel.ShowKittyGraphicsSampleInteraction.RegisterHandler(context =>
        {
            calls.Add("kitty");
            context.SetOutput(Unit.Default);
        });
        using IDisposable diagnostics = viewModel.ToggleGhosttyDiagnosticsInteraction.RegisterHandler(context =>
        {
            diagnosticsToggles.Add(context.Input);
            viewModel.SetGhosttyDiagnostics(context.Input, context.Input ? "open" : "closed");
            context.SetOutput(Unit.Default);
        });
        using IDisposable copySnapshot = viewModel.CopySnapshotInteraction.RegisterHandler(context =>
        {
            snapshotFormats.Add(context.Input);
            calls.Add($"snapshot:{context.Input}");
            context.SetOutput(Unit.Default);
        });

        viewModel.SearchQuery = "demo";
        viewModel.ApplySearchCommand.Execute().Wait();
        viewModel.SetSearchState("demo", total: 1, selected: 0, usesNativeScrollback: false);
        viewModel.NextSearchCommand.Execute().Wait();
        viewModel.PreviousSearchCommand.Execute().Wait();
        viewModel.ClearSearchCommand.Execute().Wait();
        viewModel.RestartActiveSessionCommand.Execute().Wait();
        viewModel.ClearActiveScrollbackCommand.Execute().Wait();
        viewModel.ShowHyperlinkSampleCommand.Execute().Wait();
        viewModel.ShowKittyGraphicsSampleCommand.Execute().Wait();
        viewModel.CopyPlainSnapshotCommand.Execute().Wait();
        viewModel.CopyStyledVtSnapshotCommand.Execute().Wait();
        viewModel.CopyHtmlSnapshotCommand.Execute().Wait();
        viewModel.ToggleGhosttyDiagnosticsCommand.Execute().Wait();
        viewModel.ToggleGhosttyDiagnosticsCommand.Execute().Wait();

        Assert.Equal(
            [
                "search:demo",
                "next",
                "previous",
                "clear",
                "restart",
                "clear-history",
                "hyperlink",
                "kitty",
                $"snapshot:{TerminalSnapshotExportFormat.PlainText}",
                $"snapshot:{TerminalSnapshotExportFormat.StyledVt}",
                $"snapshot:{TerminalSnapshotExportFormat.Html}",
            ],
            calls);
        Assert.Equal([true, false], diagnosticsToggles);
        Assert.Equal(
            [
                TerminalSnapshotExportFormat.PlainText,
                TerminalSnapshotExportFormat.StyledVt,
                TerminalSnapshotExportFormat.Html,
            ],
            snapshotFormats);
    }

    [Fact]
    public void SessionHistoryCommands_LeaveStatusToInteractionHandlers()
    {
        MainWindowViewModel viewModel = new();

        using IDisposable restartSession = viewModel.RestartActiveSessionInteraction.RegisterHandler(context =>
        {
            viewModel.SetStatus("restart handler status");
            context.SetOutput(Unit.Default);
        });
        using IDisposable clearHistory = viewModel.ClearActiveScrollbackInteraction.RegisterHandler(context =>
        {
            viewModel.SetStatus("clear handler status");
            context.SetOutput(Unit.Default);
        });

        viewModel.RestartActiveSessionCommand.Execute().Wait();
        Assert.Equal("restart handler status", viewModel.StatusText);

        viewModel.ClearActiveScrollbackCommand.Execute().Wait();
        Assert.Equal("clear handler status", viewModel.StatusText);
    }

    private static TransportModeOption FindTransportMode(MainWindowViewModel viewModel, string transportId)
    {
        for (int i = 0; i < viewModel.TransportModes.Count; i++)
        {
            if (string.Equals(viewModel.TransportModes[i].Id, transportId, StringComparison.Ordinal))
            {
                return viewModel.TransportModes[i];
            }
        }

        throw new InvalidOperationException($"Transport mode '{transportId}' was not found.");
    }

    private static SshAuthModeOption FindSshAuthMode(MainWindowViewModel viewModel, string authModeId)
    {
        for (int i = 0; i < viewModel.SshAuthModes.Count; i++)
        {
            if (string.Equals(viewModel.SshAuthModes[i].Id, authModeId, StringComparison.Ordinal))
            {
                return viewModel.SshAuthModes[i];
            }
        }

        throw new InvalidOperationException($"SSH auth mode '{authModeId}' was not found.");
    }

    private static SettingsCategoryOption FindSettingsCategory(MainWindowViewModel viewModel, string categoryId)
    {
        for (int i = 0; i < viewModel.SettingsCategories.Count; i++)
        {
            if (string.Equals(viewModel.SettingsCategories[i].Id, categoryId, StringComparison.Ordinal))
            {
                return viewModel.SettingsCategories[i];
            }
        }

        throw new InvalidOperationException($"Settings category '{categoryId}' was not found.");
    }

    private static NativeMenuItem FindNativeMenuItem(NativeMenu menu, string header)
    {
        foreach (NativeMenuItemBase itemBase in menu.Items)
        {
            if (itemBase is not NativeMenuItem item)
            {
                continue;
            }

            if (string.Equals(Convert.ToString(item.Header, CultureInfo.InvariantCulture), header, StringComparison.Ordinal))
            {
                return item;
            }

            if (item.Menu is { } submenu)
            {
                try
                {
                    return FindNativeMenuItem(submenu, header);
                }
                catch (InvalidOperationException)
                {
                    // Continue searching sibling menus.
                }
            }
        }

        throw new InvalidOperationException($"Native menu item '{header}' was not found.");
    }

    private static void AssertNativeMenuCommand(NativeMenu menu, object command, string header)
    {
        Assert.Same(command, FindNativeMenuItem(menu, header).Command);
    }

    private static void AssertCanExecuteNativeMenuCommand(NativeMenu menu, string header)
    {
        NativeMenuItem item = FindNativeMenuItem(menu, header);
        Assert.NotNull(item.Command);
        AssertCanExecuteNativeMenuCommand(item.Command);
    }

    private static void AssertCannotExecuteNativeMenuCommand(NativeMenu menu, string header)
    {
        NativeMenuItem item = FindNativeMenuItem(menu, header);
        Assert.NotNull(item.Command);
        AssertCannotExecuteNativeMenuCommand(item.Command);
    }

    private static void AssertCanExecuteNativeMenuCommand(object command)
    {
        Assert.True(
            command is System.Windows.Input.ICommand menuCommand && menuCommand.CanExecute(null),
            "Expected native menu command to be executable.");
    }

    private static void AssertCannotExecuteNativeMenuCommand(object command)
    {
        Assert.True(
            command is System.Windows.Input.ICommand menuCommand && !menuCommand.CanExecute(null),
            "Expected native menu command to be disabled.");
    }

    private static void AssertNativeMenuLeafItemsAreCommandBacked(NativeMenu menu, string path = "")
    {
        foreach (NativeMenuItemBase itemBase in menu.Items)
        {
            if (itemBase is NativeMenuItemSeparator)
            {
                continue;
            }

            NativeMenuItem item = Assert.IsType<NativeMenuItem>(itemBase);
            string header = Convert.ToString(item.Header, CultureInfo.InvariantCulture) ?? "<null>";
            string itemPath = string.IsNullOrWhiteSpace(path)
                ? header
                : string.Concat(path, " > ", header);

            if (item.Menu is { } submenu)
            {
                AssertNativeMenuLeafItemsAreCommandBacked(submenu, itemPath);
                continue;
            }

            Assert.NotNull(item.Command);
        }
    }

    private static bool ContainsNativeMenuItem(NativeMenu menu, string header)
    {
        foreach (NativeMenuItemBase itemBase in menu.Items)
        {
            if (itemBase is not NativeMenuItem item)
            {
                continue;
            }

            if (string.Equals(Convert.ToString(item.Header, CultureInfo.InvariantCulture), header, StringComparison.Ordinal))
            {
                return true;
            }

            if (item.Menu is { } submenu && ContainsNativeMenuItem(submenu, header))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsNativeMenuCommand(NativeMenu menu, object command)
    {
        foreach (NativeMenuItemBase itemBase in menu.Items)
        {
            if (itemBase is not NativeMenuItem item)
            {
                continue;
            }

            if (ReferenceEquals(item.Command, command))
            {
                return true;
            }

            if (item.Menu is { } submenu && ContainsNativeMenuCommand(submenu, command))
            {
                return true;
            }
        }

        return false;
    }

    private static SshHostKeyTrustPromptRequest CreateSshHostKeyPromptRequest()
    {
        return new SshHostKeyTrustPromptRequest(
            Host: "example.com",
            Port: 22,
            Username: "alice",
            HostKeyAlgorithm: "ssh-ed25519",
            FingerprintSha256: "SHA256:abc",
            FingerprintMd5: "MD5:00:11",
            KeyLengthBits: 256,
            HostKeyBase64: Convert.ToBase64String([1, 2, 3, 4]),
            WillPersistTrust: true,
            KnownHostsFilePath: "/tmp/known_hosts");
    }

    private static Window CreateWindow(object dataContext)
    {
        Window window = new()
        {
            Width = 800,
            Height = 600,
            DataContext = dataContext,
            Content = new Border(),
        };

        window.Show();
        window.Focus();
        return window;
    }
}
