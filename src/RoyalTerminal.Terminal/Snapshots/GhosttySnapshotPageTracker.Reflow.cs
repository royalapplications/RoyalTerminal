// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
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
        _pages.AddOrUpdate(page, state);
    }

    private bool TryGetCurrentState(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows,
        [NotNullWhen(true)] out State? current)
    {
        current = null;
        if (page.MetadataOverflow || !_pages.TryGetValue(page, out State? state)) return false;
        // IReadOnlyList enumeration boxes List<T>'s enumerator. Indexing also
        // lets the combined quota census validate all metadata in one pass.
        for (int i = 0; i < rows.Count; i++)
        {
            TerminalRow row = rows[i];
            if (!ReferenceEquals(row.SnapshotAllocation, page) ||
                !state.Revisions.TryGetValue(row.SnapshotAllocationRow, out ulong? revision) || revision != row.SnapshotMetadataRevision)
                return false;
        }
        current = state;
        return true;
    }

    internal bool TryGetMetadataUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows,
        out GhosttySnapshotMetadataUsage usage)
    {
        usage = default;
        if (!TryGetCurrentState(page, rows, out State? state)) return false;
        usage = new((ulong)state.Storage.Styles.Count, (ulong)state.Storage.Graphemes.Count,
            state.Storage.Graphemes.AllocatedBytes, 0, (ulong)state.Storage.Hyperlinks.Count,
            (ulong)state.Storage.Hyperlinks.CellCount, state.Storage.Hyperlinks.StringBytes);
        return true;
    }

    internal bool TryGetStyleUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows, out int count)
    {
        count = 0;
        if (!TryGetCurrentState(page, rows, out State? state)) return false;
        count = state.Storage.Styles.Count;
        return true;
    }

    internal bool TryGetGraphemeUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows,
        out ulong cells, out ulong bytes)
    {
        cells = bytes = 0;
        if (!TryGetCurrentState(page, rows, out State? state)) return false;
        cells = (ulong)state.Storage.Graphemes.Count;
        bytes = state.Storage.Graphemes.AllocatedBytes;
        return true;
    }

    internal bool TryGetHyperlinkUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows,
        out ulong links, out ulong cells, out ulong bytes)
    {
        links = cells = bytes = 0;
        if (!TryGetCurrentState(page, rows, out State? state)) return false;
        links = (ulong)state.Storage.Hyperlinks.Count;
        cells = (ulong)state.Storage.Hyperlinks.CellCount;
        bytes = state.Storage.Hyperlinks.StringBytes;
        return true;
    }
}
