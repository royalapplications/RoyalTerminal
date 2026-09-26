// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    private GhosttySnapshotStyleTracker? _snapshotStyleTracker;
    private GhosttySnapshotAllocation? _snapshotStyleLayout;
    private int _snapshotStyleAlignment;

    internal bool TracksSnapshotStyles => _snapshotScrollbackQuota is not null || _snapshotRowGeometry;

    internal bool SnapshotCursorStyleIsCurrent(int key, int cursorRow, GhosttySnapshotStyle pen)
    {
        if (!TracksSnapshotStyles) return true;
        TerminalRowBuffer? rows = GetSnapshotRows(key);
        return rows is null || (uint)cursorRow >= (uint)ViewportRows || rows.Count < ViewportRows ||
            _snapshotStyleTracker?.IsCurrent(key, rows[rows.Count - ViewportRows + cursorRow], pen) == true;
    }

    internal void SnapshotStyleChanged(int key, int cursorRow, GhosttySnapshotStyle previous, GhosttySnapshotStyle current)
    {
        if (!TracksSnapshotStyles) return;
        TerminalRowBuffer? rows = GetSnapshotRows(key);
        if (rows is null || (uint)cursorRow >= (uint)ViewportRows || rows.Count < ViewportRows) return;
        int index = rows.Count - ViewportRows + cursorRow;
        TerminalRow row = rows[index];
        if (_snapshotStyleTracker?.IsCurrent(key, row, current) == true) return;
        GhosttySnapshotAllocation layout = SnapshotStyleLayout();
        GhosttySnapshotStyleTracker tracker = _snapshotStyleTracker ??= new();
        if (row.SnapshotAllocation is null && !tracker.AssignTailRow(rows, index, layout))
            _ = GhosttySnapshotLiveAllocation.Measure(this, rows, layout);
        if (!tracker.IsCurrent(key, row, current)) tracker.ChangeCursor(rows, key, row, previous, current, layout);
    }

    internal GhosttySnapshotStyleTracker.RowEdit EditSnapshotRowStyles(TerminalRow row)
    {
        if (!TracksSnapshotStyles) return default;
        GhosttySnapshotAllocation layout = SnapshotStyleLayout();
        GhosttySnapshotStyleTracker tracker = _snapshotStyleTracker ??= new();
        if (row.SnapshotAllocation is null &&
            !(_rows.Count > 0 && ReferenceEquals(_rows[_rows.Count - 1], row) && tracker.AssignTailRow(_rows, _rows.Count - 1, layout)))
            _ = GhosttySnapshotLiveAllocation.Measure(this, _rows, layout);
        return tracker.EditRow(_rows, row, layout);
    }

    private GhosttySnapshotAllocation SnapshotStyleLayout()
    {
        int alignment = _snapshotScrollbackQuota?.PageAlignment ??
            (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096);
        if (_snapshotStyleLayout is null || _snapshotStyleAlignment != alignment)
        {
            _snapshotStyleAlignment = alignment;
            _snapshotStyleLayout = new(alignment);
        }
        return _snapshotStyleLayout;
    }

    internal void SnapshotStyleAllocationReplaced(GhosttySnapshotPageAllocation previous, GhosttySnapshotPageAllocation replacement)
        => _snapshotStyleTracker?.AllocationReplaced(previous, replacement);

    internal void SnapshotStyleRowsObserved(GhosttySnapshotPageAllocation page, int nextSlot)
        => _snapshotStyleTracker?.ObserveRowSlots(page, nextSlot);
}
