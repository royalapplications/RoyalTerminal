// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private (int Start, int End) GetLineEraseRange(TerminalRow row, int mode)
    {
        int start = mode == 0 ? _cursorCol : 0;
        int end = mode == 1 ? _cursorCol + 1 : row.Columns;
        // Expand before releasing metadata or filling: repairing a split pair
        // afterward would retain the old half's erase color and change the
        // allocator retirement order. Selective EL uses the same boundaries.
        if (start > 0 && row.ReadOnlyCells[start].Width == 0 && !row.ReadOnlyCells[start].IsWideSpacerHead) start--;
        if (end < row.Columns && row.ReadOnlyCells[end - 1].Width == 2) end++;
        return (start, end);
    }

    // Screen.splitCellBoundary clears both halves with the current erase
    // background, including their allocator references, before a character
    // edit changes positions. A boundary can be one past the final column.
    private void SplitCharacterEditBoundary(TerminalRow row, int boundary)
    {
        if (boundary == row.Columns)
        {
            if (row.WrapsToNext && row.ReadOnlyCells[^1].IsWideSpacerHead)
                EraseCells(row, row.Columns - 1, 1);
            return;
        }

        if (boundary <= 1 && row.IsWrapContinuation && row.ReadOnlyCells[0].Width == 2)
            ErasePreviousWideSpacerHead();
        if (boundary > 0 && row.ReadOnlyCells[boundary - 1].Width == 2)
            EraseCells(row, boundary - 1, 2);
    }
}
