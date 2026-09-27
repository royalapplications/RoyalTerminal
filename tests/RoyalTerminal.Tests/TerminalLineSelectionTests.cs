// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Ghostty Screen.selectLine supplies trimming and semantic-content boundaries.
/// WT TerminalSelection.cpp and xterm.js SelectionService select wrapped buffer
/// rows too, but extend to physical row edges. The shared host now follows
/// Ghostty's trimmed/semantic behavior, with its existing blank-line fallback.
/// </summary>
public sealed class TerminalLineSelectionTests
{
    public static TheoryData<string, int, int, int, int, int, int, string> Cases => new()
    {
        { "  ab ", 0, 0, 2, 0, 4, 0, "ab" },
        { "abcdefghijklmnopqrst", 2, 2, 0, 0, 5, 3, "abcdefghijklmnopqrst" },
        { "abcde\r\n  fg  h", 0, 2, 2, 1, 2, 2, "fg  h" },
        { "abcd界zabcdef", 4, 0, 0, 0, 4, 2, "abcd界zabcdef" },
        { "  界", 3, 0, 2, 0, 4, 0, "界" },
        { "\u001b[8mhidden", 1, 1, 0, 0, 1, 1, "hidden" },
        { "a\u0301bcdefghijkl", 0, 1, 0, 0, 2, 2, "a\u0301bcdefghijkl" },
        { "abcde\r\nfghij\r\nklmno", 1, 1, 0, 1, 5, 1, "fghij" },
        { "\u2003abc\u2003", 2, 0, 0, 0, 5, 0, "\u2003abc\u2003" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ManagedLineRanges(string input, int column, int row, int startColumn, int startRow,
        int endColumn, int endRow, string text)
        => AssertCase(false, input, column, row, startColumn, startRow, endColumn, endRow, text);

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeLineRanges(string input, int column, int row, int startColumn, int startRow,
        int endColumn, int endRow, string text)
        => AssertCase(true, input, column, row, startColumn, startRow, endColumn, endRow, text);

    private static void AssertCase(bool native, string input, int column, int row, int startColumn, int startRow,
        int endColumn, int endRow, string text)
    {
        TerminalScreen screen = new(5, 2, 100);
        using IVtProcessor processor = Create(native, screen);
        processor.Process(Encoding.UTF8.GetBytes(input));
        var source = (ITerminalLineSelectionSource)processor;
        var export = (ITerminalBufferSelectionExportSource)processor;
        for (int offset = 0; offset < 3; offset++)
        {
            if (processor is ITerminalViewportScrollSource scroll) scroll.SetViewportOffsetRows((ulong)offset);
            else screen.ScrollOffset = offset;
            var before = (processor as ITerminalViewportScrollSource)?.ViewportScrollState;
            int managedOffset = screen.ScrollOffset;
            Assert.True(source.TryGetLineExtent(new(column, row), [], true, out var extent));
            Assert.Equal(new TerminalLineExtent(new(startColumn, startRow), new(endColumn, endRow)), extent);
            Assert.Equal(text, export.ReadBufferSelection(new(startColumn, startRow, endColumn - 1, endRow), true));
            Assert.Equal(before, (processor as ITerminalViewportScrollSource)?.ViewportScrollState);
            Assert.Equal(managedOffset, screen.ScrollOffset);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SemanticBoundariesApplyWithinRowsAndAcrossWraps(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 100));
        processor.Process("\u001b]133;A\a$ \u001b]133;B\acmd\u001b]133;C\aout"u8);
        var source = (ITerminalLineSelectionSource)processor;
        Assert.True(source.TryGetLineExtent(new(0, 0), [], true, out var prompt));
        Assert.Equal(new TerminalLineExtent(new(0, 0), new(1, 0)), prompt);
        Assert.True(source.TryGetLineExtent(new(3, 0), [], true, out var input));
        Assert.Equal(new TerminalLineExtent(new(2, 0), new(5, 0)), input);
        Assert.True(source.TryGetLineExtent(new(1, 1), [], true, out var output));
        Assert.Equal(new TerminalLineExtent(new(0, 1), new(3, 1)), output);
        Assert.True(source.TryGetLineExtent(new(3, 0), [], false, out var whole));
        Assert.Equal(new TerminalLineExtent(new(0, 0), new(3, 1)), whole);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomWhitespaceCanPreserveSpacesOrTrimWideScalars(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 100));
        processor.Process(" ab  "u8);
        var source = (ITerminalLineSelectionSource)processor;
        Assert.True(source.TryGetLineExtent(new(1, 0), [0], false, out var spaces));
        Assert.Equal(new TerminalLineExtent(new(0, 0), new(5, 0)), spaces);
        processor.Process(Encoding.UTF8.GetBytes("\u001bc🙂ab🙂"));
        Assert.True(source.TryGetLineExtent(new(2, 0), [0x1f642], false, out var trimmed));
        Assert.Equal(new TerminalLineExtent(new(2, 0), new(4, 0)), trimmed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryBudgetRetrimsAtTheAccessiblePrefix(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 1));
        processor.Process("abcdefghij  klm     zzz"u8);
        Assert.True(((ITerminalLineSelectionSource)processor).TryGetLineExtent(new(1, 2), [], false, out var line));
        Assert.Equal(new TerminalLineExtent(new(2, 0), new(3, 2)), line);
        Assert.Equal("klm     zzz", ((ITerminalBufferSelectionExportSource)processor)
            .ReadBufferSelection(new(2, 0, 2, 2), true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidEmptyAndWhitespaceOnlyLinesAreNotSelected(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 100));
        var source = (ITerminalLineSelectionSource)processor;
        foreach (string input in new[] { "", "    ", "\u001b[44m\u001b[2K" })
        {
            processor.Process(Encoding.UTF8.GetBytes("\u001bc" + input));
            foreach (TerminalGridPosition point in new TerminalGridPosition[]
                { new(0, 0), new(-1, 0), new(5, 0), new(0, -1), new(0, int.MaxValue) })
            {
                Assert.False(source.TryGetLineExtent(point, [], true, out var extent));
                Assert.Equal(default, extent);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongHistoryLineQueryAllocatesNoManagedMemory(bool native)
    {
        using IVtProcessor processor = Create(native, new(80, 2, 1000));
        processor.Process(Encoding.ASCII.GetBytes(new string('a', 80 * 200)));
        var source = (ITerminalLineSelectionSource)processor;
        for (int i = 0; i < 20; i++) source.TryGetLineExtent(new(40, 100), [], true, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool found = true;
        TerminalLineExtent extent = default;
        for (int i = 0; i < 20; i++) found &= source.TryGetLineExtent(new(40, 100), [], true, out extent);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(found);
        Assert.Equal(new TerminalLineExtent(new(0, 0), new(80, 199)), extent);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueriesUseInputStateDuringPresentationHold(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 100));
        processor.Process("abcde\u001b[?2026hfghijklmnop"u8);
        var source = (ITerminalLineSelectionSource)processor;
        Assert.True(source.TryGetLineExtent(new(1, 2), [], true, out var held));
        Assert.Equal(new TerminalLineExtent(new(0, 0), new(1, 3)), held);
        processor.Process("\u001b[?2026l"u8);
        Assert.True(source.TryGetLineExtent(new(1, 2), [], true, out var published));
        Assert.Equal(held, published);
    }

    [Fact]
    public void DanglingWrapCannotProduceACompleteLine()
    {
        TerminalScreen screen = new(5, 2, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process("abcdefghij"u8);
        screen.GetRow(1).WrapsToNext = true;
        Assert.False(processor.TryGetLineExtent(new(0, 0), [], false, out _));
    }

    [Fact]
    public void CowReaderKeepsOldLineAndNullIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => TerminalLineSelection.TryResolve(null!, default, [], true, out _));
        TerminalScreen screen = new(5, 2, 100);
        using BasicVtProcessor processor = new(screen);
        processor.Process("abcdefghijklmnopqrst"u8);
        TerminalScreen frozen = screen.CreateStateCopy();
        processor.Process("\r\nnew"u8);
        Assert.True(TerminalLineSelection.TryResolve(frozen, new(2, 2), [], true, out var extent));
        Assert.Equal(new TerminalLineExtent(new(0, 0), new(5, 3)), extent);
    }

    [Fact]
    public void SeededSemanticHistoryMatrixMatchesNative()
    {
        if (!GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native unavailable; rebuild required before final validation.");
        Random random = new(14391);
        string[] tokens = ["a", "bc", "界", " ", "\r\n", "\u001b]133;A\a", "\u001b]133;B\a",
            "\u001b]133;C\a", "\u001b[44m", "\u001b[0m", "e\u0301"];
        for (int sample = 0; sample < 30; sample++)
        {
            TerminalScreen screen = new(7, 3, 5);
            using BasicVtProcessor managed = new(screen);
            using GhosttyVtProcessor native = new(new(7, 3, 5));
            StringBuilder input = new();
            for (int i = 0; i < 100; i++) input.Append(tokens[random.Next(tokens.Length)]);
            byte[] bytes = Encoding.UTF8.GetBytes(input.ToString());
            managed.Process(bytes);
            native.Process(bytes);
            Assert.Equal((ulong)screen.TotalRows, native.ViewportScrollState.TotalRows);
            foreach (bool semantic in new[] { false, true })
            {
                for (int row = 0; row < screen.TotalRows; row++)
                {
                    for (int column = 0; column < screen.Columns; column++)
                    {
                        ReadOnlySpan<uint> trim = (sample & 1) == 0 ? [] : [0];
                        bool actualFound = managed.TryGetLineExtent(new(column, row), trim, semantic, out var actual);
                        bool nativeFound = native.TryGetLineExtent(new(column, row), trim, semantic, out var expected);
                        Assert.True(actualFound == nativeFound, $"sample={sample}, cell={column},{row}, semantic={semantic}");
                        Assert.Equal(expected, actual);
                    }
                }
            }
        }
    }

    [Fact]
    public void NativeQueryRejectsDisposedOwner()
    {
        using IVtProcessor processor = Create(true, new(5, 2));
        processor.Dispose();
        Assert.Throws<ObjectDisposedException>(() => ((ITerminalLineSelectionSource)processor)
            .TryGetLineExtent(default, [], true, out _));
    }

    private static IVtProcessor Create(bool native, TerminalScreen screen)
    {
        if (!native) return new BasicVtProcessor(screen);
        if (!GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native unavailable; rebuild required before final validation.");
        return new GhosttyVtProcessor(screen);
    }
}
