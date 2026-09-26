// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotPageTracker
{
    // The resize owner serializes access. These are borrowed read-only source
    // metadata for this traversal, not retained publication state. Reconcile once
    // per page so restored IDs/dead slots and live mutations are both honored.
    internal Dictionary<GhosttySnapshotPageAllocation, GhosttySnapshotPageStorage> ReflowSources(
        TerminalRowBuffer rows, GhosttySnapshotAllocation layout, TerminalScreen? screen = null)
    {
        Dictionary<GhosttySnapshotPageAllocation, List<TerminalRow>> groups = [];
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].SnapshotAllocation is not { } page) continue;
            if (!groups.TryGetValue(page, out List<TerminalRow>? group)) groups.Add(page, group = []);
            group.Add(rows[i]);
        }
        Dictionary<GhosttySnapshotPageAllocation, GhosttySnapshotPageStorage> sources = new(groups.Count);
        foreach ((GhosttySnapshotPageAllocation original, List<TerminalRow> group) in groups)
        {
            GhosttySnapshotPageAllocation page = original;
            // A current COW source needs no writable fork just to be read.
            // Slot coverage also proves RetainRows would release nothing.
            if (_pages.TryGetValue(page, out State? known) && known.Revisions.Count == group.Count &&
                TryGetStyleUsage(page, group, out _))
            {
                sources.Add(page, known.Storage);
                continue;
            }
            State state = Writable(page, group);
            if (!page.MetadataOverflow && Synchronize(ref page, state, group, layout, screen)) sources.Add(page, state.Storage);
        }
        return sources;
    }

    internal void InstallReflowPage(GhosttySnapshotPageAllocation page, GhosttySnapshotPageStorage storage,
        IReadOnlyList<TerminalRow> rows)
    {
        State state = new(storage);
        foreach (TerminalRow row in rows)
        {
            state.ObserveSlot(row.SnapshotAllocationRow);
            state.Revisions[row.SnapshotAllocationRow] = row.SnapshotMetadataRevision;
        }
        // Non-reflow growth can keep the allocation identity while replacing
        // its COW-owned table (and retaining its existing cursor reference).
        _pages.Remove(page);
        _pages.Add(page, state);
    }

    internal bool TryGetStyleUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows, out int count)
    {
        count = 0;
        if (page.MetadataOverflow || !_pages.TryGetValue(page, out State? state)) return false;
        foreach (TerminalRow row in rows)
            if (!state.Revisions.TryGetValue(row.SnapshotAllocationRow, out ulong? revision) || revision != row.SnapshotMetadataRevision)
                return false;
        count = state.Storage.Styles.Count;
        return true;
    }

    internal bool TryGetGraphemeUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows,
        out ulong cells, out ulong bytes)
    {
        cells = bytes = 0;
        if (!TryGetStyleUsage(page, rows, out _) || !_pages.TryGetValue(page, out State? state)) return false;
        cells = (ulong)state.Storage.Graphemes.Count;
        bytes = state.Storage.Graphemes.AllocatedBytes;
        return true;
    }

    internal bool TryGetHyperlinkUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows,
        out ulong links, out ulong cells, out ulong bytes)
    {
        links = cells = bytes = 0;
        if (!TryGetStyleUsage(page, rows, out _) || !_pages.TryGetValue(page, out State? state)) return false;
        links = (ulong)state.Storage.Hyperlinks.Count;
        cells = (ulong)state.Storage.Hyperlinks.CellCount;
        bytes = state.Storage.Hyperlinks.StringBytes;
        return true;
    }
}
