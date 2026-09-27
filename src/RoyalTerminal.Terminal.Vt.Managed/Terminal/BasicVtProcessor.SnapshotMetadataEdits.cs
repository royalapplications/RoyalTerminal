// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private void ClearRow(TerminalRow row, uint foreground, uint background, TerminalColorIdentity backgroundIdentity = default)
    {
        try { ClearRowCore(row, foreground, background, backgroundIdentity); }
        catch (OutOfMemoryException failure) when (_screen.RecordSnapshotMutationFailure(failure)) { throw; }
    }

    private void ClearRowCore(TerminalRow row, uint foreground, uint background, TerminalColorIdentity backgroundIdentity = default)
    {
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        styles.Clear(0, row.PreservedColumns);
        row.Clear(foreground, background, backgroundIdentity);
    }

    private void EraseCells(TerminalRow row, int start, int count)
    {
        try { EraseCellsCore(row, start, count); }
        catch (OutOfMemoryException failure) when (_screen.RecordSnapshotMutationFailure(failure)) { throw; }
    }

    private void EraseCellsCore(TerminalRow row, int start, int count)
    {
        if (count <= 0) return;
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        Span<TerminalCell> destination = row.Cells.Slice(start, count);
        styles.Clear(start, count);
        destination.Fill(CreateErasedCell());
    }

    private void EraseCell(TerminalRow row, int column, TerminalCell blank)
    {
        try { EraseCellCore(row, column, blank); }
        catch (OutOfMemoryException failure) when (_screen.RecordSnapshotMutationFailure(failure)) { throw; }
    }

    private void EraseCellCore(TerminalRow row, int column, TerminalCell blank)
    {
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        ref TerminalCell destination = ref row[column];
        styles.Clear(column, 1);
        destination = blank;
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
        ref TerminalCell destination = ref row[column];
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
        destination = cell;
        ApplySnapshotCursorStyleDrops();
    }

    private void WriteStyledCell(TerminalRow row, int column, in TerminalCell cell)
    {
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        ref TerminalCell destination = ref row[column];
        if (_screen.TracksSnapshotMetadata) styles.WriteCell(column, in cell);
        destination = cell;
    }
}
