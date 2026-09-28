// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Unicode;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Ghostty Screen.selectWord walks its full page list, while xterm.js
/// SelectionService._getWordAt walks wrapped buffer lines beyond the viewport.
/// Windows Terminal TextBuffer word APIs are buffer-bounded too, but classify
/// controls separately. Retain Ghostty's soft-wrap runs and the shared host's
/// Unicode whitespace policy. Never scroll or materialize history to query it.
/// </summary>
public sealed class TerminalWordHistoryTests
{
    private const string Delimiters = " .,\t\r\n";

    public static TheoryData<bool, string, int, int, int, int, int, int, string> HistoryCases
    {
        get
        {
            var data = new TheoryData<bool, string, int, int, int, int, int, int, string>();
            foreach (bool native in new[] { false, true })
            {
                data.Add(native, "abcdefghijklmnopqrst", 2, 2, 0, 0, 5, 3, "abcdefghijklmnopqrst");
                data.Add(native, "abcde\r\nfghijklmnopqrst", 1, 2, 0, 1, 5, 3, "fghijklmnopqrst");
                data.Add(native, "ab  \u2003 cd\r\nzzzzz", 0, 1, 2, 0, 1, 1, "  \u2003 ");
                data.Add(native, "abcd界zabcdef", 4, 0, 0, 0, 4, 2, "abcd界zabcdef");
                data.Add(native, "abcd🙂zabcdef", 1, 1, 0, 0, 4, 2, "abcd🙂zabcdef");
                data.Add(native, "abcd\u0301efghijkl", 0, 1, 0, 0, 2, 2, "abcd\u0301efghijkl");
                data.Add(native, "abcde\r\nfghij\r\nklmno", 1, 1, 0, 1, 5, 1, "fghij");
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(HistoryCases))]
    public void QueriesAndCopiesWholeWordAtAnyViewport(bool native, string input, int column, int row,
        int startColumn, int startRow, int endColumn, int endRow, string expectedText)
    {
        TerminalScreen screen = new(5, 2, 100);
        using IVtProcessor processor = Create(native, screen);
        processor.Process(Encoding.UTF8.GetBytes(input));
        var source = Assert.IsAssignableFrom<ITerminalWordSelectionSource>(processor);
        var export = Assert.IsAssignableFrom<ITerminalBufferSelectionExportSource>(processor);
        TerminalWordExtent expected = new(new(startColumn, startRow), new(endColumn, endRow));
        for (int offset = 0; offset < 3; offset++)
        {
            if (processor is ITerminalViewportScrollSource scroll) scroll.SetViewportOffsetRows((ulong)offset);
            else screen.ScrollOffset = offset;
            var before = (processor as ITerminalViewportScrollSource)?.ViewportScrollState;
            int managedOffset = screen.ScrollOffset;
            Assert.True(source.TryGetWordExtent(new(column, row), Delimiters, out var actual));
            Assert.Equal(expected, actual);
            Assert.Equal(expectedText, export.ReadBufferSelection(
                new(startColumn, startRow, endColumn - 1, endRow), unwrap: true));
            Assert.Equal(before, (processor as ITerminalViewportScrollSource)?.ViewportScrollState);
            Assert.Equal(managedOffset, screen.ScrollOffset);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidAndUnwrittenCellsHaveNoExtent(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 100));
        processor.Process("ab"u8);
        var source = (ITerminalWordSelectionSource)processor;
        foreach (TerminalGridPosition point in new TerminalGridPosition[]
            { new(-1, 0), new(5, 0), new(0, -1), new(0, 2), new(2, 0), new(0, 1), new(0, int.MaxValue) })
        {
            Assert.False(source.TryGetWordExtent(point, Delimiters, out var extent));
            Assert.Equal(default, extent);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RowBudgetClampsWrappedWordAndCopyWithoutLeakingPageSlack(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 1));
        processor.Process("abcdefghijklmnopqrstuvwxyzABCD"u8);
        Assert.True(((ITerminalWordSelectionSource)processor).TryGetWordExtent(new(1, 1), Delimiters, out var extent));
        Assert.Equal(new TerminalWordExtent(new(0, 0), new(5, 2)), extent);
        var export = (ITerminalBufferSelectionExportSource)processor;
        Assert.Equal("pqrstuvwxyzABCD", export.ReadBufferSelection(new(0, 0, 4, 2), unwrap: true));
        Assert.Equal("pqrstuvwxyzABCD", export.ReadBufferSelection(new(0, -100, 4, int.MaxValue), unwrap: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarDelimiterConversionHandlesSupplementaryAndPooledInput(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 100));
        processor.Process(Encoding.UTF8.GetBytes("abcd🙂zzzzzz"));
        string delimiters = new string('x', 200) + "🙂\ud800";
        Assert.True(((ITerminalWordSelectionSource)processor).TryGetWordExtent(new(4, 0), delimiters, out var extent));
        Assert.Equal(new TerminalWordExtent(new(4, 0), new(2, 1)), extent);
        Assert.Equal("🙂", ((ITerminalBufferSelectionExportSource)processor)
            .ReadBufferSelection(new(4, 0, 1, 1), unwrap: true));
    }

    public static TheoryData<bool, int> UnicodeWhitespaceCases
    {
        get
        {
            TheoryData<bool, int> data = new();
            int[] whitespace = [0xa0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005,
                0x2006, 0x2007, 0x2008, 0x2009, 0x200a, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000];
            foreach (bool native in new[] { false, true })
                foreach (int scalar in whitespace) data.Add(native, scalar);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(UnicodeWhitespaceCases))]
    public void NonAsciiUnicodeWhitespaceUsesSharedHostPolicy(bool native, int scalar)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 100));
        string separator = char.ConvertFromUtf32(scalar);
        processor.Process(Encoding.UTF8.GetBytes("aaaaa" + separator + "zzzzzzzzzz"));
        Assert.True(((ITerminalWordSelectionSource)processor).TryGetWordExtent(new(0, 1), "", out var extent));
        if (scalar is 0x2028 or 0x2029)
        {
            // Ghostty's Unicode widths make these zero-width grapheme data,
            // not independently selectable whitespace cells (mode 2027 off).
            Assert.Equal(0, TerminalCellWidthCalculator.GetCodepointWidth(scalar));
            Assert.Equal(new TerminalWordExtent(new(0, 0), new(5, 2)), extent);
            Assert.Equal("aaaaa" + separator + "zzzzzzzzzz", ((ITerminalBufferSelectionExportSource)processor)
                .ReadBufferSelection(new(0, 0, 4, 2), true));
            return;
        }
        Assert.InRange(TerminalCellWidthCalculator.GetCodepointWidth(scalar), 1, 2);
        Assert.Equal(new TerminalGridPosition(0, 1), extent.Start);
        Assert.Equal(1, extent.End.Row);
        Assert.Equal(separator, ((ITerminalBufferSelectionExportSource)processor)
            .ReadBufferSelection(new(extent.Start.Column, extent.Start.Row, extent.End.Column - 1, extent.End.Row), true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbsoluteExportKeepsHardBreaksAndRectangleRows(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 100));
        processor.Process("abcde\r\nfghijklmnopqrst"u8);
        var export = (ITerminalBufferSelectionExportSource)processor;
        Assert.Equal("abcde\nfghijklmnopqrst", export.ReadBufferSelection(new(0, 0, 4, 3), true));
        Assert.Equal("abcde\nfghij\nklmno\npqrst", export.ReadBufferSelection(new(0, 0, 4, 3), false));
        Assert.Equal("bc\ngh\nlm\nqr", export.ReadBufferSelection(new(1, 0, 2, 3, Rectangle: true), true));
        Assert.Equal("fghijklmnopqrst", export.ReadBufferSelection(new(4, 3, 0, 1), true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueriesUseCurrentInputStateDuringPresentationHold(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 100));
        processor.Process("abcde\u001b[?2026hfghijklmnop"u8);
        var source = (ITerminalWordSelectionSource)processor;
        Assert.True(source.TryGetWordExtent(new(1, 2), Delimiters, out var held));
        Assert.Equal(new TerminalWordExtent(new(0, 0), new(1, 3)), held);
        processor.Process("\u001b[?2026l"u8);
        Assert.True(source.TryGetWordExtent(new(1, 2), Delimiters, out var published));
        Assert.Equal(held, published);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongHistoryWordQueryDoesNotAllocateManagedMemory(bool native)
    {
        using IVtProcessor processor = Create(native, new(80, 2, 1000));
        processor.Process(Encoding.ASCII.GetBytes(new string('a', 80 * 200)));
        var source = (ITerminalWordSelectionSource)processor;
        for (int i = 0; i < 20; i++) source.TryGetWordExtent(new(40, 100), Delimiters, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool found = true;
        TerminalWordExtent last = default;
        for (int i = 0; i < 20; i++) found &= source.TryGetWordExtent(new(40, 100), Delimiters, out last);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(found);
        Assert.Equal(new TerminalWordExtent(new(0, 0), new(80, 199)), last);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void AbsoluteHelperRejectsNullScreen()
        => Assert.Throws<ArgumentNullException>(() => TerminalWordSelection.TryResolveAbsolute(null!, default, "", out _));

    [Fact]
    public void AbsoluteQueriesKeepCowReadersAndViewportFallbackIndependent()
    {
        TerminalScreen screen = new(5, 2, 100);
        using BasicVtProcessor processor = new(screen);
        processor.Process("abcdefghijklmnopqrst"u8);
        TerminalScreen frozen = screen.CreateStateCopy();
        Assert.True(TerminalWordSelection.TryResolveAbsolute(frozen, new(2, 2), Delimiters, out var before));
        Assert.Equal(new TerminalWordExtent(new(0, 0), new(5, 3)), before);
        Assert.True(TerminalWordSelection.TryResolve(frozen, new(2, 0), Delimiters, out var viewport));
        Assert.Equal(new TerminalWordExtent(new(0, 0), new(5, 1)), viewport);
        processor.Process("\r\nnew"u8);
        Assert.True(TerminalWordSelection.TryResolveAbsolute(frozen, new(2, 2), Delimiters, out var after));
        Assert.Equal(before, after);
        Assert.Equal(4, frozen.TotalRows);
    }

    [Fact]
    public void AlternateIncidentalHistoryIsNotSelectableOrCopied()
    {
        TerminalScreen screen = new(5, 2, 100);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[?1049habcdefghij\u001b[22J"u8);
        Assert.True(screen.AlternateBufferActive);
        Assert.True(screen.TotalRows > screen.ViewportRows);
        Assert.Equal(0, screen.MaxScrollOffset);
        Assert.False(processor.TryGetWordExtent(new(0, 0), Delimiters, out _));
        Assert.Equal(string.Empty, processor.ReadBufferSelection(new(0, 0, 4, screen.TotalRows - 1), true));
    }

    [Fact]
    public void NativeQueriesRejectDisposedOwner()
    {
        if (!GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native unavailable; rebuild required before final validation.");
        GhosttyVtProcessor processor = new(new(5, 2));
        processor.Dispose();
        Assert.Throws<ObjectDisposedException>(() => processor.TryGetWordExtent(default, "", out _));
        Assert.Throws<ObjectDisposedException>(() => processor.ReadBufferSelection(default, true));
    }

    private static IVtProcessor Create(bool native, TerminalScreen screen)
    {
        if (!native) return new BasicVtProcessor(screen);
        if (!GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native unavailable; rebuild required before final validation.");
        return new GhosttyVtProcessor(screen);
    }
}
