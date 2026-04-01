// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — startup/fallback smoke coverage for demo controller mode routing.

using System.Reactive.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Demo.Services;
using RoyalTerminal.Demo.ViewModels;
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
            TerminalModeResolver.Default,
            skipEmbeddedGhosttyInitialization: true);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
                embeddedGhosttyAvailable: viewModel.GhosttyAvailable,
                nativeVtAvailable: viewModel.NativeVtAvailable);
            TerminalModeResolver resolver = TerminalModeResolver.Default;
            int expectedStartupTabs = CountSupportedModes(resolver, capabilities);

            bool createdExpectedTabs = await WaitUntilAsync(
                () => terminalHost.Children.Count == expectedStartupTabs,
                TimeSpan.FromSeconds(2));
            Assert.True(
                createdExpectedTabs,
                $"Expected {expectedStartupTabs} startup tabs but found {terminalHost.Children.Count}.");
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
            TerminalModeResolver.Default,
            skipEmbeddedGhosttyInitialization: true);
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

            Assert.True(standaloneModeColors.Count >= 2, "Expected at least two standalone startup modes.");
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
            skipEmbeddedGhosttyInitialization: true);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            bool initialTabCreated = await WaitUntilAsync(
                () => terminalHost.Children.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(initialTabCreated, "Controller did not create an initial tab.");

            TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
                embeddedGhosttyAvailable: viewModel.GhosttyAvailable,
                nativeVtAvailable: viewModel.NativeVtAvailable);
            TerminalModeResolver resolver = TerminalModeResolver.Default;
            StackPanel tabStrip = window.FindControl<StackPanel>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");

            TerminalRenderMode[] requestedModes =
            [
                TerminalRenderMode.GhosttyRendered,
                TerminalRenderMode.GhosttyNative,
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
                Assert.True(created, $"Controller did not create tab for requested mode '{requestedMode}'.");

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
    public async Task Controller_GhosttyRenderedCpuBackend_UsesStandaloneVtControl()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Pipe);
        viewModel.PipeCommandText = "echo rendered-cpu-native-vt";

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new FixedTerminalModeCapabilityResolver(TerminalModeCapabilities.Create(
                embeddedGhosttyAvailable: true,
                nativeVtAvailable: true)),
            TerminalModeResolver.Default,
            skipEmbeddedGhosttyInitialization: true);
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
                useNativeControl: false,
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
            VtProcessorPreference expectedPreference = viewModel.NativeVtAvailable
                ? VtProcessorPreference.Native
                : VtProcessorPreference.Managed;
            Assert.Equal(expectedPreference, standalone.VtProcessorPreference);

            StackPanel tabStrip = window.FindControl<StackPanel>("TabStrip")
                ?? throw new InvalidOperationException("TabStrip was not found.");
            Button headerButton = Assert.IsType<Button>(tabStrip.Children[^1]);
            Assert.Equal(
                "Rendered (Ghostty VT + CPU Cell Renderer)",
                ToolTip.GetTip(headerButton) as string);
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
            TerminalModeResolver.Default,
            skipEmbeddedGhosttyInitialization: true);
        IDisposable? lifetime = null;

        try
        {
            lifetime = controller.Activate();

            TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
                embeddedGhosttyAvailable: viewModel.GhosttyAvailable,
                nativeVtAvailable: viewModel.NativeVtAvailable);
            TerminalModeResolver resolver = TerminalModeResolver.Default;

            TerminalRenderMode currentMode = GetActiveMode(viewModel);

            for (int i = 0; i < 10; i++)
            {
                TerminalRenderMode expected = resolver.ResolveNextMode(currentMode, capabilities);
                viewModel.CycleRenderModeCommand.Execute().Wait();

                TerminalRenderMode actual = GetActiveMode(viewModel);
                Assert.Equal(expected, actual);
                Assert.True(
                    resolver.IsSupported(actual, capabilities),
                    $"Mode cycle produced unsupported mode '{actual}' for capabilities {capabilities}.");

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
        viewModel.EnableLigatures = true;

        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            skipEmbeddedGhosttyInitialization: true);
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
            AssertTerminalBehaviorSettings(controls, TerminalPasteSafetyPolicy.BlockUnsafe, enableTextShaping: false, enableLigatures: true);

            viewModel.SelectedPasteSafetyPolicy = TerminalPasteSafetyPolicy.SanitizeControlSequences;
            viewModel.EnableTextShaping = true;
            viewModel.EnableLigatures = false;
            Dispatcher.UIThread.RunJobs();

            AssertTerminalBehaviorSettings(controls, TerminalPasteSafetyPolicy.SanitizeControlSequences, enableTextShaping: true, enableLigatures: false);
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
            skipEmbeddedGhosttyInitialization: true);
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
            useRenderedControl: requestedMode == TerminalRenderMode.GhosttyRendered,
            useNativeControl: requestedMode == TerminalRenderMode.GhosttyNative,
            useNativeVtControl: requestedMode == TerminalRenderMode.NativeVt,
            useManagedVtControl: requestedMode == TerminalRenderMode.ManagedVt);
    }

    private static TerminalRenderMode GetActiveMode(MainWindowViewModel viewModel)
    {
        if (viewModel.UseRenderedControl)
        {
            return TerminalRenderMode.GhosttyRendered;
        }

        if (viewModel.UseNativeControl)
        {
            return TerminalRenderMode.GhosttyNative;
        }

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
        if (container is GhosttyRenderedTerminalControl)
        {
            return TerminalRenderMode.GhosttyRendered;
        }

        if (container is GhosttyNativeTerminalControl)
        {
            return TerminalRenderMode.GhosttyNative;
        }

        if (container is ScrollViewer scrollViewer && scrollViewer.Content is TerminalControl standalone)
        {
            if (IsGhosttyRenderedHeader(headerButton))
            {
                return TerminalRenderMode.GhosttyRendered;
            }

            return standalone.VtProcessorPreference switch
            {
                VtProcessorPreference.Native => TerminalRenderMode.NativeVt,
                VtProcessorPreference.Managed => TerminalRenderMode.ManagedVt,
                _ => TerminalRenderMode.RenderedAuto,
            };
        }

        if (container is TerminalControl directStandalone)
        {
            if (IsGhosttyRenderedHeader(headerButton))
            {
                return TerminalRenderMode.GhosttyRendered;
            }

            return directStandalone.VtProcessorPreference switch
            {
                VtProcessorPreference.Native => TerminalRenderMode.NativeVt,
                VtProcessorPreference.Managed => TerminalRenderMode.ManagedVt,
                _ => TerminalRenderMode.RenderedAuto,
            };
        }

        throw new InvalidOperationException(
            $"Unsupported terminal host container type '{container.GetType().FullName}'.");
    }

    private static bool IsGhosttyRenderedHeader(Button? headerButton)
    {
        string? tip = headerButton is null
            ? null
            : ToolTip.GetTip(headerButton) as string;
        return !string.IsNullOrWhiteSpace(tip)
            && tip.StartsWith("Rendered (Ghostty VT + ", StringComparison.Ordinal);
    }

    private static List<TerminalControl> GetStandaloneControls(Grid terminalHost)
    {
        List<TerminalControl> controls = [];
        for (int i = 0; i < terminalHost.Children.Count; i++)
        {
            switch (terminalHost.Children[i])
            {
                case TerminalControl direct:
                    controls.Add(direct);
                    break;
                case ScrollViewer { Content: TerminalControl wrapped }:
                    controls.Add(wrapped);
                    break;
            }
        }

        return controls;
    }

    private static void AssertTerminalBehaviorSettings(
        IReadOnlyList<TerminalControl> controls,
        TerminalPasteSafetyPolicy expectedPastePolicy,
        bool enableTextShaping,
        bool enableLigatures)
    {
        for (int i = 0; i < controls.Count; i++)
        {
            TerminalControl control = controls[i];
            Assert.Equal(expectedPastePolicy, control.PasteSafetyPolicy);
            Assert.NotNull(control.Renderer);
            Assert.Equal(enableTextShaping, control.Renderer!.EnableTextShaping);
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
            TerminalRenderMode.GhosttyRendered,
            TerminalRenderMode.GhosttyNative,
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
                        ? "Rendered (Auto VT)"
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
                        ? "Rendered (Auto VT)"
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

        public TerminalModeCapabilities Resolve(bool embeddedGhosttyAvailable, bool nativeVtAvailable)
        {
            return _capabilities;
        }
    }
}
