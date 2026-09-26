// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    private bool HasFiniteSnapshotQuota => _snapshotScrollbackQuota is { MaximumBytes: not null } or { MaximumRows: not null };

    // Host policy is separate from a frozen frame. Copying an unchanged policy
    // is not another native setMaxBytes call: metadata growth alone must not
    // trigger an immediate byte purge on the next input/publication boundary.
    internal void SynchronizeSnapshotScrollbackQuota(GhosttySnapshotScrollbackQuota? quota)
    {
        if (_snapshotScrollbackQuota != quota) SnapshotScrollbackQuota = quota;
        else _snapshotScrollbackQuota = quota; // Preserve host ownership without enforcing twice.
    }

    private void EnforceSnapshotQuotaChange()
    {
        if (!HasFiniteSnapshotQuota) return;
        EnforceActive();
        using InactiveResizeScope scope = EnterInactiveResize(Columns, ViewportRows);
        if (scope.Available) EnforceActive();

        void EnforceActive()
        {
            int removed = RemoveSnapshotQuotaRows(GhosttySnapshotQuotaCheckpoint.LimitChange);
            if (removed > 0) ShiftRasterGraphicsAfterTopRowsRemoved(removed);
            // A byte limit of zero also disables scrolling into the retained
            // active-boundary page, even though its history cannot be split.
            ScrollOffset = _snapshotScrollbackQuota!.MaximumBytes == 0 ? 0 : _viewportTop;
            if (removed > 0 || _snapshotScrollbackQuota.MaximumBytes == 0) InvalidateViewport();
        }
    }

    // Called after appending an empty row, before the printer initializes its
    // style. Tail-slot assignment is constant-time on the streaming fast path.
    // Return the prefix count so the caller adjusts anchors exactly once.
    private int RemoveSnapshotQuotaRowsAfterGrowth(GhosttySnapshotScrollbackQuota? growthQuota = null)
    {
        if (!TracksSnapshotMetadata || _rows.Count < 2 || !HasNativeSnapshotGeometry) return 0;
        GhosttySnapshotAllocation layout = SnapshotPageLayout();
        TerminalRow tail = _rows[_rows.Count - 1];
        if (tail.SnapshotAllocation is null && !(_snapshotPageTracker ??= new()).AssignTailRow(_rows, _rows.Count - 1, layout))
            _ = GhosttySnapshotLiveAllocation.Measure(this, _rows, layout);
        bool newPage = !ReferenceEquals(tail.SnapshotAllocation, _rows[_rows.Count - 2].SnapshotAllocation);
        return RemoveSnapshotQuotaRows(newPage ? GhosttySnapshotQuotaCheckpoint.PageGrowth : GhosttySnapshotQuotaCheckpoint.RowGrowth, growthQuota);
    }

    private bool HasNativeSnapshotGeometry => Columns is >= 1 and <= ushort.MaxValue && ViewportRows is >= 1 and <= ushort.MaxValue;

    private int RemoveSnapshotQuotaRows(GhosttySnapshotQuotaCheckpoint checkpoint, GhosttySnapshotScrollbackQuota? growthQuota = null)
    {
        GhosttySnapshotScrollbackQuota? quota = growthQuota ?? _snapshotScrollbackQuota;
        if (quota is not ({ MaximumBytes: not null } or { MaximumRows: not null }) ||
            !HasNativeSnapshotGeometry || _rows.Count <= ViewportRows) return 0;
        GhosttySnapshotAllocation layout = SnapshotPageLayout();
        bool bytes = quota.MaximumBytes.HasValue && checkpoint is GhosttySnapshotQuotaCheckpoint.LimitChange or GhosttySnapshotQuotaCheckpoint.PageGrowth;
        if (!bytes && (ulong)(_rows.Count - ViewportRows) <= Math.Max(quota.MaximumRows ?? ulong.MaxValue, (ulong)layout.InitialRows(Columns))) return 0;
        // Common over-limit boundary case: no complete historical first page
        // exists yet. Avoid scanning the history/metadata again on every LF.
        if (_rows[0].SnapshotAllocation is { } first &&
            ReferenceEquals(first, _rows[_rows.Count - ViewportRows].SnapshotAllocation)) return 0;
        _ = GhosttySnapshotLiveAllocation.Measure(this, _rows, layout, out HashSet<GhosttySnapshotPageAllocation>? unrepresentable);
        int removed = GhosttySnapshotQuotaEviction.SelectPrefix(_rows, Columns, ViewportRows, layout, quota, checkpoint, unrepresentable);
        if (removed > 0) RemoveRows(0, removed);
        return removed;
    }
}
