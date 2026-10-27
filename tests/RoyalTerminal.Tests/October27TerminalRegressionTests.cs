// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty #14633/#14643/#14644/#14652/#14668/#14672/#14673/#14761/#14762.
// WT also distinguishes HPR/VPR from margin-clamped CUF/CUD. xterm.js HPA
// aliases CHA, but its private CPR omits the page; follow Ghostty's DECXCPR.
public sealed class October27TerminalRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CursorMovesPreserveTheOtherOriginRelativeCoordinate(bool native)
    {
        using IVtProcessor processor = Create(native, out _);
        Write(processor, "\u001b[3;8r\u001b[?69h\u001b[4;9s\u001b[?6h\u001b[2;2H\u001b[1G");
        Assert.Equal((3, 3), (processor.CursorRow, processor.CursorCol));
        Write(processor, "\u001b[2`\u001b[1d");
        Assert.Equal((2, 4), (processor.CursorRow, processor.CursorCol));
        Write(processor, "\u001b[2a\u001b[2e");
        Assert.Equal((4, 6), (processor.CursorRow, processor.CursorCol));
        Write(processor, "\u001b[99a\u001b[99e");
        Assert.Equal((7, 8), (processor.CursorRow, processor.CursorCol));
        Write(processor, "\u001b[?6l\u001b[4;5H\u001b[99a\u001b[99e");
        Assert.Equal((9, 11), (processor.CursorRow, processor.CursorCol));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackspaceAndCursorBackwardStopAtLeftMarginUnlessAlreadyOutside(bool native)
    {
        using IVtProcessor processor = Create(native, out _);
        Write(processor, "\u001b[?69h\u001b[4;9s\u001b[1;6H\u001b[99D\b");
        Assert.Equal(3, processor.CursorCol);
        Write(processor, "\u001b[1;3H\u001b[99D");
        Assert.Equal(0, processor.CursorCol);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtendedCursorPositionAndScreenCharacterSizeReports(bool native)
    {
        using IVtProcessor processor = Create(native, out _);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        Write(processor, "\u001b[5;3H\u001b[?6n\u001b[3;8r\u001b[?69h\u001b[4;9s\u001b[?6h\u001b[2;2H\u001b[6n\u001b[?6n\u001b[18t\u001b[19t");
        Assert.Equal(["\u001b[?5;3;1R", "\u001b[2;2R", "\u001b[?2;2;1R", "\u001b[8;10;12t", "\u001b[9;10;12t"], replies);
        replies.Clear();
        Write(processor, "\u001b[?6;1n\u001b[6;1n\u001b[19;1t");
        Assert.Empty(replies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedAnsiModesReportPermanentlyResetWithoutChangingOtherModes(bool native)
    {
        using IVtProcessor processor = Create(native, out _);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        foreach (int mode in new[] { 1, 5, 7, 10, 11, 13, 14, 15, 16, 17, 18, 19 })
        {
            Write(processor, $"\u001b[{mode}h\u001b[{mode}$p");
            Assert.Equal($"\u001b[{mode};4$y", replies[^1]);
        }
        Write(processor, "\u001b[9$p\u001b[4h\u001b[4$p\u001b[?7$p");
        Assert.Equal(["\u001b[9;0$y", "\u001b[4;1$y", "\u001b[?7;1$y"], replies.TakeLast(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoftResetEndsIsoProtectionForErase(bool native)
    {
        using IVtProcessor processor = Create(native, out TerminalScreen screen);
        Write(processor, "\u001bVA\u001bW\u001b[2J");
        Assert.Equal('A', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Write(processor, "\u001b[!p\u001b[2J");
        Assert.Equal(0, screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultTabClearAndPrintingWithAutowrapOffConsumePendingWrap(bool native)
    {
        using IVtProcessor processor = Create(native, out TerminalScreen screen);
        Write(processor, "\u001b[9G\u001b[g\r\t");
        Assert.Equal(11, processor.CursorCol);
        Write(processor, "\u001b[Habcdefghijkl\u001b[?7l\u001b[ZX\u001b[?7hY");
        Assert.Equal((0, 2), (processor.CursorRow, processor.CursorCol));
        Assert.Equal('X', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal('Y', screen.GetViewportRow(0).ReadOnlyCells[1].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EraseDisplayPreservesPromptHistoryWithBlankRowsBelow(bool native)
    {
        using IVtProcessor processor = Create(native, out _);
        Write(processor, "output\r\n\u001b]133;A\a$ \u001b]133;B\a\u001b[H\u001b[2J");
        Assert.True(((ITerminalSnapshotExportSource)processor).TryExportSnapshot(
            TerminalSnapshotExportFormat.PlainText, new(), out string history));
        Assert.StartsWith("output\n$", history);
    }

    private static IVtProcessor Create(bool native, out TerminalScreen screen)
    {
        Assert.SkipWhen(native && !GhosttyVtProcessor.IsAvailable(), "Native Ghostty is unavailable.");
        screen = new(12, 10, 100);
        return native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
    }

    private static void Write(IVtProcessor processor, string input) => processor.Process(Encoding.UTF8.GetBytes(input));
}
