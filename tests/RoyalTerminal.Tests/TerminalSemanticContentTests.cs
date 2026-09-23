// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalSemanticContentTests(ITestOutputHelper output)
{
    [Fact]
    public void SemanticBitsDoNotGrowCellsOrOverlapProtectionAndSpacerBits()
    {
        Assert.True(Unsafe.SizeOf<TerminalCell>() <= 48);
        TerminalCell cell = new() { IsProtected = true, IsWideSpacerHead = true };
        foreach (TerminalSemanticContent value in Enum.GetValues<TerminalSemanticContent>())
        {
            cell.SemanticContent = value;
            Assert.Equal(value, cell.SemanticContent);
            Assert.True(cell.IsProtected);
            Assert.True(cell.IsWideSpacerHead);
        }
        cell.IsProtected = cell.IsWideSpacerHead = false;
        Assert.Equal(TerminalSemanticContent.Prompt, cell.SemanticContent);
        Assert.Equal(TerminalSemanticContent.Output, TerminalCell.Empty().SemanticContent);
    }

    [Theory]
    [InlineData("\u001b]133;A\a$ \u001b]133;B\acmd\u001b]133;C\aout\u001b]133;D;0\aend")]
    [InlineData("abc\u001b]133;A\aprompt")]
    [InlineData("abc\u001b]133;N\aprompt")]
    [InlineData("abc\u001b]133;L\ax")]
    [InlineData("abc\u001b]133;L;\ax")]
    [InlineData("\u001b]133;A\aA\u001b]133;Aextra\aB\u001b]133;Dextra\aC")]
    [InlineData("\u001b]133;A\aA\u001b]133;B\aB\r\nC")]
    [InlineData("\u001b]133;A\aA\u001b]133;I\aB\r\nC")]
    [InlineData("\u001b]133;I\a123456789AB\r\nC")]
    [InlineData("\u001b]133;P\a123456789AB\r\nC")]
    [InlineData("\u001b]133;P;k=c\aA\r\n\u001b]133;P;k=r\aB")]
    [InlineData("\u001b]133;P;k=s;k=i\aA\r\n\u001b]133;P;k=x;k=c\aB")]
    [InlineData("\u001b]133;P\aA\r\n\u001b]133;C\aB")]
    [InlineData("\u001b]133;P\aA\r\nB\u001b]133;C\aC")]
    [InlineData("\u001b]133;P\aA\u001b7\u001b]133;C\aB\u001b8C")]
    [InlineData("\u001b]133;P\aA\u001b[?47hB\u001b]133;C\aC\u001b[?47lD")]
    [InlineData("\u001b]133;P\aA\u001b[?1049hB\u001b]133;C\aC\u001b[?1049lD")]
    [InlineData("\u001b]133;P\aA\u001bcB")]
    [InlineData("\u001b]133;P\a界e\u0301\u001b]133;B\a❤\ufe0f")]
    [InlineData("\u001b]133;P\a1234567界")]
    [InlineData("\u001b]133;I\a1234567❤\ufe0fX\r\nY")]
    [InlineData("\u001b]133;P\a❤\u001b]133;B\a\ufe0f")]
    [InlineData("\u001b]133;P\a❤\u001b]133;B\a\ufe0f\u001b[H\u001b[@")]
    [InlineData("\u001b]133;P\a界\u001b]133;B\a\u0301")]
    [InlineData("\u001b]133;P\a1234567❤\u001b]133;B\a\ufe0f")]
    [InlineData("\u001b]133;P\a1234567❤\u0301\u001b]133;B\a\ufe0f")]
    [InlineData("\u001b[?2027h\u001b]133;P\a❤\u001b]133;B\a\ufe0f")]
    [InlineData("\u001b[?2027h\u001b]133;P\a1234567❤\u001b]133;B\a\ufe0f")]
    [InlineData("\u001b[?2027h\u001b]133;P\a1234567❤\u0301\u001b]133;B\a\ufe0f")]
    [InlineData("\u001b[?2027h\u001b]133;I\a1234567❤\ufe0fX\r\nY")]
    [InlineData("\u001b[?69h\u001b[3;6s\u001b[H\u001b]133;I\a1234567890")]
    [InlineData("\u001b[?69h\u001b[3;6s\u001b[1;2H\u001b]133;A\aX")]
    [InlineData("\u001b]133;P\aA\r\nB\r\nC\r\nD\r\nE")]
    [InlineData("\u001b]133;P\aABC\u001b[2KZ")]
    [InlineData("\u001b]133;P\aABC\u001b[H\u001b[KZ")]
    [InlineData("\u001b]133;P\aABC\u001b[H\u001b[@\u001b[PZ")]
    [InlineData("\u001b]133;P\aA\r\nB\u001b[H\u001b[LZ")]
    public void SemanticCellsRowsAndCursorMatchNativeAtEveryInputSplit(string input)
    {
        if (!Available()) return;
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        TerminalScreen nativeScreen = new(8, 3);
        using GhosttyVtProcessor native = new(nativeScreen);
        native.Process(bytes);
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen managedScreen = new(8, 3);
            using BasicVtProcessor managed = new(managedScreen);
            managed.Process(bytes.AsSpan(0, split));
            managed.Process(bytes.AsSpan(split));
            // The visible bridge and owned native grid snapshot must agree too.
            AssertScreens(nativeScreen, managedScreen, $"{Convert.ToHexString(bytes)} split {split}");
            Assert.Equal(native.CursorCol, managed.CursorCol);
            Assert.Equal(native.CursorRow, managed.CursorRow);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataOnlyAndHeldUpdatesArePublished(bool native)
    {
        if (native && !Available()) return;
        TerminalScreen screen = new(8, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("text\r"u8);
        processor.Process("\u001b]133;P\a"u8);
        Assert.Equal(TerminalSemanticPrompt.Prompt, screen.GetViewportRow(0).SemanticPrompt);
        processor.Process("\u001b[?2026h\u001b]133;C\a"u8);
        Assert.Equal(TerminalSemanticPrompt.Prompt, screen.GetViewportRow(0).SemanticPrompt);
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal(TerminalSemanticPrompt.None, screen.GetViewportRow(0).SemanticPrompt);
        processor.Process("\u001b]133;B\a\n"u8);
        Assert.Equal(TerminalSemanticPrompt.PromptContinuation, screen.GetViewportRow(1).SemanticPrompt);
    }

    [Fact]
    public void CopiesRecyclingAndOwnedNativeHistoryRetainSemanticMetadata()
    {
        TerminalRow source = new(4) { SemanticPrompt = TerminalSemanticPrompt.PromptContinuation };
        source[0].SemanticContent = TerminalSemanticContent.Input;
        TerminalRow copy = new(4);
        copy.CopyFrom(source);
        Assert.Equal(source.SemanticPrompt, copy.SemanticPrompt);
        copy.Clear();
        Assert.Equal(TerminalSemanticPrompt.None, copy.SemanticPrompt);
        Assert.Equal(TerminalSemanticContent.Output, copy[0].SemanticContent);
        copy.CopyActiveFrom(source);
        Assert.Equal(source.SemanticPrompt, copy.SemanticPrompt);
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 2, 16));
        native.Process("\u001b]133;A\aprompt\r\nnext\r\nlast"u8);
        Assert.True(native.TryCreateScreenSnapshot(0, 1, 0, out TerminalScreen history));
        native.Process("\u001bc"u8);
        Assert.Equal(TerminalSemanticPrompt.Prompt, history.GetViewportRow(0).SemanticPrompt);
        Assert.Equal(TerminalSemanticContent.Prompt, history.GetViewportRow(0)[0].SemanticContent);
    }

    [Fact]
    public void CopyOnWriteAndReflowPreservePromptCellsAndEmptyMarkedRows()
    {
        TerminalScreen screen = new(8, 3);
        using BasicVtProcessor managed = new(screen);
        managed.Process("\u001b]133;P\aprompt\r\n\u001b]133;P;k=s\a"u8);
        TerminalScreen copy = screen.CreateStateCopy();
        managed.Process("\u001b]133;C\a"u8);
        Assert.Equal(TerminalSemanticPrompt.PromptContinuation, copy.GetViewportRow(1).SemanticPrompt);
        copy.Resize(4, 4);
        Assert.Equal(TerminalSemanticPrompt.Prompt, copy.GetRow(0).SemanticPrompt);
        Assert.Equal(TerminalSemanticPrompt.Prompt, copy.GetRow(1).SemanticPrompt);
        Assert.Equal(TerminalSemanticPrompt.PromptContinuation, copy.GetRow(2).SemanticPrompt);
        for (int row = 0; row < copy.TotalRows; row++)
        foreach (TerminalCell cell in copy.GetRow(row).ReadOnlyCells)
            if (cell.HasContent) Assert.Equal(TerminalSemanticContent.Prompt, cell.SemanticContent);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(16)]
    public void ReflowRowMarkersMatchNativeIncludingMergedSourceRows(int columns)
    {
        if (!Available()) return;
        foreach (string input in new[]
        {
            "\u001b]133;A;redraw=0\apromptabcdef\u001b]133;D\a",
            "\u001b]133;A;redraw=0\aprompt\r\n\u001b]133;P;k=s\a\u001b]133;D\a",
            "\u001b]133;A;redraw=0\a12345678\u001b]133;B\aabcdefghijk\u001b]133;D\a",
            "\u001b]133;A;redraw=0\a1234567界abc\u001b]133;D\a",
            "\u001b]133;A;redraw=0\a界界界界界\u001b]133;D\a",
            "\u001b]133;A;redraw=0\a1234567❤\u001b]133;B\a\ufe0f",
            "\u001b]133;A;redraw=0\a❤\u001b]133;B\a\ufe0f",
            "\u001b[?2027h\u001b]133;A;redraw=0\a1234567❤\u001b]133;B\a\ufe0f",
            "\u001b[?2027h\u001b]133;A;redraw=0\a❤\u001b]133;B\a\ufe0f",
        })
        {
            TerminalScreen expected = new(8, 8), actual = new(8, 8);
            using GhosttyVtProcessor native = new(expected);
            using BasicVtProcessor managed = new(actual);
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            native.Process(bytes);
            managed.Process(bytes);
            expected.Resize(columns, 8, reflowOnResize: false);
            native.NotifyResize(columns, 8, columns * 8, 128);
            managed.ResizeScreen(columns, 8, columns * 8, 128, reflowOnResize: true);
            AssertScreens(expected, actual, $"reflow {columns} {Convert.ToHexString(bytes)}");
            Assert.Equal(native.CursorCol, managed.CursorCol);
            Assert.Equal(native.CursorRow, managed.CursorRow);
        }
    }

    [Fact]
    public void WarmSemanticPrintingAndNewlinesDoNotAllocate()
    {
        using BasicVtProcessor managed = new(new TerminalScreen(80, 24, 0), new() { ContinuationMaxBytes = 0 });
        managed.Process("\u001b]133;P\a"u8);
        for (int i = 0; i < 100; i++) managed.Process("prompt\r\n"u8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) managed.Process("prompt\r\n"u8);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ReflowPreservesValidWideTailWithIndependentPen()
    {
        TerminalScreen screen = new(4, 2);
        TerminalRow row = screen.GetRow(0);
        row[0] = new() { Codepoint = '界', Width = 2, SemanticContent = TerminalSemanticContent.Prompt };
        TerminalCell tail = new() { Width = 0, SemanticContent = TerminalSemanticContent.Input,
            IsProtected = true, Attributes = CellAttributes.Bold, HyperlinkId = 12, Foreground = 0xFF123456 };
        row[1] = tail;
        screen.Resize(3, 2);
        Assert.Equal(tail, screen.GetRow(0)[1]);
        screen.Resize(5, 2);
        Assert.Equal(tail, screen.GetRow(0)[1]);
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(4, 2));
        native.Process(Encoding.UTF8.GetBytes("\u001b[?2027h\u001b]133;P\a❤\u001b[48;5;1m\u001b[1\"q\u001b]133;B\a\ufe0f"));
        Assert.True(native.TryCreateScreenSnapshot(0, 2, 0, out TerminalScreen snapshot));
        TerminalCell nativeTail = snapshot.GetRow(0)[1];
        Assert.Equal(0, nativeTail.Width);
        Assert.NotEqual(snapshot.GetRow(0)[0].BackgroundIdentity, nativeTail.BackgroundIdentity);
        Assert.Equal(TerminalSemanticContent.Input, nativeTail.SemanticContent);
        snapshot.Resize(3, 2);
        Assert.Equal(nativeTail, snapshot.GetRow(0)[1]);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native semantic differential available: {available}");
        return available;
    }

    private static void AssertScreens(TerminalScreen expected, TerminalScreen actual, string context, bool actualAbsoluteRows = false)
    {
        for (int row = 0; row < expected.ViewportRows; row++)
        {
            TerminalRow e = expected.GetViewportRow(row), a = actualAbsoluteRows ? actual.GetRow(row) : actual.GetViewportRow(row);
            Assert.True(e.SemanticPrompt == a.SemanticPrompt, $"{context}, row {row}: {e.SemanticPrompt} != {a.SemanticPrompt}");
            Assert.True(e.IsWrapContinuation == a.IsWrapContinuation, $"{context}, continuation row {row}");
            for (int column = 0; column < expected.Columns; column++)
            {
                TerminalCell ec = e[column], ac = a[column];
                Assert.True((ec.Codepoint, ec.Width, ec.SemanticContent) == (ac.Codepoint, ac.Width, ac.SemanticContent),
                    $"{context}, cell {row},{column}: {ec.Codepoint}/{ec.Width}/{ec.SemanticContent} != {ac.Codepoint}/{ac.Width}/{ac.SemanticContent}");
            }
        }
    }
}
