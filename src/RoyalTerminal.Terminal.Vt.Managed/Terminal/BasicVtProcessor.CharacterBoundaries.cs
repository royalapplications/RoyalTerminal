// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
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
