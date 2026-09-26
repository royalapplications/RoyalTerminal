// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

// Coordinates per-screen metadata ownership and page replacement. Individual
// style/grapheme/hyperlink allocators own their algorithms. Non-cursor page keys are weak;
// each buffer also owns its last observed cursor page until the next style event.
// Copies share entries until a mutation forks them; capacity identities remain
// immutable and replacement changes only the mutating screen's row set.
internal sealed partial class GhosttySnapshotPageTracker
{
    internal sealed class State(GhosttySnapshotPageStorage storage)
    {
        internal GhosttySnapshotPageStorage Storage = storage;
        // Keys also own occupied row slots. Null means the cells still need
        // reconciliation, not that the slot is available for another row.
        internal readonly Dictionary<int, ulong?> Revisions = [];
        internal int NextRowSlot;
        internal bool Shared;
        internal State Copy()
        {
            State copy = new(Storage.Copy()) { NextRowSlot = NextRowSlot };
            foreach ((int row, ulong? revision) in Revisions) copy.Revisions.Add(row, revision);
            return copy;
        }

        internal void ObserveSlot(int slot)
        {
            Revisions.TryAdd(slot, null);
            NextRowSlot = Math.Max(NextRowSlot, slot + 1);
        }

        internal void RetireSlot(int slot)
        {
            Revisions.Remove(slot);
            // Prefix holes are not reusable while later slots remain occupied.
            // Reset tail rows are, even when logical rows have been rotated.
            while (NextRowSlot > 0 && !Revisions.ContainsKey(NextRowSlot - 1)) NextRowSlot--;
        }
    }

    private readonly ConditionalWeakTable<GhosttySnapshotPageAllocation, State> _pages = new();
    private GhosttySnapshotPageAllocation? _primaryCursor, _alternateCursor;

    internal void DiscardCursor(int key)
    {
        EndCursorHyperlink(key);
        if (key == 0) _primaryLinkPage = null; else _alternateLinkPage = null;
        GhosttySnapshotPageAllocation? page = key == 0 ? _primaryCursor : _alternateCursor;
        if (page is not null) _pages.Remove(page);
        if (key == 0) _primaryCursor = null; else _alternateCursor = null;
    }

    internal bool IsCurrent(int key, TerminalRow row, GhosttySnapshotStyle pen)
    {
        GhosttySnapshotPageAllocation? page = row.SnapshotAllocation;
        return page is not null && ReferenceEquals(key == 0 ? _primaryCursor : _alternateCursor, page) &&
            (page.MetadataOverflow || _pages.TryGetValue(page, out State? state) && state.Storage.Styles.Cursor == pen);
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
            state.ObserveSlot(slot);
        }
        else
        {
            row.SnapshotAllocation = new(layout.InitialCapacity(row.PreservedColumns));
            row.SnapshotAllocationRow = 0;
        }
        row.SnapshotAllocationUnmodified = false;
        return true;
    }

    internal void ObserveRowSlots(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows)
    {
        if (!_pages.TryGetValue(page, out State? state)) return;
        foreach (TerminalRow row in rows)
        {
            if (state.Revisions.ContainsKey(row.SnapshotAllocationRow)) continue;
            state = Exclusive(page, state);
            state.ObserveSlot(row.SnapshotAllocationRow);
        }
    }

    internal GhosttySnapshotPageTracker Copy()
    {
        GhosttySnapshotPageTracker copy = new()
        {
            _primaryCursor = _primaryCursor, _alternateCursor = _alternateCursor,
            _primaryLinkPage = _primaryLinkPage, _alternateLinkPage = _alternateLinkPage,
            _primaryLinkToken = _primaryLinkToken, _alternateLinkToken = _alternateLinkToken,
        };
        foreach (KeyValuePair<GhosttySnapshotPageAllocation, State> page in _pages)
        {
            page.Value.Shared = true;
            copy._pages.Add(page.Key, page.Value);
        }
        return copy;
    }

    internal GhosttySnapshotPageAllocation AllocationReplaced(GhosttySnapshotPageAllocation previous, GhosttySnapshotPageAllocation replacement)
    {
        if (!_pages.TryGetValue(previous, out State? source)) return replacement;
        State state = source.Copy();
        if (state.Storage.Rebuild(replacement.Capacity, restoreCursor: true, out GhosttySnapshotPageStorage? rebuilt))
            state.Storage = rebuilt!;
        else
            replacement = new(replacement.Capacity, state.Storage.Styles.Copy(), metadataOverflow: true,
                restoredGraphemes: state.Storage.Graphemes.Copy(), restoredHyperlinks: state.Storage.Hyperlinks.Copy());
        // Reconcile mutated rows before the next pen update. A checkpoint can
        // replace capacity after a bulk edit that has not yet reached an SGR.
        foreach (int slot in state.Revisions.Keys) state.Revisions[slot] = null;
        _pages.Remove(previous);
        _pages.Add(replacement, state);
        ReplaceCursorIdentity(previous, replacement);
        return replacement;
    }

    internal void ChangeCursor(TerminalRowBuffer rows, int key, TerminalRow cursorRow,
        GhosttySnapshotStyle previousPen, GhosttySnapshotStyle pen, GhosttySnapshotAllocation layout, TerminalScreen? screen = null)
    {
        if (cursorRow.SnapshotAllocation is not { } page || page.MetadataOverflow) return;
        GhosttySnapshotPageAllocation? departing = key == 0 ? _primaryCursor : _alternateCursor;
        if (departing is not null && !ReferenceEquals(departing, page) && _pages.TryGetValue(departing, out _))
        {
            List<TerminalRow> oldRows = Group(rows, departing);
            State old = Writable(departing, oldRows);
            GhosttySnapshotPageAllocation oldPage = departing;
            if (oldRows.Count != 0) Synchronize(ref oldPage, old, oldRows, layout, screen);
            old.Storage.Styles.ChangeCursor(default);
            old.Storage.Hyperlinks.EndCursor();
            if (oldRows.Count == 0) _pages.Remove(departing);
        }

        List<TerminalRow> group = Group(rows, page);
        State state = Writable(page, group);
        if (!Synchronize(ref page, state, group, layout, screen)) return;
        if (departing is null || !ReferenceEquals(departing, page))
            if (!SetPen(ref page, state, group, previousPen, layout)) return;
        _ = SetPen(ref page, state, group, pen, layout);
        if (key == 0) _primaryCursor = page; else _alternateCursor = page;
    }

    private State Writable(GhosttySnapshotPageAllocation page, List<TerminalRow> rows)
    {
        if (!_pages.TryGetValue(page, out State? state))
        {
            state = new(new(page.CopyRestoredStyles(), page.CopyRestoredGraphemes(), page.CopyRestoredHyperlinks()));
            foreach (TerminalRow row in rows)
                if (page.HasStyleSeed && page.HasGraphemeSeed && page.HasHyperlinkSeed && row.SnapshotAllocationUnmodified)
                    state.Revisions[row.SnapshotAllocationRow] = row.SnapshotMetadataRevision;
            _pages.Add(page, state);
        }
        else if (state.Shared)
        {
            state = Exclusive(page, state);
        }
        foreach (TerminalRow row in rows) state.ObserveSlot(row.SnapshotAllocationRow);
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
        List<TerminalRow> group, GhosttySnapshotAllocation layout, TerminalScreen? screen)
    {
        HashSet<int> retained = [];
        foreach (TerminalRow row in group) retained.Add(row.SnapshotAllocationRow);
        state.Storage.Styles.RetainRows(retained, page.Capacity.Columns);
        state.Storage.Graphemes.RetainRows(retained, page.Capacity.Columns);
        state.Storage.Hyperlinks.RetainRows(retained, page.Capacity.Columns);
        foreach (int slot in state.Revisions.Keys)
            if (!retained.Contains(slot)) state.RetireSlot(slot);
        foreach (TerminalRow row in group)
        {
            int slot = row.SnapshotAllocationRow;
            if (state.Revisions.TryGetValue(slot, out ulong? revision) && revision == row.SnapshotMetadataRevision) continue;
            ReadOnlySpan<TerminalCell> cells = row.ReadOnlyPreservedCells;
            for (int column = 0; column < cells.Length; column++)
            {
                int index = checked(slot * page.Capacity.Columns + column);
                int suffix = TerminalGraphemeStorage.SuffixLength(in cells[column]);
                if (state.Storage.Graphemes.SuffixLength(index) != suffix)
                {
                    state.Storage.Graphemes.Clear(index);
                    if (state.Storage.Graphemes.Set(index, suffix) != GhosttySnapshotGraphemeAddResult.Success)
                    {
                        if (!GrowGraphemes(ref page, state, group, layout)) return false;
                        if (state.Storage.Graphemes.Set(index, suffix) != GhosttySnapshotGraphemeAddResult.Success)
                            return Overflow(ref page, state, group);
                    }
                }
                byte[]? encoded = screen?.SnapshotHyperlinkEncoding(cells[column].HyperlinkId);
                if (cells[column].HyperlinkId != 0 && encoded is null) return Overflow(ref page, state, group);
                if (!ObserveHyperlink(ref page, state, group, index, encoded, layout)) return false;
                GhosttySnapshotStyle style = GhosttySnapshotLivePage.EncodeStyle(in cells[column]);
                bool empty = cells[column].Codepoint == 0 && cells[column].Grapheme is null;
                GhosttySnapshotSetAddResult result = state.Storage.Styles.ObserveCell(index, style, empty);
                if (result == GhosttySnapshotSetAddResult.Success) continue;
                if (!Grow(ref page, state, group, result, layout)) return false;
                result = state.Storage.Styles.ObserveCell(index, style, empty);
                if (result != GhosttySnapshotSetAddResult.Success) return Overflow(ref page, state, group);
            }
            state.Revisions[slot] = row.SnapshotMetadataRevision;
        }
        return true;
    }

    private bool SetPen(ref GhosttySnapshotPageAllocation page, State state,
        List<TerminalRow> group, GhosttySnapshotStyle pen, GhosttySnapshotAllocation layout)
    {
        GhosttySnapshotSetAddResult result = state.Storage.Styles.ChangeCursor(pen);
        if (result == GhosttySnapshotSetAddResult.Success) return true;
        if (!Grow(ref page, state, group, result, layout)) return false;
        return state.Storage.Styles.ChangeCursor(pen) == GhosttySnapshotSetAddResult.Success || Overflow(ref page, state, group);
    }

    private bool Grow(ref GhosttySnapshotPageAllocation page, State state, List<TerminalRow> group,
        GhosttySnapshotSetAddResult reason, GhosttySnapshotAllocation layout)
        => GrowMetadata(ref page, state, group,
            reason == GhosttySnapshotSetAddResult.OutOfMemory ? GhosttySnapshotCapacityDimension.Styles : null, layout);

    private bool GrowGraphemes(ref GhosttySnapshotPageAllocation page, State state, List<TerminalRow> group,
        GhosttySnapshotAllocation layout)
        => GrowMetadata(ref page, state, group, GhosttySnapshotCapacityDimension.GraphemeBytes, layout);

    private bool ObserveHyperlink(ref GhosttySnapshotPageAllocation page, State state, List<TerminalRow> group,
        int index, byte[]? encoded, GhosttySnapshotAllocation layout)
    {
        while (true)
        {
            GhosttySnapshotHyperlinkAddResult result = state.Storage.Hyperlinks.ObserveCell(index, encoded);
            if (result == GhosttySnapshotHyperlinkAddResult.Success) return true;
            if (result == GhosttySnapshotHyperlinkAddResult.InvalidEntry) return Overflow(ref page, state, group);
            if (!GrowMetadata(ref page, state, group, GhosttySnapshotHyperlinkStorage.GrowthDimension(result), layout)) return false;
        }
    }

    private bool GrowMetadata(ref GhosttySnapshotPageAllocation page, State state, List<TerminalRow> group,
        GhosttySnapshotCapacityDimension? dimension, GhosttySnapshotAllocation layout)
    {
        GhosttySnapshotPageCapacity capacity = page.Capacity;
        ulong used = state.Storage.Usage(dimension);
        if (dimension is { } growth && !layout.TryIncreaseCapacity(capacity, growth, used, group.Count, out capacity))
            return Overflow(ref page, state, group);
        if (!state.Storage.Rebuild(capacity, restoreCursor: true, out GhosttySnapshotPageStorage? rebuilt))
            return Overflow(ref page, state, group);
        state.Storage = rebuilt!;
        Replace(ref page, new(capacity, rebuilt!.Styles.Copy(), restoredGraphemes: rebuilt.Graphemes.Copy(),
            restoredHyperlinks: rebuilt.Hyperlinks.Copy()), state, group);
        return true;
    }

    private bool Overflow(ref GhosttySnapshotPageAllocation page, State state, List<TerminalRow> group)
    {
        // Do not wrap a capacity or undercharge history while pressure-driven
        // page splitting is still unimplemented. Rendering remains independent.
        Replace(ref page, new(page.Capacity, state.Storage.Styles.Copy(), metadataOverflow: true,
            restoredGraphemes: state.Storage.Graphemes.Copy(), restoredHyperlinks: state.Storage.Hyperlinks.Copy()), state, group);
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
        bool dropped = _pages.TryGetValue(replacement, out State? state) && state.Storage.Hyperlinks.CursorId == 0;
        if (ReferenceEquals(_primaryLinkPage, previous))
        {
            _primaryLinkPage = replacement;
            if (dropped) _primaryLinkToken = 0;
        }
        if (ReferenceEquals(_alternateLinkPage, previous))
        {
            _alternateLinkPage = replacement;
            if (dropped) _alternateLinkToken = 0;
        }
    }

    private static List<TerminalRow> Group(TerminalRowBuffer rows, GhosttySnapshotPageAllocation page)
    {
        List<TerminalRow> group = [];
        for (int i = 0; i < rows.Count; i++)
            if (ReferenceEquals(rows[i].SnapshotAllocation, page)) group.Add(rows[i]);
        return group;
    }
}
