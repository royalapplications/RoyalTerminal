// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Allocation-free logical-line selection matching Ghostty's cell-based boundaries.</summary>
public static class TerminalLineSelection
{
    /// <summary>
    /// Resolves a line in absolute buffer coordinates, following the contract of
    /// <see cref="ITerminalLineSelectionSource.TryGetLineExtent"/>.
    /// The caller must keep the screen stable while querying.
    /// </summary>
    /// <param name="screen">Current screen, including accessible history.</param>
    /// <param name="position">Absolute buffer cell under the pointer.</param>
    /// <param name="whitespace">Trim codepoints; empty uses NUL, ASCII space and tab.</param>
    /// <param name="semanticPromptBoundary">Whether changes in cell semantic content stop selection.</param>
    /// <param name="extent">Selected range with an exclusive end column, or default.</param>
    /// <returns>Whether a non-whitespace line was found.</returns>
    public static bool TryResolve(TerminalScreen screen, TerminalGridPosition position,
        ReadOnlySpan<uint> whitespace, bool semanticPromptBoundary, out TerminalLineExtent extent)
    {
        ArgumentNullException.ThrowIfNull(screen);
        extent = default;
        int firstRow = Math.Max(0, screen.TotalRows - screen.ViewportRows - screen.MaxScrollOffset);
        if ((uint)position.Column >= (uint)screen.Columns || position.Row < firstRow || position.Row >= screen.TotalRows)
            return false;
        if (whitespace.IsEmpty) whitespace = [0, 32, 9];
        TerminalSemanticContent semantic = screen.GetRow(position.Row).ReadOnlyCells[position.Column].SemanticContent;

        int startRow = position.Row, startColumn = position.Column;
        while (true)
        {
            ReadOnlySpan<TerminalCell> cells = screen.GetRow(startRow).ReadOnlyCells;
            if (semanticPromptBoundary)
            {
                while (startColumn > 0 && cells[startColumn - 1].SemanticContent == semantic) startColumn--;
                if (startColumn != 0) break;
            }
            else startColumn = 0;
            if (startRow == firstRow || !screen.GetRow(startRow - 1).WrapsToNext) break;
            if (semanticPromptBoundary && screen.GetRow(startRow - 1).ReadOnlyCells[^1].SemanticContent != semantic) break;
            startRow--;
            startColumn = screen.Columns - 1;
        }

        int endRow = position.Row, endColumn = position.Column;
        while (true)
        {
            TerminalRow row = screen.GetRow(endRow);
            ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
            if (semanticPromptBoundary)
            {
                while (endColumn + 1 < cells.Length && cells[endColumn + 1].SemanticContent == semantic) endColumn++;
                if (endColumn + 1 != cells.Length) break;
            }
            else endColumn = cells.Length - 1;
            if (!row.WrapsToNext) break;
            // Ghostty cannot resolve a line whose final wrap has no successor.
            if (endRow + 1 == screen.TotalRows) return false;
            if (semanticPromptBoundary && screen.GetRow(endRow + 1).ReadOnlyCells[0].SemanticContent != semantic) break;
            endRow++;
            endColumn = 0;
        }

        while (IsTrimmed(screen.GetRow(startRow).ReadOnlyCells[startColumn], whitespace))
        {
            if (startRow == endRow && startColumn == endColumn) return false;
            if (++startColumn == screen.Columns) { startColumn = 0; startRow++; }
        }
        while (IsTrimmed(screen.GetRow(endRow).ReadOnlyCells[endColumn], whitespace))
        {
            if (endColumn-- == 0) { endColumn = screen.Columns - 1; endRow--; }
        }
        ref readonly TerminalCell last = ref screen.GetRow(endRow).ReadOnlyCells[endColumn];
        extent = new(new(startColumn, startRow),
            new(Math.Min(screen.Columns, endColumn + Math.Max(1, (int)last.Width)), endRow));
        return true;
    }

    private static bool IsTrimmed(in TerminalCell cell, ReadOnlySpan<uint> whitespace)
        => !cell.HasContent || cell.Width == 0 || cell.IsWideSpacerHead || whitespace.Contains((uint)cell.Codepoint);
}
