// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Headless interaction tests for TerminalControl.

using System.Text;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
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
            await CleanupWindowAsync(window, control.StopPty);
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
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardInterrupt_FromHandledTunnelRouting_StillReachesTransportFallback()
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

            window.AddHandler(
                InputElement.KeyDownEvent,
                (_, e) =>
                {
                    if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
                    {
                        e.Handled = true;
                    }
                },
                RoutingStrategies.Tunnel,
                handledEventsToo: false);

            transport.ClearInputs();
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);

            bool ctrlCSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == 0x03),
                TimeSpan.FromSeconds(2));
            Assert.True(ctrlCSent);
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_TextInput_FromHandledTunnelRouting_StillReachesTransportFallback()
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

            window.AddHandler(
                InputElement.TextInputEvent,
                (_, e) =>
                {
                    if (string.Equals(e.Text, "x", StringComparison.Ordinal))
                    {
                        e.Handled = true;
                    }
                },
                RoutingStrategies.Tunnel,
                handledEventsToo: false);

            transport.ClearInputs();
            window.KeyTextInput("x");

            bool textSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == (byte)'x'),
                TimeSpan.FromSeconds(2));
            Assert.True(textSent);
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardInterrupt_WithWindowScopedKeyBinding_StillReachesTransportFallback()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        bool keyBindingExecuted = false;
        window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.C, KeyModifiers.Control),
            Command = new RecordingCommand(() => keyBindingExecuted = true),
        });
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            transport.ClearInputs();
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);

            bool ctrlCSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == 0x03),
                TimeSpan.FromSeconds(2));
            Assert.True(ctrlCSent);
            Assert.False(keyBindingExecuted, "Terminal input should preempt window-scoped key bindings for Ctrl+C when focused.");
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardSuspend_WithWindowScopedKeyBinding_StillReachesTransportFallback()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        bool keyBindingExecuted = false;
        window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Z, KeyModifiers.Control),
            Command = new RecordingCommand(() => keyBindingExecuted = true),
        });
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            transport.ClearInputs();
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);

            bool ctrlZSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == 0x1A),
                TimeSpan.FromSeconds(2));
            Assert.True(ctrlZSent);
            Assert.False(keyBindingExecuted, "Terminal input should preempt window-scoped key bindings for Ctrl+Z when focused.");
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardInterrupt_WithActiveSelection_StillReachesTransportFallback()
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

            Assert.NotNull(control.Renderer);
            control.Renderer!.SelectionStart = (1, 1);
            control.Renderer.SelectionEnd = (4, 1);
            Assert.True(control.HasSelection);

            transport.ClearInputs();
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);

            bool ctrlCSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == 0x03),
                TimeSpan.FromSeconds(2));
            Assert.True(ctrlCSent);
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
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
        const string postInterruptMarker = "__ROYALTERMINAL_AFTER_CTRL_C__";
        const string floodNeedle = "busy-output";

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

            control.SendInput(
                "while :; do printf 'busy-output\\n'; done\n");

            bool floodSeen = await WaitUntilAsync(
                () => ContainsOutput(outputSync, output, floodNeedle),
                TimeSpan.FromSeconds(5));
            Assert.True(floodSeen, $"Did not observe flood output before interrupt. Output: {SnapshotOutput(outputSync, output)}");

            Stopwatch interruptLatency = Stopwatch.StartNew();
            control.SendInput(new byte[] { 0x03 });
            control.SendInput($"echo {postInterruptMarker}\n");

            bool interrupted = await WaitUntilAsync(
                () => ContainsOutput(outputSync, output, postInterruptMarker),
                TimeSpan.FromSeconds(5));
            Assert.True(interrupted, $"Did not observe post-interrupt marker. Output: {SnapshotOutput(outputSync, output)}");
            Assert.True(
                interruptLatency.Elapsed < TimeSpan.FromSeconds(3),
                $"Expected managed PTY Ctrl+C interrupt to be prompt under flood. Latency={interruptLatency.Elapsed}.");
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_ManagedPty_CtrlC_RecoversVisibleScreenAfterUnterminatedOscFlood()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        const string readyMarker = "__ROYALTERMINAL_OSC_READY__";
        const string visibleRecoveryMarker = "__ROYALTERMINAL_OSC_VISIBLE_RECOVERY__";

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
                output.Append(Encoding.ASCII.GetString(args.Data.Span));
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

            control.SendInput("printf '\\033]2;broken'; while :; do printf x; done\n");
            bool oscFloodSeen = await WaitUntilAsync(
                () => SnapshotOutput(outputSync, output).Length >= 4096,
                TimeSpan.FromSeconds(5));
            Assert.True(oscFloodSeen, $"Did not observe OSC flood output before interrupt. Output: {SnapshotOutput(outputSync, output)}");

            control.SendInput(new byte[] { 0x03 });
            control.SendInput($"printf '{visibleRecoveryMarker}\\n'\n");

            bool visibleRecoverySeen = await WaitUntilAsync(
                () => ContainsScreenText(control, visibleRecoveryMarker),
                TimeSpan.FromSeconds(5));
            Assert.True(
                visibleRecoverySeen,
                $"Did not observe visible post-interrupt marker on the managed VT screen. Output: {SnapshotOutput(outputSync, output)}");
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_ManagedPty_KeyDownCtrlC_InterruptsAnsiFloodWithinLatencyBudget()
    {
        // Linux remains stable for managed PTY keydown Ctrl+C interruption
        // under ANSI flood, but macOS runners intermittently destabilize while
        // the interactive shell is resynchronizing after the interrupt. macOS
        // still retains non-keydown PTY interrupt coverage and transport-level
        // key handling coverage elsewhere in this suite.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await VerifyManagedPtyControlCharacterKeyDownInterruptsAnsiFloodAsync(
            PhysicalKey.C,
            "__ROYALTERMINAL_AFTER_KEYDOWN_CTRL_C__",
            "Ctrl+C",
            0x03);
    }

    [AvaloniaFact]
    public async Task Headless_ManagedPty_KeyDownCtrlZ_SuspendsAnsiFloodWithinLatencyBudget()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        await VerifyManagedPtyControlCharacterKeyDownInterruptsAnsiFloodAsync(
            PhysicalKey.Z,
            "__ROYALTERMINAL_AFTER_KEYDOWN_CTRL_Z__",
            "Ctrl+Z",
            0x1A);
    }

    [AvaloniaFact]
    public async Task Headless_ManagedPty_RepeatedKeyDownCtrlC_InterruptsBase64FloodAcrossCycles()
    {
        // The repeated multi-process base64/head/head interrupt cycle is stable
        // on Linux, but macOS runners intermittently fail to re-synchronize the
        // interactive shell prompt after later cycles even when single-cycle and
        // repeated ANSI flood coverage remain healthy. macOS retains the single-
        // cycle Ctrl+C/base64 path and repeated ANSI flood coverage elsewhere.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await VerifyManagedPtyControlCharacterKeyDownSurvivesRepeatedFloodCyclesAsync(
            PhysicalKey.C,
            "Ctrl+C",
            0x03,
            cycleCount: 4,
            RepeatedFloodScenario.Base64);
    }

    [AvaloniaFact]
    public async Task Headless_ManagedPty_RepeatedKeyDownCtrlC_InterruptsAnsiFloodAcrossCycles()
    {
        // Linux remains stable for repeated multi-cycle ANSI flood interruption,
        // but macOS runners intermittently destabilize during later prompt
        // recovery cycles even though the single-cycle ANSI flood path and
        // repeated Linux coverage remain healthy.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await VerifyManagedPtyControlCharacterKeyDownSurvivesRepeatedFloodCyclesAsync(
            PhysicalKey.C,
            "Ctrl+C",
            0x03,
            cycleCount: 6,
            RepeatedFloodScenario.Ansi);
    }

    [AvaloniaFact]
    public async Task Headless_ManagedPty_RepeatedKeyDownCtrlZ_SuspendsBase64FloodAcrossCycles()
    {
        // The repeated multi-process suspend/restart cycle is stable on Linux,
        // but macOS CI runners do not reliably re-enter the base64/head/head
        // flood on subsequent cycles even after cleanup. macOS still retains
        // single-cycle Ctrl+Z managed PTY coverage above.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await VerifyManagedPtyControlCharacterKeyDownSurvivesRepeatedFloodCyclesAsync(
            PhysicalKey.Z,
            "Ctrl+Z",
            0x1A,
            cycleCount: 4,
            RepeatedFloodScenario.Base64);
    }

    [AvaloniaFact]
    public async Task Headless_VtQueryResponses_AreSuppressed_ForPtyBackedSessions_WhenOutputBacklogIsHigh()
    {
        await VerifyVtQueryResponseBehaviorUnderOutputBacklogAsync(
            new RecordingPtyTransport(),
            expectSuppression: true,
            expectationLabel: "PTY-backed sessions should suppress VT query responses under backlog.");
    }

    [AvaloniaFact]
    public async Task Headless_VtQueryResponses_AreNotSuppressed_ForNonPtyTransports_WhenOutputBacklogIsHigh()
    {
        await VerifyVtQueryResponseBehaviorUnderOutputBacklogAsync(
            new RecordingTransport(),
            expectSuppression: false,
            expectationLabel: "Non-PTY transports should continue sending VT query responses under backlog.");
    }

    [AvaloniaFact]
    public async Task Headless_ManagedPty_DaResponse_IsForwardedWithoutUrgentControlSuppression()
    {
        RecordingPtyTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport, preference: VtProcessorPreference.Managed);
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

            transport.ClearInputs();
            transport.RaiseData("\x1b[c"u8.ToArray());

            bool daResponseObserved = await WaitUntilAsync(
                () => transport.HasInput(static input => Encoding.ASCII.GetString(input) == "\x1b[?62;22c"),
                TimeSpan.FromSeconds(2));

            Assert.True(daResponseObserved, "Expected DA response to be forwarded for PTY sessions when no urgent control suppression is active.");
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_ManagedPty_CtrlC_SuppressesLateDaResponseAtPromptBoundary()
    {
        RecordingPtyTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport, preference: VtProcessorPreference.Managed);
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

            transport.ClearInputs();
            control.SendInput(new byte[] { 0x03 });

            bool ctrlCObserved = await WaitUntilAsync(
                () => transport.HasInput(static input => input.Length == 1 && input[0] == 0x03),
                TimeSpan.FromSeconds(2));
            Assert.True(ctrlCObserved, "Expected Ctrl+C to reach the PTY before testing late DA suppression.");

            int inputCountAfterCtrlC = transport.GetInputCount();
            transport.RaiseData("\x1b[c"u8.ToArray());

            await Task.Delay(150);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                inputCountAfterCtrlC,
                transport.GetInputCount());
            Assert.False(
                transport.HasInput(static input => Encoding.ASCII.GetString(input) == "\x1b[?62;22c"),
                "Did not expect a late DA response to be injected into the PTY after Ctrl+C.");
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_ManagedTransport_DaResponse_IsForwardedWithoutUrgentControlSuppression()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport, preference: VtProcessorPreference.Managed);
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

            transport.ClearInputs();
            transport.RaiseData("\x1b[c"u8.ToArray());

            bool daResponseObserved = await WaitUntilAsync(
                () => transport.HasInput(static input => Encoding.ASCII.GetString(input) == "\x1b[?62;22c"),
                TimeSpan.FromSeconds(2));

            Assert.True(daResponseObserved, "Expected DA response to be forwarded for transport sessions when no urgent control suppression is active.");
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_ManagedTransport_CtrlC_SuppressesLateDaResponseAtPromptBoundary()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport, preference: VtProcessorPreference.Managed);
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

            transport.ClearInputs();
            control.SendInput(new byte[] { 0x03 });

            bool ctrlCObserved = await WaitUntilAsync(
                () => transport.HasInput(static input => input.Length == 1 && input[0] == 0x03),
                TimeSpan.FromSeconds(2));
            Assert.True(ctrlCObserved, "Expected Ctrl+C to reach the active transport before testing late DA suppression.");

            int inputCountAfterCtrlC = transport.GetInputCount();
            transport.RaiseData("\x1b[c"u8.ToArray());

            await Task.Delay(150);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(inputCountAfterCtrlC, transport.GetInputCount());
            Assert.False(
                transport.HasInput(static input => Encoding.ASCII.GetString(input) == "\x1b[?62;22c"),
                "Did not expect a late DA response to be injected into the transport after Ctrl+C.");
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
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
            await CleanupWindowAsync(window, control.DetachEndpoint);
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
            await CleanupWindowAsync(window, control.DetachEndpoint);
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
            await CleanupWindowAsync(window, control.DetachEndpoint);
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
            await CleanupWindowAsync(window, control.DetachEndpoint);
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
            await CleanupWindowAsync(window, control.DetachEndpoint);
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
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_EncodesSgrReleaseToTransport_WhenMode1006Enabled()
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
            control.WriteOutput("\x1b[?1000h\x1b[?1006h"u8);
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            Point point = await GetInteractionPointAsync(control, window);
            RaiseMousePressReleaseSequence(control, window, point);
            Dispatcher.UIThread.RunJobs();

            bool sgrReleaseSent = await WaitUntilAsync(
                () => transport.Inputs.Any(IsSgrReleaseMouseProtocolInput),
                TimeSpan.FromSeconds(2));
            Assert.True(sgrReleaseSent);
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
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
            await CleanupWindowAsync(window, control.StopPty);
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
            await CleanupWindowAsync(window, control.StopPty);
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
            await CleanupWindowAsync(window, control.StopPty);
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
            await CleanupWindowAsync(window, control.StopPty);
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
            await CleanupWindowAsync(window, control.StopPty);
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
            await CleanupWindowAsync(window, control.StopPty);
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
            await CleanupWindowAsync(window, control.StopPty);
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
        ITerminalTransport transport,
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
            await CleanupWindowAsync(window, control.StopPty);
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

    private static bool IsSgrReleaseMouseProtocolInput(byte[] input)
    {
        if (!IsMouseProtocolInput(input))
        {
            return false;
        }

        string text = Encoding.ASCII.GetString(input);
        return text.StartsWith("\x1b[<0;", StringComparison.Ordinal) &&
               text.EndsWith("m", StringComparison.Ordinal);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        return await HeadlessTerminalTestCleanup.WaitUntilAsync(predicate, timeout);
    }

    private static async Task VerifyVtQueryResponseBehaviorUnderOutputBacklogAsync(
        RecordingTransport transport,
        bool expectSuppression,
        string expectationLabel)
    {
        ResponseStormVtProcessorFactory vtProcessorFactory = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            vtProcessorFactory,
            VtProcessorPreference.Managed);
        Window window = new()
        {
            Width = 960,
            Height = 640,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));

            byte[] floodChunk = new byte[8 * 1024];
            Array.Fill(floodChunk, (byte)'x');
            Task producer = Task.Run(() =>
            {
                for (int i = 0; i < 1024; i++)
                {
                    transport.RaiseData(floodChunk);
                }
            });

            // Give the producer a short head-start before the UI pumps,
            // so backlog-triggered response behavior is exercised deterministically.
            Thread.Sleep(150);

            bool producerCompleted = await WaitUntilAsync(
                () => producer.IsCompleted,
                TimeSpan.FromSeconds(10));
            Assert.True(producerCompleted, "Expected flood producer to complete after UI drain catches up.");
            await producer;

            bool expectedBehaviorObserved = await WaitUntilAsync(
                () =>
                {
                    ResponseStormVtProcessor? processor = vtProcessorFactory.LastProcessor;
                    if (processor is null || processor.ProcessCallCount == 0)
                    {
                        return false;
                    }

                    int processCount = processor.ProcessCallCount;
                    int sentInputCount = transport.GetInputCount();
                    return expectSuppression
                        ? processCount > sentInputCount
                        : processCount == sentInputCount;
                },
                TimeSpan.FromSeconds(5));

            int finalProcessCount = vtProcessorFactory.LastProcessor?.ProcessCallCount ?? 0;
            int finalSentInputCount = transport.GetInputCount();
            Assert.True(
                expectedBehaviorObserved,
                $"{expectationLabel} ProcessCount={finalProcessCount}, SentInputCount={finalSentInputCount}.");
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    private static async Task VerifyManagedPtyControlCharacterKeyDownInterruptsAnsiFloodAsync(
        PhysicalKey physicalKey,
        string postInterruptMarker,
        string controlCharacterLabel,
        byte expectedInputByte)
    {
        const string readyMarker = "__ROYALTERMINAL_KEYDOWN_CTRL_READY__";
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
        object inputSync = new();
        List<byte[]> inputs = [];
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
        control.TerminalSessionService.InputSent += (_, args) =>
        {
            lock (inputSync)
            {
                inputs.Add(args.Data.ToArray());
            }
        };

        window.Show();
        try
        {
            await StabilizeWindowAsync(window, control);
            (string? shell, IReadOnlyList<string> arguments) = ResolveManagedPtyControlTestShell(requireJobControl: physicalKey == PhysicalKey.Z);
            Assert.True(shell is not null, $"Expected a job-control capable shell for {controlCharacterLabel} managed PTY test.");
            control.StartPty(shell: shell, workingDirectory: Environment.CurrentDirectory, arguments: arguments);

            control.SendInput($"echo {readyMarker}\n");
            bool readySeen = await WaitUntilAsync(
                () => ContainsOutput(outputSync, output, readyMarker),
                TimeSpan.FromSeconds(5));
            Assert.True(readySeen, $"Did not observe PTY ready marker. Output: {SnapshotOutput(outputSync, output)}");

            control.SendInput(BuildAnsiFloodCommand(launchAsChildJob: physicalKey == PhysicalKey.Z));

            bool floodSeen = await WaitUntilAsync(
                () => ContainsOutput(outputSync, output, floodNeedle),
                TimeSpan.FromSeconds(5));
            Assert.True(floodSeen, $"Did not observe ANSI flood output before interrupt. Output: {SnapshotOutput(outputSync, output)}");

            control.Focus();
            Dispatcher.UIThread.RunJobs();

            Stopwatch totalLatency = Stopwatch.StartNew();
            Stopwatch keyDispatchLatency = Stopwatch.StartNew();
            window.KeyPressQwerty(physicalKey, RawInputModifiers.Control);
            keyDispatchLatency.Stop();

            Stopwatch inputObservationLatency = Stopwatch.StartNew();
            bool expectedInputObserved = await WaitUntilAsync(
                () => ContainsInputByte(inputSync, inputs, expectedInputByte),
                TimeSpan.FromSeconds(2));
            inputObservationLatency.Stop();
            Assert.True(
                expectedInputObserved,
                $"Did not observe expected PTY input byte 0x{expectedInputByte:X2} after {controlCharacterLabel}. Inputs: {SnapshotInputs(inputSync, inputs)}");

            Stopwatch postInterruptLatency = Stopwatch.StartNew();
            control.SendInput($"echo {postInterruptMarker}\n");

            bool interrupted = await WaitUntilAsync(
                () => ContainsOutput(outputSync, output, postInterruptMarker),
                TimeSpan.FromSeconds(5));
            postInterruptLatency.Stop();
            Assert.True(interrupted, $"Did not observe post-interrupt marker after {controlCharacterLabel}. Output: {SnapshotOutput(outputSync, output)}");
            Assert.True(
                totalLatency.Elapsed < TimeSpan.FromSeconds(3),
                $"Expected managed PTY {controlCharacterLabel} keydown interrupt to be prompt under ANSI flood. " +
                $"Total={totalLatency.Elapsed}, KeyDispatch={keyDispatchLatency.Elapsed}, InputObserved={inputObservationLatency.Elapsed}, " +
                $"PostInterrupt={postInterruptLatency.Elapsed}.");
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
    }

    private static async Task VerifyManagedPtyControlCharacterKeyDownSurvivesRepeatedFloodCyclesAsync(
        PhysicalKey physicalKey,
        string controlCharacterLabel,
        byte expectedInputByte,
        int cycleCount,
        RepeatedFloodScenario scenario)
    {
        const string readyMarker = "__ROYALTERMINAL_REPEAT_CTRL_READY__";

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
        object inputSync = new();
        List<byte[]> inputs = [];
        control.DataReceived += (_, args) =>
        {
            lock (outputSync)
            {
                output.Append(Encoding.UTF8.GetString(args.Data.Span));
                if (output.Length > 262144)
                {
                    output.Remove(0, output.Length - 131072);
                }
            }
        };
        control.TerminalSessionService.InputSent += (_, args) =>
        {
            lock (inputSync)
            {
                inputs.Add(args.Data.ToArray());
            }
        };

        window.Show();
        try
        {
            await StabilizeWindowAsync(window, control);
            (string? shell, IReadOnlyList<string> arguments) = ResolveManagedPtyControlTestShell(requireJobControl: physicalKey == PhysicalKey.Z);
            Assert.True(shell is not null, $"Expected a job-control capable shell for repeated {controlCharacterLabel} managed PTY test.");
            control.StartPty(shell: shell, workingDirectory: Environment.CurrentDirectory, arguments: arguments);

            control.SendInput($"echo {readyMarker}\n");
            bool readySeen = await WaitUntilAsync(
                () => ContainsOutput(outputSync, output, readyMarker),
                TimeSpan.FromSeconds(5));
            Assert.True(readySeen, $"Did not observe PTY ready marker. Output: {SnapshotOutput(outputSync, output)}");

            control.Focus();
            Dispatcher.UIThread.RunJobs();

            for (int cycle = 1; cycle <= cycleCount; cycle++)
            {
                ClearOutput(outputSync, output);
                int inputCountBefore = GetInputCount(inputSync, inputs);

                control.SendInput(BuildRepeatedFloodCommand(
                    scenario,
                    launchAsChildJob: physicalKey == PhysicalKey.Z));

                bool floodSeen = await WaitUntilAsync(
                    () => IsRepeatedFloodObserved(scenario, outputSync, output),
                    GetRepeatedFloodObservationTimeout(scenario));
                Assert.True(
                    floodSeen,
                    $"Cycle {cycle}: Did not observe {scenario} flood before {controlCharacterLabel}. " +
                    $"Mode={SnapshotModeState(control.TerminalSessionService.ModeSource)}, Kitty={SnapshotKittyKeyboardFlags(control.TerminalSessionService.ModeSource)}, " +
                    $"Output={SnapshotOutput(outputSync, output)}");

                Assert.True(
                    control.IsKeyboardFocusWithin,
                    $"Cycle {cycle}: Terminal lost keyboard focus before {controlCharacterLabel}.");

                window.KeyPressQwerty(physicalKey, RawInputModifiers.Control);

                bool expectedInputObserved = await WaitUntilAsync(
                    () => ContainsInputByteAfterIndex(inputSync, inputs, expectedInputByte, inputCountBefore),
                    TimeSpan.FromSeconds(2));
                Assert.True(
                    expectedInputObserved,
                    $"Cycle {cycle}: Did not observe expected PTY input byte 0x{expectedInputByte:X2} after {controlCharacterLabel}. " +
                    $"Mode={SnapshotModeState(control.TerminalSessionService.ModeSource)}, Kitty={SnapshotKittyKeyboardFlags(control.TerminalSessionService.ModeSource)}, " +
                    $"Inputs={SnapshotInputs(inputSync, inputs)}");

                string cycleMarker = $"__ROYALTERMINAL_REPEAT_{physicalKey}_{cycle}__";
                control.SendInput($"echo {cycleMarker}\n");

                bool promptRecovered = await WaitUntilAsync(
                    () => ContainsOutput(outputSync, output, cycleMarker),
                    GetRepeatedFloodRecoveryTimeout(scenario));
                Assert.True(
                    promptRecovered,
                    $"Cycle {cycle}: Did not observe prompt recovery marker after {controlCharacterLabel}. " +
                    $"Mode={SnapshotModeState(control.TerminalSessionService.ModeSource)}, Kitty={SnapshotKittyKeyboardFlags(control.TerminalSessionService.ModeSource)}, " +
                    $"Output={SnapshotOutput(outputSync, output)}");

                await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

                if (physicalKey == PhysicalKey.Z)
                {
                    control.SendInput("for job in $(jobs -p 2>/dev/null); do kill \"$job\" >/dev/null 2>&1; done\n");
                    control.SendInput("wait >/dev/null 2>&1\n");
                    string cleanupMarker = $"__ROYALTERMINAL_REPEAT_CLEANUP_{cycle}__";
                    control.SendInput($"echo {cleanupMarker}\n");
                    bool cleanupCompleted = await WaitUntilAsync(
                        () => ContainsOutput(outputSync, output, cleanupMarker),
                        TimeSpan.FromSeconds(5));
                    Assert.True(
                        cleanupCompleted,
                        $"Cycle {cycle}: Did not observe cleanup marker after {controlCharacterLabel}. " +
                        $"Output={SnapshotOutput(outputSync, output)}");
                }
            }
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
        }
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
                for (int i = 0; i < 2_048; i++)
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
                bool producerCompleted = await WaitUntilAsync(
                    () => floodProducer.IsCompleted,
                    TimeSpan.FromSeconds(5));
                Assert.True(producerCompleted, "Expected synthetic flood producer to complete before test teardown.");
                await floodProducer;
            }
        }
        finally
        {
            await CleanupWindowAsync(window, control.StopPty);
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

    private static bool ContainsScreenText(TerminalControl control, string needle)
    {
        TerminalScreen? screen = control.Screen;
        if (screen is null)
        {
            return false;
        }

        for (int row = 0; row < screen.ViewportRows; row++)
        {
            if (ReadVisibleAsciiRow(screen, row).Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInputByte(object sync, List<byte[]> inputs, byte expectedByte)
    {
        lock (sync)
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].Contains(expectedByte))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool ContainsInputByteAfterIndex(object sync, List<byte[]> inputs, byte expectedByte, int startIndex)
    {
        lock (sync)
        {
            for (int i = startIndex; i < inputs.Count; i++)
            {
                if (inputs[i].Contains(expectedByte))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static int GetInputCount(object sync, List<byte[]> inputs)
    {
        lock (sync)
        {
            return inputs.Count;
        }
    }

    private static void ClearOutput(object sync, StringBuilder output)
    {
        lock (sync)
        {
            output.Clear();
        }
    }

    private static bool IsRepeatedFloodObserved(RepeatedFloodScenario scenario, object sync, StringBuilder output)
    {
        lock (sync)
        {
            return scenario switch
            {
                RepeatedFloodScenario.Base64 => output.Length >= 2048,
                RepeatedFloodScenario.Ansi => output.ToString().Contains("Build step", StringComparison.Ordinal),
                _ => false,
            };
        }
    }

    private static string SnapshotOutput(object sync, StringBuilder output)
    {
        lock (sync)
        {
            return output.ToString();
        }
    }

    private static TerminalModeState SnapshotModeState(ITerminalModeSource? modeSource)
    {
        return modeSource?.ModeState ?? default;
    }

    private static int SnapshotKittyKeyboardFlags(ITerminalModeSource? modeSource)
    {
        return modeSource is IKittyKeyboardStateSource kitty ? kitty.KittyKeyboardFlags : 0;
    }

    private static string SnapshotInputs(object sync, List<byte[]> inputs)
    {
        lock (sync)
        {
            return string.Join(
                " | ",
                inputs.Select(static input => BitConverter.ToString(input)));
        }
    }

    private static string ReadVisibleAsciiRow(TerminalScreen screen, int row)
    {
        TerminalRow terminalRow = screen.GetViewportRow(row);
        char[] chars = new char[terminalRow.Columns];
        for (int col = 0; col < terminalRow.Columns; col++)
        {
            int codepoint = terminalRow[col].Codepoint;
            chars[col] = codepoint <= 0 ? ' ' : codepoint <= 0x7F ? (char)codepoint : '?';
        }

        return new string(chars);
    }

    private static (string? Shell, IReadOnlyList<string> Arguments) ResolveManagedPtyControlTestShell(bool requireJobControl)
    {
        if (File.Exists("/bin/zsh"))
        {
            return requireJobControl
                ? ("/bin/zsh", ["-f", "-i"])
                : ("/bin/zsh", Array.Empty<string>());
        }

        if (File.Exists("/bin/bash"))
        {
            return requireJobControl
                ? ("/bin/bash", ["--noprofile", "--norc", "-i"])
                : ("/bin/bash", Array.Empty<string>());
        }

        return requireJobControl
            ? (null, Array.Empty<string>())
            : ("/bin/sh", Array.Empty<string>());
    }

    private static TimeSpan GetRepeatedFloodObservationTimeout(RepeatedFloodScenario scenario)
    {
        return scenario switch
        {
            RepeatedFloodScenario.Base64 => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(5),
        };
    }

    private static TimeSpan GetRepeatedFloodRecoveryTimeout(RepeatedFloodScenario scenario)
    {
        return scenario switch
        {
            RepeatedFloodScenario.Base64 => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(5),
        };
    }

    private static string BuildAnsiFloodCommand(bool launchAsChildJob)
    {
        const string loopCommand =
            "i=1; while [ \"$i\" -le 200000 ]; do " +
            "printf '\\033[32mINFO\\033[0m Build step %d completed\\n' \"$i\"; " +
            "printf '\\033[33mWARN\\033[0m Something suspicious\\n'; " +
            "printf '\\033[31mERROR\\033[0m Something failed\\n'; " +
            "i=$((i+1)); " +
            "done";

        return launchAsChildJob
            ? $"/bin/sh -c '{EscapeSingleQuoted(loopCommand)}'\n"
            : $"{loopCommand}\n";
    }

    private static string BuildRepeatedFloodCommand(RepeatedFloodScenario scenario, bool launchAsChildJob)
    {
        return scenario switch
        {
            RepeatedFloodScenario.Base64 =>
                BuildBase64FloodCommand(launchAsChildJob),
            RepeatedFloodScenario.Ansi =>
                BuildAnsiFloodCommand(launchAsChildJob),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown repeated flood scenario."),
        };
    }

    private static string BuildBase64FloodCommand(bool launchAsChildJob)
    {
        const string loopCommand =
            "while :; do " +
            "head -c 10000 /dev/urandom | base64 | head -n 40; " +
            "done";

        return launchAsChildJob
            ? $"/bin/sh -c '{EscapeSingleQuoted(loopCommand)}'\n"
            : $"{loopCommand}\n";
    }

    private static string EscapeSingleQuoted(string value)
    {
        return value.Replace("'", "'\"'\"'", StringComparison.Ordinal);
    }

    private static async Task StabilizeWindowAsync(Window window, TerminalControl control)
    {
        Assert.True(window.IsVisible);
        bool arranged = await WaitUntilAsync(
            () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
            TimeSpan.FromSeconds(2));
        Assert.True(arranged, $"Terminal control was not arranged in time. Bounds={control.Bounds}");

        control.Focus();
        HeadlessTerminalTestCleanup.RunDispatcherJobs();
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

    private static async Task CleanupWindowAsync(Window window, Action cleanup)
    {
        await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, cleanup);
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

    private static void RaiseMousePressReleaseSequence(
        TerminalControl control,
        Window window,
        Point windowPoint)
    {
        Pointer pointer = new(id: 4, PointerType.Mouse, isPrimary: true);
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
        control.RaiseEvent(release);
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
        private readonly ITerminalTransport _transport;

        public RecordingTransportProvider(ITerminalTransport transport)
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

    private class RecordingTransport : ITerminalTransport
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

        public int GetInputCount()
        {
            lock (_inputSync)
            {
                return Inputs.Count;
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

    private sealed class RecordingPtyTransport : RecordingTransport, ITerminalPtyTransport
    {
        public IPty Pty { get; } = new StubPty();
    }

    private sealed class StubPty : IPty
    {
        public event Action<byte[], int>? DataReceived
        {
            add { }
            remove { }
        }

        public event Action<int>? ProcessExited
        {
            add { }
            remove { }
        }

        public bool IsRunning => true;
        public int ChildPid => -1;

        public void Start(
            string? shell = null,
            int columns = 80,
            int rows = 24,
            string? workingDirectory = null,
            Dictionary<string, string>? environment = null,
            IReadOnlyList<string>? arguments = null)
        {
            _ = shell;
            _ = columns;
            _ = rows;
            _ = workingDirectory;
            _ = environment;
            _ = arguments;
        }

        public void Write(string text)
        {
            _ = text;
        }

        public void Write(byte[] data, int offset, int count)
        {
            _ = data;
            _ = offset;
            _ = count;
        }

        public void Resize(int columns, int rows)
        {
            _ = columns;
            _ = rows;
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ResponseStormVtProcessorFactory : IVtProcessorFactory
    {
        public ResponseStormVtProcessor? LastProcessor { get; private set; }

        public IVtProcessor Create(global::RoyalTerminal.Avalonia.Rendering.TerminalScreen screen, VtProcessorPreference preference)
        {
            _ = screen;
            _ = preference;
            ResponseStormVtProcessor processor = new();
            LastProcessor = processor;
            return processor;
        }
    }

    private sealed class ResponseStormVtProcessor : IVtProcessor
    {
        private int _processCallCount;

        public int ProcessCallCount => Volatile.Read(ref _processCallCount);
        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => true;
        public bool ApplicationCursorKeys => false;
        public bool ApplicationKeypad => false;
        public bool AlternateScreen => false;
        public bool BracketedPaste => false;
        public bool Win32InputMode => false;
        public TerminalModeState ModeState => new(
            CursorVisible,
            ApplicationCursorKeys,
            ApplicationKeypad,
            AlternateScreen,
            BracketedPaste,
            Win32InputMode);

        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add { }
            remove { }
        }

        public Action<byte[]>? ResponseCallback { get; set; }
        public Action? BellCallback { get; set; }
        public Action<string>? TitleCallback { get; set; }

        public void Process(ReadOnlySpan<byte> data)
        {
            _ = data;
            Interlocked.Increment(ref _processCallCount);
            ResponseCallback?.Invoke("\x1b[0n"u8.ToArray());
        }

        public void NotifyResize(int columns, int rows)
        {
            _ = columns;
            _ = rows;
        }

        public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
        {
            _ = columns;
            _ = rows;
            _ = widthPx;
            _ = heightPx;
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
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

    private sealed class RecordingCommand : ICommand
    {
        private readonly Action _execute;

        public RecordingCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            _ = parameter;
            return true;
        }

        public void Execute(object? parameter)
        {
            _ = parameter;
            _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private readonly record struct InteractionCoverageResult(
        bool KeyboardSent,
        bool TextSent,
        bool MouseSent,
        bool PasteSent,
        bool ResizeSent);

    private enum RepeatedFloodScenario
    {
        Base64,
        Ansi,
    }
}
