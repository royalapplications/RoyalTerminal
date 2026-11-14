// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.InspectionTests;

public sealed class TerminalEffectEventsTests
{
    [Fact]
    public async Task FragmentedWireEffectsAreStructuredAndOrderedWithoutExecutingHostEffects()
    {
        OutputOnly wire = new();
        using InspectingTerminalTransport inspection = new(wire, new(80, 24, 0, 0));
        const string pwd = "file://host/tmp/test%20folder";
        wire.Write($"\u001b]7;{pwd}\a\u001b]777;notify;Build;Ready 🌍\a\u001b]9;4;1;37\a\a");
        var page = await inspection.Metadata.ReadAsync(0);
        Assert.Collection(page.Events,
            e => { Assert.Equal("terminal.pwd_changed", e.Name); Assert.Equal(pwd, e.Pwd); },
            e => { Assert.Equal("terminal.desktop_notification", e.Name); Assert.Equal(new TerminalDesktopNotification("Build", "Ready 🌍"), e.Notification); },
            e => { Assert.Equal("terminal.progress_report", e.Name); Assert.Equal(new TerminalProgressReport(TerminalProgressState.Set, 37), e.ProgressReport); },
            e => Assert.Equal("terminal.bell", e.Name));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, page.Events.Select(e => e.Sequence));
    }

    [Fact]
    public async Task NotificationBodyCapDoesNotSplitASurrogatePair()
    {
        OutputOnly wire = new();
        using InspectingTerminalTransport inspection = new(wire, new(80, 24, 0, 0));
        string prefix = new('a', 2047);
        wire.Write($"\u001b]777;notify;Title;{prefix}🌍end\a");
        Assert.Equal(prefix, Assert.Single((await inspection.Metadata.ReadAsync(0)).Events).Notification!.Body);
    }

    private sealed class OutputOnly : ITerminalTransport
    {
        public event Action<byte[], int>? DataReceived;
        public event Action<int>? ProcessExited { add { } remove { } }
        public bool IsRunning => true;
        public void Write(string s) { foreach (byte b in Encoding.UTF8.GetBytes(s)) DataReceived?.Invoke([b], 1); }
        public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken token = default) => ValueTask.CompletedTask;
        public ValueTask StopAsync() => ValueTask.CompletedTask;
        public void SendInput(ReadOnlySpan<byte> bytes) => throw new InvalidOperationException("An observation generated an unexpected terminal reply.");
        public void Resize(TerminalSessionDimensions dimensions) { }
        public void Dispose() { }
    }
}
