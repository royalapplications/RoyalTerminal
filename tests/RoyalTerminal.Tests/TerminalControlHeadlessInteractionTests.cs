// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Headless interaction tests for TerminalControl.

using System.Text;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalControlHeadlessInteractionTests
{
    [AvaloniaFact]
    public async Task Headless_WindowResize_UpdatesGrid_AndPropagatesToTransport()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            Dispatcher.UIThread.RunJobs();

            int initialCols = control.Columns;
            int initialRows = control.Rows;
            int resizeCountBefore = transport.Resizes.Count;

            window.Width = 960;
            window.Height = 640;
            Dispatcher.UIThread.RunJobs();

            bool gridChanged = await WaitUntilAsync(
                () => control.Columns != initialCols || control.Rows != initialRows,
                TimeSpan.FromSeconds(2));
            Assert.True(gridChanged);

            bool resizeRecorded = await WaitUntilAsync(
                () => transport.Resizes.Count > resizeCountBefore,
                TimeSpan.FromSeconds(2));
            Assert.True(resizeRecorded);

            TerminalSessionDimensions lastResize = transport.Resizes[^1];
            Assert.Equal(control.Columns, lastResize.Columns);
            Assert.Equal(control.Rows, lastResize.Rows);
            Assert.True(lastResize.WidthPixels > 0);
            Assert.True(lastResize.HeightPixels > 0);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardInput_SendsToTransportFallback()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            transport.ClearInputs();

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            bool enterSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == (byte)'\r'),
                TimeSpan.FromSeconds(2));
            Assert.True(enterSent);

            transport.Inputs.Clear();
            window.KeyTextInput("x");

            bool textSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == (byte)'x'),
                TimeSpan.FromSeconds(2));
            Assert.True(textSent);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardInterrupt_UnderOutputFlood_SendsCtrlCImmediately()
    {
        await VerifyControlCharacterDispatchUnderOutputFloodAsync(
            physicalKey: PhysicalKey.C,
            expectedByte: 0x03,
            controlCharacterLabel: "Ctrl+C");
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardSuspend_UnderOutputFlood_SendsCtrlZImmediately()
    {
        await VerifyControlCharacterDispatchUnderOutputFloodAsync(
            physicalKey: PhysicalKey.Z,
            expectedByte: 0x1A,
            controlCharacterLabel: "Ctrl+Z");
    }

    [AvaloniaFact]
    public async Task Headless_ManagedPty_CtrlC_InterruptsFloodedShellWithinLatencyBudget()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        const string readyMarker = "__ROYALTERMINAL_CTRL_READY__";
        const string interruptedMarker = "__ROYALTERMINAL_CTRL_INTERRUPTED__";
        const string floodNeedle = "Build step";

        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };
        Window window = new()
        {
            Width = 960,
            Height = 640,
            Content = control,
        };

        object outputSync = new();
        StringBuilder output = new();
        control.DataReceived += (_, args) =>
        {
            lock (outputSync)
            {
                output.Append(Encoding.UTF8.GetString(args.Data.Span));
                if (output.Length > 131072)
                {
                    output.Remove(0, output.Length - 65536);
                }
            }
        };

        window.Show();
        try
        {
            await StabilizeWindowAsync(window, control);
            control.StartPty(shell: "/bin/sh", workingDirectory: Environment.CurrentDirectory);

            control.SendInput($"echo {readyMarker}\n");
            bool readySeen = await WaitUntilAsync(
                () => ContainsOutput(outputSync, output, readyMarker),
                TimeSpan.FromSeconds(5));
            Assert.True(readySeen, $"Did not observe PTY ready marker. Output: {SnapshotOutput(outputSync, output)}");

            control.SendInput($"trap 'echo {interruptedMarker}' INT\n");
            control.SendInput(
                "i=1; while [ $i -le 200000 ]; do " +
                "printf '\\033[32mINFO\\033[0m Build step %d completed\\n' \"$i\"; " +
                "printf '\\033[33mWARN\\033[0m Something suspicious\\n'; " +
                "printf '\\033[31mERROR\\033[0m Something failed\\n'; " +
                "i=$((i+1)); " +
                "done\n");

            bool floodSeen = await WaitUntilAsync(
                () => ContainsOutput(outputSync, output, floodNeedle),
                TimeSpan.FromSeconds(5));
            Assert.True(floodSeen, $"Did not observe flood output before interrupt. Output: {SnapshotOutput(outputSync, output)}");

            Stopwatch interruptLatency = Stopwatch.StartNew();
            control.SendInput(new byte[] { 0x03 });

            bool interrupted = await WaitUntilAsync(
                () => ContainsOutput(outputSync, output, interruptedMarker),
                TimeSpan.FromSeconds(5));
            Assert.True(interrupted, $"Did not observe interrupt marker. Output: {SnapshotOutput(outputSync, output)}");
            Assert.True(
                interruptLatency.Elapsed < TimeSpan.FromSeconds(2),
                $"Expected managed PTY Ctrl+C interrupt to be prompt under flood. Latency={interruptLatency.Elapsed}.");
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardInput_UsesAttachedEndpointSink()
    {
        RecordingEndpoint endpoint = new();
        TerminalControl control = new();
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            control.AttachEndpoint(endpoint);
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            bool keySent = await WaitUntilAsync(
                () => endpoint.KeyEvents.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(keySent);
            Assert.Contains(endpoint.KeyEvents, static key => key.KeyCode == (uint)Key.Return);

            window.KeyTextInput("z");
            bool textSent = await WaitUntilAsync(
                () => endpoint.TextInputs.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(textSent);
            Assert.Contains("z", endpoint.TextInputs);
        }
        finally
        {
            window.Close();
            control.DetachEndpoint();
        }
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardInput_FallsBackToAttachedEndpointTextPath_WhenNoInputSink()
    {
        RecordingTextEndpoint endpoint = new();
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            control.AttachEndpoint(endpoint);
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            endpoint.Inputs.Clear();

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            bool enterSent = await WaitUntilAsync(
                () => endpoint.Inputs.Any(static input => input.Length == 1 && input[0] == (byte)'\r'),
                TimeSpan.FromSeconds(2));
            Assert.True(enterSent);

            endpoint.Inputs.Clear();
            window.KeyTextInput("x");

            bool textSent = await WaitUntilAsync(
                () => endpoint.Inputs.Any(static input => input.Length == 1 && input[0] == (byte)'x'),
                TimeSpan.FromSeconds(2));
            Assert.True(textSent);
        }
        finally
        {
            window.Close();
            control.DetachEndpoint();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_UsesAttachedEndpointSink()
    {
        RecordingEndpoint endpoint = new();
        TerminalControl control = new()
        {
            Width = 640,
            Height = 400,
        };
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            control.AttachEndpoint(endpoint);
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            Point point = await GetInteractionPointAsync(control, window);

            RaiseMouseSequence(control, window, point);
            Dispatcher.UIThread.RunJobs();

            bool pointerSent = await WaitUntilAsync(
                () => endpoint.PointerEvents.Count >= 3,
                TimeSpan.FromSeconds(2));
            Assert.True(pointerSent);

            Assert.Contains(endpoint.PointerEvents, static evt =>
                evt.Kind == TerminalPointerEventKind.Button && evt.Action == TerminalInputAction.Press);
            Assert.Contains(endpoint.PointerEvents, static evt =>
                evt.Kind == TerminalPointerEventKind.Button && evt.Action == TerminalInputAction.Release);
            Assert.Contains(endpoint.PointerEvents, static evt =>
                evt.Kind == TerminalPointerEventKind.Scroll);
        }
        finally
        {
            window.Close();
            control.DetachEndpoint();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_EncodesToAttachedEndpointTextPath_WhenNoInputSinkAndMouseModeEnabled()
    {
        RecordingTextEndpoint endpoint = new();
        TerminalControl control = new()
        {
            Width = 640,
            Height = 400,
            VtProcessorPreference = VtProcessorPreference.Managed,
        };
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            control.AttachEndpoint(endpoint);
            control.WriteOutput("\x1b[?1002h\x1b[?1006h"u8);
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            endpoint.Inputs.Clear();

            Point point = await GetInteractionPointAsync(control, window);
            RaiseMouseSequence(control, window, point);
            Dispatcher.UIThread.RunJobs();

            bool vtMouseSent = await WaitUntilAsync(
                () => endpoint.Inputs.Any(IsMouseProtocolInput),
                TimeSpan.FromSeconds(2));
            Assert.True(vtMouseSent);
        }
        finally
        {
            window.Close();
            control.DetachEndpoint();
        }
    }

    [AvaloniaFact]
    public async Task Headless_FocusEvents_EncodeToAttachedEndpointTextPath_WhenNoInputSinkAndMode1004Enabled()
    {
        RecordingTextEndpoint endpoint = new();
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };
        Button other = new();
        StackPanel root = new()
        {
            Children =
            {
                control,
                other,
            },
        };
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = root,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            control.AttachEndpoint(endpoint);
            control.WriteOutput("\x1b[?1004h"u8);
            Dispatcher.UIThread.RunJobs();

            other.Focus();
            Dispatcher.UIThread.RunJobs();
            endpoint.Inputs.Clear();

            control.Focus();
            Dispatcher.UIThread.RunJobs();
            other.Focus();
            Dispatcher.UIThread.RunJobs();

            bool focusInSent = await WaitUntilAsync(
                () => endpoint.Inputs.Any(static input => Encoding.UTF8.GetString(input) == "\x1b[I"),
                TimeSpan.FromSeconds(2));
            bool focusOutSent = await WaitUntilAsync(
                () => endpoint.Inputs.Any(static input => Encoding.UTF8.GetString(input) == "\x1b[O"),
                TimeSpan.FromSeconds(2));

            Assert.True(focusInSent);
            Assert.True(focusOutSent);
        }
        finally
        {
            window.Close();
            control.DetachEndpoint();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_EncodesToTransport_WhenMouseModeEnabled()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\x1b[?1002h\x1b[?1006h"u8);
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            Point point = await GetInteractionPointAsync(control, window);
            RaiseMouseSequence(control, window, point);
            Dispatcher.UIThread.RunJobs();

            bool vtMouseSent = await WaitUntilAsync(
                () => transport.Inputs.Any(IsMouseProtocolInput),
                TimeSpan.FromSeconds(2));
            Assert.True(vtMouseSent);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_FromTopLevelRouting_EncodesToTransport_WhenMouseModeEnabled()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\x1b[?1002h\x1b[?1006h"u8);
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            int pointerPressedCount = 0;
            control.AddHandler(
                InputElement.PointerPressedEvent,
                (_, _) => pointerPressedCount++,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);

            Point point = await GetInteractionPointAsync(control, window);
            SendMouseSequenceViaTopLevel(window, point);
            Dispatcher.UIThread.RunJobs();

            bool vtMouseSent = await WaitUntilAsync(
                () => transport.Inputs.Any(IsMouseProtocolInput),
                TimeSpan.FromSeconds(2));
            Assert.True(vtMouseSent);
            Assert.True(pointerPressedCount > 0, "Top-level pointer input did not route to TerminalControl.");
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_DoesNotEncodeToTransport_WhenMouseModeDisabled()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            Point point = await GetInteractionPointAsync(control, window);
            RaiseMouseSequence(control, window, point);
            Dispatcher.UIThread.RunJobs();

            // No mouse mode means no VT mouse encoding should be written to transport.
            Assert.DoesNotContain(transport.Inputs, IsMouseProtocolInput);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_EncodesWithPrimaryPointerFallback_WhenButtonMetadataMissing()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\x1b[?1002h\x1b[?1006h"u8);
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            Point point = await GetInteractionPointAsync(control, window);
            RaisePrimaryPointerSequenceWithoutButtonMetadata(control, window, point);
            Dispatcher.UIThread.RunJobs();

            bool vtMouseSent = await WaitUntilAsync(
                () => transport.Inputs.Any(IsMouseProtocolInput),
                TimeSpan.FromSeconds(2));
            Assert.True(vtMouseSent);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_EncodesFromHandledEvents_WhenMouseModeEnabled()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\x1b[?1000h"u8);
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            Point point = await GetInteractionPointAsync(control, window);
            RaiseHandledMouseButtonSequence(control, window, point);
            Dispatcher.UIThread.RunJobs();

            bool vtMouseSent = await WaitUntilAsync(
                () => transport.Inputs.Any(IsMouseProtocolInput),
                TimeSpan.FromSeconds(2));
            Assert.True(vtMouseSent);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_Osc8Hover_AutoResolvesHoveredLinkUrl_WithoutCallerSetter()
    {
        const string url = "https://example.com/parity";
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            preference: VtProcessorPreference.Managed);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);

            control.WriteOutput(Encoding.UTF8.GetBytes($"\x1b]8;;{url}\x1b\\LINK\x1b]8;;\x1b\\ plain"));
            Dispatcher.UIThread.RunJobs();

            Point linkPoint = await GetCellInteractionPointAsync(control, window, column: 1, row: 0);
            RaisePointerMove(control, window, linkPoint);
            bool hovered = await WaitUntilAsync(
                () => string.Equals(control.HoveredLinkUrl, url, StringComparison.Ordinal),
                TimeSpan.FromSeconds(2));
            Assert.True(hovered);

            Point plainPoint = await GetCellInteractionPointAsync(control, window, column: 6, row: 0);
            RaisePointerMove(control, window, plainPoint);
            bool cleared = await WaitUntilAsync(
                () => control.HoveredLinkUrl is null,
                TimeSpan.FromSeconds(2));
            Assert.True(cleared);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_PlainUrlHover_AutoResolvesHoveredLinkUrl_InManagedMode()
    {
        const string url = "https://github.com/login/device";
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            preference: VtProcessorPreference.Managed);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);

            control.WriteOutput(Encoding.UTF8.GetBytes($"open {url} now"));
            Dispatcher.UIThread.RunJobs();

            Point linkPoint = await GetCellInteractionPointAsync(control, window, column: 8, row: 0);
            RaisePointerMove(control, window, linkPoint);
            bool hovered = await WaitUntilAsync(
                () => string.Equals(control.HoveredLinkUrl, url, StringComparison.Ordinal),
                TimeSpan.FromSeconds(2));
            Assert.True(hovered);

            Point plainPoint = await GetCellInteractionPointAsync(control, window, column: 1, row: 0);
            RaisePointerMove(control, window, plainPoint);
            bool cleared = await WaitUntilAsync(
                () => control.HoveredLinkUrl is null,
                TimeSpan.FromSeconds(2));
            Assert.True(cleared);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_ModifiedClick_ActivatesHoveredHyperlink_InManagedMode()
    {
        const string url = "https://example.com/activate";
        LinkActivationProbeTerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
            Width = 640,
            Height = 400,
        };
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);

            control.WriteOutput(Encoding.UTF8.GetBytes($"open {url} now"));
            Dispatcher.UIThread.RunJobs();

            Point linkPoint = await GetCellInteractionPointAsync(control, window, column: 8, row: 0);
            RaisePointerMove(control, window, linkPoint);

            RaiseModifiedLeftClick(control, window, linkPoint, KeyModifiers.Control);
            bool ctrlActivated = await WaitUntilAsync(
                () => control.ActivatedLinks.Any(static uri => uri.AbsoluteUri == url),
                TimeSpan.FromSeconds(2));
            Assert.True(ctrlActivated);

            control.ActivatedLinks.Clear();
            RaiseModifiedLeftClick(control, window, linkPoint, KeyModifiers.Meta);
            bool metaActivated = await WaitUntilAsync(
                () => control.ActivatedLinks.Any(static uri => uri.AbsoluteUri == url),
                TimeSpan.FromSeconds(2));
            Assert.True(metaActivated);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_FallbackParity_AutoAndManaged_PreserveKeyboardMousePasteAndResize()
    {
        DefaultVtProcessorFactory vtFactory = new();
        InteractionCoverageResult autoResult = await RunInteractionCoverageScenarioAsync(
            vtFactory,
            VtProcessorPreference.Auto);
        InteractionCoverageResult managedResult = await RunInteractionCoverageScenarioAsync(
            vtFactory,
            VtProcessorPreference.Managed);

        AssertInteractionCoverage(autoResult);
        AssertInteractionCoverage(managedResult);
    }

    [AvaloniaFact]
    public async Task Headless_FallbackParity_AutoAndManaged_PreserveKeyboardMousePasteAndResize_WhenHostedInScrollViewer()
    {
        DefaultVtProcessorFactory vtFactory = new();
        InteractionCoverageResult autoResult = await RunInteractionCoverageScenarioAsync(
            vtFactory,
            VtProcessorPreference.Auto,
            hostInScrollViewer: true,
            useTopLevelMouseRouting: true);
        InteractionCoverageResult managedResult = await RunInteractionCoverageScenarioAsync(
            vtFactory,
            VtProcessorPreference.Managed,
            hostInScrollViewer: true,
            useTopLevelMouseRouting: true);

        AssertInteractionCoverage(autoResult);
        AssertInteractionCoverage(managedResult);
    }

    [AvaloniaFact]
    public async Task Headless_FallbackParity_NativeAndManaged_PreserveKeyboardMousePasteAndResize_WhenNativeAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        INativeVtProcessorProvider[] nativeProviders =
        [
            new GhosttyVtProcessorProvider(),
        ];
        DefaultVtProcessorFactory vtFactory = new(nativeProviders);

        InteractionCoverageResult nativeResult = await RunInteractionCoverageScenarioAsync(
            vtFactory,
            VtProcessorPreference.Native);
        InteractionCoverageResult managedResult = await RunInteractionCoverageScenarioAsync(
            vtFactory,
            VtProcessorPreference.Managed);

        AssertInteractionCoverage(nativeResult);
        AssertInteractionCoverage(managedResult);
    }

    private static TerminalControl CreateControlWithTransport(
        RecordingTransport transport,
        IVtProcessorFactory? vtProcessorFactory = null,
        VtProcessorPreference preference = VtProcessorPreference.Auto)
    {
        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new RecordingTransportProvider(transport),
            });

        TerminalControl control = new(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            vtProcessorFactory ?? new DefaultVtProcessorFactory(),
            new DefaultPtyFactory(),
            new NullSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            factory)
        {
            Background = Brushes.Transparent,
        };

        control.VtProcessorPreference = preference;
        return control;
    }

    private static async Task<InteractionCoverageResult> RunInteractionCoverageScenarioAsync(
        IVtProcessorFactory vtProcessorFactory,
        VtProcessorPreference preference,
        bool hostInScrollViewer = false,
        bool useTopLevelMouseRouting = false)
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport, vtProcessorFactory, preference);
        Control content = hostInScrollViewer
            ? new ScrollViewer
            {
                Content = control,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            }
            : control;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = content,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            bool keyboardSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == (byte)'\r'),
                TimeSpan.FromSeconds(2));

            transport.Inputs.Clear();
            window.KeyTextInput("p");
            bool textSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == (byte)'p'),
                TimeSpan.FromSeconds(2));

            transport.Inputs.Clear();
            control.WriteOutput("\x1b[?1002h\x1b[?1006h"u8);
            Dispatcher.UIThread.RunJobs();
            Point point = await GetInteractionPointAsync(control, window);
            if (useTopLevelMouseRouting)
            {
                SendMouseSequenceViaTopLevel(window, point);
            }
            else
            {
                RaiseMouseSequence(control, window, point);
            }
            Dispatcher.UIThread.RunJobs();
            bool mouseSent = await WaitUntilAsync(
                () => transport.Inputs.Any(IsMouseProtocolInput),
                TimeSpan.FromSeconds(2));

            transport.Inputs.Clear();
            control.WriteOutput("\x1b[?2004h"u8);
            await window.Clipboard!.SetTextAsync("echo parity");
            await control.PasteAsync();
            bool pasteSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static payload =>
                    Encoding.UTF8.GetString(payload) == "\x1b[200~echo parity\x1b[201~"),
                TimeSpan.FromSeconds(2));

            int initialCols = control.Columns;
            int initialRows = control.Rows;
            int resizeCountBefore = transport.Resizes.Count;
            window.Width = 960;
            window.Height = 640;
            Dispatcher.UIThread.RunJobs();

            bool resizeSent = await WaitUntilAsync(
                () => transport.Resizes.Count > resizeCountBefore
                      && (control.Columns != initialCols || control.Rows != initialRows),
                TimeSpan.FromSeconds(2));

            return new InteractionCoverageResult(
                KeyboardSent: keyboardSent,
                TextSent: textSent,
                MouseSent: mouseSent,
                PasteSent: pasteSent,
                ResizeSent: resizeSent);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    private static void AssertInteractionCoverage(InteractionCoverageResult result)
    {
        Assert.True(result.KeyboardSent);
        Assert.True(result.TextSent);
        Assert.True(result.MouseSent);
        Assert.True(result.PasteSent);
        Assert.True(result.ResizeSent);
    }

    private static bool IsMouseProtocolInput(byte[] input)
    {
        if (input.Length < 3 || input[0] != 0x1B || input[1] != (byte)'[')
        {
            return false;
        }

        string text = Encoding.ASCII.GetString(input);
        return text.StartsWith("\x1b[<", StringComparison.Ordinal) ||
               text.StartsWith("\x1b[M", StringComparison.Ordinal);
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

    private static async Task VerifyControlCharacterDispatchUnderOutputFloodAsync(
        PhysicalKey physicalKey,
        byte expectedByte,
        string controlCharacterLabel)
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            transport.ClearInputs();

            byte[] floodChunk = Encoding.UTF8.GetBytes(
                "\u001b[32mINFO\u001b[0m Build step completed\n" +
                "\u001b[33mWARN\u001b[0m Something suspicious\n" +
                "\u001b[31mERROR\u001b[0m Something failed\n");

            Task floodProducer = Task.Run(() =>
            {
                for (int i = 0; i < 20_000; i++)
                {
                    transport.RaiseData(floodChunk);
                }
            });

            try
            {
                Stopwatch keyDispatchLatency = Stopwatch.StartNew();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    KeyEventArgs keyDown = new()
                    {
                        RoutedEvent = InputElement.KeyDownEvent,
                        Source = control,
                        Key = MapPhysicalKey(physicalKey),
                        KeyModifiers = KeyModifiers.Control,
                    };
                    control.RaiseEvent(keyDown);
                }, DispatcherPriority.Input);
                keyDispatchLatency.Stop();
                bool immediateMatch = transport.HasInput(input => input.Length == 1 && input[0] == expectedByte);

                Assert.True(
                    immediateMatch,
                    $"Expected {controlCharacterLabel} ({expectedByte}) to be sent synchronously while output flood is active. keyDispatch={keyDispatchLatency.Elapsed}.");
            }
            finally
            {
                _ = await Task.WhenAny(floodProducer, Task.Delay(TimeSpan.FromSeconds(2)));
            }
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    private static Key MapPhysicalKey(PhysicalKey physicalKey)
    {
        return physicalKey switch
        {
            PhysicalKey.C => Key.C,
            PhysicalKey.Z => Key.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(physicalKey), physicalKey, "Only C and Z are supported in flood interrupt tests."),
        };
    }

    private static bool ContainsOutput(object sync, StringBuilder output, string needle)
    {
        lock (sync)
        {
            return output.ToString().Contains(needle, StringComparison.Ordinal);
        }
    }

    private static string SnapshotOutput(object sync, StringBuilder output)
    {
        lock (sync)
        {
            return output.ToString();
        }
    }

    private static async Task StabilizeWindowAsync(Window window, TerminalControl control)
    {
        Assert.True(window.IsVisible);
        bool arranged = await WaitUntilAsync(
            () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
            TimeSpan.FromSeconds(2));
        Assert.True(arranged, $"Terminal control was not arranged in time. Bounds={control.Bounds}");

        control.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<Point> GetInteractionPointAsync(TerminalControl control, Window window)
    {
        bool arranged = await WaitUntilAsync(
            () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
            TimeSpan.FromSeconds(2));
        Assert.True(arranged, $"Terminal control was not arranged in time. Bounds={control.Bounds}");

        Point local = new(control.Bounds.Width * 0.5, control.Bounds.Height * 0.5);
        Point? translated = control.TranslatePoint(local, window);
        Assert.True(translated.HasValue, "Failed to translate interaction point to window coordinates.");
        return translated!.Value;
    }

    private static async Task<Point> GetCellInteractionPointAsync(
        TerminalControl control,
        Window window,
        int column,
        int row)
    {
        bool arranged = await WaitUntilAsync(
            () => control.Bounds.Width > 1 &&
                  control.Bounds.Height > 1 &&
                  control.Renderer is { CellWidth: > 0f, CellHeight: > 0f },
            TimeSpan.FromSeconds(2));
        Assert.True(arranged, $"Terminal control was not arranged in time. Bounds={control.Bounds}");

        double x = (column + 0.5) * control.Renderer!.CellWidth;
        double y = (row + 0.5) * control.Renderer.CellHeight;
        Point local = new(x, y);
        Point? translated = control.TranslatePoint(local, window);
        Assert.True(translated.HasValue, "Failed to translate cell point to window coordinates.");
        return translated!.Value;
    }

    private static void RaisePointerMove(TerminalControl control, Window window, Point windowPoint)
    {
        Pointer pointer = new(id: 4, PointerType.Mouse, isPrimary: true);
        ulong timestamp = (ulong)Environment.TickCount64;

        PointerEventArgs move = new(
            InputElement.PointerMovedEvent,
            control,
            pointer,
            window,
            windowPoint,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None);
        control.RaiseEvent(move);
    }

    private static void RaiseMouseSequence(TerminalControl control, Window window, Point windowPoint)
    {
        Pointer pointer = new(id: 1, PointerType.Mouse, isPrimary: true);
        ulong timestamp = (ulong)Environment.TickCount64;

        PointerEventArgs move = new(
            InputElement.PointerMovedEvent,
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None);
        control.RaiseEvent(move);

        PointerPressedEventArgs press = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clickCount: 1);
        control.RaiseEvent(press);

        PointerReleasedEventArgs release = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left);
        control.RaiseEvent(release);

        PointerWheelEventArgs wheel = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            new Vector(0, -1));
        control.RaiseEvent(wheel);
    }

    private static void RaisePrimaryPointerSequenceWithoutButtonMetadata(
        TerminalControl control,
        Window window,
        Point windowPoint)
    {
        Pointer pointer = new(id: 2, PointerType.Touch, isPrimary: true);
        ulong timestamp = (ulong)Environment.TickCount64;

        PointerPressedEventArgs press = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            clickCount: 1);
        control.RaiseEvent(press);

        PointerReleasedEventArgs release = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            MouseButton.None);
        control.RaiseEvent(release);
    }

    private static void RaiseHandledMouseButtonSequence(TerminalControl control, Window window, Point windowPoint)
    {
        Pointer pointer = new(id: 3, PointerType.Mouse, isPrimary: true);
        ulong timestamp = (ulong)Environment.TickCount64;

        PointerPressedEventArgs press = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clickCount: 1);
        press.Handled = true;
        control.RaiseEvent(press);

        PointerReleasedEventArgs release = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left);
        release.Handled = true;
        control.RaiseEvent(release);
    }

    private static void RaiseModifiedLeftClick(
        TerminalControl control,
        Window window,
        Point windowPoint,
        KeyModifiers modifiers)
    {
        Pointer pointer = new(id: 5, PointerType.Mouse, isPrimary: true);
        ulong timestamp = (ulong)Environment.TickCount64;

        PointerEventArgs move = new(
            InputElement.PointerMovedEvent,
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            modifiers);
        control.RaiseEvent(move);

        PointerPressedEventArgs press = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            modifiers,
            clickCount: 1);
        control.RaiseEvent(press);

        PointerReleasedEventArgs release = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            modifiers,
            MouseButton.Left);
        control.RaiseEvent(release);
    }

    private static void SendMouseSequenceViaTopLevel(Window window, Point windowPoint)
    {
        window.MouseMove(windowPoint, RawInputModifiers.None);
        window.MouseDown(windowPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(windowPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseWheel(windowPoint, new Vector(0, -1), RawInputModifiers.None);
    }

    private sealed record FakeTransportOptions(string TransportId) : ITerminalTransportOptions
    {
        public TerminalSessionDimensions Dimensions => new(80, 24, 640, 480);
    }

    private sealed class RecordingTransportProvider : ITerminalTransportProvider
    {
        private readonly RecordingTransport _transport;

        public RecordingTransportProvider(RecordingTransport transport)
        {
            _transport = transport;
        }

        public string TransportId => "fake";

        public bool CanHandle(ITerminalTransportOptions options)
        {
            return options is FakeTransportOptions;
        }

        public ITerminalTransport Create()
        {
            return _transport;
        }
    }

    private sealed class RecordingTransport : ITerminalTransport
    {
        public event Action<byte[], int>? DataReceived;
        public event Action<int>? ProcessExited;
        private readonly object _inputSync = new();

        public bool IsRunning { get; private set; }
        public List<byte[]> Inputs { get; } = [];
        public List<TerminalSessionDimensions> Resizes { get; } = [];

        public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
        {
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();
            IsRunning = true;
            return ValueTask.CompletedTask;
        }

        public void SendInput(ReadOnlySpan<byte> utf8)
        {
            lock (_inputSync)
            {
                Inputs.Add(utf8.ToArray());
            }
            _ = DataReceived;
        }

        public void RaiseData(byte[] data)
        {
            DataReceived?.Invoke(data, data.Length);
        }

        public void ClearInputs()
        {
            lock (_inputSync)
            {
                Inputs.Clear();
            }
        }

        public bool HasInput(Func<byte[], bool> predicate)
        {
            lock (_inputSync)
            {
                return Inputs.Any(predicate);
            }
        }

        public void Resize(TerminalSessionDimensions dimensions)
        {
            Resizes.Add(dimensions);
        }

        public ValueTask StopAsync()
        {
            IsRunning = false;
            ProcessExited?.Invoke(0);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            IsRunning = false;
        }
    }

    private sealed class RecordingEndpoint : ITerminalEndpoint, ITerminalInputSink
    {
        public List<TerminalKeyEvent> KeyEvents { get; } = [];
        public List<string> TextInputs { get; } = [];
        public List<TerminalPointerEvent> PointerEvents { get; } = [];

        public void SendText(ReadOnlySpan<byte> utf8)
        {
            _ = utf8;
        }

        public void SetFocus(bool focused)
        {
            _ = focused;
        }

        public void SetSize(int widthPx, int heightPx)
        {
            _ = widthPx;
            _ = heightPx;
        }

        public bool SendKey(TerminalKeyEvent keyEvent)
        {
            KeyEvents.Add(keyEvent);
            return true;
        }

        public bool SendText(string text)
        {
            TextInputs.Add(text);
            return true;
        }

        public bool SendPointer(TerminalPointerEvent pointerEvent)
        {
            PointerEvents.Add(pointerEvent);
            return true;
        }
    }

    private sealed class RecordingTextEndpoint : ITerminalEndpoint
    {
        public List<byte[]> Inputs { get; } = [];

        public void SendText(ReadOnlySpan<byte> utf8)
        {
            Inputs.Add(utf8.ToArray());
        }

        public void SetFocus(bool focused)
        {
            _ = focused;
        }

        public void SetSize(int widthPx, int heightPx)
        {
            _ = widthPx;
            _ = heightPx;
        }
    }

    private sealed class LinkActivationProbeTerminalControl : TerminalControl
    {
        public List<Uri> ActivatedLinks { get; } = [];

        protected override bool TryActivateHyperlink(Uri uri)
        {
            ActivatedLinks.Add(uri);
            return true;
        }
    }

    private readonly record struct InteractionCoverageResult(
        bool KeyboardSent,
        bool TextSent,
        bool MouseSent,
        bool PasteSent,
        bool ResizeSent);
}
