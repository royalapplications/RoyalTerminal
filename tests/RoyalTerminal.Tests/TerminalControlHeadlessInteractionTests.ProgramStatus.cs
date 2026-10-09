// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
    public async Task Headless_ProgramStatusSnapshotsCoalesceAndFollowProcessLifecycle(VtProcessorPreference preference)
    {
        Assert.SkipWhen(preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable(), "Native unavailable.");
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport, new PasswordStateProcessorFactory(), preference);
        Window window = new() { Width = 640, Height = 400, Content = control };
        window.Show();
        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            Dispatcher.UIThread.RunJobs();
            int updates = 0;
            control.ProgramStatusesChanged += (_, _) =>
            {
                Assert.True(Dispatcher.UIThread.CheckAccess());
                updates++;
            };
            control.WriteOutput("\u001b]7501;state=idle:app=deploy\a\u001b]7501;state=working:id=us\a\u001b]7501;state=done:id=eu\a"u8);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, updates);
            Assert.Equal(3, control.ProgramStatuses.Count);
            Assert.All(control.ProgramStatuses, status => Assert.Equal("deploy", status.App));
            IReadOnlyList<TerminalProgramStatus> previous = control.ProgramStatuses;
            await transport.StopAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, control.ProgramStatuses.Count);
            Assert.DoesNotContain(control.ProgramStatuses, status => status.State == TerminalProgramStatusState.Working);
            Assert.Equal(3, previous.Count); // Published snapshots never change in place.
            control.StopPty();
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(control.ProgramStatuses);
            control.StopPty();
            control.WriteOutput("\u001b]7501;state=done\a"u8);
            control.VtProcessorPreference = preference == VtProcessorPreference.Native
                ? VtProcessorPreference.Managed : VtProcessorPreference.Auto;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(control.ProgramStatuses);
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }
}
