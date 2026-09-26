// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotStyleTracker
{
    // Called before removing rows or reusing their allocation identities. Like
    // Page.resetRow, retirement releases cells, not the independent cursor pin.
    // Do not reconcile disappearing cells: their last tracked references are
    // simply released, and survivors keep their own pending row revisions.
    internal void RetireRows(TerminalRowBuffer rows, int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (start > rows.Count || count > rows.Count - start) throw new ArgumentOutOfRangeException(nameof(count));
        int end = start + count;
        for (int index = start; index < end; index++)
        {
            TerminalRow row = rows[index];
            if (row.SnapshotAllocation is not { } page) continue;
            // An unseen restored page still owns the seed's cell references.
            // Fork it even if this owner has never moved its cursor onto it.
            State state = _pages.TryGetValue(page, out State? known)
                ? Exclusive(page, known) : Writable(page, Group(rows, page));
            int slot = row.SnapshotAllocationRow;
            state.Storage.ClearCells(checked(slot * page.Capacity.Columns), page.Capacity.Columns);
            state.RetireSlot(slot);
        }
    }
}
