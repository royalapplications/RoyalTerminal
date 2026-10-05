// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Resolves viewport selections into inclusive, whole-glyph row ranges.</summary>
internal readonly record struct ManagedSnapshotSelection(
    int FirstRow, int LastRow, int FirstColumn, int LastColumn, bool Rectangle, bool Unwrap)
{
    internal static ManagedSnapshotSelection Create(TerminalScreen screen, TerminalSelectionRange selection, bool unwrap)
    {
        selection = selection.Normalize();
        int top = Math.Max(0, screen.TotalRows - screen.ViewportRows - screen.ScrollOffset);
        int firstRow = top + Math.Clamp(selection.StartRow, 0, screen.ViewportRows - 1);
        int lastRow = top + Math.Clamp(selection.EndRow, 0, screen.ViewportRows - 1);
        int firstColumn = Math.Clamp(selection.StartColumn, 0, screen.Columns - 1);
        int lastColumn = Math.Clamp(selection.EndColumn, 0, screen.Columns - 1);
        if ((selection.Rectangle || firstRow == lastRow) && firstColumn > lastColumn)
            (firstColumn, lastColumn) = (lastColumn, firstColumn);

        unwrap &= !selection.Rectangle;
        // Ghostty's formatter includes the wide glyph belonging to an end spacer.
        // Managed rows have no native page boundary, so this also works across pages.
        if (unwrap && lastRow + 1 < screen.TotalRows &&
            screen.GetRow(lastRow).ReadOnlyCells[lastColumn].IsWideSpacerHead)
        {
            lastRow++;
            lastColumn = 0;
        }

        return new(firstRow, lastRow, firstColumn, lastColumn, selection.Rectangle, unwrap);
    }

    internal bool TryGetColumns(TerminalRow row, int absoluteRow, out int start, out int end)
    {
        start = Rectangle || absoluteRow == FirstRow ? FirstColumn : 0;
        end = Rectangle || absoluteRow == LastRow ? LastColumn : row.Columns - 1;
        if (start > end) return false;
        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        if (start > 0)
        {
            if (cells[start].IsWideSpacerHead) return false;
            if (cells[start].Width == 0) start--;
        }
        return true;
    }
}
