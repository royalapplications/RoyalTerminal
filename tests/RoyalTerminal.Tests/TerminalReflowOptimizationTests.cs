// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

public class TerminalReflowOptimizationTests
{
    [Fact]
    public void RecycledScrollbackRowClearsCellsAndMetadataWithoutAllocating()
    {
        TerminalScreen screen = new(12, 2, scrollbackLimit: 1);
        screen.AddRow();
        TerminalRow evicted = screen.GetRow(0);
        evicted[0].Grapheme = "a\u0301";
        evicted[0].HyperlinkId = 42;
        evicted[0].Attributes = CellAttributes.Bold;
        evicted.WrapsToNext = true;
        evicted.IsTransientResizeRow = true;

        TerminalRow recycled = screen.AddRow();

        Assert.Same(evicted, recycled);
        Assert.False(recycled.WrapsToNext);
        Assert.False(recycled.IsTransientResizeRow);
        Assert.True(recycled.IsDirty);
        Assert.Equal(3, screen.TotalRows);
        for (int column = 0; column < recycled.Columns; column++)
        {
            Assert.Equal(TerminalCell.Empty(screen.DefaultForeground, screen.DefaultBackground), recycled[column]);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
        {
            screen.AddRow();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void BulkReflowPreservesStyledGraphemesAndHyperlinks(int columns)
    {
        TerminalScreen screen = new(8, 2);
        TerminalCell[] expected = new TerminalCell[8];
        for (int column = 0; column < 8; column++)
        {
            TerminalCell cell = TerminalCell.Empty();
            cell.Codepoint = 'A' + column;
            cell.Grapheme = column % 2 == 0 ? "e\u0301" : null;
            cell.Foreground = 0xFF000000u + (uint)column;
            cell.Attributes = CellAttributes.Bold | CellAttributes.Italic;
            cell.HyperlinkId = column + 1;
            cell.UnderlineStyle = TerminalUnderlineStyle.Curly;
            cell.HasUnderlineColor = true;
            cell.UnderlineColor = 0xFF00FF00;
            cell.Decorations = CellDecorations.Overline;
            screen.GetRow(0)[column] = cell;
            expected[column] = cell;
        }

        screen.Resize(columns, 2);

        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index], screen.GetRow(index / columns)[index % columns]);
        }

        screen.Resize(8, 2);
        Assert.Equal(expected, screen.GetRow(0).ReadOnlyCells.ToArray());
    }

    [Fact]
    public void BulkReflowMapsCursorInsideWidePairAfterThePair()
    {
        TerminalScreen screen = new(8, 2);
        TerminalRow row = screen.GetRow(0);
        row[0].Codepoint = 'A';
        row[1].Codepoint = 0x754C;
        row[1].Width = 2;
        row[2].Width = 0;
        row[3].Codepoint = 'B';

        TerminalGridPosition cursor = screen.Resize(
            4, 2, reflowOnResize: true, trackedViewportPosition: new TerminalGridPosition(2, 0));

        Assert.Equal(new TerminalGridPosition(3, 0), cursor);
        Assert.Equal(2, screen.GetRow(0)[1].Width);
        Assert.Equal(0, screen.GetRow(0)[2].Width);
    }

    [Fact]
    public void ReflowNormalizesMalformedWideSpacerAndInitializesRowRemainder()
    {
        TerminalScreen screen = new(4, 2);
        TerminalRow row = screen.GetRow(0);
        row[0].Codepoint = 0x754C;
        row[0].Width = 2;
        row[0].Attributes = CellAttributes.Bold;
        row[1].Width = 0;
        row[1].Grapheme = "stale";
        row[1].HyperlinkId = 99;

        screen.Resize(3, 2);

        Assert.Null(screen.GetRow(0)[1].Grapheme);
        Assert.Equal(0, screen.GetRow(0)[1].HyperlinkId);
        Assert.Equal(CellAttributes.Bold, screen.GetRow(0)[1].Attributes);
        Assert.Equal(TerminalCell.Empty(screen.DefaultForeground, screen.DefaultBackground), screen.GetRow(0)[2]);
    }
}
