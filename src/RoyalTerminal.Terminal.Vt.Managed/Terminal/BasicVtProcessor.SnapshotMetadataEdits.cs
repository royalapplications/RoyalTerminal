// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private void ClearRow(TerminalRow row, uint foreground, uint background, TerminalColorIdentity backgroundIdentity = default)
    {
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        styles.Clear(0, row.PreservedColumns);
        row.Clear(foreground, background, backgroundIdentity);
    }

    private void EraseCells(TerminalRow row, int start, int count)
    {
        if (count <= 0) return;
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        styles.Clear(start, count);
        row.Cells.Slice(start, count).Fill(CreateErasedCell());
    }

    private void EraseCell(TerminalRow row, int column, TerminalCell blank)
    {
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        styles.Clear(column, 1);
        row[column] = blank;
    }

    private void WriteCellFromPen(TerminalRow row, int column, int codepoint, byte width)
    {
        GhosttySnapshotStyle pen = default;
        if (_screen.TracksSnapshotMetadata)
        {
            // Page migration may refuse the old pen; use the accepted style.
            pen = RecordSnapshotCursorStyle(CaptureSnapshotPen());
        }
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        if ((ApplySnapshotCursorStyleDrops() & (_inAltScreen ? 2 : 1)) != 0) pen = default;
        // Native writes the cell's style before hyperlink map growth. That
        // growth may drop the pen for later cells, not restyle this cell.
        TerminalCell cell = default;
        WriteCellFromPen(ref cell, codepoint, width);
        int cellHyperlink = _currentHyperlinkId;
        if (_screen.TracksSnapshotMetadata)
        {
            styles.Write(column, pen);
            cellHyperlink = styles.WriteCursorHyperlink(column, _currentHyperlinkId);
            // A refused cell-map insertion omits this cell's link, not the
            // active OSC 8 cursor. A successful page rebuild can drop that
            // cursor independently; read its authoritative state afterward.
            _currentHyperlinkId = _screen.SnapshotCursorHyperlinkToken(_inAltScreen ? 1 : 0, _currentHyperlinkId);
        }
        cell.HyperlinkId = cellHyperlink;
        row[column] = cell;
        ApplySnapshotCursorStyleDrops();
    }

    private void WriteStyledCell(TerminalRow row, int column, in TerminalCell cell)
    {
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        if (_screen.TracksSnapshotMetadata) styles.WriteCell(column, in cell);
        row[column] = cell;
    }
}
