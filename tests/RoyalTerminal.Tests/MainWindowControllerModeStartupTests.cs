// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — startup/fallback smoke coverage for shared shell controller mode routing.

using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.App.Services;
using RoyalTerminal.Avalonia.App.ViewModels;
using RoyalTerminal.Avalonia.App.Views;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using ReactiveUI;
using ReactiveUI.Reactive;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("MainWindowControllerHeadlessTests")]
public sealed class MainWindowControllerModeStartupTests
{
    private const string StartAllRenderModesEnvVar = "ROYALTERMINAL_DEMO_START_ALL_RENDER_MODES";

    [Fact]
    public void Controller_BuildPipeCommandSpec_UsesPowerShellArgumentsFromShellPath()
    {
        TerminalCommandSpec command = MainWindowController.BuildPipeCommandSpec(
            "Write-Output ok",
            "/usr/local/bin/pwsh");

        Assert.Equal("/usr/local/bin/pwsh", command.FileName);
        Assert.Equal(
            ["-NoLogo", "-NoProfile", "-Command", "Write-Output ok"],
            command.Arguments);
    }

    [Fact]
    public void Controller_BuildPipeCommandSpec_UsesCmdArgumentsFromShellPath()
    {
        TerminalCommandSpec command = MainWindowController.BuildPipeCommandSpec(
            "echo ok",
            @"C:\Windows\System32\cmd.exe");

        Assert.Equal(@"C:\Windows\System32\cmd.exe", command.FileName);
        Assert.Equal(["/c", "echo ok"], command.Arguments);
    }

    [Fact]
    public void Controller_BuildPipeCommandSpec_UsesPosixLoginCommandForNonPowerShellProfiles()
    {
        TerminalCommandSpec command = MainWindowController.BuildPipeCommandSpec(
            "echo ok",
            @"C:\Program Files\Git\bin\bash.exe");

        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", command.FileName);
        Assert.Equal(["-lc", "echo ok"], command.Arguments);
    }

    [Fact]
    public async Task ShellSshCredentialProvider_ResolvesCredentialsFromRequestedSecretIds()
    {
        InMemorySshSecretStore store = new();
        await store.SaveSecretAsync("profiles/first/password", "first-password");
        await store.SaveSecretAsync("profiles/second/password", "second-password");
        await store.SaveSecretAsync("profiles/second/key", "/keys/second");
        MainWindowController.ShellSshCredentialProvider provider = new(store);

        SshResolvedCredentials credentials = await provider.ResolveAsync(
            new SshCredentialRequest(
                new SshEndpointOptions("example.com", 22, "demo"),
                new SshAuthenticationOptions(
                    UsePassword: true,
                    PasswordSecretId: "profiles/second/password",
                    PrivateKeySecretIds: ["profiles/second/key"],
                    UseAgent: false)));

        Assert.Equal("second-password", credentials.Password);
        Assert.Equal(["/keys/second"], credentials.PrivateKeyPemOrPath);
        Assert.False(credentials.UseAgent);
    }

    [AvaloniaFact]
    public async Task Controller_Startup_CreatesSingleRenderedTabByDefault()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup-default";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: CreateEmptyProfileStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool createdSingleTab = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(createdSingleTab);

            ItemsControl tabStrip = window.FindControl<ItemsControl>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            Button startupHeader = GetTabHeader(tabStrip, 0);
            TerminalRenderMode startupMode = ResolveModeFromContainer(
                terminalHost.Children[0],
                startupHeader);
            Assert.Equal(TerminalRenderMode.RenderedAuto, startupMode);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_Startup_UsesStoredDefaultProfileAppearance()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        TerminalSessionProfilesDocument document = new()
        {
            DefaultProfileId = "default",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "default",
                    DisplayName = "Default Session",
                    Transport = CreatePipeTransportProfile("echo default-profile"),
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        FontSource = TerminalFontSource.System,
                        FontFamilyName = "Cascadia Code",
                        FontSize = 13,
                    },
                    Behavior = new TerminalSessionBehaviorSettings
                    {
                        EnableTextShaping = true,
                        EnableLigatures = true,
                    },
                },
            ],
        };
        MainWindowViewModel viewModel = new();

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            settingsProfileStore: new InMemoryProfileStore(document),
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool createdSingleTab = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(createdSingleTab);

            TerminalControl control = Assert.Single(GetStandaloneControls(terminalHost));
            Assert.Equal("Cascadia Code", control.FontFamilyName);
            Assert.Equal(TerminalFontSource.System, control.FontSource);
            Assert.Equal(13, control.TerminalFontSize);
            Assert.NotNull(control.Renderer);
            Assert.True(control.Renderer!.EnableLigatures);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_TabHeader_CloseButton_UsesCenteredIcon()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo tab-close-alignment";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: CreateEmptyProfileStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool createdSingleTab = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(createdSingleTab);

            ItemsControl tabStrip = window.FindControl<ItemsControl>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            Button startupHeader = GetTabHeader(tabStrip, 0);
            Button closeButton = Assert.IsType<Button>(startupHeader.Tag);
            PathIcon closeIcon = Assert.IsType<PathIcon>(closeButton.Content);

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            Assert.Contains("tabCloseButton", closeButton.Classes);
            Assert.Contains("tabCloseIcon", closeIcon.Classes);
            Assert.Equal(new Thickness(0), closeButton.Padding);
            Assert.Equal(24d, closeButton.Width);
            Assert.Equal(24d, closeButton.Height);
            Assert.Equal(HorizontalAlignment.Center, closeButton.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, closeButton.VerticalContentAlignment);
            Assert.Equal(HorizontalAlignment.Center, closeIcon.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, closeIcon.VerticalAlignment);
            Assert.Equal(12d, closeIcon.Width);
            Assert.Equal(12d, closeIcon.Height);
            Assert.NotNull(closeIcon.Data);

            Point iconCenter = closeIcon.TranslatePoint(
                    new Point(closeIcon.Bounds.Width / 2d, closeIcon.Bounds.Height / 2d),
                    closeButton)
                ?? throw new InvalidOperationException("Close icon was not attached to the close button visual tree.");

            Assert.InRange(Math.Abs(iconCenter.X - closeButton.Bounds.Width / 2d), 0d, 0.75d);
            Assert.InRange(Math.Abs(iconCenter.Y - closeButton.Bounds.Height / 2d), 0d, 0.75d);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_TabsInTitleBar_MovesTabStripHidesLogoAndPersistsWorkspace()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        InMemoryWorkspaceStore workspaceStore = new();
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo titlebar-tabs";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool createdSingleTab = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(createdSingleTab);

            ContentControl titleBarTabStripHost = window.FindControl<ContentControl>("TitleBarTabStripHost")
                ?? throw new InvalidOperationException("TitleBarTabStripHost was not found.");
            ContentControl bodyTabStripHost = window.FindControl<ContentControl>("BodyTabStripHost")
                ?? throw new InvalidOperationException("BodyTabStripHost was not found.");
            Border tabStripSurface = window.FindControl<Border>("TabStripSurface")
                ?? throw new InvalidOperationException("TabStripSurface was not found.");
            Grid tabStripLayout = window.FindControl<Grid>("TabStripLayout")
                ?? throw new InvalidOperationException("TabStripLayout was not found.");
            ScrollViewer tabStripScrollViewer = window.FindControl<ScrollViewer>("TabStripScrollViewer")
                ?? throw new InvalidOperationException("TabStripScrollViewer was not found.");
            ItemsControl tabStrip = window.FindControl<ItemsControl>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            RepeatButton tabStripScrollLeftButton = window.FindControl<RepeatButton>("TabStripScrollLeftButton")
                ?? throw new InvalidOperationException("TabStripScrollLeftButton was not found.");
            RepeatButton tabStripScrollRightButton = window.FindControl<RepeatButton>("TabStripScrollRightButton")
                ?? throw new InvalidOperationException("TabStripScrollRightButton was not found.");
            Button tabStripNewTabButton = window.FindControl<Button>("TabStripNewTabButton")
                ?? throw new InvalidOperationException("TabStripNewTabButton was not found.");
            Border titleBarBrandIcon = window.FindControl<Border>("TitleBarBrandIcon")
                ?? throw new InvalidOperationException("TitleBarBrandIcon was not found.");
            ScrollContentPresenter tabStripScrollContentPresenter =
                FindTabStripScrollContentPresenter(tabStripScrollViewer);

            Button tabHeader = GetTabHeader(tabStrip, 0);
            Button closeButton = Assert.IsType<Button>(tabHeader.Tag);

            Assert.False(viewModel.IsTabsInTitleBar);
            Assert.Same(tabStripSurface, bodyTabStripHost.Content);
            Assert.Null(titleBarTabStripHost.Content);
            Assert.True(bodyTabStripHost.IsVisible);
            Assert.False(titleBarTabStripHost.IsVisible);
            Assert.Equal(viewModel.IsTitleBarLogoVisible, titleBarBrandIcon.IsVisible);
            Assert.Contains("bodyTabs", tabStripSurface.Classes);
            Assert.DoesNotContain("titleBarTabs", tabStripSurface.Classes);
            Assert.Equal(WindowDecorationsElementRole.None, WindowDecorationProperties.GetElementRole(tabStripSurface));
            Assert.Equal(WindowDecorationsElementRole.None, WindowDecorationProperties.GetElementRole(tabStripLayout));
            Assert.Equal(WindowDecorationsElementRole.None, WindowDecorationProperties.GetElementRole(tabStripScrollViewer));
            Assert.Equal(WindowDecorationsElementRole.None, WindowDecorationProperties.GetElementRole(tabStripScrollContentPresenter));

            await viewModel.ToggleTabsInTitleBarCommand.Execute();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            tabStripScrollContentPresenter = FindTabStripScrollContentPresenter(tabStripScrollViewer);

            Assert.True(viewModel.IsTabsInTitleBar);
            Assert.Same(tabStripSurface, titleBarTabStripHost.Content);
            Assert.Null(bodyTabStripHost.Content);
            Assert.True(titleBarTabStripHost.IsVisible);
            Assert.False(bodyTabStripHost.IsVisible);
            Assert.False(titleBarBrandIcon.IsVisible);
            Assert.Contains("titleBarTabs", tabStripSurface.Classes);
            Assert.DoesNotContain("bodyTabs", tabStripSurface.Classes);
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(tabStripSurface));
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(tabStripLayout));
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(tabStripScrollViewer));
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(tabStripScrollContentPresenter));
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(tabStrip));
            Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(tabHeader));
            Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(closeButton));
            Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(tabStripScrollLeftButton));
            Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(tabStripScrollRightButton));
            Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(tabStripNewTabButton));
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceWindow savedWindow = Assert.Single(workspaceStore.Document.Windows);
        Assert.True(savedWindow.TabsInTitleBar);
    }

    [AvaloniaFact]
    public async Task Controller_TabStripScrollButtons_ScrollOverflowingTabs()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo tab-scroll";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        window.Width = 460;
        window.Height = 320;
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: CreateEmptyProfileStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool createdSingleTab = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(createdSingleTab);

            ItemsControl tabStrip = window.FindControl<ItemsControl>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            ScrollViewer tabStripScrollViewer = window.FindControl<ScrollViewer>("TabStripScrollViewer")
                ?? throw new InvalidOperationException("TabStripScrollViewer was not found.");
            RepeatButton tabStripScrollLeftButton = window.FindControl<RepeatButton>("TabStripScrollLeftButton")
                ?? throw new InvalidOperationException("TabStripScrollLeftButton was not found.");
            RepeatButton tabStripScrollRightButton = window.FindControl<RepeatButton>("TabStripScrollRightButton")
                ?? throw new InvalidOperationException("TabStripScrollRightButton was not found.");
            Border tabStripSurface = window.FindControl<Border>("TabStripSurface")
                ?? throw new InvalidOperationException("TabStripSurface was not found.");
            tabStripScrollViewer.Width = 260;
            tabStrip.Width = 920;

            for (int i = 0; i < 9; i++)
            {
                await viewModel.NewTabCommand.Execute().ToTask();
            }

            bool tabsCreated = await WaitUntilAsync(
                () => GetTabHeaders(tabStrip).Count >= 10,
                TimeSpan.FromSeconds(2));
            Assert.True(tabsCreated);

            bool overflowReady = await WaitUntilAsync(
                () =>
                {
                    window.Measure(new Size(window.Width, window.Height));
                    window.Arrange(new Rect(0, 0, window.Width, window.Height));
                    Dispatcher.UIThread.RunJobs();
                    return tabStripScrollViewer.Extent.Width > tabStripScrollViewer.Viewport.Width &&
                        tabStripScrollRightButton.IsVisible;
                },
                TimeSpan.FromSeconds(2));
            Assert.True(
                overflowReady,
                $"Expected tab strip overflow. Extent={tabStripScrollViewer.Extent}, Viewport={tabStripScrollViewer.Viewport}, " +
                $"RightVisible={tabStripScrollRightButton.IsVisible}, ChildBounds={tabStrip.Bounds}, ViewerBounds={tabStripScrollViewer.Bounds}.");

            tabStripScrollViewer.Offset = new Vector(0, 0);
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            Dispatcher.UIThread.RunJobs();

            Assert.True(tabStripScrollLeftButton.IsVisible);
            Assert.True(tabStripScrollRightButton.IsVisible);
            Assert.False(tabStripScrollLeftButton.IsEnabled);
            Assert.True(tabStripScrollRightButton.IsEnabled);

            tabStripScrollRightButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bool scrolledRight = await WaitUntilAsync(
                () => tabStripScrollViewer.Offset.X > 0.5 && tabStripScrollLeftButton.IsEnabled,
                TimeSpan.FromSeconds(2));
            Assert.True(scrolledRight);

            tabStripScrollViewer.Offset = new Vector(0, 0);
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            RaiseTabStripPointerWheel(tabStripSurface, window, new Vector(0, -1));
            bool wheelScrolledRight = await WaitUntilAsync(
                () => tabStripScrollViewer.Offset.X > 0.5 && tabStripScrollLeftButton.IsEnabled,
                TimeSpan.FromSeconds(2));
            Assert.True(wheelScrolledRight);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_AppPreferences_PersistsMovedNormalPlacementBeforeMaximize()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        AppWindowPlacement startupPlacement = new(
            X: 32,
            Y: 48,
            Width: 980,
            Height: 640,
            State: AppWindowState.Normal);
        InMemoryAppPreferencesStore appPreferencesStore = new(new AppPreferencesDocument
        {
            WindowPlacement = startupPlacement,
        });
        MainWindowViewModel viewModel = new();
        Window window = CreateControllerHostWindow(viewModel, out _);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: CreateEmptyProfileStore(),
            appPreferencesStore: appPreferencesStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            window.Position = new PixelPoint(220, 180);
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            window.WindowState = WindowState.Maximized;
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            AppWindowPlacement placement = appPreferencesStore.Document.WindowPlacement
                ?? throw new InvalidOperationException("Window placement was not persisted.");
            Assert.Equal(AppWindowState.Maximized, placement.State);
            Assert.Equal(220, placement.X);
            Assert.Equal(180, placement.Y);
            Assert.Equal(980, placement.Width);
            Assert.Equal(640, placement.Height);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_AppPreferences_SavesWindowPlacementWhenWindowIsClosing()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryAppPreferencesStore appPreferencesStore = new(new AppPreferencesDocument());
        MainWindowViewModel viewModel = new();
        Window window = CreateControllerHostWindow(viewModel, out _);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: CreateEmptyProfileStore(),
            appPreferencesStore: appPreferencesStore);
        IDisposable? lifetime = null;
        bool closed = false;

        try
        {
            lifetime = controller.Activate();
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            window.Position = new PixelPoint(260, 210);
            window.Width = 1120;
            window.Height = 720;
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            window.Close();
            closed = true;
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            AppWindowPlacement placement = appPreferencesStore.Document.WindowPlacement
                ?? throw new InvalidOperationException("Window placement was not persisted.");
            Assert.Equal(AppWindowState.Normal, placement.State);
            Assert.Equal(260, placement.X);
            Assert.Equal(210, placement.Y);
            Assert.Equal(1120, placement.Width);
            Assert.Equal(720, placement.Height);
        }
        finally
        {
            lifetime?.Dispose();
            if (!closed)
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public async Task Controller_TabStripItemDragBehavior_ReordersTabsAndPersistsWorkspaceOrder()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new();
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo tab-reorder";

        MainView mainView = new()
        {
            DataContext = viewModel,
        };
        Window window = new()
        {
            Width = 900,
            Height = 520,
            DataContext = viewModel,
            Content = mainView,
        };
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            visualRoot: mainView);
        IDisposable? lifetime = null;
        string[] reorderedTitles = [];

        try
        {
            window.Show();
            window.Focus();
            lifetime = controller.Activate();

            bool createdSingleTab = await WaitUntilAsync(
                () => GetStandaloneControls(mainView.FindControl<Grid>("TerminalHost")!).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(createdSingleTab);

            await viewModel.NewTabCommand.Execute().ToTask();
            await viewModel.NewTabCommand.Execute().ToTask();

            ItemsControl tabStrip = mainView.FindControl<ItemsControl>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            bool tabsCreated = await WaitUntilAsync(
                () => GetTabHeaders(tabStrip).Count == 3,
                TimeSpan.FromSeconds(2));
            Assert.True(tabsCreated);

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            string[] originalTitles = GetTabHeaderTitles(tabStrip);
            Assert.Equal(3, originalTitles.Length);
            reorderedTitles = [originalTitles[1], originalTitles[2], originalTitles[0]];

            IReadOnlyList<ContentPresenter> presenters = GetTabItemPresenters(tabStrip);
            Point start = GetCenterPointInWindow(presenters[0], window);
            Point end = GetCenterPointInWindow(presenters[2], window);
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point((start.X + end.X) / 2d, start.Y), RawInputModifiers.LeftMouseButton);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);

            bool reordered = await WaitUntilAsync(
                () => GetTabHeaderTitles(tabStrip).SequenceEqual(reorderedTitles),
                TimeSpan.FromSeconds(2));
            Assert.True(reordered);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceWindow savedWindow = Assert.Single(workspaceStore.Document.Windows);
        Assert.Equal(reorderedTitles, savedWindow.Tabs.Select(static tab => tab.Title ?? string.Empty).ToArray());
    }

    [AvaloniaFact]
    public async Task Controller_TabStripItemDragBehavior_ClickAndCloseDoNotReorderTabs()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo tab-click-close";

        MainView mainView = new()
        {
            DataContext = viewModel,
        };
        Window window = new()
        {
            Width = 900,
            Height = 520,
            DataContext = viewModel,
            Content = mainView,
        };
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            visualRoot: mainView);
        IDisposable? lifetime = null;

        try
        {
            window.Show();
            window.Focus();
            lifetime = controller.Activate();

            bool createdSingleTab = await WaitUntilAsync(
                () => GetStandaloneControls(mainView.FindControl<Grid>("TerminalHost")!).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(createdSingleTab);

            await viewModel.NewTabCommand.Execute().ToTask();
            await viewModel.NewTabCommand.Execute().ToTask();

            ItemsControl tabStrip = mainView.FindControl<ItemsControl>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            bool tabsCreated = await WaitUntilAsync(
                () => GetTabHeaders(tabStrip).Count == 3,
                TimeSpan.FromSeconds(2));
            Assert.True(tabsCreated);

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            string[] originalTitles = GetTabHeaderTitles(tabStrip);
            Button secondHeader = GetTabHeader(tabStrip, 1);
            Point secondHeaderCenter = GetCenterPointInWindow(secondHeader, window);
            window.MouseDown(secondHeaderCenter, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(secondHeaderCenter, MouseButton.Left, RawInputModifiers.None);

            bool secondTabActivated = await WaitUntilAsync(
                () => GetTabHeader(tabStrip, 1).Classes.Contains("active"),
                TimeSpan.FromSeconds(2));
            Assert.True(secondTabActivated);
            Assert.Equal(originalTitles, GetTabHeaderTitles(tabStrip));

            Button closeButton = Assert.IsType<Button>(GetTabHeader(tabStrip, 1).Tag);
            Point closeButtonCenter = GetCenterPointInWindow(closeButton, window);
            window.MouseDown(closeButtonCenter, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(closeButtonCenter, MouseButton.Left, RawInputModifiers.None);

            string[] remainingTitles = [originalTitles[0], originalTitles[2]];
            bool secondTabClosed = await WaitUntilAsync(
                () => GetTabHeaderTitles(tabStrip).SequenceEqual(remainingTitles),
                TimeSpan.FromSeconds(2));
            Assert.True(secondTabClosed);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_Startup_RestoresWorkspaceTabs_AndShutdownSavesWorkspace()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        InMemoryWorkspaceStore workspaceStore = new(new TerminalWorkspaceDocument
        {
            SelectedWindowId = "main",
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = "main",
                    SelectedTabId = "tab-two",
                    WidthPixels = 1366,
                    HeightPixels = 777,
                    IsMaximized = true,
                    TabsInTitleBar = true,
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = "tab-one",
                            ProfileId = "default",
                            Title = "One",
                            TransportId = TerminalTransportIds.Pipe,
                            RenderMode = TerminalWorkspaceRenderModes.Skia,
                        },
                        new TerminalWorkspaceTab
                        {
                            Id = "tab-two",
                            ProfileId = "default",
                            Title = "Two",
                            TransportId = TerminalTransportIds.Pipe,
                            RenderMode = TerminalWorkspaceRenderModes.Text,
                        },
                    ],
                },
            ],
        });
        MainWindowViewModel viewModel = new();
        viewModel.PipeCommandText = "echo workspace-restore";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool restoredTabs = await WaitUntilAsync(
                () => terminalHost.Children.Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(restoredTabs);
            Assert.Equal(1366, window.Width);
            Assert.Equal(777, window.Height);
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.True(viewModel.IsTabsInTitleBar);

            Assert.Single(GetStandaloneControls(terminalHost));

            ItemsControl tabStrip = window.FindControl<ItemsControl>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            Assert.Equal(2, GetTabHeaders(tabStrip).Count);

            Button firstTab = GetTabHeader(tabStrip, 0);
            firstTab.Command!.Execute(firstTab.CommandParameter);
            bool materializedInactiveTab = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(materializedInactiveTab);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        Assert.True(workspaceStore.SaveCount > 0);
        TerminalWorkspaceWindow savedWindow = Assert.Single(workspaceStore.Document.Windows);
        Assert.Equal(2, savedWindow.Tabs.Count);
        Assert.Equal("main", workspaceStore.Document.SelectedWindowId);
        Assert.False(string.IsNullOrWhiteSpace(savedWindow.SelectedTabId));
        Assert.True(savedWindow.IsMaximized);
        Assert.True(savedWindow.TabsInTitleBar);
    }

    [AvaloniaFact]
    public async Task Controller_WorkspaceRestore_AppliesProfileScrollbackLimit()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new(new TerminalWorkspaceDocument
        {
            SelectedWindowId = "main",
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = "main",
                    SelectedTabId = "tab-layout",
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = "tab-layout",
                            ProfileId = "profile-layout",
                            Title = "Layout",
                            TransportId = TerminalTransportIds.Pipe,
                            RenderMode = TerminalWorkspaceRenderModes.Skia,
                            RootPane = new TerminalWorkspacePane
                            {
                                Id = "pane-layout",
                                ProfileId = "profile-layout",
                                TransportId = TerminalTransportIds.Pipe,
                            },
                        },
                    ],
                },
            ],
        });
        InMemoryProfileStore profileStore = new(new TerminalSessionProfilesDocument
        {
            DefaultProfileId = "profile-layout",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "profile-layout",
                    DisplayName = "Profile Layout",
                    Transport = CreatePipeTransportProfile("restored-layout"),
                    Layout = new TerminalSessionLayoutSettings
                    {
                        ScrollbackLimit = 1_234,
                    },
                },
            ],
        });
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo restored-layout";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            settingsProfileStore: profileStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool restoredPane = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(restoredPane);

            TerminalControl control = Assert.Single(GetStandaloneControls(terminalHost));
            Assert.Equal(1_234, control.ScrollbackLimit);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_WorkspaceRestore_MissingProfileUsesDefaultAppearance()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new(new TerminalWorkspaceDocument
        {
            SelectedWindowId = "main",
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = "main",
                    SelectedTabId = "tab-pwsh",
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = "tab-pwsh",
                            ProfileId = "pwsh",
                            Title = "Terminal 1",
                            TransportId = TerminalTransportIds.Pty,
                            RenderMode = TerminalWorkspaceRenderModes.Skia,
                            RootPane = new TerminalWorkspacePane
                            {
                                Id = "pane-pwsh",
                                ProfileId = "pwsh",
                                TransportId = TerminalTransportIds.Pty,
                            },
                        },
                    ],
                },
            ],
        });
        InMemoryProfileStore profileStore = new(new TerminalSessionProfilesDocument
        {
            DefaultProfileId = "default",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "default",
                    DisplayName = "Default Session",
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        FontSource = TerminalFontSource.System,
                        FontFamilyName = "Cascadia Code",
                        FontSize = 13,
                    },
                    Behavior = new TerminalSessionBehaviorSettings
                    {
                        EnableLigatures = true,
                    },
                },
            ],
        });
        MainWindowViewModel viewModel = new();

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            settingsProfileStore: profileStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool restoredPane = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(restoredPane);

            TerminalControl control = Assert.Single(GetStandaloneControls(terminalHost));
            Assert.Equal("Cascadia Code", control.FontFamilyName);
            Assert.Equal(13, control.TerminalFontSize);
            Assert.True(control.Renderer?.EnableLigatures);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_Shutdown_AssignsUniqueIdsAfterRestoringNumberedTabIds()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new(new TerminalWorkspaceDocument
        {
            SelectedWindowId = "main",
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = "main",
                    SelectedTabId = "tab-2",
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = "tab-2",
                            ProfileId = "default",
                            Title = "Restored",
                            TransportId = TerminalTransportIds.Pipe,
                            RenderMode = TerminalWorkspaceRenderModes.Skia,
                        },
                    ],
                },
            ],
        });
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo unique-tab-id";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            commandHistoryStore: new InMemoryCommandHistoryStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool restoredTab = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(restoredTab);

            await viewModel.NewTabCommand.Execute().ToTask();
            bool newTabCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(newTabCreated);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        IReadOnlyList<TerminalWorkspaceTab> savedTabs = workspaceStore.Document.Windows[0].Tabs;
        Assert.Equal(2, savedTabs.Count);
        Assert.Equal(2, savedTabs.Select(tab => tab.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(savedTabs, tab => string.Equals(tab.Id, "tab-2", StringComparison.Ordinal));
        Assert.Contains(savedTabs, tab => string.Equals(tab.Id, "tab-3", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Controller_Startup_RestoresSplitPaneWorkspace_AndShutdownPreservesPaneTree()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        TerminalWorkspacePane rootPane = new()
        {
            Id = "root",
            Split = new TerminalWorkspacePaneSplit
            {
                Orientation = TerminalWorkspacePaneSplitOrientations.Horizontal,
                Ratio = 0.42,
                FirstPane = new TerminalWorkspacePane
                {
                    Id = "left",
                    ProfileId = "default",
                    TransportId = TerminalTransportIds.Pipe,
                    WorkingDirectory = "/tmp/left",
                },
                SecondPane = new TerminalWorkspacePane
                {
                    Id = "right",
                    ProfileId = "default",
                    TransportId = TerminalTransportIds.Pipe,
                    WorkingDirectory = "/tmp/right",
                },
            },
        };
        InMemoryWorkspaceStore workspaceStore = new(new TerminalWorkspaceDocument
        {
            SelectedWindowId = "main",
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = "main",
                    SelectedTabId = "tab-split",
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = "tab-split",
                            ProfileId = "default",
                            Title = "Split",
                            TransportId = TerminalTransportIds.Pipe,
                            RenderMode = TerminalWorkspaceRenderModes.Skia,
                            RootPane = rootPane,
                        },
                    ],
                },
            ],
        });
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo split-restore";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            commandHistoryStore: new InMemoryCommandHistoryStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool restoredSplit = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1 && terminalHost.Children[0] is Grid { Children.Count: 3 },
                TimeSpan.FromSeconds(2));
            Assert.True(restoredSplit);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceTab savedTab = Assert.Single(workspaceStore.Document.Windows[0].Tabs);
        Assert.NotNull(savedTab.RootPane.Split);
        Assert.Equal(TerminalWorkspacePaneSplitOrientations.Horizontal, savedTab.RootPane.Split!.Orientation);
        Assert.Equal("left", savedTab.RootPane.Split.FirstPane.Id);
        Assert.Equal("right", savedTab.RootPane.Split.SecondPane.Id);
    }

    [AvaloniaFact]
    public async Task Controller_SplitPaneAfterRestore_AssignsUniquePaneIds()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new(new TerminalWorkspaceDocument
        {
            SelectedWindowId = "main",
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = "main",
                    SelectedTabId = "tab-split",
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = "tab-split",
                            ProfileId = "default",
                            Title = "Split",
                            TransportId = TerminalTransportIds.Pipe,
                            RenderMode = TerminalWorkspaceRenderModes.Skia,
                            RootPane = new TerminalWorkspacePane
                            {
                                Id = "pane-2",
                                ProfileId = "default",
                                TransportId = TerminalTransportIds.Pipe,
                            },
                        },
                    ],
                },
            ],
        });
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo unique-pane-id";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            commandHistoryStore: new InMemoryCommandHistoryStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool restoredPane = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1 &&
                      GetStandaloneControls(terminalHost).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(restoredPane);

            await viewModel.SplitPaneRightCommand.Execute().ToTask();
            bool splitCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1 &&
                      terminalHost.Children[0] is Grid { Children.Count: 3 } &&
                      GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(splitCreated);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceTab savedTab = Assert.Single(workspaceStore.Document.Windows[0].Tabs);
        List<string> paneIds = [];
        CollectPaneIds(savedTab.RootPane, paneIds);
        Assert.Equal(paneIds.Count, paneIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("pane-2", paneIds);
        Assert.Contains("pane-3", paneIds);
        Assert.Contains("pane-4", paneIds);
    }

    [AvaloniaFact]
    public async Task Controller_SplitPanePolicyOverride_DoesNotInheritSourceTransportProfileId()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new(new TerminalWorkspaceDocument
        {
            SelectedWindowId = "main",
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = "main",
                    SelectedTabId = "tab-transport-profile",
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = "tab-transport-profile",
                            ProfileId = "default",
                            Title = "Transport Profile",
                            TransportId = TerminalTransportIds.Pipe,
                            RenderMode = TerminalWorkspaceRenderModes.Skia,
                            RootPane = new TerminalWorkspacePane
                            {
                                Id = "pane-transport-profile",
                                ProfileId = "default",
                                TransportId = TerminalTransportIds.Pipe,
                                TransportProfileId = "source-transport-profile",
                            },
                        },
                    ],
                },
            ],
        });
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo restored-source";
        ITerminalPaneSplitPolicy splitPolicy = new DelegateTerminalPaneSplitPolicy(context =>
            TerminalPaneSplitDecision.Allow(context.DefaultLaunchProfile with
            {
                Id = "policy-profile",
                DisplayName = "Policy Profile",
            }));

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            commandHistoryStore: new InMemoryCommandHistoryStore(),
            paneSplitPolicy: splitPolicy);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool restoredPane = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1 &&
                      GetStandaloneControls(terminalHost).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(restoredPane);

            await viewModel.SplitPaneRightCommand.Execute().ToTask();
            bool splitCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1 &&
                      terminalHost.Children[0] is Grid { Children.Count: 3 } &&
                      GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(splitCreated);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceTab savedTab = Assert.Single(workspaceStore.Document.Windows[0].Tabs);
        TerminalWorkspacePaneSplit savedSplit = savedTab.RootPane.Split
            ?? throw new InvalidOperationException("Saved split pane was not found.");
        Assert.Equal("source-transport-profile", savedSplit.FirstPane.TransportProfileId);
        Assert.Equal("policy-profile", savedSplit.SecondPane.ProfileId);
        Assert.Null(savedSplit.SecondPane.TransportProfileId);
    }

    [AvaloniaFact]
    public async Task Controller_Shutdown_FlushesQueuedShellIntegrationBeforeSavingState()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new();
        InMemoryCommandHistoryStore commandHistoryStore = new();
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo shutdown-flush";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            commandHistoryStore: commandHistoryStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool tabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(tabCreated);

            TerminalControl control = Assert.Single(GetStandaloneControls(terminalHost));
            byte[] shellIntegrationOutput = Encoding.UTF8.GetBytes(
                "\u001b]7;file://localhost/tmp/shutdown-flush\u0007" +
                "\u001b]133;C;cmdline_url=echo%20queued\u0007" +
                "\u001b]133;D;0\u0007");
            await Task.Run(() => control.WriteOutput(shellIntegrationOutput));
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceTab savedTab = Assert.Single(workspaceStore.Document.Windows[0].Tabs);
        Assert.Equal("/tmp/shutdown-flush", savedTab.WorkingDirectory);
        Assert.Equal("/tmp/shutdown-flush", savedTab.RootPane.WorkingDirectory);

        TerminalCommandHistoryEntry savedEntry = Assert.Single(commandHistoryStore.Document.Entries);
        Assert.Equal("echo queued", savedEntry.CommandLine);
        Assert.Equal("/tmp/shutdown-flush", savedEntry.WorkingDirectory);
        Assert.Equal(0, savedEntry.ExitCode);
    }

    [AvaloniaFact]
    public async Task Controller_CommandSuggestions_UseFocusedSplitPaneProfileSnippets()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        TerminalSessionProfilesDocument profiles = new()
        {
            DefaultProfileId = "root-profile",
            Profiles =
            [
                CreateSnippetProfile("root-profile", "Root Profile", "root-only", "echo root-only"),
                CreateSnippetProfile("pane-profile", "Pane Profile", "pane-only", "echo pane-only"),
            ],
        };
        TerminalWorkspacePane rootPane = new()
        {
            Id = "root",
            Split = new TerminalWorkspacePaneSplit
            {
                Orientation = TerminalWorkspacePaneSplitOrientations.Horizontal,
                Ratio = 0.5,
                FirstPane = new TerminalWorkspacePane
                {
                    Id = "left",
                    ProfileId = "root-profile",
                    TransportId = TerminalTransportIds.Pipe,
                },
                SecondPane = new TerminalWorkspacePane
                {
                    Id = "right",
                    ProfileId = "pane-profile",
                    TransportId = TerminalTransportIds.Pipe,
                },
            },
        };
        InMemoryWorkspaceStore workspaceStore = new(new TerminalWorkspaceDocument
        {
            SelectedWindowId = "main",
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = "main",
                    SelectedTabId = "tab-split",
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = "tab-split",
                            ProfileId = "root-profile",
                            Title = "Split",
                            TransportId = TerminalTransportIds.Pipe,
                            RenderMode = TerminalWorkspaceRenderModes.Skia,
                            RootPane = rootPane,
                        },
                    ],
                },
            ],
        });
        InMemoryProfileStore profileStore = new(profiles);
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo snippets";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            settingsProfileStore: profileStore,
            commandHistoryStore: new InMemoryCommandHistoryStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool restoredSplit = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1 &&
                      terminalHost.Children[0] is Grid { Children.Count: 3 } &&
                      GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(restoredSplit);

            await viewModel.FocusPaneRightCommand.Execute();
            Dispatcher.UIThread.RunJobs();
            viewModel.CommandSuggestionQuery = "pane";
            await viewModel.OpenCommandHistoryOverlayCommand.Execute();

            bool snippetsLoaded = await WaitUntilAsync(
                () => viewModel.CommandSuggestions.Any(
                    suggestion => suggestion.CommandLine == "echo pane-only"),
                TimeSpan.FromSeconds(2));
            Assert.True(snippetsLoaded);
            Assert.DoesNotContain(
                viewModel.CommandSuggestions,
                suggestion => suggestion.CommandLine == "echo root-only");
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_Shutdown_PreservesDeferredSplitPaneWorkspace()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        TerminalWorkspacePane inactiveRootPane = new()
        {
            Id = "inactive-root",
            Split = new TerminalWorkspacePaneSplit
            {
                Orientation = TerminalWorkspacePaneSplitOrientations.Vertical,
                Ratio = 0.35,
                FirstPane = new TerminalWorkspacePane
                {
                    Id = "inactive-top",
                    ProfileId = "default",
                    TransportId = TerminalTransportIds.Pipe,
                },
                SecondPane = new TerminalWorkspacePane
                {
                    Id = "inactive-bottom",
                    ProfileId = "default",
                    TransportId = TerminalTransportIds.Pipe,
                },
            },
        };
        InMemoryWorkspaceStore workspaceStore = new(new TerminalWorkspaceDocument
        {
            SelectedWindowId = "main",
            Windows =
            [
                new TerminalWorkspaceWindow
                {
                    Id = "main",
                    SelectedTabId = "active-tab",
                    Tabs =
                    [
                        new TerminalWorkspaceTab
                        {
                            Id = "active-tab",
                            ProfileId = "default",
                            Title = "Active",
                            TransportId = TerminalTransportIds.Pipe,
                            RenderMode = TerminalWorkspaceRenderModes.Skia,
                        },
                        new TerminalWorkspaceTab
                        {
                            Id = "inactive-tab",
                            ProfileId = "default",
                            Title = "Inactive Split",
                            TransportId = TerminalTransportIds.Pipe,
                            RenderMode = TerminalWorkspaceRenderModes.Skia,
                            RootPane = inactiveRootPane,
                        },
                    ],
                },
            ],
        });
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo deferred-split";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            commandHistoryStore: new InMemoryCommandHistoryStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool restoredTabs = await WaitUntilAsync(
                () => terminalHost.Children.Count == 2 && GetStandaloneControls(terminalHost).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(restoredTabs);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceWindow savedWindow = Assert.Single(workspaceStore.Document.Windows);
        Assert.Equal("active-tab", savedWindow.SelectedTabId);
        TerminalWorkspaceTab inactiveTab = Assert.Single(
            savedWindow.Tabs,
            static tab => string.Equals(tab.Id, "inactive-tab", StringComparison.Ordinal));
        Assert.NotNull(inactiveTab.RootPane.Split);
        Assert.Equal(TerminalWorkspacePaneSplitOrientations.Vertical, inactiveTab.RootPane.Split!.Orientation);
        Assert.Equal(0.35, inactiveTab.RootPane.Split.Ratio, precision: 3);
        Assert.Equal("inactive-top", inactiveTab.RootPane.Split.FirstPane.Id);
        Assert.Equal("inactive-bottom", inactiveTab.RootPane.Split.SecondPane.Id);
    }

    [AvaloniaFact]
    public async Task Controller_SplitPaneCommands_CreatePanes_AndShutdownPreservesLiveRatio()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new();
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo split-command";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            commandHistoryStore: new InMemoryCommandHistoryStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabCreated);

            await viewModel.SplitPaneRightCommand.Execute().ToTask();
            bool splitCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1 &&
                      terminalHost.Children[0] is Grid { Children.Count: 3 } &&
                      GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(splitCreated);

            Grid splitGrid = Assert.IsType<Grid>(terminalHost.Children[0]);
            Assert.Equal(3, splitGrid.ColumnDefinitions.Count);

            await viewModel.FocusPaneLeftCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Focused pane", viewModel.StatusText, StringComparison.Ordinal);

            await viewModel.ResizePaneRightCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Pane ratio", viewModel.StatusText, StringComparison.Ordinal);

            splitGrid.ColumnDefinitions[0].Width = new GridLength(0.7, GridUnitType.Star);
            splitGrid.ColumnDefinitions[2].Width = new GridLength(0.3, GridUnitType.Star);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceTab savedTab = Assert.Single(workspaceStore.Document.Windows[0].Tabs);
        Assert.NotNull(savedTab.RootPane.Split);
        TerminalWorkspacePaneSplit savedSplit = savedTab.RootPane.Split!;
        Assert.Equal(TerminalWorkspacePaneSplitOrientations.Horizontal, savedSplit.Orientation);
        Assert.Equal(0.7, savedSplit.Ratio, precision: 3);
        Assert.NotNull(savedSplit.FirstPane);
        Assert.NotNull(savedSplit.SecondPane);
    }

    [AvaloniaFact]
    public async Task Controller_CloseCurrentPane_CollapsesSplitAndClearsCommandState()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new();
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo close-pane";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            commandHistoryStore: new InMemoryCommandHistoryStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabCreated);
            Assert.False(viewModel.CanCloseCurrentPane);

            await viewModel.SplitPaneRightCommand.Execute().ToTask();
            bool splitCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1 &&
                      terminalHost.Children[0] is Grid { Children.Count: 3 } &&
                      GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(splitCreated);
            Assert.True(viewModel.CanCloseCurrentPane);
            bool activePaneShown = await WaitUntilAsync(
                () => CountActivePaneContainers(terminalHost) == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(activePaneShown);

            Button tabStripNewTabButton = window.FindControl<Button>("TabStripNewTabButton")
                ?? throw new InvalidOperationException("TabStripNewTabButton was not found.");
            tabStripNewTabButton.Focus();
            bool activePaneHidden = await WaitUntilAsync(
                () => CountActivePaneContainers(terminalHost) == 0,
                TimeSpan.FromSeconds(2));
            Assert.True(activePaneHidden);
            Assert.True(viewModel.CanCloseCurrentPane);

            await viewModel.FocusPaneLeftCommand.Execute().ToTask();
            bool activePaneMoved = await WaitUntilAsync(
                () => CountActivePaneContainers(terminalHost) == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(activePaneMoved);

            await viewModel.CloseCurrentPaneCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(GetStandaloneControls(terminalHost));
            Border remainingPane = Assert.Single(GetPaneContainers(terminalHost));
            Assert.DoesNotContain("activePane", remainingPane.Classes);
            Assert.False(viewModel.CanCloseCurrentPane);
            Assert.Contains("Closed pane", viewModel.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceTab savedTab = Assert.Single(workspaceStore.Document.Windows[0].Tabs);
        Assert.Null(savedTab.RootPane.Split);
    }

    [AvaloniaFact]
    public async Task Controller_CloseCurrentPane_PromotesNestedSiblingSplit()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new();
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo close-nested-pane";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            commandHistoryStore: new InMemoryCommandHistoryStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabCreated);

            await viewModel.SplitPaneRightCommand.Execute().ToTask();
            await viewModel.SplitPaneDownCommand.Execute().ToTask();
            bool nestedSplitCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 3,
                TimeSpan.FromSeconds(2));
            Assert.True(nestedSplitCreated);

            await viewModel.CloseCurrentPaneCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, GetStandaloneControls(terminalHost).Count);
            Assert.True(viewModel.CanCloseCurrentPane);
            bool activePaneShown = await WaitUntilAsync(
                () => CountActivePaneContainers(terminalHost) == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(activePaneShown);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceTab savedTab = Assert.Single(workspaceStore.Document.Windows[0].Tabs);
        TerminalWorkspacePaneSplit savedSplit = savedTab.RootPane.Split
            ?? throw new InvalidOperationException("Saved promoted split was not found.");
        Assert.Null(savedSplit.FirstPane.Split);
        Assert.Null(savedSplit.SecondPane.Split);
    }

    [AvaloniaFact]
    public async Task Controller_SplitPanePolicy_DeniesSshTransport()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Ssh);
        viewModel.SshHost = "example.test";
        viewModel.SshUsername = "royal";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            commandHistoryStore: new InMemoryCommandHistoryStore(),
            settingsProfileStore: CreateEmptyProfileStore(),
            paneSplitPolicy: TerminalPaneSplitPolicies.PtyOnly);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabCreated);

            await viewModel.SplitPaneRightCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(GetStandaloneControls(terminalHost));
            Assert.Contains("not available for ssh sessions", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_SplitPanePolicy_CanOverrideClonedLaunchProfile()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        InMemoryWorkspaceStore workspaceStore = new();
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo policy-source";
        bool policyInvoked = false;
        ITerminalPaneSplitPolicy splitPolicy = new DelegateTerminalPaneSplitPolicy(context =>
        {
            policyInvoked = true;
            Assert.Equal(TerminalPaneSplitRequest.Right, context.Request);
            Assert.Equal(TerminalTransportIds.Pipe, context.SourceTransportId);
            Assert.False(context.SourceHasActiveSession);

            TerminalSessionProfile launchProfile = context.DefaultLaunchProfile with
            {
                Id = "rebex-mfa-clone",
                DisplayName = "Rebex MFA Clone",
                Layout = context.DefaultLaunchProfile.Layout with
                {
                    ScrollbackLimit = 1_234,
                },
                Transport = context.DefaultLaunchProfile.Transport with
                {
                    TransportId = TerminalTransportIds.Pipe,
                    Pipe = context.DefaultLaunchProfile.Transport.Pipe with
                    {
                        FileName = "echo",
                        Arguments = ["policy-clone"],
                    },
                },
            };

            return TerminalPaneSplitDecision.Allow(launchProfile);
        });

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: workspaceStore,
            commandHistoryStore: new InMemoryCommandHistoryStore(),
            settingsProfileStore: CreateEmptyProfileStore(),
            paneSplitPolicy: splitPolicy);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabCreated);

            await viewModel.SplitPaneRightCommand.Execute().ToTask();
            bool splitCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1 &&
                      terminalHost.Children[0] is Grid { Children.Count: 3 } &&
                      GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(splitCreated);
            Assert.True(policyInvoked);
            Assert.Contains(
                GetStandaloneControls(terminalHost),
                control => control.ScrollbackLimit == 1_234);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }

        TerminalWorkspaceTab savedTab = Assert.Single(workspaceStore.Document.Windows[0].Tabs);
        TerminalWorkspacePaneSplit savedSplit = savedTab.RootPane.Split
            ?? throw new InvalidOperationException("Saved split pane was not found.");
        Assert.Equal("rebex-mfa-clone", savedSplit.SecondPane.ProfileId);
        Assert.Equal(TerminalTransportIds.Pipe, savedSplit.SecondPane.TransportId);
    }

    [AvaloniaFact]
    public async Task Controller_ResizePaneRight_GrowsFocusedSecondPane()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo resize-pane";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            commandHistoryStore: new InMemoryCommandHistoryStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabCreated);

            await viewModel.SplitPaneRightCommand.Execute().ToTask();
            bool splitCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1 &&
                      terminalHost.Children[0] is Grid { Children.Count: 3 } &&
                      GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(splitCreated);

            Grid splitGrid = Assert.IsType<Grid>(terminalHost.Children[0]);
            Assert.Equal(0.5, splitGrid.ColumnDefinitions[0].Width.Value, precision: 3);
            Assert.Equal(0.5, splitGrid.ColumnDefinitions[2].Width.Value, precision: 3);

            await viewModel.ResizePaneRightCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0.45, splitGrid.ColumnDefinitions[0].Width.Value, precision: 3);
            Assert.Equal(0.55, splitGrid.ColumnDefinitions[2].Width.Value, precision: 3);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_Startup_DiagnosticMode_CreatesTabsForEachSupportedMode()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, "1");
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup-modes";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: CreateEmptyProfileStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(viewModel.NativeVtAvailable);
            TerminalModeResolver resolver = TerminalModeResolver.Default;
            int expectedStartupTabs = CountSupportedModes(resolver, capabilities);

            bool createdExpectedTabs = await WaitUntilAsync(
                () => terminalHost.Children.Count == expectedStartupTabs,
                TimeSpan.FromSeconds(2));
            Assert.True(createdExpectedTabs);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_Startup_DiagnosticModeIndicators_UseDistinctColors()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, "1");
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup-mode-indicators";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            ItemsControl tabStrip = window.FindControl<ItemsControl>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            Dictionary<string, Color> standaloneModeColors = GetStandaloneModeIndicatorColors(tabStrip);
            Dictionary<string, string> standaloneModeGlyphs = GetStandaloneModeIndicatorGlyphs(tabStrip);

            Assert.True(standaloneModeColors.Count >= 2);
            Assert.Equal(standaloneModeColors.Count, standaloneModeColors.Values.Distinct().Count());
            Assert.Equal(standaloneModeGlyphs.Count, standaloneModeGlyphs.Values.Distinct().Count());
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_NewTab_RequestModes_ResolveToSupportedModesWithoutCrash()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo mode-startup-smoke";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool initialTabCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(initialTabCreated);

            TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(viewModel.NativeVtAvailable);
            TerminalModeResolver resolver = TerminalModeResolver.Default;
            ItemsControl tabStrip = window.FindControl<ItemsControl>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");

            TerminalRenderMode[] requestedModes =
            [
                TerminalRenderMode.NativeVt,
                TerminalRenderMode.ManagedVt,
                TerminalRenderMode.RenderedAuto,
            ];

            for (int i = 0; i < requestedModes.Length; i++)
            {
                TerminalRenderMode requestedMode = requestedModes[i];
                SetRequestedMode(viewModel, requestedMode);

                int countBefore = terminalHost.Children.Count;
                await viewModel.NewTabCommand.Execute().ToTask();

                bool created = await WaitUntilAsync(
                    () => terminalHost.Children.Count > countBefore,
                    TimeSpan.FromSeconds(2));
                Assert.True(created);

                Control newContainer = terminalHost.Children[^1];
                Button newHeader = GetLastTabHeader(tabStrip);
                TerminalRenderMode actualMode = ResolveModeFromContainer(newContainer, newHeader);
                TerminalRenderMode expectedMode = resolver.ResolveSupportedMode(requestedMode, capabilities);
                Assert.Equal(expectedMode, actualMode);
            }
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_RenderedAuto_UsesStandaloneVtControl()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo rendered-auto";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new FixedTerminalModeCapabilityResolver(TerminalModeCapabilities.Create(nativeVtAvailable: true)),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            viewModel.SetRenderMode(
                useRenderedControl: true,
                useNativeVtControl: false);

            int countBefore = terminalHost.Children.Count;
            await viewModel.NewTabCommand.Execute().ToTask();

            bool created = await WaitUntilAsync(
                () => terminalHost.Children.Count == countBefore + 1,
                TimeSpan.FromSeconds(2));
            Assert.True(created);

            Control newContainer = terminalHost.Children[^1];
            Border paneContainer = Assert.IsType<Border>(newContainer);
            Assert.Contains("terminalPane", paneContainer.Classes);
            ScrollViewer scrollViewer = Assert.IsType<ScrollViewer>(paneContainer.Child);
            TerminalControl standalone = Assert.IsType<TerminalControl>(scrollViewer.Content);
            Assert.Equal(VtProcessorPreference.Auto, standalone.VtProcessorPreference);

            ItemsControl tabStrip = window.FindControl<ItemsControl>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            Button headerButton = GetLastTabHeader(tabStrip);
            string expectedVtLabel = viewModel.NativeVtAvailable
                ? "Ghostty VT"
                : "Basic VT";
            Assert.Equal(
                $"Rendered (Pipe - {expectedVtLabel})",
                ToolTip.GetTip(headerButton) as string);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_RuntimeCapabilities_KeepModeCycleStable()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo mode-cycle-smoke";

        Window window = CreateControllerHostWindow(viewModel, out _);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(viewModel.NativeVtAvailable);
            TerminalModeResolver resolver = TerminalModeResolver.Default;

            TerminalRenderMode currentMode = GetActiveMode(viewModel);
            for (int i = 0; i < 10; i++)
            {
                TerminalRenderMode expected = resolver.ResolveNextMode(currentMode, capabilities);
                await viewModel.CycleRenderModeCommand.Execute().ToTask();

                TerminalRenderMode actual = GetActiveMode(viewModel);
                Assert.Equal(expected, actual);
                Assert.True(resolver.IsSupported(actual, capabilities));
                currentMode = actual;
            }
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_TerminalBehaviorSettings_AreAppliedAndUpdated()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo behavior-settings";
        viewModel.SelectedPasteSafetyPolicy = TerminalPasteSafetyPolicy.BlockUnsafe;
        viewModel.EnableTextShaping = false;
        viewModel.ReflowOnResize = false;
        viewModel.PreserveScrollbackOnRestart = true;
        viewModel.SixelGraphicsEnabled = true;
        viewModel.EnableLigatures = true;

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: CreateEmptyProfileStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            List<TerminalControl> controls = GetStandaloneControls(terminalHost);
            Assert.NotEmpty(controls);
            AssertTerminalBehaviorSettings(
                controls,
                TerminalPasteSafetyPolicy.BlockUnsafe,
                enableTextShaping: true,
                reflowOnResize: false,
                preserveScrollbackOnSessionStart: true,
                sixelGraphicsEnabled: true,
                enableLigatures: true);

            viewModel.SelectedPasteSafetyPolicy = TerminalPasteSafetyPolicy.SanitizeControlSequences;
            viewModel.EnableTextShaping = true;
            viewModel.ReflowOnResize = true;
            viewModel.PreserveScrollbackOnRestart = false;
            viewModel.SixelGraphicsEnabled = false;
            viewModel.EnableLigatures = false;
            Dispatcher.UIThread.RunJobs();

            AssertTerminalBehaviorSettings(
                controls,
                TerminalPasteSafetyPolicy.SanitizeControlSequences,
                enableTextShaping: true,
                reflowOnResize: true,
                preserveScrollbackOnSessionStart: false,
                sixelGraphicsEnabled: false,
                enableLigatures: false);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_ProfileLaunch_AppliesAppearanceRuntimeSettings()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        TerminalSessionProfilesDocument document = new()
        {
            DefaultProfileId = "profile-appearance",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "profile-appearance",
                    DisplayName = "Profile Appearance",
                    Transport = new TerminalSessionTransportProfile
                    {
                        TransportId = TerminalTransportIds.Pipe,
                        Pipe = new TerminalSessionPipeSettings
                        {
                            FileName = "echo",
                            Arguments = ["appearance"],
                        },
                    },
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        AutoScroll = false,
                        BackgroundOpacityEnabled = true,
                    },
                },
            ],
        };
        InMemoryProfileStore profileStore = new(document);
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: profileStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            await viewModel.LaunchSessionProfileCommand.Execute("profile:profile-appearance").ToTask();
            bool profileTabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(profileTabCreated);

            TerminalControl launched = GetVisibleStandaloneControl(terminalHost);
            Assert.False(launched.AutoScroll);
            Assert.True(launched.BackgroundOpacityEnabled);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_ProfileLaunch_AppliesLayoutScrollbackLimit()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        TerminalSessionProfilesDocument document = new()
        {
            DefaultProfileId = "profile-layout",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "profile-layout",
                    DisplayName = "Profile Layout",
                    Transport = CreatePipeTransportProfile("profile-layout"),
                    Layout = new TerminalSessionLayoutSettings
                    {
                        ScrollbackLimit = 50_000,
                    },
                },
            ],
        };
        InMemoryProfileStore profileStore = new(document);
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: profileStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            await viewModel.LaunchSessionProfileCommand.Execute("profile:profile-layout").ToTask();
            bool profileTabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(profileTabCreated);

            TerminalControl launched = GetVisibleStandaloneControl(terminalHost);
            Assert.Equal(50_000, launched.ScrollbackLimit);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_ProfileLaunch_KeepsBehaviorScopedToLaunchedControl()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        TerminalSessionProfilesDocument document = new()
        {
            DefaultProfileId = "profile-a",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "profile-a",
                    DisplayName = "Profile A",
                    Transport = CreatePipeTransportProfile("echo profile-a"),
                    Behavior = new TerminalSessionBehaviorSettings
                    {
                        EnableTextShaping = false,
                        ReflowOnResize = false,
                        SixelGraphicsEnabled = false,
                        EnableLigatures = false,
                        PasteSafetyPolicy = "SanitizeControlSequences",
                    },
                },
                new TerminalSessionProfile
                {
                    Id = "profile-b",
                    DisplayName = "Profile B",
                    Transport = CreatePipeTransportProfile("echo profile-b"),
                    Behavior = new TerminalSessionBehaviorSettings
                    {
                        EnableTextShaping = true,
                        ReflowOnResize = true,
                        SixelGraphicsEnabled = true,
                        EnableLigatures = true,
                        PasteSafetyPolicy = "BlockUnsafe",
                    },
                },
            ],
        };
        InMemoryProfileStore profileStore = new(document);
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup";
        viewModel.SelectedPasteSafetyPolicy = TerminalPasteSafetyPolicy.None;
        viewModel.EnableTextShaping = true;
        viewModel.ReflowOnResize = true;
        viewModel.PreserveScrollbackOnRestart = false;
        viewModel.SixelGraphicsEnabled = true;
        viewModel.EnableLigatures = true;

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: profileStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            await viewModel.LaunchSessionProfileCommand.Execute("profile:profile-a").ToTask();
            bool profileATabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(profileATabCreated);

            await viewModel.LaunchSessionProfileCommand.Execute("profile:profile-b").ToTask();
            bool profileBTabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 3,
                TimeSpan.FromSeconds(2));
            Assert.True(profileBTabCreated);

            List<TerminalControl> controls = GetStandaloneControls(terminalHost);
            AssertTerminalBehaviorSettings(
                [controls[0]],
                TerminalPasteSafetyPolicy.SanitizeControlSequences,
                enableTextShaping: false,
                reflowOnResize: false,
                preserveScrollbackOnSessionStart: false,
                sixelGraphicsEnabled: false,
                enableLigatures: false);
            AssertTerminalBehaviorSettings(
                [controls[1]],
                TerminalPasteSafetyPolicy.SanitizeControlSequences,
                enableTextShaping: false,
                reflowOnResize: false,
                preserveScrollbackOnSessionStart: false,
                sixelGraphicsEnabled: false,
                enableLigatures: false);
            AssertTerminalBehaviorSettings(
                [controls[2]],
                TerminalPasteSafetyPolicy.BlockUnsafe,
                enableTextShaping: true,
                reflowOnResize: true,
                preserveScrollbackOnSessionStart: false,
                sixelGraphicsEnabled: true,
                enableLigatures: true);
            Assert.Equal(TerminalPasteSafetyPolicy.None, viewModel.SelectedPasteSafetyPolicy);
            Assert.True(viewModel.SixelGraphicsEnabled);
            Assert.True(viewModel.EnableLigatures);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_ProfileLaunch_QuotesPipeProfileShellMetacharacters()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        TerminalSessionProfilesDocument document = new()
        {
            DefaultProfileId = "quoted-pipe",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "quoted-pipe",
                    DisplayName = "Quoted Pipe",
                    Transport = new TerminalSessionTransportProfile
                    {
                        TransportId = TerminalTransportIds.Pipe,
                        Pipe = new TerminalSessionPipeSettings
                        {
                            FileName = "/tmp/a&b",
                            Arguments = ["$HOME", "has space", "it's"],
                        },
                    },
                },
            ],
        };
        InMemoryProfileStore profileStore = new(document);
        MainWindowViewModel viewModel = new();
        ShellProfileOption shellProfile = new("cmd", "Command shell", "cmd.exe");
        viewModel.SetShellProfiles([shellProfile]);
        viewModel.SelectedShellProfile = shellProfile;
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: profileStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            await viewModel.LaunchSessionProfileCommand.Execute("profile:quoted-pipe").ToTask();
            bool profileTabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(profileTabCreated);

            string expected = OperatingSystem.IsWindows()
                ? "\"/tmp/a&b\" \"$HOME\" \"has space\" \"it's\""
                : "'/tmp/a&b' '$HOME' 'has space' 'it'\"'\"'s'";
            Assert.Equal(expected, viewModel.PipeCommandText);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_ProfileLaunch_QuotesPipeProfileArgumentsAsPowerShellLiterals()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        TerminalSessionProfilesDocument document = new()
        {
            DefaultProfileId = "powershell-pipe",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "powershell-pipe",
                    DisplayName = "PowerShell Pipe",
                    Transport = new TerminalSessionTransportProfile
                    {
                        TransportId = TerminalTransportIds.Pipe,
                        Pipe = new TerminalSessionPipeSettings
                        {
                            FileName = "/tmp/a&b",
                            Arguments = ["$HOME", "$(Get-Date)", "has space", "it's"],
                        },
                    },
                },
            ],
        };
        InMemoryProfileStore profileStore = new(document);
        MainWindowViewModel viewModel = new();
        ShellProfileOption shellProfile = new(
            "pwsh",
            "PowerShell",
            @"C:\Program Files\PowerShell\7\pwsh.exe");
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "Write-Output startup";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: profileStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            viewModel.SetShellProfiles([shellProfile]);
            viewModel.SelectedShellProfile = shellProfile;
            await viewModel.LaunchSessionProfileCommand.Execute("profile:powershell-pipe").ToTask();
            bool profileTabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(profileTabCreated);

            Assert.Equal(
                "'/tmp/a&b' '$HOME' '$(Get-Date)' 'has space' 'it''s'",
                viewModel.PipeCommandText);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_ProfileSessionLogging_RemainsScopedToLaunchedControl()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        string directory = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid().ToString("N"));
        string firstLogPath = Path.Combine(directory, "first.log");
        string secondLogPath = Path.Combine(directory, "second.log");
        TerminalSessionProfilesDocument document = new()
        {
            DefaultProfileId = "first",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "first",
                    DisplayName = "First",
                    Transport = CreatePipeTransportProfile("echo first"),
                    Logging = new TerminalSessionLoggingSettings
                    {
                        Enabled = true,
                        FilePath = firstLogPath,
                        Format = TerminalSessionLogFormat.PlainText,
                        FlushFrequently = true,
                    },
                },
                new TerminalSessionProfile
                {
                    Id = "second",
                    DisplayName = "Second",
                    Transport = CreatePipeTransportProfile("echo second"),
                    Logging = new TerminalSessionLoggingSettings
                    {
                        Enabled = true,
                        FilePath = secondLogPath,
                        Format = TerminalSessionLogFormat.PlainText,
                        FlushFrequently = true,
                    },
                },
            ],
        };
        InMemoryProfileStore profileStore = new(document);
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: profileStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            await viewModel.LaunchSessionProfileCommand.Execute("profile:first").ToTask();
            bool firstProfileTabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(firstProfileTabCreated);

            await viewModel.LaunchSessionProfileCommand.Execute("profile:second").ToTask();
            bool secondProfileTabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 3,
                TimeSpan.FromSeconds(2));
            Assert.True(secondProfileTabCreated);

            List<TerminalControl> controls = GetStandaloneControls(terminalHost);
            TerminalControl firstProfileControl = controls[1];
            TerminalControl secondProfileControl = controls[2];

            firstProfileControl.WriteOutput(Encoding.UTF8.GetBytes("first-profile-output\n"));
            secondProfileControl.WriteOutput(Encoding.UTF8.GetBytes("second-profile-output\n"));
            Dispatcher.UIThread.RunJobs();

            bool logsWritten = await WaitUntilAsync(
                () => File.Exists(firstLogPath) &&
                      File.Exists(secondLogPath) &&
                      ReadSharedText(firstLogPath).Contains("first-profile-output", StringComparison.Ordinal) &&
                      ReadSharedText(secondLogPath).Contains("second-profile-output", StringComparison.Ordinal),
                TimeSpan.FromSeconds(2));
            Assert.True(logsWritten);

            string firstLog = ReadSharedText(firstLogPath);
            string secondLog = ReadSharedText(secondLogPath);
            Assert.DoesNotContain("second-profile-output", firstLog, StringComparison.Ordinal);
            Assert.DoesNotContain("first-profile-output", secondLog, StringComparison.Ordinal);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public async Task Controller_SettingsApply_DisposesSessionLogWriterWhenLoggingDisabled()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        string directory = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid().ToString("N"));
        string logPath = Path.Combine(directory, "disable-logging.log");
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo disable-logging";
        viewModel.SessionLoggingEnabled = true;
        viewModel.SessionLogFilePath = logPath;
        viewModel.SelectedSessionLogFormat = TerminalSessionLogFormat.PlainText;
        viewModel.SessionLogFlushFrequently = false;

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            commandHistoryStore: new InMemoryCommandHistoryStore(),
            settingsProfileStore: CreateEmptyProfileStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool tabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(tabCreated);

            TerminalControl control = Assert.Single(GetStandaloneControls(terminalHost));
            control.WriteOutput(Encoding.UTF8.GetBytes("buffered-before-disable\n"));
            Dispatcher.UIThread.RunJobs();

            viewModel.SettingsPanelState.SessionLoggingEnabled = false;
            viewModel.SettingsPanelState.SessionLogFilePath = logPath;
            viewModel.SettingsPanelState.SelectedSessionLogFormat = TerminalSessionLogFormat.PlainText;
            viewModel.SettingsPanelState.SessionLogFlushFrequently = false;
            viewModel.SettingsPanelState.ApplyCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            bool logFlushed = await WaitUntilAsync(
                () => File.Exists(logPath) &&
                      File.ReadAllText(logPath).Contains("buffered-before-disable", StringComparison.Ordinal),
                TimeSpan.FromSeconds(2));
            Assert.True(logFlushed);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public async Task Controller_SplitPaneFromProfile_ClonesActiveAppearanceAfterOtherProfileLaunch()
    {
        using IDisposable environment = SetProcessEnvironmentVariable(StartAllRenderModesEnvVar, null);
        using IDisposable autostart = SetProcessEnvironmentVariable("ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART", "1");
        TerminalSessionProfilesDocument document = new()
        {
            DefaultProfileId = "profile-a",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "profile-a",
                    DisplayName = "Profile A",
                    Transport = CreatePipeTransportProfile("echo profile-a"),
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        FontFamilyName = "Profile A Mono",
                        FontSize = 19.0,
                        FontRendering = new TerminalFontRenderingSettings
                        {
                            SubpixelPositioning = false,
                            Edging = TerminalFontEdging.Alias,
                            Hinting = TerminalFontHinting.None,
                            BaselineSnap = false,
                            EmbeddedBitmaps = true,
                            Embolden = true,
                            ForceAutoHinting = true,
                            LinearMetrics = true,
                        },
                        AutoScroll = false,
                        BackgroundOpacityEnabled = true,
                        TextHighlightingMode = TerminalTextHighlightingMode.Realtime,
                        TextHighlightRules =
                        [
                            new TerminalSessionTextHighlightRule
                            {
                                Name = "Errors",
                                Pattern = "ERROR",
                                ForegroundColor = "#FFFF0000",
                            },
                        ],
                    },
                },
                new TerminalSessionProfile
                {
                    Id = "profile-b",
                    DisplayName = "Profile B",
                    Transport = CreatePipeTransportProfile("echo profile-b"),
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        FontFamilyName = "Profile B Mono",
                        FontSize = 11.0,
                        AutoScroll = true,
                        BackgroundOpacityEnabled = false,
                        TextHighlightingMode = TerminalTextHighlightingMode.Disabled,
                    },
                },
            ],
        };
        InMemoryProfileStore profileStore = new(document);
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore(),
            settingsProfileStore: profileStore);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == 1,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            await viewModel.LaunchSessionProfileCommand.Execute("profile:profile-a").ToTask();
            bool profileATabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 2,
                TimeSpan.FromSeconds(2));
            Assert.True(profileATabCreated);
            TerminalControl profileAControl = GetVisibleStandaloneControl(terminalHost);

            await viewModel.LaunchSessionProfileCommand.Execute("profile:profile-b").ToTask();
            bool profileBTabCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 3,
                TimeSpan.FromSeconds(2));
            Assert.True(profileBTabCreated);
            Assert.Equal(11.0, GetVisibleStandaloneControl(terminalHost).TerminalFontSize);

            await viewModel.ActivateTabCommand.Execute(2).ToTask();
            bool profileAReactivated = await WaitUntilAsync(
                () => ReferenceEquals(GetVisibleStandaloneControl(terminalHost), profileAControl),
                TimeSpan.FromSeconds(2));
            Assert.True(profileAReactivated);

            HashSet<TerminalControl> controlsBeforeSplit = [.. GetStandaloneControls(terminalHost)];
            await viewModel.SplitPaneRightCommand.Execute().ToTask();
            bool splitCreated = await WaitUntilAsync(
                () => GetStandaloneControls(terminalHost).Count == 4,
                TimeSpan.FromSeconds(2));
            Assert.True(splitCreated);

            TerminalControl splitControl = Assert.Single(
                GetStandaloneControls(terminalHost),
                control => !controlsBeforeSplit.Contains(control) && control.TerminalFontSize == 19.0);
            Assert.Equal("Profile A Mono", splitControl.FontFamilyName);
            Assert.Equal(TerminalFontSource.System, splitControl.FontSource);
            Assert.False(splitControl.FontSubpixelPositioning);
            Assert.Equal(TerminalFontEdging.Alias, splitControl.FontEdging);
            Assert.Equal(TerminalFontHinting.None, splitControl.FontHinting);
            Assert.False(splitControl.FontBaselineSnap);
            Assert.True(splitControl.FontEmbeddedBitmaps);
            Assert.True(splitControl.FontEmbolden);
            Assert.True(splitControl.FontForceAutoHinting);
            Assert.True(splitControl.FontLinearMetrics);
            Assert.False(splitControl.AutoScroll);
            Assert.True(splitControl.BackgroundOpacityEnabled);
            Assert.Equal(TerminalTextHighlightingMode.Realtime, splitControl.TextHighlightingMode);
            TerminalTextHighlightRule rule = Assert.Single(splitControl.TextHighlightRules ?? []);
            Assert.Equal("Errors", rule.Name);
            Assert.Equal("ERROR", rule.Pattern);
            Assert.Equal(0xFFFF0000u, rule.Foreground);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_StandaloneTerminalOutput_DoesNotSpamStatusBar()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo status-spam-check";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            List<TerminalControl> controls = GetStandaloneControls(terminalHost);
            Assert.NotEmpty(controls);
            TerminalControl control = controls[0];

            viewModel.SetStatus("status-marker");
            Dispatcher.UIThread.RunJobs();

            control.WriteOutput(Encoding.UTF8.GetBytes("demo-output\n"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("status-marker", viewModel.StatusText);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_ClearHistoryCommand_DropsActiveStandaloneScrollback()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetRenderMode(
            useRenderedControl: false,
            useNativeVtControl: false,
            useManagedVtControl: true);
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo clear-history-demo";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            viewModel.SetRenderMode(
                useRenderedControl: false,
                useNativeVtControl: false,
                useManagedVtControl: true);
            int managedTabIndex = terminalHost.Children.Count;
            await viewModel.NewTabCommand.Execute().ToTask();
            bool managedTabCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == managedTabIndex + 1,
                TimeSpan.FromSeconds(2));
            Assert.True(managedTabCreated);

            await viewModel.SwitchToTabByIndexCommand.Execute(managedTabIndex).ToTask();
            Dispatcher.UIThread.RunJobs();

            TerminalControl activeControl = GetVisibleStandaloneControl(terminalHost);
            Assert.Equal(VtProcessorPreference.Managed, activeControl.VtProcessorPreference);
            bool startupOutputSeen = await WaitUntilAsync(
                () =>
                {
                    TerminalScreen? activeScreen = activeControl.Screen as TerminalScreen;
                    if (activeScreen is null)
                    {
                        return false;
                    }

                    lock (activeScreen.SyncRoot)
                    {
                        return ReadAllRows(activeScreen).Contains("clear-history-demo", StringComparison.Ordinal);
                    }
                },
                TimeSpan.FromSeconds(2));
            Assert.True(startupOutputSeen);

            if (activeControl.HasActiveSession || activeControl.HasPty)
            {
                activeControl.StopPty();
                await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                int historyRowsToWrite = Math.Max(64, activeControl.Rows + 64);
                for (int i = 0; i < historyRowsToWrite; i++)
                {
                    activeControl.WriteOutput(Encoding.UTF8.GetBytes($"HISTORY-{i:000}\r\n"));
                }

                activeControl.WriteOutput("prompt$ "u8.ToArray());
                activeControl.ScrollByRows(-3);
            });

            TerminalScreen screen = Assert.IsType<TerminalScreen>(activeControl.Screen);
            lock (screen.SyncRoot)
            {
                Assert.True(
                    screen.MaxScrollOffset > 0,
                    $"Expected synthetic history to create scrollback. Preference={activeControl.VtProcessorPreference}, " +
                    $"Native={activeControl.IsUsingNativeVtProcessor}, Rows={activeControl.Rows}, " +
                    $"ViewportRows={screen.ViewportRows}, TotalRows={screen.TotalRows}, " +
                    $"MaxScrollOffset={screen.MaxScrollOffset}, ScrollOffset={screen.ScrollOffset}, Status='{viewModel.StatusText}'.");
                Assert.True(
                    screen.ScrollOffset > 0,
                    $"Expected ScrollByRows to move into scrollback. MaxScrollOffset={screen.MaxScrollOffset}, ScrollOffset={screen.ScrollOffset}.");
            }

            List<byte[]> sentInputs = [];
            activeControl.TerminalSessionService.InputSent += (_, args) => sentInputs.Add(args.Data.ToArray());

            await viewModel.ClearActiveScrollbackCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();

            lock (screen.SyncRoot)
            {
                string allRows = ReadAllRows(screen);
                Assert.True(screen.MaxScrollOffset == 0, allRows);
                Assert.Equal(0, screen.ScrollOffset);
                Assert.Equal(screen.ViewportRows, screen.TotalRows);
                Assert.DoesNotContain("HISTORY-", allRows, StringComparison.Ordinal);
                Assert.Contains("prompt$ ", allRows, StringComparison.Ordinal);
                Assert.StartsWith("prompt$ ", ReadRow(screen.GetViewportRow(0)), StringComparison.Ordinal);
            }

            Assert.Empty(sentInputs);
            Assert.Contains("Cleared history", viewModel.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_SearchCommands_SyncActiveTerminalSearchSurface()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo search-demo";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            TerminalControl activeControl = GetVisibleStandaloneControl(terminalHost);
            activeControl.WriteOutput(Encoding.UTF8.GetBytes("alpha beta alpha\r\n"));
            Dispatcher.UIThread.RunJobs();

            viewModel.SearchQuery = "alpha";
            await viewModel.ApplySearchCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("alpha", activeControl.SearchNeedle);
            Assert.Equal(2, activeControl.SearchTotal);
            Assert.Equal(1, activeControl.SearchSelected);
            Assert.Equal(0, activeControl.SearchSelectedDisplayIndex);
            Assert.Contains("1/2 matches", viewModel.SearchResultText, StringComparison.Ordinal);
            Assert.Contains("/2 matches", viewModel.SearchResultText, StringComparison.Ordinal);

            await viewModel.NextSearchCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, activeControl.SearchSelected);
            Assert.Equal(1, activeControl.SearchSelectedDisplayIndex);
            Assert.Contains("2/2 matches", viewModel.SearchResultText, StringComparison.Ordinal);

            await viewModel.ClearSearchCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(activeControl.SearchNeedle);
            Assert.Equal("Search idle", viewModel.SearchResultText);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_CopySnapshotCommand_ExportsActiveTerminalToClipboard()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo snapshot-demo";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            TerminalControl activeControl = GetVisibleStandaloneControl(terminalHost);
            activeControl.WriteOutput(Encoding.UTF8.GetBytes("snapshot demo\r\n"));
            Dispatcher.UIThread.RunJobs();

            await viewModel.CopyPlainSnapshotCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();

            string? copied = await window.Clipboard!.TryGetTextAsync();
            Assert.NotNull(copied);
            Assert.Contains("snapshot demo", copied, StringComparison.Ordinal);
            Assert.Contains("Copied PlainText snapshot", viewModel.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_ShowcaseHyperlinkAndDiagnostics_UpdateActiveTab()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo showcase-demo";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            await viewModel.ToggleGhosttyDiagnosticsCommand.Execute().ToTask();
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.ShowGhosttyDiagnostics);
            Assert.Contains("libghostty-vt available:", viewModel.GhosttyDiagnosticsText, StringComparison.Ordinal);

            await viewModel.ShowHyperlinkSampleCommand.Execute().ToTask();
            TerminalControl activeControl = GetVisibleStandaloneControl(terminalHost);
            bool hyperlinkApplied = await WaitUntilAsync(
                () => ViewportContainsHyperlink(activeControl),
                TimeSpan.FromSeconds(2));
            Assert.True(hyperlinkApplied);
            Assert.Contains("Hyperlink showcase injected", viewModel.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_ShowcaseKittyGraphics_UsesNativeGhosttyTab_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        GhosttyVtHelpers.GhosttyBuildFeatures features = GhosttyVtHelpers.GetBuildFeatures();
        if (!features.KittyGraphics)
        {
            return;
        }

        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo kitty-demo";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new FixedTerminalModeCapabilityResolver(TerminalModeCapabilities.Create(nativeVtAvailable: true)),
            TerminalModeResolver.Default,
            workspaceStore: new InMemoryWorkspaceStore());
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            viewModel.SetRenderMode(
                useRenderedControl: false,
                useNativeVtControl: true,
                useManagedVtControl: false);

            int countBefore = terminalHost.Children.Count;
            await viewModel.NewTabCommand.Execute().ToTask();
            bool newTabCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == countBefore + 1,
                TimeSpan.FromSeconds(2));
            Assert.True(newTabCreated);

            TerminalControl activeControl = GetVisibleStandaloneControl(terminalHost);
            Assert.True(activeControl.IsUsingNativeVtProcessor);

            await viewModel.ShowKittyGraphicsSampleCommand.Execute().ToTask();
            bool kittyApplied = await WaitUntilAsync(
                () => activeControl.Screen?.HasKittyGraphics == true,
                TimeSpan.FromSeconds(2));
            Assert.True(kittyApplied);
            Assert.Contains("Kitty Graphics showcase injected", viewModel.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    private static Window CreateControllerHostWindow(MainWindowViewModel viewModel, out Grid terminalHost)
    {
        Border titleBarBrandIcon = new()
        {
            Name = "TitleBarBrandIcon",
            IsVisible = viewModel.IsTitleBarLogoVisible,
        };
        titleBarBrandIcon.Bind(Visual.IsVisibleProperty, viewModel.WhenAnyValue(static model => model.IsTitleBarLogoVisible));

        ContentControl titleBarTabStripHost = new()
        {
            Name = "TitleBarTabStripHost",
            IsVisible = viewModel.IsTabsInTitleBar,
        };
        titleBarTabStripHost.Bind(Visual.IsVisibleProperty, viewModel.WhenAnyValue(static model => model.IsTabsInTitleBar));

        ItemsControl tabStrip = new()
        {
            Name = "TabStrip",
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
        };
        RepeatButton tabStripScrollLeftButton = new()
        {
            Name = "TabStripScrollLeftButton",
            IsVisible = false,
        };
        ScrollViewer tabStripScrollViewer = new()
        {
            Name = "TabStripScrollViewer",
            Content = tabStrip,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        RepeatButton tabStripScrollRightButton = new()
        {
            Name = "TabStripScrollRightButton",
            IsVisible = false,
        };
        Button tabStripNewTabButton = new()
        {
            Name = "TabStripNewTabButton",
            Command = viewModel.NewTabCommand,
        };
        StackPanel windowsCaptionButtonStrip = CreateWindowsCaptionButtonStrip(
            out Button captionMinimizeButton,
            out Button captionMaximizeButton,
            out Button captionRestoreButton,
            out Button captionFullscreenButton,
            out Button captionCloseButton);
        WindowDecorationProperties.SetElementRole(tabStripScrollLeftButton, WindowDecorationsElementRole.User);
        WindowDecorationProperties.SetElementRole(tabStripScrollRightButton, WindowDecorationsElementRole.User);
        WindowDecorationProperties.SetElementRole(tabStripNewTabButton, WindowDecorationsElementRole.User);
        WindowDecorationProperties.SetElementRole(windowsCaptionButtonStrip, WindowDecorationsElementRole.User);

        Grid tabStripLayout = new()
        {
            Name = "TabStripLayout",
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        tabStripLayout.Children.Add(tabStripScrollLeftButton);
        tabStripLayout.Children.Add(tabStripScrollViewer);
        tabStripLayout.Children.Add(tabStripScrollRightButton);
        tabStripLayout.Children.Add(tabStripNewTabButton);
        Grid.SetColumn(tabStripScrollViewer, 1);
        Grid.SetColumn(tabStripScrollRightButton, 2);
        Grid.SetColumn(tabStripNewTabButton, 3);

        Border tabStripSurface = new()
        {
            Name = "TabStripSurface",
            Child = tabStripLayout,
        };

        ContentControl bodyTabStripHost = new()
        {
            Name = "BodyTabStripHost",
            Content = tabStripSurface,
            IsVisible = viewModel.IsBodyTabStripVisible,
        };
        bodyTabStripHost.Bind(Visual.IsVisibleProperty, viewModel.WhenAnyValue(static model => model.IsBodyTabStripVisible));

        terminalHost = new Grid
        {
            Name = "TerminalHost",
        };

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        Grid titleBar = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        titleBar.Children.Add(titleBarBrandIcon);
        titleBar.Children.Add(titleBarTabStripHost);
        titleBar.Children.Add(windowsCaptionButtonStrip);
        Grid.SetColumn(titleBarTabStripHost, 1);
        Grid.SetColumn(windowsCaptionButtonStrip, 2);
        root.Children.Add(titleBar);
        root.Children.Add(bodyTabStripHost);
        root.Children.Add(terminalHost);
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(bodyTabStripHost, 1);
        Grid.SetRow(terminalHost, 2);

        Window window = new()
        {
            Width = 1200,
            Height = 800,
            DataContext = viewModel,
            Content = root,
        };

        NameScope nameScope = new();
        NameScope.SetNameScope(window, nameScope);
        nameScope.Register(tabStrip.Name!, tabStrip);
        nameScope.Register(titleBarBrandIcon.Name!, titleBarBrandIcon);
        nameScope.Register(titleBarTabStripHost.Name!, titleBarTabStripHost);
        nameScope.Register(bodyTabStripHost.Name!, bodyTabStripHost);
        nameScope.Register(tabStripSurface.Name!, tabStripSurface);
        nameScope.Register(tabStripLayout.Name!, tabStripLayout);
        nameScope.Register(tabStripScrollLeftButton.Name!, tabStripScrollLeftButton);
        nameScope.Register(tabStripScrollViewer.Name!, tabStripScrollViewer);
        nameScope.Register(tabStripScrollRightButton.Name!, tabStripScrollRightButton);
        nameScope.Register(tabStripNewTabButton.Name!, tabStripNewTabButton);
        nameScope.Register(windowsCaptionButtonStrip.Name!, windowsCaptionButtonStrip);
        nameScope.Register(captionMinimizeButton.Name!, captionMinimizeButton);
        nameScope.Register(captionMaximizeButton.Name!, captionMaximizeButton);
        nameScope.Register(captionRestoreButton.Name!, captionRestoreButton);
        nameScope.Register(captionFullscreenButton.Name!, captionFullscreenButton);
        nameScope.Register(captionCloseButton.Name!, captionCloseButton);
        nameScope.Register(terminalHost.Name!, terminalHost);

        window.Show();
        window.Focus();
        return window;
    }

    private static StackPanel CreateWindowsCaptionButtonStrip(
        out Button minimizeButton,
        out Button maximizeButton,
        out Button restoreButton,
        out Button fullscreenButton,
        out Button closeButton)
    {
        minimizeButton = new Button { Name = "CaptionMinimizeButton" };
        maximizeButton = new Button { Name = "CaptionMaximizeButton" };
        restoreButton = new Button { Name = "CaptionRestoreButton" };
        fullscreenButton = new Button { Name = "CaptionFullscreenButton" };
        closeButton = new Button { Name = "CaptionCloseButton" };

        StackPanel strip = new()
        {
            Name = "WindowsCaptionButtonStrip",
            Orientation = Orientation.Horizontal,
        };
        strip.Children.Add(minimizeButton);
        strip.Children.Add(maximizeButton);
        strip.Children.Add(restoreButton);
        strip.Children.Add(fullscreenButton);
        strip.Children.Add(closeButton);
        return strip;
    }

    private static TransportModeOption FindTransportMode(MainWindowViewModel viewModel, string id)
    {
        for (int i = 0; i < viewModel.TransportModes.Count; i++)
        {
            if (string.Equals(viewModel.TransportModes[i].Id, id, StringComparison.Ordinal))
            {
                return viewModel.TransportModes[i];
            }
        }

        throw new InvalidOperationException($"Transport mode '{id}' was not found.");
    }

    private static TerminalSessionTransportProfile CreatePipeTransportProfile(string command)
    {
        return new TerminalSessionTransportProfile
        {
            TransportId = TerminalTransportIds.Pipe,
            Pipe = new TerminalSessionPipeSettings
            {
                FileName = "echo",
                Arguments = [command],
            },
        };
    }

    private static void SetRequestedMode(MainWindowViewModel viewModel, TerminalRenderMode requestedMode)
    {
        viewModel.SetRenderMode(
            useRenderedControl: requestedMode == TerminalRenderMode.RenderedAuto,
            useNativeVtControl: requestedMode == TerminalRenderMode.NativeVt,
            useManagedVtControl: requestedMode == TerminalRenderMode.ManagedVt);
    }

    private static TerminalRenderMode GetActiveMode(MainWindowViewModel viewModel)
    {
        if (viewModel.UseNativeVtControl)
        {
            return TerminalRenderMode.NativeVt;
        }

        if (viewModel.UseManagedVtControl)
        {
            return TerminalRenderMode.ManagedVt;
        }

        return TerminalRenderMode.RenderedAuto;
    }

    private static TerminalRenderMode ResolveModeFromContainer(Control container, Button? headerButton = null)
    {
        if (container is ScrollViewer { Content: TerminalControl standalone })
        {
            if (headerButton is not null && (ToolTip.GetTip(headerButton) as string)?.StartsWith("Rendered (", StringComparison.Ordinal) == true)
            {
                return TerminalRenderMode.RenderedAuto;
            }

            return standalone.VtProcessorPreference switch
            {
                VtProcessorPreference.Native => TerminalRenderMode.NativeVt,
                VtProcessorPreference.Managed => TerminalRenderMode.ManagedVt,
                _ => TerminalRenderMode.RenderedAuto,
            };
        }

        if (container is Border { Child: Control child })
        {
            return ResolveModeFromContainer(child, headerButton);
        }

        throw new InvalidOperationException(
            $"Unsupported terminal host container type '{container.GetType().FullName}'.");
    }

    private static List<TerminalControl> GetStandaloneControls(Grid terminalHost)
    {
        List<TerminalControl> controls = [];
        AddStandaloneControls(terminalHost, controls);
        return controls;
    }

    private static List<Border> GetPaneContainers(Grid terminalHost)
    {
        return terminalHost.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("terminalPane"))
            .ToList();
    }

    private static int CountActivePaneContainers(Grid terminalHost)
    {
        return GetPaneContainers(terminalHost).Count(static pane => pane.Classes.Contains("activePane"));
    }

    private static TerminalControl GetVisibleStandaloneControl(Grid terminalHost)
    {
        if (TryGetVisibleStandaloneControl(terminalHost, out TerminalControl? control))
        {
            return control!;
        }

        throw new InvalidOperationException("No visible standalone terminal control was found.");
    }

    private static void AddStandaloneControls(Control control, List<TerminalControl> controls)
    {
        if (control is ScrollViewer { Content: TerminalControl wrapped })
        {
            controls.Add(wrapped);
            return;
        }

        if (control is Border { Child: Control child })
        {
            AddStandaloneControls(child, controls);
            return;
        }

        if (control is Panel panel)
        {
            for (int i = 0; i < panel.Children.Count; i++)
            {
                AddStandaloneControls(panel.Children[i], controls);
            }
        }
    }

    private static bool TryGetVisibleStandaloneControl(Control control, out TerminalControl? terminal)
    {
        terminal = null;
        if (!control.IsVisible)
        {
            return false;
        }

        if (control is ScrollViewer { Content: TerminalControl wrapped })
        {
            terminal = wrapped;
            return true;
        }

        if (control is Border { Child: Control child })
        {
            return TryGetVisibleStandaloneControl(child, out terminal);
        }

        if (control is Panel panel)
        {
            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (TryGetVisibleStandaloneControl(panel.Children[i], out terminal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void RaiseTabStripPointerWheel(Control target, Window window, Vector delta)
    {
        Pointer pointer = new(id: 6, PointerType.Mouse, isPrimary: true);
        ulong timestamp = (ulong)Environment.TickCount64;
        Point localPoint = new(
            Math.Max(1d, target.Bounds.Width / 2d),
            Math.Max(1d, target.Bounds.Height / 2d));
        Point windowPoint = target.TranslatePoint(localPoint, window) ?? localPoint;

        PointerWheelEventArgs wheel = new(
            target,
            pointer,
            window,
            windowPoint,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            delta);
        target.RaiseEvent(wheel);
    }

    private static ScrollContentPresenter FindTabStripScrollContentPresenter(ScrollViewer scrollViewer)
    {
        scrollViewer.ApplyTemplate();
        foreach (object descendant in scrollViewer.GetVisualDescendants())
        {
            if (descendant is ScrollContentPresenter presenter)
            {
                return presenter;
            }
        }

        throw new InvalidOperationException("Tab strip scroll content presenter was not found.");
    }

    private static bool ViewportContainsHyperlink(TerminalControl control)
    {
        Assert.NotNull(control.Screen);

        for (int row = 0; row < control.Screen!.ViewportRows; row++)
        {
            TerminalRow terminalRow = control.Screen.GetViewportRow(row);
            for (int column = 0; column < terminalRow.Columns; column++)
            {
                if (terminalRow[column].HyperlinkId > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ReadAllRows(TerminalScreen screen)
    {
        StringBuilder builder = new();
        for (int rowIndex = 0; rowIndex < screen.TotalRows; rowIndex++)
        {
            builder.AppendLine(ReadRow(screen.GetRow(rowIndex)));
        }

        return builder.ToString();
    }

    private static string ReadRow(TerminalRow row)
    {
        StringBuilder builder = new();
        int last = row.Columns - 1;
        while (last >= 0 && row[last].Codepoint == 0)
        {
            last--;
        }

        for (int column = 0; column <= last; column++)
        {
            int codepoint = row[column].Codepoint;
            builder.Append(codepoint == 0 ? ' ' : char.ConvertFromUtf32((int)codepoint));
        }

        return builder.ToString();
    }

    private static void CollectPaneIds(TerminalWorkspacePane pane, List<string> ids)
    {
        ids.Add(pane.Id);
        if (pane.Split is null)
        {
            return;
        }

        CollectPaneIds(pane.Split.FirstPane, ids);
        CollectPaneIds(pane.Split.SecondPane, ids);
    }

    private static void AssertTerminalBehaviorSettings(
        IReadOnlyList<TerminalControl> controls,
        TerminalPasteSafetyPolicy expectedPastePolicy,
        bool enableTextShaping,
        bool reflowOnResize,
        bool preserveScrollbackOnSessionStart,
        bool sixelGraphicsEnabled,
        bool enableLigatures)
    {
        for (int i = 0; i < controls.Count; i++)
        {
            TerminalControl control = controls[i];
            Assert.Equal(expectedPastePolicy, control.PasteSafetyPolicy);
            Assert.NotNull(control.Renderer);
            Assert.Equal(enableTextShaping, control.Renderer!.EnableTextShaping);
            Assert.Equal(reflowOnResize, control.ReflowOnResize);
            Assert.Equal(preserveScrollbackOnSessionStart, control.PreserveScrollbackOnSessionStart);
            Assert.Equal(sixelGraphicsEnabled, control.SixelGraphicsEnabled);
            Assert.Equal(enableLigatures, control.Renderer.EnableLigatures);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate())
            {
                return true;
            }

            await Task.Delay(25);
        }

        Dispatcher.UIThread.RunJobs();
        return predicate();
    }

    private static int CountSupportedModes(TerminalModeResolver resolver, TerminalModeCapabilities capabilities)
    {
        TerminalRenderMode[] startupModes =
        [
            TerminalRenderMode.NativeVt,
            TerminalRenderMode.ManagedVt,
            TerminalRenderMode.RenderedAuto,
        ];

        int count = 0;
        for (int i = 0; i < startupModes.Length; i++)
        {
            if (resolver.IsSupported(startupModes[i], capabilities))
            {
                count++;
            }
        }

        return Math.Max(1, count);
    }

    private static IDisposable SetProcessEnvironmentVariable(string variableName, string? value)
    {
        string? originalValue = Environment.GetEnvironmentVariable(variableName);
        Environment.SetEnvironmentVariable(variableName, value, EnvironmentVariableTarget.Process);
        return new ProcessEnvironmentVariableScope(variableName, originalValue);
    }

    private static IReadOnlyList<Button> GetTabHeaders(ItemsControl tabStrip)
    {
        tabStrip.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        return tabStrip
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(static button => button.Classes.Contains("tabHeader"))
            .ToArray();
    }

    private static Button GetTabHeader(ItemsControl tabStrip, int index)
    {
        IReadOnlyList<Button> headers = GetTabHeaders(tabStrip);
        Assert.InRange(index, 0, headers.Count - 1);
        return headers[index];
    }

    private static Button GetLastTabHeader(ItemsControl tabStrip)
    {
        IReadOnlyList<Button> headers = GetTabHeaders(tabStrip);
        Assert.NotEmpty(headers);
        return headers[^1];
    }

    private static string[] GetTabHeaderTitles(ItemsControl tabStrip)
    {
        return GetTabHeaders(tabStrip)
            .Select(GetTabHeaderTitle)
            .ToArray();
    }

    private static string GetTabHeaderTitle(Button headerButton)
    {
        if (headerButton.Content is StackPanel content &&
            content.Children.Count > 1 &&
            content.Children[1] is TextBlock titleText)
        {
            return titleText.Text ?? string.Empty;
        }

        return string.Empty;
    }

    private static IReadOnlyList<ContentPresenter> GetTabItemPresenters(ItemsControl tabStrip)
    {
        tabStrip.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        return tabStrip
            .GetVisualDescendants()
            .OfType<ContentPresenter>()
            .Where(static presenter => presenter.GetVisualDescendants()
                .OfType<Button>()
                .Any(static button => button.Classes.Contains("tabHeader")))
            .ToArray();
    }

    private static Point GetCenterPointInWindow(Control control, Window window)
    {
        Point localPoint = new(control.Bounds.Width / 2d, control.Bounds.Height / 2d);
        return control.TranslatePoint(localPoint, window) ?? localPoint;
    }

    private static Dictionary<string, Color> GetStandaloneModeIndicatorColors(ItemsControl tabStrip)
    {
        Dictionary<string, Color> colors = new(StringComparer.Ordinal);
        IReadOnlyList<Button> headers = GetTabHeaders(tabStrip);

        for (int i = 0; i < headers.Count; i++)
        {
            Button headerButton = headers[i];
            string? tip = ToolTip.GetTip(headerButton) as string;
            if (string.IsNullOrWhiteSpace(tip) || !tip.Contains(" - ", StringComparison.Ordinal))
            {
                continue;
            }

            string modeName = tip.StartsWith("Native VT", StringComparison.Ordinal)
                ? "Native VT"
                : tip.StartsWith("Managed VT", StringComparison.Ordinal)
                    ? "Managed VT"
                    : tip.StartsWith("Rendered (", StringComparison.Ordinal)
                        ? "Rendered"
                        : string.Empty;
            if (string.IsNullOrEmpty(modeName))
            {
                continue;
            }

            if (headerButton.Content is not StackPanel content
                || content.Children.Count == 0
                || content.Children[0] is not TextBlock modeIndicator
                || modeIndicator.Foreground is not SolidColorBrush brush)
            {
                continue;
            }

            colors[modeName] = brush.Color;
        }

        return colors;
    }

    private static Dictionary<string, string> GetStandaloneModeIndicatorGlyphs(ItemsControl tabStrip)
    {
        Dictionary<string, string> glyphs = new(StringComparer.Ordinal);
        IReadOnlyList<Button> headers = GetTabHeaders(tabStrip);

        for (int i = 0; i < headers.Count; i++)
        {
            Button headerButton = headers[i];
            string? tip = ToolTip.GetTip(headerButton) as string;
            if (string.IsNullOrWhiteSpace(tip) || !tip.Contains(" - ", StringComparison.Ordinal))
            {
                continue;
            }

            string modeName = tip.StartsWith("Native VT", StringComparison.Ordinal)
                ? "Native VT"
                : tip.StartsWith("Managed VT", StringComparison.Ordinal)
                    ? "Managed VT"
                    : tip.StartsWith("Rendered (", StringComparison.Ordinal)
                        ? "Rendered"
                        : string.Empty;
            if (string.IsNullOrEmpty(modeName))
            {
                continue;
            }

            if (headerButton.Content is not StackPanel content
                || content.Children.Count == 0
                || content.Children[0] is not TextBlock modeIndicator
                || string.IsNullOrEmpty(modeIndicator.Text))
            {
                continue;
            }

            glyphs[modeName] = modeIndicator.Text;
        }

        return glyphs;
    }

    private static TerminalSessionProfile CreateSnippetProfile(
        string id,
        string displayName,
        string trigger,
        string commandLine)
    {
        return new TerminalSessionProfile
        {
            Id = id,
            DisplayName = displayName,
            Transport = new TerminalSessionTransportProfile
            {
                TransportId = TerminalTransportIds.Pipe,
                Pipe = new TerminalSessionPipeSettings
                {
                    FileName = "echo",
                    Arguments = [id],
                },
            },
            CommandSnippets =
            [
                new TerminalCommandSnippet(trigger, commandLine, displayName),
            ],
        };
    }

    private static InMemoryProfileStore CreateEmptyProfileStore()
        => new(new TerminalSessionProfilesDocument());

    private static string ReadSharedText(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private sealed class FixedTerminalModeCapabilityResolver : ITerminalModeCapabilityResolver
    {
        private readonly TerminalModeCapabilities _capabilities;

        public FixedTerminalModeCapabilityResolver(TerminalModeCapabilities capabilities)
        {
            _capabilities = capabilities;
        }

        public TerminalModeCapabilities Resolve(bool nativeVtAvailable)
        {
            return _capabilities;
        }
    }

    private sealed class InMemoryProfileStore(TerminalSessionProfilesDocument document) : ITerminalSessionProfileStore
    {
        public TerminalSessionProfilesDocument Document { get; private set; } = document;

        public ValueTask<TerminalSessionProfilesDocument> LoadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Document);

        public ValueTask SaveAsync(
            TerminalSessionProfilesDocument document,
            CancellationToken cancellationToken = default)
        {
            Document = document;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InMemoryAppPreferencesStore(AppPreferencesDocument document) : IAppPreferencesStore
    {
        public AppPreferencesDocument Document { get; private set; } = document;

        public ValueTask<AppPreferencesDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Document);
        }

        public ValueTask SaveAsync(AppPreferencesDocument document, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Document = document;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProcessEnvironmentVariableScope : IDisposable
    {
        private readonly string _variableName;
        private readonly string? _originalValue;
        private bool _disposed;

        public ProcessEnvironmentVariableScope(string variableName, string? originalValue)
        {
            _variableName = variableName;
            _originalValue = originalValue;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Environment.SetEnvironmentVariable(_variableName, _originalValue, EnvironmentVariableTarget.Process);
            _disposed = true;
        }
    }
}
