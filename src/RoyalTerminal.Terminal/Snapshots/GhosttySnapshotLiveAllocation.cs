// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

// Immutable allocation identity survives row moves, partial pruning and COW.
// No live screen/row references are retained by the identity itself.
internal sealed class GhosttySnapshotPageAllocation(GhosttySnapshotPageCapacity capacity, GhosttySnapshotStyleStorage? restoredStyles = null,
    bool metadataOverflow = false, GhosttySnapshotGraphemeStorage? restoredGraphemes = null,
    GhosttySnapshotHyperlinkStorage? restoredHyperlinks = null)
{
    internal GhosttySnapshotPageCapacity Capacity { get; } = capacity;
    internal bool MetadataOverflow { get; } = metadataOverflow;
    internal bool HasStyleSeed => restoredStyles is not null;
    // This is the decode-time seed, not a measurement of mutable live rows.
    internal GhosttySnapshotStyleStorage CopyRestoredStyles() => restoredStyles?.Copy() ?? new(Capacity.Styles);
    internal bool HasGraphemeSeed => restoredGraphemes is not null;
    internal GhosttySnapshotGraphemeStorage CopyRestoredGraphemes() => restoredGraphemes?.Copy() ?? new(Capacity.GraphemeBytes);
    internal bool HasHyperlinkSeed => restoredHyperlinks is not null;
    internal GhosttySnapshotHyperlinkStorage CopyRestoredHyperlinks() => restoredHyperlinks?.Copy() ?? new(Capacity.HyperlinkBytes, Capacity.StringBytes);
}

/// <summary>Measures only currently retained storage, including the unpublished COW screen.</summary>
internal static class GhosttySnapshotLiveAllocation
{
    internal static ulong Measure(TerminalScreen screen, TerminalRowBuffer rows, GhosttySnapshotAllocation allocation)
        => Measure(screen, rows, allocation, out _);

    internal static ulong Measure(TerminalScreen screen, TerminalRowBuffer rows, GhosttySnapshotAllocation allocation,
        out HashSet<GhosttySnapshotPageAllocation>? unrepresentable)
    {
        unrepresentable = null;
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
            screen.SnapshotRowsObserved(page, group);
            bool unchanged = true;
            foreach (TerminalRow row in group) unchanged &= row.SnapshotAllocationUnmodified;
            if (unchanged)
            {
                if (page.MetadataOverflow)
                {
                    (unrepresentable ??= []).Add(page);
                    bytes = ulong.MaxValue;
                    continue;
                }
                bytes = Add(bytes, allocation.AllocatedBytes(page.Capacity));
                continue;
            }
            if (!TryMeasureCapacity(screen, group, allocation, page.Capacity, out GhosttySnapshotPageCapacity capacity))
            {
                (unrepresentable ??= []).Add(page);
                bytes = ulong.MaxValue;
                continue;
            }
            GhosttySnapshotPageAllocation updated = capacity == page.Capacity && !page.MetadataOverflow ? page : new(capacity);
            if (!ReferenceEquals(page, updated)) updated = screen.SnapshotAllocationReplaced(page, updated, group);
            foreach (TerminalRow row in group)
            {
                // Replace the immutable identity only on this row set. A held
                // publication/search COW copy retains its own original charge.
                row.SnapshotAllocation = updated;
                row.SnapshotAllocationUnmodified = true;
            }
            if (updated.MetadataOverflow)
            {
                (unrepresentable ??= []).Add(updated);
                bytes = ulong.MaxValue;
                continue;
            }
            bytes = Add(bytes, allocation.AllocatedBytes(capacity));
        }
        return bytes;
    }

    // Changes to styles, graphemes and OSC8 strings must count, not just rows.
    // Used at admission/resize and quota-enforcement checkpoints, not per cell.
    // Native growth buckets are evaluated against observed content, including
    // a replacement grapheme slice. The resulting high-water charge is retained
    // at subsequent admission checkpoints, even after an erase. Mutation-time
    // occupancy/fragmentation must still be tracked for exact allocator history.
    private static bool TryMeasureCapacity(TerminalScreen screen, List<TerminalRow> rows,
        GhosttySnapshotAllocation layout, GhosttySnapshotPageCapacity original, out GhosttySnapshotPageCapacity capacity)
    {
        int trackedStyleCount = 0;
        bool trackedStyles = rows[0].SnapshotAllocation is { } page && screen.TryGetSnapshotStyleUsage(page, rows, out trackedStyleCount);
        HashSet<GhosttySnapshotStyle>? styles = trackedStyles ? null : [];
        int columns = 1;
        ulong graphemes = 0, temporaryGrapheme = 0, graphemeCells = 0, strings = 0, linkedCells = 0;
        bool trackedGraphemes = rows[0].SnapshotAllocation is { } owner &&
            screen.TryGetSnapshotGraphemeUsage(owner, rows, out graphemeCells, out graphemes);
        ulong trackedLinkCount = 0;
        bool trackedLinks = rows[0].SnapshotAllocation is { } linkOwner &&
            screen.TryGetSnapshotHyperlinkUsage(linkOwner, rows, out trackedLinkCount, out linkedCells, out strings);
        HashSet<int>? links = trackedLinks ? null : [];
        foreach (TerminalRow row in rows)
        {
            columns = Math.Max(columns, row.PreservedColumns);
            // At quota checkpoints the mutation tracker already owns exact
            // counts. Do not scan every cell of fully accounted live history.
            if (trackedStyles && trackedGraphemes && trackedLinks) continue;
            foreach (ref readonly TerminalCell cell in row.ReadOnlyPreservedCells)
            {
                if (styles is not null)
                {
                    GhosttySnapshotStyle style = GhosttySnapshotLivePage.EncodeStyle(in cell);
                    if (style != default) styles.Add(style);
                }
                if (!trackedGraphemes && cell.Grapheme is { Length: > 0 } text)
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
                if (links is null || cell.HyperlinkId == 0) continue;
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
        // A current event tracker is authoritative: inline background-only
        // cells need no style entry, while an unprinted cursor can own one.
        int styleCount = trackedStyles ? trackedStyleCount : styles!.Count;
        GhosttySnapshotMetadataUsage usage = new((ulong)styleCount, graphemeCells, graphemes, temporaryGrapheme,
            trackedLinks ? trackedLinkCount : (ulong)links!.Count, linkedCells, strings);
        return layout.TryFitMetadata(original with
        {
            Columns = (ushort)Math.Max(columns, original.Columns),
            Rows = (ushort)Math.Max(rows.Count, original.Rows),
        }, usage, rows.Count, out capacity);
    }

    internal static ulong Add(ulong left, ulong right) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
    private static ulong Align(ulong bytes, ulong alignment) => (bytes + alignment - 1) & ~(alignment - 1);
}
