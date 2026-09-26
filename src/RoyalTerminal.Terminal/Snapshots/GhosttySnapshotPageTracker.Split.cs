// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotPageTracker
{
    // Screen.splitForCapacity compares exact LIVE row layouts, including the
    // target row in both sides. PageList.split keeps the upper allocation and
    // clones the suffix to a same-capacity allocation; ties choose the suffix.
    internal bool SplitForCapacity(ref GhosttySnapshotPageAllocation page, ref State state,
        ref List<TerminalRow> group, TerminalRow target, GhosttySnapshotAllocation layout)
    {
        if (group.Count <= 1) return false;
        int index = group.IndexOf(target);
        if (index < 0) throw new InvalidOperationException("Split target does not belong to its page.");
        ulong above = ExactRowLayoutBytes(state.Storage, page.Capacity, group, 0, index + 1, layout);
        ulong below = ExactRowLayoutBytes(state.Storage, page.Capacity, group, index, group.Count, layout);
        int split = above < below ? index + 1 : index;
        // Splitting at zero is native's no-op; retry cannot free any capacity.
        if (split <= 0 || split >= group.Count) return false;

        List<TerminalRow> upperRows = group.GetRange(0, split);
        List<TerminalRow> lowerRows = group.GetRange(split, group.Count - split);
        State upper = state.Copy();
        State lower = new(new(page.Capacity));
        int columns = page.Capacity.Columns;
        for (int rowIndex = 0; rowIndex < lowerRows.Count; rowIndex++)
        {
            TerminalRow row = lowerRows[rowIndex];
            int source = checked(row.SnapshotAllocationRow * columns);
            int destination = checked(rowIndex * columns);
            // clonePartialRowFrom orders grapheme, hyperlink, then style for
            // each cell. It preserves native IDs but allocates new string/slice
            // storage. Temporary cursor/table references are not cell copies.
            for (int column = 0; column < row.PreservedColumns; column++)
            {
                if (lower.Storage.Graphemes.CopyCellFrom(destination + column, state.Storage.Graphemes, source + column) != GhosttySnapshotGraphemeAddResult.Success ||
                    lower.Storage.Hyperlinks.CopyCellFrom(destination + column, state.Storage.Hyperlinks, source + column) != GhosttySnapshotHyperlinkAddResult.Success ||
                    lower.Storage.Styles.CopyCellFrom(destination + column, state.Storage.Styles, source + column) != GhosttySnapshotSetAddResult.Success)
                    return false;
            }
            lower.ObserveSlot(rowIndex);
            lower.Revisions[rowIndex] = row.SnapshotMetadataRevision;
            upper.Storage.Graphemes.ClearCells(source, columns);
            upper.Storage.Hyperlinks.ClearCells(source, columns);
            upper.Storage.Styles.ClearCells(source, columns);
            upper.RetireSlot(row.SnapshotAllocationRow);
        }

        GhosttySnapshotPageAllocation added = new(page.Capacity);
        _pages.Add(added, lower); // Allocate before changing any existing owner.
        state.Storage = upper.Storage;
        state.Revisions = upper.Revisions;
        state.NextRowSlot = upper.NextRowSlot;
        for (int rowIndex = 0; rowIndex < lowerRows.Count; rowIndex++)
        {
            TerminalRow row = lowerRows[rowIndex];
            row.SnapshotAllocation = added;
            row.SnapshotAllocationRow = rowIndex;
            row.SnapshotAllocationUnmodified = false;
        }
        // Logical row/anchor positions and cells did not move. Upper physical
        // slots, dead entries, bitmap fragmentation and allocator identity stay.
        if (index >= split) { page = added; state = lower; group = lowerRows; }
        else group = upperRows;
        return true;
    }

    private static ulong ExactRowLayoutBytes(GhosttySnapshotPageStorage storage,
        GhosttySnapshotPageCapacity capacity, List<TerminalRow> rows, int first, int end,
        GhosttySnapshotAllocation layout)
    {
        HashSet<int> styles = [], hyperlinks = [];
        ulong graphemes = 0, strings = 0, linkCells = 0;
        int width = 1;
        for (int rowIndex = first; rowIndex < end; rowIndex++)
        {
            TerminalRow row = rows[rowIndex];
            width = Math.Max(width, row.PreservedColumns);
            int offset = checked(row.SnapshotAllocationRow * capacity.Columns);
            for (int column = 0; column < row.PreservedColumns; column++)
            {
                int cell = offset + column;
                int style = storage.Styles.CellId(cell);
                if (style != 0) styles.Add(style);
                graphemes += Align((ulong)storage.Graphemes.SuffixLength(cell) * 4, 16);
                int link = storage.Hyperlinks.CellId(cell);
                if (link == 0) continue;
                linkCells++;
                if (hyperlinks.Add(link) && storage.Hyperlinks.TryGetCell(cell, out GhosttySnapshotHyperlink value))
                    strings += Align((ulong)value.Uri.Length, 32) + Align((ulong)value.ExplicitId.Length, 32);
            }
        }
        ulong linkCapacity = Math.Max(SetCapacity(hyperlinks.Count), (linkCells + 15) / 16) * 48;
        // capacityForCount can round 65535 up to 65536 at the u16 limit.
        // Saturating there preserves the source's identical table layout rather
        // than wrapping its charge to zero (the pinned Zig cast can overflow).
        GhosttySnapshotPageCapacity exact = new((ushort)width, (ushort)(end - first),
            (ushort)Math.Min(ushort.MaxValue, SetCapacity(styles.Count)),
            (ushort)Math.Min(ushort.MaxValue, linkCapacity),
            (uint)Math.Min(uint.MaxValue, graphemes), (uint)Math.Min(uint.MaxValue, strings));
        return layout.LayoutBytes(exact);

        static ulong SetCapacity(int count) => count == 0 ? 0 : ((ulong)(count + 1) * 16 + 12) / 13;
        static ulong Align(ulong bytes, ulong alignment) => (bytes + alignment - 1) & ~(alignment - 1);
    }
}
