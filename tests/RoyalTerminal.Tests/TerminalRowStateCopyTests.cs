// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalRowStateCopyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void StateCopy_DetachesBeforeEachCellMutation(int mutation)
    {
        TerminalRow source = new(4);
        source[0].Codepoint = 'A';
        source[0].Grapheme = "A\u0301";
        source[0].HyperlinkId = 7;
        source.WrapsToNext = true;
        TerminalRow copy = source.CreateStateCopy();
        switch (mutation)
        {
            case 0: copy[0].Codepoint = 'B'; break;
            case 1: copy.Cells[0].Codepoint = 'B'; break;
            case 2: copy.Clear(); break;
            case 3: copy.CopyFrom(new TerminalRow(4)); break;
            case 4: copy.CopyActiveFrom(new TerminalRow(4)); break;
            case 5: copy.ClearPreservedCellsFrom(0); break;
            case 6: copy.ResolveCellColors(TerminalTheme.Dark.WithDefaultForeground(0xFF123456)); break;
        }

        Assert.Equal('A', source.ReadOnlyCells[0].Codepoint);
        Assert.Equal("A\u0301", source.ReadOnlyCells[0].Grapheme);
        Assert.Equal(7, source.ReadOnlyCells[0].HyperlinkId);
        Assert.True(source.WrapsToNext);
        Assert.NotEqual(0xFF123456u, source.ReadOnlyCells[0].Foreground);
        // The other side must also detach if it is the first one to be edited.
        TerminalRow secondCopy = source.CreateStateCopy();
        source[0].Codepoint = 'C';
        Assert.Equal('A', secondCopy.ReadOnlyCells[0].Codepoint);
    }

    [Fact]
    public void StateCopy_PreservesHiddenColumnsThroughIndependentResize()
    {
        TerminalRow source = new(6);
        source[5].Codepoint = 'Z';
        source.Resize(3);
        TerminalRow copy = source.CreateStateCopy();
        copy.Resize(8);
        copy[5].Codepoint = 'X';
        Assert.Equal(3, source.Columns);
        Assert.Equal(6, source.PreservedColumns);
        Assert.Equal('Z', source.ReadOnlyPreservedCells[5].Codepoint);
        source.ClearPreservedCellsFrom(3);
        Assert.Equal('X', copy.ReadOnlyCells[5].Codepoint);
        Assert.Equal(8, copy.PreservedColumns);
    }

    [Fact]
    public void ScreenStateCopy_OnlyCopiesRowMetadataBeforeMutation()
    {
        TerminalScreen screen = new(80, 24, 1_000);
        for (int row = 0; row < 1_000; row++) screen.AddRow();
        screen.GetRow(0)[0].Codepoint = 'A';
        _ = screen.CreateStateCopy(); // JIT warmup
        long before = GC.GetAllocatedBytesForCurrentThread();
        TerminalScreen copy = screen.CreateStateCopy();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Deep-copying80 cells per row costs over3MiB. Metadata is under100KiB.
        Assert.True(allocated < 100_000, $"Unexpected state-copy allocations: {allocated}");
        copy.AddRow(); // Recycles an evicted row, which must detach before clearing.
        Assert.Equal('A', screen.GetRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal(0, copy.GetRow(copy.TotalRows - 1).ReadOnlyCells[0].Codepoint);
    }
}
