// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class TerminalControlHeadlessInteractionTests
{
    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed)]
    [InlineData(VtProcessorPreference.Native)]
    public async Task Headless_PromptClickHonorsDragSelectionAndHostPolicy(VtProcessorPreference preference)
    {
        Assert.SkipWhen(preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable(), "Native unavailable.");
        RecordingTransport transport = new();
        DefaultVtProcessorFactory factory = new(new INativeVtProcessorProvider[] { new GhosttyVtProcessorProvider() });
        TerminalControl control = CreateControlWithTransport(transport, factory, preference);
        Window window = new() { Width = 640, Height = 400, Content = control };
        window.Show();
        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("out\r\n\u001b]133;A;click_events=2\a$ \u001b]133;B\aabcdef"u8);
            Dispatcher.UIThread.RunJobs();
            Point start = await GetCellInteractionPointAsync(control, window, column: 2, row: 1);
            Point end = await GetCellInteractionPointAsync(control, window, column: 5, row: 1);
            transport.ClearInputs();
            RaiseMousePressReleaseSequence(control, window, start);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("\u001b[<0;3;1M", Encoding.ASCII.GetString(Assert.Single(transport.Inputs)));
            transport.ClearInputs();
            RaiseMouseDragReleaseSequence(control, window, start, end);
            Dispatcher.UIThread.RunJobs();
            Assert.True(control.HasSelection);
            Assert.Empty(transport.Inputs);
            control.CursorClickToMove = false;
            RaiseMousePressReleaseSequence(control, window, start);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(transport.Inputs);
            control.CursorClickToMove = true;
            RaiseMousePressReleaseSequence(control, window, start, KeyModifiers.Alt);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(transport.Inputs);
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed, MouseButton.XButton1, 128)]
    [InlineData(VtProcessorPreference.Managed, MouseButton.XButton2, 129)]
    [InlineData(VtProcessorPreference.Native, MouseButton.XButton1, 128)]
    [InlineData(VtProcessorPreference.Native, MouseButton.XButton2, 129)]
    public async Task Headless_ExtendedMouseButtonsReachBothEngines(VtProcessorPreference preference, MouseButton button, int code)
    {
        Assert.SkipWhen(preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable(), "Native unavailable.");
        RecordingTransport transport = new();
        DefaultVtProcessorFactory factory = new(new INativeVtProcessorProvider[] { new GhosttyVtProcessorProvider() });
        TerminalControl control = CreateControlWithTransport(transport, factory, preference);
        Window window = new() { Width = 640, Height = 400, Content = control };
        window.Show();
        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\u001b[?1000;1006h"u8);
            Dispatcher.UIThread.RunJobs();
            Point point = await GetCellInteractionPointAsync(control, window, column: 2, row: 1);
            transport.ClearInputs();
            window.MouseDown(point, button);
            window.MouseUp(point, button);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { $"\u001b[<{code};3;2M", $"\u001b[<{code};3;2m" },
                transport.Inputs.Select(Encoding.ASCII.GetString));
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }
}
