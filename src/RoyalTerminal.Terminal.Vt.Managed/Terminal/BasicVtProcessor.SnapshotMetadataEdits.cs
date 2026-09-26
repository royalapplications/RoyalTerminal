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
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        if (_screen.TracksSnapshotMetadata) styles.Write(column, CaptureSnapshotPen());
        WriteCellFromPen(ref row[column], codepoint, width);
    }

    private void WriteStyledCell(TerminalRow row, int column, in TerminalCell cell)
    {
        using GhosttySnapshotPageTracker.RowEdit styles = _screen.EditSnapshotRowMetadata(row);
        if (_screen.TracksSnapshotMetadata) styles.WriteCell(column, in cell);
        row[column] = cell;
    }
}
