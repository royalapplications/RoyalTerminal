// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

// Immutable allocation identity survives row moves, partial pruning and COW.
// No live screen/row references are retained by the identity itself.
internal sealed class GhosttySnapshotPageAllocation(GhosttySnapshotPageCapacity capacity)
{
    internal GhosttySnapshotPageCapacity Capacity { get; } = capacity;
}

/// <summary>Measures only currently retained storage, including the unpublished COW screen.</summary>
internal static class GhosttySnapshotLiveAllocation
{
    internal static ulong Measure(TerminalScreen screen, TerminalRowBuffer rows, GhosttySnapshotAllocation allocation)
    {
        Dictionary<GhosttySnapshotPageAllocation, List<TerminalRow>> pages = [];
        Dictionary<GhosttySnapshotPageAllocation, int> occupied = [];
        // Row rotations (for example a top-origin scrolling region) preserve
        // logical allocation identity but need not preserve slot order. Count
        // every existing slot before assigning a newly inserted/appended row.
        for (int i = 0; i < rows.Count; i++)
        {
            TerminalRow row = rows[i];
            if (row.SnapshotAllocation is not { } page) continue;
            occupied.TryGetValue(page, out int used);
            occupied[page] = Math.Max(used, row.SnapshotAllocationRow + 1);
        }
        ulong bytes = 0;
        GhosttySnapshotPageAllocation? tail = null;
        for (int i = 0; i < rows.Count; i++)
        {
            TerminalRow row = rows[i];
            GhosttySnapshotPageAllocation? page = row.SnapshotAllocation;
            if (page is null)
            {
                if (row.PreservedColumns is < 1 or > ushort.MaxValue) return ulong.MaxValue;
                // PageList.grow consumes unused tail slots before allocating a
                // standard page. A retained slot number prevents partial prefix
                // pruning from making occupied capacity available a second time.
                int nextSlot = tail is not null ? occupied[tail] : 0;
                if (tail is null || tail.Capacity.Columns != row.PreservedColumns || nextSlot >= tail.Capacity.Rows)
                {
                    tail = new(allocation.InitialCapacity(row.PreservedColumns));
                    nextSlot = 0;
                }
                page = tail;
                row.SnapshotAllocation = page;
                row.SnapshotAllocationRow = nextSlot;
                row.SnapshotAllocationUnmodified = false;
                occupied[page] = nextSlot + 1;
            }
            tail = page;
            if (!pages.TryGetValue(page, out List<TerminalRow>? group)) pages.Add(page, group = []);
            group.Add(row);
        }
        foreach ((GhosttySnapshotPageAllocation page, List<TerminalRow> group) in pages)
        {
            bool unchanged = true;
            foreach (TerminalRow row in group) unchanged &= row.SnapshotAllocationUnmodified;
            if (unchanged)
            {
                bytes = Add(bytes, allocation.AllocatedBytes(page.Capacity));
                continue;
            }
            if (!TryMeasureCapacity(screen, group, allocation, page.Capacity, out GhosttySnapshotPageCapacity capacity)) return ulong.MaxValue;
            GhosttySnapshotPageAllocation updated = capacity == page.Capacity ? page : new(capacity);
            foreach (TerminalRow row in group)
            {
                // Replace the immutable identity only on this row set. A held
                // publication/search COW copy retains its own original charge.
                row.SnapshotAllocation = updated;
                row.SnapshotAllocationUnmodified = true;
            }
            bytes = Add(bytes, allocation.AllocatedBytes(capacity));
        }
        return bytes;
    }

    // Changes to styles, graphemes and OSC8 strings must count, not just rows.
    // This path is used only during incremental restore, never the IO hot path.
    // Native growth buckets are evaluated against observed content, including
    // a replacement grapheme slice. The resulting high-water charge is retained
    // at subsequent admission checkpoints, even after an erase. Mutation-time
    // occupancy/fragmentation must still be tracked for exact allocator history.
    private static bool TryMeasureCapacity(TerminalScreen screen, List<TerminalRow> rows,
        GhosttySnapshotAllocation layout, GhosttySnapshotPageCapacity original, out GhosttySnapshotPageCapacity capacity)
    {
        HashSet<GhosttySnapshotStyle> styles = [];
        HashSet<int> links = [];
        int columns = 1;
        ulong graphemes = 0, temporaryGrapheme = 0, graphemeCells = 0, strings = 0, linkedCells = 0;
        foreach (TerminalRow row in rows)
        {
            columns = Math.Max(columns, row.PreservedColumns);
            foreach (ref readonly TerminalCell cell in row.ReadOnlyPreservedCells)
            {
                GhosttySnapshotStyle style = GhosttySnapshotLivePage.EncodeStyle(in cell);
                if (style != default) styles.Add(style);
                if (cell.Grapheme is { Length: > 0 } text)
                {
                    ulong scalars = 0;
                    foreach (Rune _ in text.EnumerateRunes()) scalars++;
                    if (scalars > 1)
                    {
                        ulong bytes = Align((scalars - 1) * 4, 16);
                        graphemes += bytes;
                        graphemeCells++;
                        // Only the old slice for the cell currently growing
                        // coexists with its replacement, not every page cell.
                        temporaryGrapheme = Math.Max(temporaryGrapheme, bytes - 16);
                    }
                }
                if (cell.HyperlinkId == 0) continue;
                linkedCells++;
                if (!links.Add(cell.HyperlinkId)) continue;
                if (screen.TryGetHyperlink(cell.HyperlinkId, out TerminalHyperlink? link) && link is not null)
                    strings += Align((ulong)link.UriBytes.Length, 32) + Align((ulong)link.ExplicitId.Length, 32);
                else if (screen.TryGetHyperlinkUrl(cell.HyperlinkId, out string? uri) && uri is not null)
                    strings += Align((ulong)Encoding.UTF8.GetByteCount(uri), 32);
            }
        }
        if (columns > ushort.MaxValue || rows.Count > ushort.MaxValue)
        {
            capacity = original;
            return false; // Saturate rather than wrap and accidentally admit history.
        }
        GhosttySnapshotMetadataUsage usage = new((ulong)styles.Count, graphemeCells, graphemes, temporaryGrapheme,
            (ulong)links.Count, linkedCells, strings);
        return layout.TryFitMetadata(original with
        {
            Columns = (ushort)Math.Max(columns, original.Columns),
            Rows = (ushort)Math.Max(rows.Count, original.Rows),
        }, usage, rows.Count, out capacity);
    }

    internal static ulong Add(ulong left, ulong right) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
    private static ulong Align(ulong bytes, ulong alignment) => (bytes + alignment - 1) & ~(alignment - 1);
}
