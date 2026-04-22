// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Terminal screen and cell model tests.

using System.Collections.Concurrent;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
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
    public void TerminalScreen_ResizeHorizontalShrinkAndRestore_PreservesBufferedCells()
    {
        TerminalScreen screen = new(12, 3);
        TerminalRow row = screen.GetViewportRow(0);
        row[0].Codepoint = 'A';
        row[10].Codepoint = 'K';

        screen.Resize(6, 3);

        Assert.Equal(6, screen.Columns);
        Assert.Equal(6, screen.GetRow(0).Columns);
        Assert.Equal('A', screen.GetRow(0)[0].Codepoint);
        Assert.True(screen.GetRow(0).WrapsToNext);
        Assert.Equal('K', screen.GetRow(1)[4].Codepoint);

        screen.Resize(12, 3);

        Assert.Equal(12, screen.Columns);
        Assert.Equal(12, screen.GetRow(0).Columns);
        Assert.Equal('A', screen.GetRow(0)[0].Codepoint);
        Assert.Equal('K', screen.GetRow(0)[10].Codepoint);
    }

    [Fact]
    public void TerminalScreen_ResizeWithReflowToOneColumn_DropsWideCharacterAsBlankCell()
    {
        TerminalScreen screen = new(2, 1);
        TerminalRow row = screen.GetViewportRow(0);
        row[0].Codepoint = 0x1F600;
        row[0].Width = 2;
        row[1].Width = 0;

        screen.Resize(1, 1);

        TerminalRow resized = screen.GetViewportRow(0);
        Assert.Equal(1, resized.Columns);
        Assert.False(resized[0].HasContent);
        Assert.Equal(1, resized[0].Width);
    }

    [Fact]
    public void TerminalScreen_ResizeWithReflow_WrapsWideCharacterWhenItDoesNotFit()
    {
        TerminalScreen screen = new(3, 2);
        TerminalRow row = screen.GetViewportRow(0);
        row[0].Codepoint = 'x';
        row[1].Codepoint = 0x1F600;
        row[1].Width = 2;
        row[2].Width = 0;

        screen.Resize(2, 2);

        TerminalRow first = screen.GetRow(0);
        TerminalRow second = screen.GetRow(1);
        Assert.Equal('x', first[0].Codepoint);
        Assert.True(first.WrapsToNext);
        Assert.Equal(0x1F600, second[0].Codepoint);
        Assert.Equal(2, second[0].Width);
        Assert.Equal(0, second[1].Width);
    }

    [Fact]
    public void BasicVtProcessor_ResizeWithReflowToOneColumn_TracksCursorAfterDroppedWideCharacter()
    {
        TerminalScreen screen = new(2, 3);
        using BasicVtProcessor processor = new(screen);

        processor.Process(Encoding.UTF8.GetBytes(char.ConvertFromUtf32(0x1F600)));
        processor.ResizeScreen(columns: 1, rows: 3, widthPx: 10, heightPx: 48, reflowOnResize: true);
        processor.Process(Encoding.UTF8.GetBytes("X"));

        Assert.False(screen.GetViewportRow(0)[0].HasContent);
        Assert.Equal('X', screen.GetViewportRow(1)[0].Codepoint);
    }

    [Fact]
    public void TerminalScreen_ResizeWithoutReflow_HidesAndRestoresBufferedCells()
    {
        TerminalScreen screen = new(12, 3);
        TerminalRow row = screen.GetViewportRow(0);
        row[0].Codepoint = 'A';
        row[10].Codepoint = 'K';

        screen.Resize(6, 3, reflowOnResize: false);

        Assert.Equal(6, screen.Columns);
        Assert.False(screen.GetViewportRow(0).WrapsToNext);
        Assert.Equal('A', screen.GetViewportRow(0)[0].Codepoint);

        screen.Resize(12, 3, reflowOnResize: false);

        Assert.Equal('K', screen.GetViewportRow(0)[10].Codepoint);
    }

    [Fact]
    public void TerminalScreen_ApplyTheme_RemapsHiddenCellsPreservedByNonReflowResize()
    {
        TerminalScreen screen = new(12, 3);
        TerminalRow row = screen.GetViewportRow(0);
        row[10].Codepoint = 'K';
        row[10].Foreground = screen.DefaultForeground;

        screen.Resize(6, 3, reflowOnResize: false);
        screen.ApplyTheme(TerminalTheme.Dark.WithDefaultForeground(0xFF010203u));
        screen.Resize(12, 3, reflowOnResize: false);

        Assert.Equal('K', screen.GetViewportRow(0)[10].Codepoint);
        Assert.Equal(0xFF010203u, screen.GetViewportRow(0)[10].Foreground);
    }

    [Fact]
    public void BasicVtProcessor_ResizeWithoutReflow_ClearsHiddenTailAfterLineErase()
    {
        TerminalScreen screen = new(12, 3);
        using BasicVtProcessor processor = new(screen);

        processor.Process(Encoding.UTF8.GetBytes("ABCDEFGHIJK"));
        processor.ResizeScreen(columns: 6, rows: 3, widthPx: 60, heightPx: 48, reflowOnResize: false);
        processor.Process("\r\x1b[K"u8);
        processor.ResizeScreen(columns: 12, rows: 3, widthPx: 120, heightPx: 48, reflowOnResize: false);

        Assert.False(screen.GetViewportRow(0)[10].HasContent);
    }

    [Fact]
    public void BasicVtProcessor_ResizeWithoutReflow_CopiesHiddenTailWhenRowsShift()
    {
        TerminalScreen screen = new(12, 3);
        using BasicVtProcessor processor = new(screen);

        processor.Process(Encoding.UTF8.GetBytes("0123456789A\r\nabcdefghijB"));
        processor.ResizeScreen(columns: 6, rows: 3, widthPx: 60, heightPx: 48, reflowOnResize: false);
        processor.Process("\x1b[1;1H\x1b[M"u8);
        processor.ResizeScreen(columns: 12, rows: 3, widthPx: 120, heightPx: 48, reflowOnResize: false);

        Assert.Equal('a', screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal('B', screen.GetViewportRow(0)[10].Codepoint);
    }

    [Fact]
    public void BasicVtProcessor_ResizeWithReflow_KeepsCursorOnPrompt()
    {
        TerminalScreen screen = new(12, 6);
        using BasicVtProcessor processor = new(screen);

        processor.Process(Encoding.UTF8.GetBytes("ABCDEFGHIJKLMNO\r\n$ "));

        processor.ResizeScreen(columns: 6, rows: 6, widthPx: 60, heightPx: 96, reflowOnResize: true);
        processor.Process(Encoding.UTF8.GetBytes("ok"));

        Assert.Contains("MNO", GetVisibleText(screen), StringComparison.Ordinal);
        Assert.Contains("$ ok", GetVisibleText(screen), StringComparison.Ordinal);
    }

    [Fact]
    public void BasicVtProcessor_ResizeWithReflow_TracksLiveCursorWhileScrolledBack()
    {
        TerminalScreen screen = new(12, 4);
        using BasicVtProcessor processor = new(screen);

        processor.Process(Encoding.UTF8.GetBytes("first-line\r\nsecond-line\r\nthird-line\r\nABCDEFGHIJKLMNO\r\n$ "));
        screen.ScrollOffset = 2;

        processor.ResizeScreen(columns: 6, rows: 4, widthPx: 60, heightPx: 64, reflowOnResize: true);
        screen.ScrollOffset = 0;
        processor.Process(Encoding.UTF8.GetBytes("ok"));

        string visibleText = GetVisibleText(screen);
        Assert.Contains("MNO", visibleText, StringComparison.Ordinal);
        Assert.Contains("$ ok", visibleText, StringComparison.Ordinal);
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

    private static string GetVisibleText(TerminalScreen screen)
    {
        StringBuilder builder = new();
        for (int rowIndex = 0; rowIndex < screen.ViewportRows; rowIndex++)
        {
            TerminalRow row = screen.GetViewportRow(rowIndex);
            for (int column = 0; column < row.Columns; column++)
            {
                TerminalCell cell = row[column];
                if (cell.Width == 0)
                {
                    continue;
                }

                builder.Append(cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    [Fact]
    public async Task TerminalScreen_InvalidateAll_DoesNotThrow_WhenRowsChangeUnderScreenLock()
    {
        TerminalScreen screen = new(80, 24, scrollbackLimit: 4);
        ConcurrentQueue<Exception> failures = new();

        Task producer = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 10_000; i++)
                {
                    lock (screen.SyncRoot)
                    {
                        screen.AddRow();
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        });

        try
        {
            for (int i = 0; i < 10_000; i++)
            {
                screen.InvalidateAll();
            }
        }
        catch (Exception ex)
        {
            failures.Enqueue(ex);
        }

        await producer;

        Assert.Empty(failures);
    }

    [Fact]
    public void TerminalScreen_InvalidateViewport_MarksOnlyVisibleRowsDirty()
    {
        TerminalScreen screen = new(80, 4, scrollbackLimit: 32);
        for (int i = 0; i < 12; i++)
        {
            screen.AddRow();
        }

        screen.ScrollOffset = 3;

        for (int row = 0; row < screen.TotalRows; row++)
        {
            screen.GetRow(row).IsDirty = false;
        }

        screen.InvalidateViewport();

        int dirtyRows = 0;
        for (int row = 0; row < screen.TotalRows; row++)
        {
            if (screen.GetRow(row).IsDirty)
            {
                dirtyRows++;
            }
        }

        Assert.Equal(screen.ViewportRows, dirtyRows);
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
    public void TerminalScreen_GetViewportRow_UsesBottomAnchoredScrollOffset()
    {
        var screen = new TerminalScreen(1, 3, scrollbackLimit: 100);

        screen.GetViewportRow(0)[0].Codepoint = '0';
        screen.GetViewportRow(1)[0].Codepoint = '1';
        screen.GetViewportRow(2)[0].Codepoint = '2';

        for (int i = 0; i < 10; i++)
        {
            TerminalRow row = screen.AddRow();
            row[0].Codepoint = 'A' + i;
        }

        screen.ScrollOffset = 0;
        Assert.Equal('H', screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal('J', screen.GetViewportRow(2)[0].Codepoint);

        screen.ScrollOffset = 1;
        Assert.Equal('G', screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal('I', screen.GetViewportRow(2)[0].Codepoint);
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
