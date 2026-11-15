// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal enum GhosttySnapshotQuotaCheckpoint { LimitChange, RowGrowth, PageGrowth, Resize }

// PageList.Limits.enforce and grow choose an oldest, whole-allocation prefix.
// This selector owns no rows or anchors and performs no mutation. In particular,
// pages sharing the active boundary cannot be split merely to satisfy a quota.
internal static class GhosttySnapshotQuotaEviction
{
    internal static int SelectPrefix(TerminalRowBuffer rows, int columns, int viewportRows,
        GhosttySnapshotAllocation layout, GhosttySnapshotScrollbackQuota quota, GhosttySnapshotQuotaCheckpoint checkpoint,
        HashSet<GhosttySnapshotPageAllocation>? unrepresentable = null)
    {
        int history = rows.Count - viewportRows;
        if (history <= 0) return 0;
        (ulong minimumBytes, ulong minimumRows) = layout.MinimumLimits(columns, viewportRows);
        ulong maximumRows = Math.Max(quota.MaximumRows ?? ulong.MaxValue, minimumRows);
        ulong maximumBytes = Math.Max(quota.MaximumBytes ?? ulong.MaxValue, minimumBytes);
        bool bytes = quota.MaximumBytes.HasValue && checkpoint is GhosttySnapshotQuotaCheckpoint.LimitChange or GhosttySnapshotQuotaCheckpoint.PageGrowth;
        if (!bytes && (ulong)history <= maximumRows) return 0;

        Dictionary<GhosttySnapshotPageAllocation, (int First, int Last)> pages = [];
        UInt128 charge = 0;
        for (int index = 0; index < rows.Count; index++)
        {
            // A host row wider than the native ABI has no representable page.
            // Keep its prefix rather than pretending partial accounting is exact.
            if (rows[index].SnapshotAllocation is not { } page) return 0;
            if (pages.TryGetValue(page, out (int First, int Last) range)) pages[page] = (range.First, index);
            else
            {
                pages.Add(page, (index, index));
                charge += Charge(page);
            }
        }

        // Carried pages can be larger than the page they replace. Upstream
        // now enforces the byte limit after growth, removing every eligible
        // historical page needed to fit, while retaining the active boundary.
        int removed = 0;
        while ((ulong)(history - removed) > maximumRows || (bytes && charge > maximumBytes))
        {
            int end = pages[rows[removed].SnapshotAllocation!].Last;
            // Host row rotation may interleave logical allocations. Keep that
            // connected prefix indivisible; never retire a surviving page's
            // partial identity, or reorder visible content for accounting.
            for (int index = removed; index <= end && end < history; index++)
            {
                (int First, int Last) range = pages[rows[index].SnapshotAllocation!];
                end = Math.Max(end, range.Last);
            }
            if (end >= history) break;
            for (int index = removed; index <= end; index++)
            {
                GhosttySnapshotPageAllocation page = rows[index].SnapshotAllocation!;
                if (pages[page].First == index) charge -= Charge(page);
            }
            removed = end + 1;
            if (removed >= history) break;
        }
        return removed;

        UInt128 Charge(GhosttySnapshotPageAllocation page)
            => page.MetadataOverflow || unrepresentable?.Contains(page) == true
                ? (UInt128)ulong.MaxValue + 1 : layout.AllocatedBytes(page.Capacity);
    }
}
