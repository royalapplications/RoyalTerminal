// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedGraphemeModeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NativeWrappedWideOverwriteRefreshesPreviousRowLikeRawSnapshot(int column)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(12, 3, 0);
        using GhosttyVtProcessor processor = new(screen);
        processor.Process("ABCDEFGHIJK界"u8);
        processor.Process(Encoding.ASCII.GetBytes($"\u001b[2;{column}HX"));
        Assert.True(processor.TryCreateScreenSnapshot(0, 3, 0, out TerminalScreen raw));
        Assert.Equal((byte)1, raw.GetViewportRow(0).ReadOnlyCells[11].Width);
        Assert.Equal(raw.GetViewportRow(0).ReadOnlyCells[11].Width, screen.GetViewportRow(0).ReadOnlyCells[11].Width);
    }

    [Theory]
    [InlineData(false, "\U0001F1E6\U0001F1E7", 4)]
    [InlineData(true, "\U0001F1E6\U0001F1E7", 2)]
    [InlineData(false, "#\uFE0F\u20E3", 1)]
    [InlineData(true, "#\uFE0F\u20E3", 2)]
    [InlineData(false, "☔\uFE0E", 2)]
    [InlineData(true, "☔\uFE0E", 1)]
    [InlineData(false, "\u0301A", 1)]
    [InlineData(true, "\u0301A", 1)]
    [InlineData(false, "\u0600A", 2)]
    [InlineData(true, "\u0600A", 2)]
    public void ModeChangesPrintingAndReportsItsActualState(bool clusters, string text, int cursor)
    {
        TerminalScreen screen = new(12, 3, 0);
        using BasicVtProcessor processor = new(screen);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.Process(Encoding.UTF8.GetBytes($"\u001b[?2027{(clusters ? 'h' : 'l')}{text}\u001b[?2027$p"));
        Assert.Equal(cursor, processor.CursorCol);
        Assert.Equal($"\u001b[?2027;{(clusters ? 1 : 2)}$y", Assert.Single(replies));
    }

    [Theory]
    [InlineData("\u001bc")]
    [InlineData("\u001b[!p")]
    public void ResetsRestoreNonClusterMode(string reset)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(12, 3, 0));
        string? reply = null;
        processor.ResponseCallback = bytes => reply = Encoding.ASCII.GetString(bytes);
        processor.Process(Encoding.ASCII.GetBytes("\u001b[?2027h" + reset + "\u001b[?2027$p"));
        Assert.Equal("\u001b[?2027;2$y", reply);
    }

    [Theory]
    [InlineData("\U0001F1E6\U0001F1E7")]
    [InlineData("\U0001F469\u200D\u2764\uFE0F\u200D\U0001F469")]
    [InlineData("A\U0001F3FB")]
    [InlineData("#\uFE0F\u20E3")]
    [InlineData("☔\uFE0E")]
    [InlineData("\u0301A")]
    [InlineData("\u0600A")]
    [InlineData("?\u094D\u0924")]
    [InlineData("A\r\n\u0301B")]
    [InlineData("\u001b[?7lABCDEFGHIJKL\u0301")]
    [InlineData("ABCDEFGHIJK#\uFE0F\u20E3")]
    [InlineData("ABCDEFGHIJK界")]
    [InlineData("\u001b[?7lABCDEFGHIJK界")]
    [InlineData("ABCDEFGHIJK界\u001b[2;1HX")]
    [InlineData("ABCDEFGHIJK界\u001b[2;2HX")]
    [InlineData("ABCDEFGHIJK界\u001b[2;1H\u001b[X")]
    [InlineData("ABCDEFGHIJK界\u001b[1;1H\u001b[X")]
    [InlineData("ABCDEFGHIJK界\u001b[2;1H語")]
    [InlineData("界界界界界界界\u001b[2;1HX")]
    [InlineData("ABCDEFGHIJK界\u001b[2;1H\u001b[K")]
    [InlineData("ABCDEFGHIJK界\u001b[2;1H\u001b[@")]
    [InlineData("ABCDEFGHIJK界\u001b[2;1H\u001b[P")]
    [InlineData("ABCDEFGHIJK界\u001b[2;1H\u001b[L")]
    [InlineData("ABCDEFGHIJK界\u001b[2;1H\u001b[M")]
    [InlineData("☔\uFE0E\u001b[?2027h\u0301")]
    [InlineData("A\u1161\u001b[?2027h\u0301")]
    public void ManagedStreamingMatchesNativeWithModeOffAndOn(string text)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        foreach (bool clusters in new[] { false, true })
        {
            TerminalScreen managedScreen = new(12, 3, 20);
            TerminalScreen nativeScreen = new(12, 3, 20);
            using BasicVtProcessor managed = new(managedScreen);
            using GhosttyVtProcessor native = new(nativeScreen);
            byte[] sequence = Encoding.UTF8.GetBytes($"\u001b[?2027{(clusters ? 'h' : 'l')}{text}");
            foreach (byte value in sequence)
            {
                managed.Process([value]);
                native.Process([value]);
            }
            Assert.Equal(native.CursorCol, managed.CursorCol);
            Assert.Equal(native.CursorRow, managed.CursorRow);
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 12; column++)
            {
                TerminalCell expected = nativeScreen.GetViewportRow(row)[column];
                TerminalCell actual = managedScreen.GetViewportRow(row)[column];
                Assert.True(expected.Codepoint == actual.Codepoint && expected.Grapheme == actual.Grapheme &&
                    expected.Width == actual.Width && expected.IsWideSpacerHead == actual.IsWideSpacerHead,
                    $"mode={clusters}, row={row}, col={column}: native={expected.Codepoint:X}/{expected.Grapheme}/{expected.Width}/{expected.IsWideSpacerHead}, managed={actual.Codepoint:X}/{actual.Grapheme}/{actual.Width}/{actual.IsWideSpacerHead}");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleColumnWidePrintingProducesEmptyCellAndPendingWrap(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(1, 3, 0);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("界"u8);
        TerminalCell cell = screen.GetViewportRow(0).ReadOnlyCells[0];
        Assert.Equal(0, cell.Codepoint);
        Assert.Equal((byte)1, cell.Width);
        Assert.False(cell.IsWideSpacerHead);
        Assert.Equal(0, processor.CursorRow);
        processor.Process("X"u8);
        Assert.Equal('X', screen.GetViewportRow(1).ReadOnlyCells[0].Codepoint);
        Assert.Equal(1, processor.CursorRow);
    }

    [Fact]
    public void ManagedReflowAndScreenCopiesPreserveDistinctSpacerHeads()
    {
        TerminalScreen screen = new(6, 3, 20);
        using BasicVtProcessor processor = new(screen);
        processor.Process("abc界"u8);
        screen.Resize(4, 3);
        TerminalCell head = screen.GetRow(0).ReadOnlyCells[3];
        Assert.True(head.IsWideSpacerHead);
        Assert.Equal((byte)0, head.Width);
        Assert.False(screen.GetRow(1).ReadOnlyCells[1].IsWideSpacerHead);
        TerminalScreen copy = screen.CreateStateCopy();
        screen.Resize(6, 3);
        Assert.True(copy.GetRow(0).ReadOnlyCells[3].IsWideSpacerHead);
        Assert.Equal('界', screen.GetRow(0).ReadOnlyCells[3].Codepoint);
        Assert.False(screen.GetRow(0).ReadOnlyCells[3].IsWideSpacerHead);
    }

    [Fact]
    public void RasterCoveringSpacerHeadDoesNotEraseUnrelatedCellToItsLeft()
    {
        TerminalScreen screen = new(4, 3, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process("abc界"u8);
        int imageId = screen.AllocateRasterImageId();
        screen.ReplaceRasterImage(
            new TerminalRasterImageSource(imageId, TerminalRasterImageProtocol.Sixel, 1, 1, new byte[4]),
            new TerminalRasterImagePlacement(imageId, TerminalRasterImageLayer.BelowText,
                3, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1));
        Assert.Equal('c', screen.GetViewportRow(0).ReadOnlyCells[2].Codepoint);
        Assert.False(screen.GetViewportRow(0).ReadOnlyCells[3].IsWideSpacerHead);
        Assert.Equal('界', screen.GetViewportRow(1).ReadOnlyCells[0].Codepoint);
    }
}
