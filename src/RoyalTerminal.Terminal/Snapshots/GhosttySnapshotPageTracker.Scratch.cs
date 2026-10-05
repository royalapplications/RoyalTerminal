// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotPageTracker
{
    // Bounded, owner-local scratch, not allocator/snapshot state. Two lists
    // support cursor-style splitting's nested hyperlink migration. Cold/deeper
    // nesting remains correct without growing a permanent cache or sharing it
    // between independently writable COW owners.
    private const int MaximumCachedGroupCapacity = 4096;
    private List<TerminalRow>? _firstGroupScratch, _secondGroupScratch;
    private HashSet<int>? _retainedSlotScratch;

    private PageRowsLease RentGroup(TerminalRowBuffer rows, GhosttySnapshotPageAllocation page)
    {
        List<TerminalRow> group;
        if (_firstGroupScratch is { } first)
        {
            group = first;
            _firstGroupScratch = null;
        }
        else if (_secondGroupScratch is { } second)
        {
            group = second;
            _secondGroupScratch = null;
        }
        else group = [];

        try
        {
            for (int i = 0; i < rows.Count; i++)
                if (ReferenceEquals(rows[i].SnapshotAllocation, page)) group.Add(rows[i]);
            return new(this, group);
        }
        catch
        {
            ReturnGroup(group);
            throw;
        }
    }

    private void ReturnGroup(List<TerminalRow> group)
    {
        group.Clear(); // Never root rows, their cell arrays, or page identities.
        if (group.Capacity > MaximumCachedGroupCapacity) return;
        if (_firstGroupScratch is null) _firstGroupScratch = group;
        else if (_secondGroupScratch is null) _secondGroupScratch = group;
    }

    private readonly ref struct PageRowsLease(GhosttySnapshotPageTracker owner, List<TerminalRow> rows)
    {
        internal List<TerminalRow> Rows => rows;
        public void Dispose() => owner.ReturnGroup(rows);
    }
}
