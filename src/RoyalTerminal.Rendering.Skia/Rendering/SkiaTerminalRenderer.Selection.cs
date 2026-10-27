// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class SkiaTerminalRenderer
{
    private void PrepareWideSelectionDamage(TerminalRenderViewport rows, ReadOnlySpan<TerminalHighlightSpan> spans)
    {
        if (SelectionIsRectangle || (spans.IsEmpty && (SelectionStart is null || SelectionEnd is null))) return;

        // A selected right-edge spacer can change the following row's overlay
        // without changing that row's cells. Invalidate before computing image
        // damage as well as text damage. Walk backwards to avoid propagating
        // newly dirtied rows farther than the one dependent row.
        for (int index = rows.Count - 1; index > 0; index--)
        {
            TerminalRenderRow previous = rows[index - 1];
            if (previous.Row.IsDirty && IsSelectedCell(previous.ViewportY, previous.Row.Columns - 1, spans))
                rows[index].Row.IsDirty = true;
        }
    }

    private void ExpandWideSelectionFlags(TerminalRenderViewport rows, int index,
        ReadOnlySpan<TerminalHighlightSpan> spans, Span<CellOverlayFlags> overlays)
    {
        if (spans.IsEmpty && (SelectionStart is null || SelectionEnd is null)) return;
        ReadOnlySpan<TerminalCell> cells = rows[index].Row.ReadOnlyCells;
        if (cells.IsEmpty) return;

        // Ghostty render.zig 4af7e19c7: a non-rectangular selection ending
        // on a spacer head includes the glyph on the next wrapped row.
        if (!SelectionIsRectangle && index > 0)
        {
            TerminalRenderRow previous = rows[index - 1];
            ReadOnlySpan<TerminalCell> precedingCells = previous.Row.ReadOnlyCells;
            if (!precedingCells.IsEmpty && precedingCells[^1].IsWideSpacerHead &&
                IsSelectedCell(previous.ViewportY, precedingCells.Length - 1, spans))
                overlays[0] |= CellOverlayFlags.Selection;
        }

        // Copying either half includes the full glyph. Apply the same overlay
        // to both cells so its foreground and backgrounds agree with copying.
        for (int column = 0; column + 1 < cells.Length; column++)
        {
            if (cells[column].Width == 2 &&
                ((overlays[column] | overlays[column + 1]) & CellOverlayFlags.Selection) != 0)
            {
                overlays[column] |= CellOverlayFlags.Selection;
                overlays[column + 1] |= CellOverlayFlags.Selection;
            }
        }
    }

    private bool IsSelectedCell(int row, int column, ReadOnlySpan<TerminalHighlightSpan> spans)
    {
        if (column < 0) return false;
        if (!spans.IsEmpty)
        {
            for (int i = FindFirstHighlightSpanForRow(spans, row); i < spans.Length && spans[i].Row <= row; i++)
                if (spans[i].Row == row && column >= spans[i].StartColumn && column <= spans[i].EndColumn)
                    return true;
            return false;
        }
        if (SelectionStart is not { } start || SelectionEnd is not { } end) return false;
        if (start.Row > end.Row || (start.Row == end.Row && start.Column > end.Column))
            (start, end) = (end, start);
        return row >= start.Row && row <= end.Row &&
            (row != start.Row || column >= start.Column) && (row != end.Row || column < end.Column);
    }
}
