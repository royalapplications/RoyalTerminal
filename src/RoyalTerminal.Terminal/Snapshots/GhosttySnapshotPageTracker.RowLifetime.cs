// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotPageTracker
{
    internal void RetireHistoryRows(TerminalRowBuffer rows, int count)
    {
        State? boundary = null;
        Queue<int>? reusable = null;
        if (count > 0 && count < rows.Count && rows[count].SnapshotAllocation is { } page)
        {
            int start = count;
            while (start > 0 && ReferenceEquals(rows[start - 1].SnapshotAllocation, page)) start--;
            if (start < count)
            {
                int end = count + 1;
                while (end < rows.Count && ReferenceEquals(rows[end].SnapshotAllocation, page)) end++;
                boundary = Writable(page, Group(rows, page));
                int erased = count - start, remaining = end - count;
                int[] slots = new int[end - start];
                for (int index = 0; index < slots.Length; index++)
                    slots[index] = rows[start + index].SnapshotAllocationRow;
                // PageList.eraseRows swaps row headers, not cells. The unused
                // tail ordering can differ from the erased prefix ordering.
                for (int index = 0; index < remaining; index++)
                    (slots[index], slots[index + erased]) = (slots[index + erased], slots[index]);
                reusable = new(erased + (boundary.ReusableTailSlots?.Count ?? 0));
                for (int index = remaining; index < slots.Length; index++) reusable.Enqueue(slots[index]);
                if (boundary.ReusableTailSlots is { } tail)
                    foreach (int slot in tail) reusable.Enqueue(slot);
            }
        }
        RetireRows(rows, 0, count);
        if (boundary is not null) boundary.ReusableTailSlots = reusable;
    }

    // Called before removing rows or reusing their metadata allocation identities. Like
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
            state.Storage.Graphemes.ClearCells(checked(slot * page.Capacity.Columns), page.Capacity.Columns);
            state.Storage.Hyperlinks.ClearCells(checked(slot * page.Capacity.Columns), page.Capacity.Columns);
            state.Storage.Styles.ClearCells(checked(slot * page.Capacity.Columns), page.Capacity.Columns);
            state.RetireSlot(slot);
        }
    }
}
