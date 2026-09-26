// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    // One owner coordinates all per-page metadata through COW publication.
    private GhosttySnapshotPageTracker? _snapshotPageTracker;
    private GhosttySnapshotAllocation? _snapshotPageLayout;
    private int _snapshotPageAlignment;

    internal bool TracksSnapshotMetadata => _snapshotScrollbackQuota is not null || _snapshotRowGeometry;

    internal bool SnapshotCursorStyleIsCurrent(int key, int cursorRow, GhosttySnapshotStyle pen)
    {
        if (!TracksSnapshotMetadata) return true;
        TerminalRowBuffer? rows = GetSnapshotRows(key);
        return rows is null || (uint)cursorRow >= (uint)ViewportRows || rows.Count < ViewportRows ||
            _snapshotPageTracker?.IsCurrent(key, rows[rows.Count - ViewportRows + cursorRow], pen) == true;
    }

    internal void SnapshotStyleChanged(int key, int cursorRow, GhosttySnapshotStyle previous, GhosttySnapshotStyle current)
    {
        if (!TracksSnapshotMetadata) return;
        TerminalRowBuffer? rows = GetSnapshotRows(key);
        if (rows is null || (uint)cursorRow >= (uint)ViewportRows || rows.Count < ViewportRows) return;
        int index = rows.Count - ViewportRows + cursorRow;
        TerminalRow row = rows[index];
        if (_snapshotPageTracker?.IsCurrent(key, row, current) == true) return;
        GhosttySnapshotAllocation layout = SnapshotPageLayout();
        GhosttySnapshotPageTracker tracker = _snapshotPageTracker ??= new();
        if (row.SnapshotAllocation is null && !tracker.AssignTailRow(rows, index, layout))
            _ = GhosttySnapshotLiveAllocation.Measure(this, rows, layout);
        if (!tracker.IsCurrent(key, row, current)) tracker.ChangeCursor(rows, key, row, previous, current, layout);
    }

    internal GhosttySnapshotPageTracker.RowEdit EditSnapshotRowMetadata(TerminalRow row)
    {
        if (!TracksSnapshotMetadata) return default;
        GhosttySnapshotAllocation layout = SnapshotPageLayout();
        GhosttySnapshotPageTracker tracker = _snapshotPageTracker ??= new();
        if (row.SnapshotAllocation is null &&
            !(_rows.Count > 0 && ReferenceEquals(_rows[_rows.Count - 1], row) && tracker.AssignTailRow(_rows, _rows.Count - 1, layout)))
            _ = GhosttySnapshotLiveAllocation.Measure(this, _rows, layout);
        return tracker.EditRow(_rows, row, layout);
    }

    private GhosttySnapshotAllocation SnapshotPageLayout()
    {
        int alignment = _snapshotScrollbackQuota?.PageAlignment ??
            (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096);
        if (_snapshotPageLayout is null || _snapshotPageAlignment != alignment)
        {
            _snapshotPageAlignment = alignment;
            _snapshotPageLayout = new(alignment);
        }
        return _snapshotPageLayout;
    }

    internal GhosttySnapshotPageAllocation SnapshotAllocationReplaced(GhosttySnapshotPageAllocation previous, GhosttySnapshotPageAllocation replacement)
        => _snapshotPageTracker?.AllocationReplaced(previous, replacement) ?? replacement;

    internal void SnapshotRowsObserved(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows)
        => _snapshotPageTracker?.ObserveRowSlots(page, rows);

    internal bool TryGetSnapshotStyleUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows, out int count)
    {
        count = 0;
        return _snapshotPageTracker?.TryGetStyleUsage(page, rows, out count) == true;
    }

    internal bool TryGetSnapshotGraphemeUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows,
        out ulong cells, out ulong bytes)
    {
        cells = bytes = 0;
        return _snapshotPageTracker?.TryGetGraphemeUsage(page, rows, out cells, out bytes) == true;
    }

    private void RetireSnapshotRows(int start, int count)
    {
        if (!TracksSnapshotMetadata || count == 0) return;
        (_snapshotPageTracker ??= new()).RetireRows(_rows, start, count);
    }

    private void RemoveRows(int start, int count)
    {
        RetireSnapshotRows(start, count);
        _rows.RemoveRange(start, count);
    }

    private void ClearRow(TerminalRow row)
    {
        using GhosttySnapshotPageTracker.RowEdit styles = row.SnapshotAllocation is null ? default : EditSnapshotRowMetadata(row);
        styles.Clear(0, row.PreservedColumns);
        row.Clear(DefaultForeground, DefaultBackground);
    }
}
