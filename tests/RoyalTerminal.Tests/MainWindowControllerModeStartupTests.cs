// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — startup/fallback smoke coverage for demo controller mode routing.

using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Demo.Services;
using RoyalTerminal.Demo.ViewModels;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class MainWindowControllerModeStartupTests
{
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
                TerminalRenderMode actualMode = ResolveModeFromContainer(newContainer);
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

    private static TerminalRenderMode ResolveModeFromContainer(Control container)
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
            return standalone.VtProcessorPreference switch
            {
                VtProcessorPreference.Native => TerminalRenderMode.NativeVt,
                VtProcessorPreference.Managed => TerminalRenderMode.ManagedVt,
                _ => TerminalRenderMode.RenderedAuto,
            };
        }

        if (container is TerminalControl directStandalone)
        {
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
}
