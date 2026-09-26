// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private int _scrollLeft;
    private int _scrollRight = int.MaxValue;
    private int RightMargin => Math.Min(_scrollRight, _screen.Columns - 1);
    private bool HasHorizontalMargins => _scrollLeft != 0 || RightMargin != _screen.Columns - 1;
    private bool CursorInsideHorizontalMargins => _cursorCol >= _scrollLeft && _cursorCol <= RightMargin;
    private int CursorLeftLimit => _cursorCol >= _scrollLeft ? _scrollLeft : 0;
    private int CursorRightLimit => _cursorCol <= RightMargin ? RightMargin : _screen.Columns - 1;

    private void ResetHorizontalMargins() { _scrollLeft = 0; _scrollRight = int.MaxValue; }
    private void HomeCursor() { _cursorCol = _originMode ? _scrollLeft : 0; _cursorRow = _originMode ? _scrollTop : 0; }
    private void CarriageReturn() => _cursorCol = _originMode ? _scrollLeft : CursorLeftLimit;

    private void SetHorizontalMargins()
    {
        int left = _params.Count > 0 && _params[0] > 0 ? _params[0] - 1 : 0;
        int right = _params.Count > 1 && _params[1] > 0 ? Math.Min(_params[1] - 1, _screen.Columns - 1) : _screen.Columns - 1;
        if (left >= right) return;
        _scrollLeft = left;
        _scrollRight = right;
        ResetDelayedWrap();
        HomeCursor();
    }

    // Full-width operations retain the row/history fast path. Rectangular
    // scrolling copies only the affected cell runs and never creates history.
    private void ScrollRectangle(int top, int bottom, int count, bool down)
    {
        count = Math.Clamp(count, 1, bottom - top + 1);
        int width = RightMargin - _scrollLeft + 1;
        // A wide glyph cannot be split across a rectangle edge. Clear both
        // halves before copying so an unrelated outside cell cannot adopt a
        // copied spacer tail as its own continuation.
        for (int index = top; index <= bottom; index++)
        {
            TerminalRow row = _screen.GetViewportRow(index);
            if ((RightMargin == _screen.Columns - 1 || _scrollLeft < 2) && row.ReadOnlyCells[^1].IsWideSpacerHead)
            {
                row[row.Columns - 1].IsWideSpacerHead = false;
                row[row.Columns - 1].Width = 1;
            }
            if (_scrollLeft > 0 && row.ReadOnlyCells[_scrollLeft - 1].Width == 2)
            {
                ClearSplitWideCell(ref row[_scrollLeft - 1]);
                ClearSplitWideCell(ref row[_scrollLeft]);
            }
            if (RightMargin + 1 < row.Columns && row.ReadOnlyCells[RightMargin].Width == 2)
            {
                ClearSplitWideCell(ref row[RightMargin]);
                ClearSplitWideCell(ref row[RightMargin + 1]);
            }
        }
        _screen.ShiftAnchorsInViewportRows(top, bottom, down ? count : -count, _scrollLeft, RightMargin);
        for (int index = 0; index <= bottom - top; index++)
        {
            int destination = down ? bottom - index : top + index;
            int source = destination + (down ? -count : count);
            TerminalRow row = _screen.GetViewportRow(destination);
            ClearPreservedCellsForMutation(row);
            if (source >= top && source <= bottom)
            {
                TerminalRow sourceRow = _screen.GetViewportRow(source);
                using GhosttySnapshotPageTracker.RowEdit sourceStyles = _screen.EditSnapshotRowMetadata(sourceRow);
                using GhosttySnapshotPageTracker.RowEdit destinationStyles = _screen.EditSnapshotRowMetadata(row);
                if (destinationStyles.ShiftFrom(sourceStyles, _scrollLeft, width, wholeRow: false))
                {
                    Span<TerminalCell> sourceCells = sourceRow.Cells.Slice(_scrollLeft, width);
                    Span<TerminalCell> destinationCells = row.Cells.Slice(_scrollLeft, width);
                    sourceCells.CopyTo(destinationCells);
                    sourceCells.Fill(TerminalCell.Empty(_screen.DefaultForeground, _screen.DefaultBackground));
                }
                else sourceRow.ReadOnlyCells.Slice(_scrollLeft, width).CopyTo(row.Cells.Slice(_scrollLeft, width));
            }
            else EraseCells(row, _scrollLeft, width);
            // Partial-width edits retain row metadata because content outside
            // the margins (including real-edge spacer heads) stays in place.
            NormalizeRowWideCells(row);
            row.IsDirty = true;
        }
        _screen.InvalidateViewport();
    }

    private static void ClearSplitWideCell(ref TerminalCell cell)
    {
        cell.Codepoint = 0;
        cell.Grapheme = null;
        cell.Width = 1;
        cell.IsWideSpacerHead = false;
    }
}
