// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — startup/fallback smoke coverage for demo controller mode routing.

using System.Reactive.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Demo.Services;
using RoyalTerminal.Demo.ViewModels;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("MainWindowControllerHeadlessTests")]
public sealed class MainWindowControllerModeStartupTests
{
    [AvaloniaFact]
    public async Task Controller_Startup_CreatesTabsForEachSupportedMode()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup-modes";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default);
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
    public async Task Controller_Startup_StandaloneModeIndicators_UseDistinctColors()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo startup-mode-indicators";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            StackPanel tabStrip = window.FindControl<StackPanel>("TabStrip")
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
            TerminalModeResolver.Default);
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
            StackPanel tabStrip = window.FindControl<StackPanel>("TabStrip")
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
                viewModel.NewTabCommand.Execute().Wait();

                bool created = await WaitUntilAsync(
                    () => terminalHost.Children.Count > countBefore,
                    TimeSpan.FromSeconds(2));
                Assert.True(created);

                Control newContainer = terminalHost.Children[^1];
                Button newHeader = Assert.IsType<Button>(tabStrip.Children[^1]);
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
            TerminalModeResolver.Default);
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
            viewModel.NewTabCommand.Execute().Wait();

            bool created = await WaitUntilAsync(
                () => terminalHost.Children.Count == countBefore + 1,
                TimeSpan.FromSeconds(2));
            Assert.True(created);

            Control newContainer = terminalHost.Children[^1];
            ScrollViewer scrollViewer = Assert.IsType<ScrollViewer>(newContainer);
            TerminalControl standalone = Assert.IsType<TerminalControl>(scrollViewer.Content);
            Assert.Equal(VtProcessorPreference.Auto, standalone.VtProcessorPreference);

            StackPanel tabStrip = window.FindControl<StackPanel>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            Button headerButton = Assert.IsType<Button>(tabStrip.Children[^1]);
            string expectedVtLabel = standalone.IsUsingNativeVtProcessor ? "Ghostty VT" : "Basic VT";
            Assert.Equal($"Rendered (Pipe - {expectedVtLabel})", ToolTip.GetTip(headerButton) as string);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Controller_RuntimeCapabilities_KeepModeCycleStable()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo mode-cycle-smoke";

        Window window = CreateControllerHostWindow(viewModel, out _);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default);
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
                viewModel.CycleRenderModeCommand.Execute().Wait();

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
            TerminalModeResolver.Default);
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
                enableTextShaping: false,
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
            TerminalModeResolver.Default);
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
            TerminalModeResolver.Default);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            int managedTabIndex = viewModel.NativeVtAvailable ? 1 : 0;
            viewModel.SwitchToTabByIndexCommand.Execute(managedTabIndex).Wait();
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

            viewModel.ClearActiveScrollbackCommand.Execute().Wait();
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
            TerminalModeResolver.Default);
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
            viewModel.ApplySearchCommand.Execute().Wait();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("alpha", activeControl.SearchNeedle);
            Assert.Equal(2, activeControl.SearchTotal);
            Assert.Contains("/2 matches", viewModel.SearchResultText, StringComparison.Ordinal);

            viewModel.NextSearchCommand.Execute().Wait();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, activeControl.SearchSelected);

            viewModel.ClearSearchCommand.Execute().Wait();
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
            TerminalModeResolver.Default);
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

            viewModel.CopyPlainSnapshotCommand.Execute().Wait();
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
            TerminalModeResolver.Default);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool startupTabsCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(startupTabsCreated);

            viewModel.ToggleGhosttyDiagnosticsCommand.Execute().Wait();
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.ShowGhosttyDiagnostics);
            Assert.Contains("libghostty-vt available:", viewModel.GhosttyDiagnosticsText, StringComparison.Ordinal);

            viewModel.ShowHyperlinkSampleCommand.Execute().Wait();
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
            TerminalModeResolver.Default);
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
            viewModel.NewTabCommand.Execute().Wait();
            bool newTabCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count == countBefore + 1,
                TimeSpan.FromSeconds(2));
            Assert.True(newTabCreated);

            TerminalControl activeControl = GetVisibleStandaloneControl(terminalHost);
            Assert.True(activeControl.IsUsingNativeVtProcessor);

            viewModel.ShowKittyGraphicsSampleCommand.Execute().Wait();
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
        StackPanel tabStrip = new()
        {
            Name = "TabStrip",
        };

        terminalHost = new Grid
        {
            Name = "TerminalHost",
        };

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.Children.Add(tabStrip);
        root.Children.Add(terminalHost);
        Grid.SetRow(tabStrip, 0);
        Grid.SetRow(terminalHost, 1);

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
        nameScope.Register(terminalHost.Name!, terminalHost);

        window.Show();
        window.Focus();
        return window;
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

        throw new InvalidOperationException(
            $"Unsupported terminal host container type '{container.GetType().FullName}'.");
    }

    private static List<TerminalControl> GetStandaloneControls(Grid terminalHost)
    {
        List<TerminalControl> controls = [];
        for (int i = 0; i < terminalHost.Children.Count; i++)
        {
            if (terminalHost.Children[i] is ScrollViewer { Content: TerminalControl wrapped })
            {
                controls.Add(wrapped);
            }
        }

        return controls;
    }

    private static TerminalControl GetVisibleStandaloneControl(Grid terminalHost)
    {
        for (int i = 0; i < terminalHost.Children.Count; i++)
        {
            if (terminalHost.Children[i] is ScrollViewer
                {
                    IsVisible: true,
                    Content: TerminalControl wrapped,
                })
            {
                return wrapped;
            }
        }

        throw new InvalidOperationException("No visible standalone terminal control was found.");
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

    private static Dictionary<string, Color> GetStandaloneModeIndicatorColors(StackPanel tabStrip)
    {
        Dictionary<string, Color> colors = new(StringComparer.Ordinal);

        for (int i = 0; i < tabStrip.Children.Count; i++)
        {
            if (tabStrip.Children[i] is not Button headerButton)
            {
                continue;
            }

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

    private static Dictionary<string, string> GetStandaloneModeIndicatorGlyphs(StackPanel tabStrip)
    {
        Dictionary<string, string> glyphs = new(StringComparer.Ordinal);

        for (int i = 0; i < tabStrip.Children.Count; i++)
        {
            if (tabStrip.Children[i] is not Button headerButton)
            {
                continue;
            }

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
}
