// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty #13901 emits an unconditional remove effect on each protocol RIS.
// WT's ConEmu dispatch and xterm.js CoreTerminal.reset were compared; follow
// Ghostty's stream-handler effect, not a progress-active flag or snapshot state.
public sealed class TerminalProgressLifecycleTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public void EveryRisClearsProgressIncludingWhenAlreadyInactive(bool native, int state)
    {
        using IVtProcessor? processor = Create(native);
        if (processor is null) return;
        List<TerminalProgressReport> reports = [];
        ((ITerminalEffectSource)processor).ProgressReportCallback = reports.Add;
        processor.Process("\u001bc\u001bc"u8);
        Assert.Equal(new[] { Removed, Removed }, reports);
        reports.Clear();
        processor.Process(Encoding.ASCII.GetBytes($"\u001b]9;4;{state}\a\u001bc\u001bc"));
        Assert.Equal(3, reports.Count);
        Assert.Equal((TerminalProgressState)state, reports[0].State);
        Assert.Equal(new(TerminalProgressState.Remove, null), reports[1]);
        Assert.Equal(Removed, reports[2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgressTracksWithoutAConsumerAndSoftResetDoesNotClearIt(bool native)
    {
        using IVtProcessor? processor = Create(native);
        if (processor is null) return;
        processor.Process("\u001b]9;4;1;0\a\u001b[!p"u8);
        List<TerminalProgressReport> reports = [];
        ((ITerminalEffectSource)processor).ProgressReportCallback = reports.Add;
        processor.Process("\u001bc\u001bc"u8);
        Assert.Equal(new[] { Removed, Removed }, reports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitRemoveAndRisAreDeliveredAndMalformedReportsAreIgnored(bool native)
    {
        using IVtProcessor? processor = Create(native);
        if (processor is null) return;
        List<TerminalProgressReport> reports = [];
        ((ITerminalEffectSource)processor).ProgressReportCallback = reports.Add;
        processor.Process("\u001b]9;4;1;25\a\u001b]9;4;0\a\u001b]9;4;0\a\u001bc"u8);
        Assert.Equal(4, reports.Count);
        Assert.Equal(new(TerminalProgressState.Remove, null), reports[1]);
        Assert.Equal(reports[1], reports[2]);
        Assert.Equal(Removed, reports[3]);
        reports.Clear();
        // The state selector itself must be invalid: Ghostty intentionally
        // accepts a recognized first digit even with trailing non-semicolon text.
        processor.Process("\u001b]9;4;garbage\a"u8);
        Assert.Empty(reports);
        processor.Process("\u001bc"u8);
        Assert.Equal(Removed, Assert.Single(reports));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgrammaticResetEmitsNoHostEffectUnlikeProtocolRis(bool native)
    {
        using IVtProcessor? processor = Create(native);
        if (processor is null) return;
        List<TerminalProgressReport> reports = [];
        ((ITerminalEffectSource)processor).ProgressReportCallback = reports.Add;
        processor.Process("\u001b]9;4;1;25\a"u8);
        reports.Clear();
        processor.Reset();
        Assert.Empty(reports);
        processor.Process("\u001bc\u001bc"u8);
        Assert.Equal(new[] { Removed, Removed }, reports);
    }

    [Fact]
    public void SnapshotDoesNotReplayOrTransferHostProgressState()
    {
        using BasicVtProcessor source = new(new TerminalScreen(8, 2));
        source.Process("\u001b]9;4;1;25\a"u8);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(source.GetBinarySnapshot());
        List<TerminalProgressReport> reports = [];
        restored.Processor.ProgressReportCallback = reports.Add;
        restored.Processor.Process("X"u8);
        Assert.Empty(reports);
        restored.Processor.Process("\u001bc"u8);
        Assert.Equal(Removed, Assert.Single(reports));
        reports.Clear();
        source.ProgressReportCallback = reports.Add;
        source.Process("\u001bc"u8);
        Assert.Equal(new(TerminalProgressState.Remove, null), Assert.Single(reports));
    }

    private static TerminalProgressReport Removed => new(TerminalProgressState.Remove, null);

    private IVtProcessor? Create(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable())
        {
            output.WriteLine("Native progress lifecycle comparison unavailable.");
            return null;
        }
        TerminalScreen screen = new(8, 2);
        return native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
    }
}
