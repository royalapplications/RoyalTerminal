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
        List<TerminalRow> added = [];
        ulong bytes = 0;
        int capacityRows = allocation.InitialRows(screen.Columns);
        for (int i = 0; i < rows.Count; i++)
        {
            TerminalRow row = rows[i];
            if (row.SnapshotAllocation is { } page)
            {
                FlushAdded();
                if (!pages.TryGetValue(page, out List<TerminalRow>? group)) pages.Add(page, group = []);
                group.Add(row);
            }
            else
            {
                // New managed rows are accounted in standard logical pages.
                if (added.Count > 0 && added[0].PreservedColumns != row.PreservedColumns) FlushAdded();
                added.Add(row);
                if (added.Count >= capacityRows) FlushAdded();
            }
        }
        FlushAdded();
        foreach ((GhosttySnapshotPageAllocation page, List<TerminalRow> group) in pages)
        {
            bool unchanged = true;
            foreach (TerminalRow row in group) unchanged &= row.SnapshotAllocationUnmodified;
            ulong original = allocation.AllocatedBytes(page.Capacity);
            bytes = Add(bytes, unchanged ? original : MeasureRows(screen, group, allocation, page.Capacity));
        }
        return bytes;

        void FlushAdded()
        {
            if (added.Count == 0) return;
            bytes = Add(bytes, MeasureRows(screen, added, allocation));
            added.Clear();
        }
    }

    // Changes to styles, graphemes and OSC8 strings must count, not just rows.
    // This path is used only during incremental restore, never the IO hot path.
    // Managed rows do not share Ghostty's mutable page allocator; growth uses a
    // conservative, content-derived capacity and retains the original charge.
    private static ulong MeasureRows(TerminalScreen screen, List<TerminalRow> rows, GhosttySnapshotAllocation allocation,
        GhosttySnapshotPageCapacity original = default)
    {
        HashSet<GhosttySnapshotStyle> styles = [];
        HashSet<int> links = [];
        int columns = 1;
        ulong graphemes = 0, strings = 0, linkedCells = 0;
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
                    if (scalars > 1) graphemes += 2 * Align((scalars - 1) * 4, 16);
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
        ulong styleCapacity = SetCapacity((ulong)styles.Count);
        ulong linkBytes = Math.Max(SetCapacity((ulong)links.Count), (linkedCells + 15) / 16) * 64;
        if (columns > ushort.MaxValue || rows.Count > ushort.MaxValue || styleCapacity > ushort.MaxValue ||
            linkBytes > ushort.MaxValue || graphemes > uint.MaxValue || strings > uint.MaxValue)
            return ulong.MaxValue; // Saturate rather than wrap and accidentally admit history.
        return allocation.AllocatedBytes(new((ushort)Math.Max(columns, original.Columns), (ushort)Math.Max(rows.Count, original.Rows),
            (ushort)Math.Max(styleCapacity, original.Styles), (ushort)Math.Max(linkBytes, original.HyperlinkBytes),
            (uint)Math.Max(graphemes, original.GraphemeBytes), (uint)Math.Max(strings, original.StringBytes)));
    }

    internal static ulong Add(ulong left, ulong right) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
    private static ulong SetCapacity(ulong count) => count == 0 ? 0 : ((count + 1) * 16 + 12) / 13;
    private static ulong Align(ulong bytes, ulong alignment) => (bytes + alignment - 1) & ~(alignment - 1);
}
