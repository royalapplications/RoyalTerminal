// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedTerminalSearchTests
{
    [Fact]
    public void MatchDefaultsToOneRowAndPreservesExistingDeconstruction()
    {
        TerminalSearchMatch match = new(4, 2, 8);
        Assert.Equal(4, match.EndAbsoluteRow);
        (int row, int start, int end) = match;
        Assert.Equal((4, 2, 8), (row, start, end));
        Assert.NotEqual(match, match with { EndAbsoluteRow = 5 });
    }

    [Theory]
    [InlineData("aAaAa", "AA", 4)]
    [InlineData("AbC abc ABC", "abc", 3)]
    [InlineData("é É", "é", 1)]
    [InlineData("aaaaab", "aaab", 1)]
    [InlineData("abc", "", 0)]
    [InlineData("abc", "abcdefgh", 0)]
    [InlineData("x   ", " ", 0)]
    [InlineData("x   y", " ", 3)]
    public void LiteralSearchUsesAsciiFoldingOverlapsAndTrim(string text, string needle, int expected)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(40, 4));
        processor.Process(Encoding.UTF8.GetBytes(text));
        List<TerminalSearchMatch> matches = [];
        processor.PopulateSearchMatches(needle, matches);
        Assert.Equal(expected, matches.Count);
        processor.PopulateSearchMatches("not present", matches);
        Assert.Empty(matches);
    }

    [Fact]
    public void WrapIsOneRangeButHardLineBreakRequiresNewlineInNeedle()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(4, 4));
        processor.Process("xxABCDEF\r\nAB\r\nCD"u8);
        List<TerminalSearchMatch> matches = [];
        processor.PopulateSearchMatches("abcdef", matches);
        Assert.Equal(new TerminalSearchMatch(0, 2, 3) { EndAbsoluteRow = 1 }, Assert.Single(matches));
        processor.PopulateSearchMatches("abcd", matches);
        Assert.Single(matches);
        processor.PopulateSearchMatches("AB\nCD", matches);
        Assert.Equal(new TerminalSearchMatch(2, 0, 1) { EndAbsoluteRow = 3 }, Assert.Single(matches));
    }

    [Theory]
    [InlineData("a界b", "界b", 1, 3)]
    [InlineData("a😀b", "😀", 1, 1)]
    [InlineData("ae\u0301b", "\u0301b", 1, 2)]
    [InlineData("a👩‍💻b", "💻b", 1, 3)]
    public void UnicodeComponentsMapBackToOwningCells(string text, string needle, int start, int end)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(40, 4));
        processor.Process(Encoding.UTF8.GetBytes("\u001b[?2027h" + text));
        List<TerminalSearchMatch> matches = [];
        processor.PopulateSearchMatches(needle, matches);
        Assert.Equal(new TerminalSearchMatch(0, start, end), Assert.Single(matches));
    }

    [Theory]
    [InlineData(4, "xxABCDEF", "abcdef")]
    [InlineData(4, "abc界z", "c界z")]
    [InlineData(4, "a   b", "a   b")]
    [InlineData(10, "aAaAa", "aa")]
    [InlineData(10, "é É", "é")]
    [InlineData(10, "ae\u0301b", "\u0301b")]
    [InlineData(10, "a👩‍💻b", "💻b")]
    [InlineData(10, "abc\r\ndef", "c\nd")]
    [InlineData(10, "a\r\n\r\nb", "a\n\nb")]
    [InlineData(10, "a\tB", "a       b")]
    [InlineData(10, "aaa   ", " ")]
    [InlineData(10, "a  b", "  ")]
    public void MatchesPinnedNativeForLiteralCasesWhenAvailable(int columns, string text, string needle)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using BasicVtProcessor managed = new(new TerminalScreen(columns, 6));
        using GhosttyVtProcessor native = new(new TerminalScreen(columns, 6));
        byte[] bytes = Encoding.UTF8.GetBytes("\u001b[?2027h" + text);
        managed.Process(bytes);
        native.Process(bytes);
        List<TerminalSearchMatch> actual = [];
        List<TerminalSearchMatch> expected = [];
        managed.PopulateSearchMatches(needle, actual);
        native.PopulateSearchMatches(needle, expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SearchRefreshesAfterEditsResizeAlternateScreenResetAndHistoryEviction()
    {
        TerminalScreen screen = new(8, 2, 2);
        using BasicVtProcessor processor = new(screen);
        List<TerminalSearchMatch> matches = [];
        processor.Process("needle\r\nneedle\r\nneedle"u8);
        processor.PopulateSearchMatches("needle", matches);
        Assert.Equal(3, matches.Count);
        processor.Process("\u001b[?1049h\u001b[Halt"u8);
        processor.PopulateSearchMatches("needle", matches);
        Assert.Empty(matches);
        processor.PopulateSearchMatches("alt", matches);
        Assert.Single(matches);
        processor.Process("\u001b[?1049l"u8);
        processor.PopulateSearchMatches("needle", matches);
        Assert.Equal(3, matches.Count);
        processor.ResizeScreen(4, 2, 40, 40, reflowOnResize: true);
        processor.PopulateSearchMatches("needle", matches);
        Assert.NotEmpty(matches);
        Assert.All(matches, match => Assert.True(match.EndAbsoluteRow > match.AbsoluteRow));
        processor.Process("\r\none\r\ntwo\r\nthree\r\nfour\r\nfive"u8);
        processor.PopulateSearchMatches("needle", matches);
        Assert.Empty(matches);
        processor.Process("\u001bc"u8);
        processor.PopulateSearchMatches("five", matches);
        Assert.Empty(matches);
    }

    [Fact]
    public void SearchAcrossHistoryAndViewportMatchesNativeWhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        Random random = new(14097);
        string[] tokens = ["a", "b", "c", " ", "界", "😀", "e\u0301", "\r\n"];
        string[] needles = ["a", "ab", "ba", "界", "😀", "\u0301", "c界", "b a"];
        for (int sample = 0; sample < 20; sample++)
        {
            StringBuilder text = new("\u001b[?2027h");
            for (int i = 0; i < 150; i++) text.Append(tokens[random.Next(tokens.Length)]);
            byte[] bytes = Encoding.UTF8.GetBytes(text.ToString());
            using BasicVtProcessor managed = new(new TerminalScreen(7, 3, 200));
            using GhosttyVtProcessor native = new(new TerminalScreen(7, 3, 200));
            managed.Process(bytes);
            native.Process(bytes);
            List<TerminalSearchMatch> actual = [];
            List<TerminalSearchMatch> expected = [];
            foreach (string needle in needles)
            {
                managed.PopulateSearchMatches(needle, actual);
                native.PopulateSearchMatches(needle, expected);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void NativeWrappedRangeClipsToEffectiveHistoryLimitWhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 20);
        using GhosttyVtProcessor processor = new(screen);
        processor.Process("abcdefghi"u8);
        screen.ScrollbackLimit = 0;
        processor.SetViewportOffsetRows(processor.ViewportScrollState.OffsetRows);
        List<TerminalSearchMatch> matches = [];
        processor.PopulateSearchMatches("abcdefghi", matches);
        Assert.Equal(new TerminalSearchMatch(0, 0, 0) { EndAbsoluteRow = 1 }, Assert.Single(matches));
    }

    [Fact]
    public void SearchUsesPublishedBufferDuringSynchronizedOutput()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(20, 4));
        List<TerminalSearchMatch> matches = [];
        processor.Process("before\u001b[?2026h\r\u001b[2Kafter"u8);
        processor.PopulateSearchMatches("before", matches);
        Assert.Single(matches);
        processor.PopulateSearchMatches("after", matches);
        Assert.Empty(matches);
        processor.Process("\u001b[?2026l"u8);
        processor.PopulateSearchMatches("after", matches);
        Assert.Single(matches);
    }

    [Fact]
    public void WarmedSearchReusesNeedleScratchAndDoesNotAllocatePerRow()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(80, 4, 2000));
        for (int i = 0; i < 1000; i++) processor.Process("prefix needle suffix\r\n"u8);
        List<TerminalSearchMatch> matches = new(1024);
        for (int i = 0; i < 10; i++) processor.PopulateSearchMatches("needle", matches);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) processor.PopulateSearchMatches("needle", matches);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1000, matches.Count);
        Assert.Equal(0, allocated);
    }
}
