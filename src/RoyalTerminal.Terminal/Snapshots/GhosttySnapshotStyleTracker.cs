// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

// Per-screen ownership for live style references. Non-cursor page keys are weak;
// each buffer also owns its last observed cursor page until the next style event.
// Copies share entries until a mutation forks them; capacity identities remain
// immutable and replacement changes only the mutating screen's row set.
internal sealed class GhosttySnapshotStyleTracker
{
    private sealed class State(GhosttySnapshotStyleStorage storage)
    {
        internal GhosttySnapshotStyleStorage Storage = storage;
        internal readonly Dictionary<int, ulong> Revisions = [];
        internal int NextRowSlot;
        internal bool Shared;
        internal State Copy()
        {
            State copy = new(Storage.Copy()) { NextRowSlot = NextRowSlot };
            foreach ((int row, ulong revision) in Revisions) copy.Revisions.Add(row, revision);
            return copy;
        }
    }

    private readonly ConditionalWeakTable<GhosttySnapshotPageAllocation, State> _pages = new();
    private GhosttySnapshotPageAllocation? _primaryCursor, _alternateCursor;

    internal void DiscardCursor(int key)
    {
        GhosttySnapshotPageAllocation? page = key == 0 ? _primaryCursor : _alternateCursor;
        if (page is not null) _pages.Remove(page);
        if (key == 0) _primaryCursor = null; else _alternateCursor = null;
    }

    internal bool IsCurrent(int key, TerminalRow row, GhosttySnapshotStyle pen)
    {
        GhosttySnapshotPageAllocation? page = row.SnapshotAllocation;
        return page is not null && ReferenceEquals(key == 0 ? _primaryCursor : _alternateCursor, page) &&
            (page.MetadataOverflow || _pages.TryGetValue(page, out State? state) && state.Storage.Cursor == pen);
    }

    // Streaming LF at the tail must not measure/rescan all history per row.
    // The slot watermark belongs to this COW owner, not the shared identity.
    internal bool AssignTailRow(TerminalRowBuffer rows, int index, GhosttySnapshotAllocation layout)
    {
        if (index != rows.Count - 1 || index == 0 || rows[index].SnapshotAllocation is not null ||
            rows[index].PreservedColumns is < 1 or > ushort.MaxValue || rows[index - 1].SnapshotAllocation is not { } previous)
            return false;
        TerminalRow row = rows[index];
        State state = _pages.TryGetValue(previous, out State? existing)
            ? Exclusive(previous, existing) : Writable(previous, Group(rows, previous));
        int slot = Math.Max(state.NextRowSlot, rows[index - 1].SnapshotAllocationRow + 1);
        if (!previous.MetadataOverflow && previous.Capacity.Columns == row.PreservedColumns && slot < previous.Capacity.Rows)
        {
            row.SnapshotAllocation = previous;
            row.SnapshotAllocationRow = slot;
            state.NextRowSlot = slot + 1;
        }
        else
        {
            row.SnapshotAllocation = new(layout.InitialCapacity(row.PreservedColumns));
            row.SnapshotAllocationRow = 0;
        }
        row.SnapshotAllocationUnmodified = false;
        return true;
    }

    internal void ObserveRowSlots(GhosttySnapshotPageAllocation page, int nextSlot)
    {
        if (_pages.TryGetValue(page, out State? state) && nextSlot > state.NextRowSlot)
            Exclusive(page, state).NextRowSlot = nextSlot;
    }

    internal GhosttySnapshotStyleTracker Copy()
    {
        GhosttySnapshotStyleTracker copy = new() { _primaryCursor = _primaryCursor, _alternateCursor = _alternateCursor };
        foreach (KeyValuePair<GhosttySnapshotPageAllocation, State> page in _pages)
        {
            page.Value.Shared = true;
            copy._pages.Add(page.Key, page.Value);
        }
        return copy;
    }

    internal void AllocationReplaced(GhosttySnapshotPageAllocation previous, GhosttySnapshotPageAllocation replacement)
    {
        if (!_pages.TryGetValue(previous, out State? source)) return;
        State state = source.Copy();
        GhosttySnapshotStyle cursor = state.Storage.Cursor;
        if (state.Storage.Rebuild(replacement.Capacity.Styles, out GhosttySnapshotStyleStorage? rebuilt) == GhosttySnapshotSetAddResult.Success &&
            rebuilt!.ChangeCursor(cursor) == GhosttySnapshotSetAddResult.Success)
            state.Storage = rebuilt;
        // Reconcile mutated rows before the next pen update. A checkpoint can
        // replace capacity after a bulk edit that has not yet reached an SGR.
        state.Revisions.Clear();
        _pages.Remove(previous);
        _pages.Add(replacement, state);
        ReplaceCursorIdentity(previous, replacement);
    }

    internal void ChangeCursor(TerminalRowBuffer rows, int key, TerminalRow cursorRow,
        GhosttySnapshotStyle previousPen, GhosttySnapshotStyle pen, GhosttySnapshotAllocation layout)
    {
        if (cursorRow.SnapshotAllocation is not { } page || page.MetadataOverflow) return;
        GhosttySnapshotPageAllocation? departing = key == 0 ? _primaryCursor : _alternateCursor;
        if (departing is not null && !ReferenceEquals(departing, page) && _pages.TryGetValue(departing, out _))
        {
            List<TerminalRow> oldRows = Group(rows, departing);
            State old = Writable(departing, oldRows);
            GhosttySnapshotPageAllocation oldPage = departing;
            if (oldRows.Count != 0) Synchronize(ref oldPage, old, oldRows, layout);
            old.Storage.ChangeCursor(default);
            if (oldRows.Count == 0) _pages.Remove(departing);
        }

        List<TerminalRow> group = Group(rows, page);
        State state = Writable(page, group);
        if (!Synchronize(ref page, state, group, layout)) return;
        if (departing is null || !ReferenceEquals(departing, page))
            if (!SetPen(ref page, state, group, previousPen, layout)) return;
        _ = SetPen(ref page, state, group, pen, layout);
        if (key == 0) _primaryCursor = page; else _alternateCursor = page;
    }

    private State Writable(GhosttySnapshotPageAllocation page, List<TerminalRow> rows)
    {
        if (!_pages.TryGetValue(page, out State? state))
        {
            state = new(page.CopyRestoredStyles());
            foreach (TerminalRow row in rows)
                if (page.HasStyleSeed && row.SnapshotAllocationUnmodified)
                    state.Revisions[row.SnapshotAllocationRow] = row.SnapshotStyleRevision;
            _pages.Add(page, state);
        }
        else if (state.Shared)
        {
            state = Exclusive(page, state);
        }
        foreach (TerminalRow row in rows) state.NextRowSlot = Math.Max(state.NextRowSlot, row.SnapshotAllocationRow + 1);
        return state;
    }

    private State Exclusive(GhosttySnapshotPageAllocation page, State state)
    {
        if (!state.Shared) return state;
        State owned = state.Copy();
        _pages.Remove(page);
        _pages.Add(page, owned);
        return owned;
    }

    private bool Synchronize(ref GhosttySnapshotPageAllocation page, State state,
        List<TerminalRow> group, GhosttySnapshotAllocation layout)
    {
        HashSet<int> retained = [];
        foreach (TerminalRow row in group) retained.Add(row.SnapshotAllocationRow);
        state.Storage.RetainRows(retained, page.Capacity.Columns);
        foreach (TerminalRow row in group)
        {
            int slot = row.SnapshotAllocationRow;
            if (state.Revisions.TryGetValue(slot, out ulong revision) && revision == row.SnapshotStyleRevision) continue;
            ReadOnlySpan<TerminalCell> cells = row.ReadOnlyPreservedCells;
            for (int column = 0; column < cells.Length; column++)
            {
                int index = checked(slot * page.Capacity.Columns + column);
                GhosttySnapshotStyle style = GhosttySnapshotLivePage.EncodeStyle(in cells[column]);
                bool empty = cells[column].Codepoint == 0 && cells[column].Grapheme is null;
                GhosttySnapshotSetAddResult result = state.Storage.ObserveCell(index, style, empty);
                if (result == GhosttySnapshotSetAddResult.Success) continue;
                if (!Grow(ref page, state, group, result, layout)) return false;
                result = state.Storage.ObserveCell(index, style, empty);
                if (result != GhosttySnapshotSetAddResult.Success) return Overflow(ref page, state, group);
            }
            state.Revisions[slot] = row.SnapshotStyleRevision;
        }
        return true;
    }

    private bool SetPen(ref GhosttySnapshotPageAllocation page, State state,
        List<TerminalRow> group, GhosttySnapshotStyle pen, GhosttySnapshotAllocation layout)
    {
        GhosttySnapshotSetAddResult result = state.Storage.ChangeCursor(pen);
        if (result == GhosttySnapshotSetAddResult.Success) return true;
        if (!Grow(ref page, state, group, result, layout)) return false;
        return state.Storage.ChangeCursor(pen) == GhosttySnapshotSetAddResult.Success || Overflow(ref page, state, group);
    }

    private bool Grow(ref GhosttySnapshotPageAllocation page, State state, List<TerminalRow> group,
        GhosttySnapshotSetAddResult reason, GhosttySnapshotAllocation layout)
    {
        GhosttySnapshotPageCapacity capacity = page.Capacity;
        if (reason == GhosttySnapshotSetAddResult.OutOfMemory &&
            !layout.TryIncreaseCapacity(capacity, GhosttySnapshotCapacityDimension.Styles, (ulong)state.Storage.Count, group.Count, out capacity))
            return Overflow(ref page, state, group);
        GhosttySnapshotStyle pen = state.Storage.Cursor;
        if (state.Storage.Rebuild(capacity.Styles, out GhosttySnapshotStyleStorage? rebuilt) != GhosttySnapshotSetAddResult.Success ||
            rebuilt!.ChangeCursor(pen) != GhosttySnapshotSetAddResult.Success)
            return Overflow(ref page, state, group);
        state.Storage = rebuilt;
        Replace(ref page, new(capacity, rebuilt.Copy()), state, group);
        return true;
    }

    private bool Overflow(ref GhosttySnapshotPageAllocation page, State state, List<TerminalRow> group)
    {
        // Do not wrap a capacity or undercharge history while pressure-driven
        // page splitting is still unimplemented. Rendering remains independent.
        Replace(ref page, new(page.Capacity, state.Storage.Copy(), metadataOverflow: true), state, group);
        return false;
    }

    private void Replace(ref GhosttySnapshotPageAllocation page, GhosttySnapshotPageAllocation replacement,
        State state, List<TerminalRow> group)
    {
        _pages.Remove(page);
        _pages.Add(replacement, state);
        foreach (TerminalRow row in group) row.SnapshotAllocation = replacement;
        ReplaceCursorIdentity(page, replacement);
        page = replacement;
    }

    private void ReplaceCursorIdentity(GhosttySnapshotPageAllocation previous, GhosttySnapshotPageAllocation replacement)
    {
        if (ReferenceEquals(_primaryCursor, previous)) _primaryCursor = replacement;
        if (ReferenceEquals(_alternateCursor, previous)) _alternateCursor = replacement;
    }

    private static List<TerminalRow> Group(TerminalRowBuffer rows, GhosttySnapshotPageAllocation page)
    {
        List<TerminalRow> group = [];
        for (int i = 0; i < rows.Count; i++)
            if (ReferenceEquals(rows[i].SnapshotAllocation, page)) group.Add(rows[i]);
        return group;
    }
}
