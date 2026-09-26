// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Borrowed live rows grouped without mixing physical widths or exceeding wire allocation capacities.</summary>
internal sealed class GhosttySnapshotPagePlan
{
    internal readonly record struct Range(int Start, int Count);
    internal TerminalRow[] Rows { get; }
    internal List<Range> Resident { get; } = [];
    internal List<Range> History { get; } = [];
    internal int HistoryRows { get; }

    internal GhosttySnapshotPagePlan(TerminalScreen screen, int key, GhosttySnapshotDecodeLimits limits, ref long cells, ref int pages)
    {
        TerminalRowBuffer source = screen.GetSnapshotRows(key) ?? throw new InvalidDataException("Missing snapshot screen.");
        HistoryRows = source.Count - screen.ViewportRows;
        if (HistoryRows < 0) throw new InvalidDataException("Snapshot rows do not cover the viewport.");
        // Check the aggregate budget before allocating even the row reference array.
        for (int i = 0; i < source.Count; i++)
        {
            int columns = source[i].Columns;
            if (columns is < 1 or > ushort.MaxValue) throw new InvalidDataException("Unrepresentable snapshot row width.");
            cells = checked(cells + columns);
            if (cells > limits.MaximumCells) throw new InvalidDataException("Snapshot exceeds the cell limit.");
        }
        Rows = new TerminalRow[source.Count];
        for (int i = 0; i < Rows.Length; i++) Rows[i] = source[i];
        Build(0, HistoryRows, History, screen, limits, ref pages);
        Build(HistoryRows, Rows.Length, Resident, screen, limits, ref pages);
        if (Resident.Count > ushort.MaxValue) throw new InvalidDataException("Too many resident snapshot pages.");
    }

    private void Build(int start, int end, List<Range> result, TerminalScreen owner, GhosttySnapshotDecodeLimits limits, ref int pages)
    {
        HashSet<int> links = [];
        while (start < end)
        {
            links.Clear();
            int columns = Rows[start].Columns;
            int maximumRows = Math.Max(1, 40960 / columns);
            int count = 0, linkedCells = 0;
            long strings = 0, suffixes = 0;
            while (start + count < end && count < maximumRows && Rows[start + count].Columns == columns)
            {
                foreach (ref readonly TerminalCell cell in Rows[start + count].ReadOnlyCells)
                {
                    if (!string.IsNullOrEmpty(cell.Grapheme))
                    {
                        int scalars = 0;
                        foreach (Rune _ in cell.Grapheme.EnumerateRunes()) scalars++;
                        suffixes += Math.Max(0, scalars - 1);
                    }
                    if (cell.HyperlinkId != 0 && owner.TryGetHyperlink(cell.HyperlinkId, out TerminalHyperlink? link))
                    {
                        linkedCells++;
                        if (links.Add(cell.HyperlinkId)) strings += (long)link!.UriBytes.Length + link.ExplicitId.Length;
                    }
                }
                // 800 unique IDs and 16,000 references leave conservative native
                // set/map headroom within the 16-bit hyperlink capacity field.
                if (links.Count > 800 || linkedCells > 16000 || strings > limits.MaximumStringBytesPerRecord ||
                    suffixes > limits.MaximumSuffixCodepointsPerPage)
                {
                    if (count == 0) throw new InvalidDataException("One snapshot row exceeds page capacity or decode limits.");
                    break; // The rejected row is rescanned into the next page.
                }
                count++;
            }
            if (++pages > limits.MaximumPages) throw new InvalidDataException("Snapshot exceeds the page limit.");
            result.Add(new(start, count));
            start += count;
        }
    }
}
