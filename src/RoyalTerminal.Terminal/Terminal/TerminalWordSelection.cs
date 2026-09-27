// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>A word's cell range, in the coordinate space of the resolving API.</summary>
/// <param name="Start">First selected cell (inclusive).</param>
/// <param name="End">Column immediately after the last selected cell, on that cell's row.</param>
public readonly record struct TerminalWordExtent(TerminalGridPosition Start, TerminalGridPosition End);

/// <summary>Allocation-free word boundaries for both terminal engines' shared cell model.</summary>
public static class TerminalWordSelection
{
    /// <summary>
    /// Finds the contiguous delimiter or non-delimiter run under a viewport cell.
    /// Only soft-wrapped row boundaries are crossed. Wide-character spacers use
    /// their owning glyph; unwritten cells stop selection. Unicode whitespace
    /// and characters in <paramref name="delimiters"/> are delimiters.
    /// The caller must keep the screen stable throughout the operation.
    /// </summary>
    /// <param name="screen">Screen whose visible rows are searched; off-viewport history is excluded.</param>
    /// <param name="position">Cell under the pointer, in viewport coordinates.</param>
    /// <param name="delimiters">Additional delimiter characters (UTF-16).</param>
    /// <param name="extent">The resolved range, or the default value if the cell is empty or outside the viewport.</param>
    /// <returns>Whether a written word or delimiter run was found.</returns>
    public static bool TryResolve(TerminalScreen screen, TerminalGridPosition position,
        ReadOnlySpan<char> delimiters, out TerminalWordExtent extent)
    {
        ArgumentNullException.ThrowIfNull(screen);
        extent = default;
        if ((uint)position.Row >= (uint)screen.ViewportRows) return false;
        int firstRow = screen.ViewportTopAbsoluteRow;
        if (!TryResolveCore(screen, new(position.Column, firstRow + position.Row), delimiters,
            firstRow, Math.Min(screen.TotalRows, firstRow + screen.ViewportRows), out TerminalWordExtent absolute))
            return false;
        extent = new(new(absolute.Start.Column, absolute.Start.Row - firstRow),
            new(absolute.End.Column, absolute.End.Row - firstRow));
        return true;
    }

    /// <summary>
    /// Finds a word over all accessible rows, without changing the viewport or copying history.
    /// Non-scrollable alternate-buffer or zero-history-policy prefixes are excluded.
    /// Soft-wrap, wide-cell and delimiter rules match <see cref="TryResolve"/>.
    /// The caller must keep the screen stable throughout the operation.
    /// </summary>
    /// <param name="screen">Screen whose current buffer is searched.</param>
    /// <param name="position">Absolute, top-anchored buffer cell.</param>
    /// <param name="delimiters">Additional UTF-16 delimiter characters.</param>
    /// <param name="extent">Absolute range, or default if the cell is empty or out of range.</param>
    /// <returns>Whether a written word or delimiter run was found.</returns>
    public static bool TryResolveAbsolute(TerminalScreen screen, TerminalGridPosition position,
        ReadOnlySpan<char> delimiters, out TerminalWordExtent extent)
    {
        ArgumentNullException.ThrowIfNull(screen);
        int firstRow = Math.Max(0, screen.TotalRows - screen.ViewportRows - screen.MaxScrollOffset);
        return TryResolveCore(screen, position, delimiters, firstRow, screen.TotalRows, out extent);
    }

    private static bool TryResolveCore(TerminalScreen screen, TerminalGridPosition position,
        ReadOnlySpan<char> delimiters, int firstRow, int endRowExclusive, out TerminalWordExtent extent)
    {
        extent = default;
        if (position.Row < firstRow || position.Row >= endRowExclusive ||
            (uint)position.Column >= (uint)screen.Columns ||
            !TryGetCodepoint(screen, position.Column, position.Row, endRowExclusive, out int codepoint)) return false;

        // Ghostty Screen.selectWord (#14354/#14391): classify cells rather than
        // UTF-16 offsets, and check the wrap flag on the row being left. Unlike
        // xterm.js's string/recursive walk this needs no row strings or recursion.
        // WT TextBuffer.GetWordStart/End also uses runs, but separates controls
        // from delimiters. Keep RoyalTerminal's whitespace + delimiter policy.
        bool boundary = IsDelimiter(codepoint, delimiters);
        int startColumn = position.Column, startRow = position.Row;
        while (true)
        {
            int column = startColumn - 1, row = startRow;
            if (column < 0)
            {
                if (row == firstRow || !screen.GetRow(row - 1).WrapsToNext) break;
                row--;
                column = screen.Columns - 1;
            }
            if (!TryGetCodepoint(screen, column, row, endRowExclusive, out codepoint) ||
                IsDelimiter(codepoint, delimiters) != boundary) break;
            startColumn = column; startRow = row;
        }

        int endColumn = position.Column, endRow = position.Row;
        while (true)
        {
            int column = endColumn + 1, row = endRow;
            if (column == screen.Columns)
            {
                if (row + 1 >= endRowExclusive || !screen.GetRow(row).WrapsToNext) break;
                row++;
                column = 0;
            }
            if (!TryGetCodepoint(screen, column, row, endRowExclusive, out codepoint) ||
                IsDelimiter(codepoint, delimiters) != boundary) break;
            endColumn = column; endRow = row;
        }
        extent = new(new(startColumn, startRow), new(endColumn + 1, endRow));
        return true;
    }

    private static bool TryGetCodepoint(TerminalScreen screen, int column, int row,
        int endRowExclusive, out int codepoint)
    {
        codepoint = 0;
        ReadOnlySpan<TerminalCell> cells = screen.GetRow(row).ReadOnlyCells;
        if ((uint)column >= (uint)cells.Length) return false;
        ref readonly TerminalCell cell = ref cells[column];
        if (cell.IsWideSpacerHead)
        {
            if (row + 1 >= endRowExclusive) return false;
            ReadOnlySpan<TerminalCell> next = screen.GetRow(row + 1).ReadOnlyCells;
            if (next.IsEmpty || next[0].Width != 2) return false;
            codepoint = next[0].Codepoint;
        }
        else if (cell.Width == 0)
        {
            if (column == 0 || cells[column - 1].Width != 2) return false;
            codepoint = cells[column - 1].Codepoint;
        }
        else codepoint = cell.Codepoint;
        return codepoint != 0;
    }

    private static bool IsDelimiter(int codepoint, ReadOnlySpan<char> delimiters)
    {
        if (!Rune.IsValid(codepoint)) return false;
        Rune rune = new(codepoint);
        if (Rune.IsWhiteSpace(rune)) return true;
        if (rune.IsBmp) return delimiters.Contains((char)codepoint);
        Span<char> encoded = stackalloc char[2];
        rune.EncodeToUtf16(encoded);
        return delimiters.IndexOf(encoded) >= 0;
    }
}
