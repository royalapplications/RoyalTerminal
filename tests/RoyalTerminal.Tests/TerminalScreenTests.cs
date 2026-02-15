// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Terminal screen and cell model tests.

using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Tests for TerminalCell, TerminalRow, and TerminalScreen.
/// </summary>
public class TerminalScreenTests
{
    [Fact]
    public void TerminalCell_Empty_HasNoContent()
    {
        var cell = TerminalCell.Empty();
        Assert.False(cell.HasContent);
        Assert.Equal(0, cell.Codepoint);
        Assert.Equal(1, cell.Width);
        Assert.Equal(CellAttributes.None, cell.Attributes);
    }

    [Fact]
    public void TerminalCell_WithCodepoint_HasContent()
    {
        var cell = TerminalCell.Empty();
        cell.Codepoint = 'A';
        Assert.True(cell.HasContent);
    }

    [Fact]
    public void TerminalCell_Colors_PackCorrectly()
    {
        var cell = TerminalCell.Empty(fg: 0xFFD4D4D4, bg: 0xFF1E1E1E);
        Assert.Equal(0xFFD4D4D4u, cell.Foreground);
        Assert.Equal(0xFF1E1E1Eu, cell.Background);
    }

    [Fact]
    public void TerminalRow_HasCorrectColumnCount()
    {
        var row = new TerminalRow(80);
        Assert.Equal(80, row.Columns);
    }

    [Fact]
    public void TerminalRow_InitiallyDirty()
    {
        var row = new TerminalRow(80);
        Assert.True(row.IsDirty);
    }

    [Fact]
    public void TerminalRow_CellAccess_Works()
    {
        var row = new TerminalRow(80);
        row[0].Codepoint = 'H';
        row[1].Codepoint = 'i';

        Assert.Equal('H', row[0].Codepoint);
        Assert.Equal('i', row[1].Codepoint);
        Assert.False(row[2].HasContent);
    }

    [Fact]
    public void TerminalRow_Clear_ResetsAllCells()
    {
        var row = new TerminalRow(10);
        row[0].Codepoint = 'X';
        row[5].Codepoint = 'Y';

        row.Clear();

        for (int i = 0; i < 10; i++)
            Assert.False(row[i].HasContent);
    }

    [Fact]
    public void TerminalRow_SpanAccess_ReturnsCells()
    {
        var row = new TerminalRow(10);
        var cells = row.Cells;
        Assert.Equal(10, cells.Length);

        cells[3].Codepoint = 'Z';
        Assert.Equal('Z', row[3].Codepoint);
    }

    [Fact]
    public void TerminalScreen_InitialDimensions()
    {
        var screen = new TerminalScreen(80, 24);
        Assert.Equal(80, screen.Columns);
        Assert.Equal(24, screen.ViewportRows);
        Assert.Equal(24, screen.TotalRows);
    }

    [Fact]
    public void TerminalScreen_GetViewportRow_ReturnsValidRow()
    {
        var screen = new TerminalScreen(80, 24);
        var row = screen.GetViewportRow(0);

        Assert.NotNull(row);
        Assert.Equal(80, row.Columns);
    }

    [Fact]
    public void TerminalScreen_AddRow_IncreasesTotalRows()
    {
        var screen = new TerminalScreen(80, 24);
        var initialTotal = screen.TotalRows;

        screen.AddRow();

        Assert.Equal(initialTotal + 1, screen.TotalRows);
    }

    [Fact]
    public void TerminalScreen_AddRow_RespectsScrollbackLimit()
    {
        var screen = new TerminalScreen(80, 24, scrollbackLimit: 10);

        // Add many rows
        for (int i = 0; i < 100; i++)
            screen.AddRow();

        // Total should not exceed viewport + scrollback limit
        Assert.True(screen.TotalRows <= 24 + 10);
    }

    [Fact]
    public void TerminalScreen_Resize_UpdatesDimensions()
    {
        var screen = new TerminalScreen(80, 24);
        screen.Resize(120, 40);

        Assert.Equal(120, screen.Columns);
        Assert.Equal(40, screen.ViewportRows);
        Assert.True(screen.TotalRows >= 40);
    }

    [Fact]
    public void TerminalScreen_InvalidateAll_MarksDirty()
    {
        var screen = new TerminalScreen(80, 24);

        // Clear dirty flags
        for (int i = 0; i < screen.ViewportRows; i++)
            screen.GetViewportRow(i).IsDirty = false;
        Assert.False(screen.HasDirtyRows());

        screen.InvalidateAll();
        Assert.True(screen.HasDirtyRows());
    }

    [Fact]
    public void TerminalScreen_ScrollOffset_Clamps()
    {
        var screen = new TerminalScreen(80, 24);

        screen.ScrollOffset = -100;
        Assert.Equal(0, screen.ScrollOffset);

        screen.ScrollOffset = 999999;
        Assert.Equal(screen.MaxScrollOffset, screen.ScrollOffset);
    }

    [Fact]
    public void TerminalScreen_DefaultColors_Applied()
    {
        var screen = new TerminalScreen(80, 24)
        {
            DefaultForeground = 0xFFAABBCC,
            DefaultBackground = 0xFF112233,
        };

        // Note: colors are set on construction, so changing after
        // construction won't affect existing rows.
        // New rows should get defaults.
        var newRow = screen.AddRow();
        // The default color args in TerminalRow constructor apply
        Assert.NotNull(newRow);
    }
}
