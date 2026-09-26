// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

// PageList.resizeWithoutReflowGrowCols: reuse reserved columns, then backfill
// the preceding page, then split into source-capacity-derived pages. Logical
// row order and physical source slots are independent after host rotations.
internal static class GhosttySnapshotColumnResize
{
    private readonly record struct Source(TerminalRow Row, GhosttySnapshotPageAllocation Page, int Slot);

    private sealed class Destination(GhosttySnapshotPageAllocation page, GhosttySnapshotPageStorage storage)
    {
        internal readonly GhosttySnapshotPageAllocation Page = page;
        internal readonly GhosttySnapshotPageStorage Storage = storage;
        internal readonly List<TerminalRow> Rows = [];
        internal int NextSlot;
    }

    internal static void Resize(TerminalRowBuffer rows, int columns, uint foreground, uint background,
        GhosttySnapshotAllocation layout, GhosttySnapshotPageTracker tracker)
    {
        Dictionary<GhosttySnapshotPageAllocation, GhosttySnapshotPageStorage> storage = tracker.ReflowSources(rows, layout);
        Source[] sources = new Source[rows.Count];
        Dictionary<GhosttySnapshotPageAllocation, int> counts = [];
        for (int i = 0; i < rows.Count; i++)
        {
            TerminalRow row = rows[i];
            GhosttySnapshotPageAllocation page = row.SnapshotAllocation!;
            sources[i] = new(row, page, row.SnapshotAllocationRow);
            counts.TryGetValue(page, out int count);
            counts[page] = count + 1;
        }

        List<Destination> destinations = [];
        Destination? previous = null;
        for (int start = 0; start < sources.Length;)
        {
            GhosttySnapshotPageAllocation page = sources[start].Page;
            int end = start + 1;
            while (end < sources.Length && ReferenceEquals(sources[end].Page, page)) end++;
            bool reusable = page.Capacity.Columns >= columns && end - start == counts[page];
            int preservedColumns = columns;
            for (int i = start; i < end; i++)
            {
                TerminalRow row = sources[i].Row;
                preservedColumns = Math.Max(preservedColumns, row.PreservedColumns);
                if (row.Columns < columns && row.ReadOnlyCells[^1].IsWideSpacerHead) reusable = false;
            }

            if (reusable && storage.TryGetValue(page, out GhosttySnapshotPageStorage? original))
            {
                // Copy even when no cells change: later backfill must not mutate
                // either a borrowed source or a retained publication's table.
                previous = new(page, original.Copy());
                destinations.Add(previous);
                for (int i = start; i < end; i++)
                {
                    Source source = sources[i];
                    source.Row.Resize(columns, foreground, background);
                    source.Row.SnapshotAllocationUnmodified = false;
                    previous.Rows.Add(source.Row);
                    previous.NextSlot = Math.Max(previous.NextSlot, source.Slot + 1);
                }
                start = end;
                continue;
            }

            // The generic screen API deliberately preserves hidden cells. They
            // still need a valid stride if a spacer forces a clone after shrink.
            // Processor-owned native-like resizes clear hidden cells separately.
            GhosttySnapshotPageCapacity capacity = Adjust(page.Capacity, preservedColumns, counts[page], layout);
            storage.TryGetValue(page, out GhosttySnapshotPageStorage? sourceStorage);
            int index = start;
            if (previous is not null && !previous.Page.MetadataOverflow && sourceStorage is not null &&
                previous.Page.Capacity.Columns >= preservedColumns)
            {
                while (index < end && previous.NextSlot < previous.Page.Capacity.Rows)
                {
                    if (!Copy(previous, sources[index], sourceStorage)) break;
                    Assign(previous, sources[index++], columns, foreground, background);
                }
            }

            while (index < end)
            {
                Destination destination = new(new(capacity, metadataOverflow: sourceStorage is null), new(capacity));
                destinations.Add(destination);
                while (index < end && destination.NextSlot < capacity.Rows)
                {
                    if (sourceStorage is not null && !Copy(destination, sources[index], sourceStorage))
                    {
                        if (destination.NextSlot > 0) break;
                        // A source row should fit its inherited style capacity.
                        // If adversarial probing prevents even one row fitting,
                        // retain the payload with explicit unrepresentable charge;
                        // never retry forever or silently admit more history.
                        destinations.RemoveAt(destinations.Count - 1);
                        destination = new(new(capacity, metadataOverflow: true), new(capacity));
                        destinations.Add(destination);
                        Assign(destination, sources[index++], columns, foreground, background);
                        break;
                    }
                    Assign(destination, sources[index++], columns, foreground, background);
                }
                previous = destination;
            }
            start = end;
        }

        foreach (Destination destination in destinations)
            tracker.InstallReflowPage(destination.Page, destination.Storage, destination.Rows);
    }

    private static GhosttySnapshotPageCapacity Adjust(GhosttySnapshotPageCapacity source, int columns,
        int liveRows, GhosttySnapshotAllocation layout)
        => layout.TryAdjustColumns(source, columns, out GhosttySnapshotPageCapacity adjusted) ? adjusted : source with
        {
            Columns = checked((ushort)columns),
            Rows = checked((ushort)Math.Min(liveRows, source.Rows)),
        };

    private static bool Copy(Destination destination, Source source, GhosttySnapshotPageStorage storage)
    {
        int target = checked(destination.NextSlot * destination.Page.Capacity.Columns);
        int offset = checked(source.Slot * source.Page.Capacity.Columns);
        int count = source.Row.PreservedColumns;
        GhosttySnapshotStyleStorage.CopyCache cache = default;
        int copied = 0;
        while (copied < count)
        {
            int batch = storage.Graphemes.Count == 0 ? count - copied : 1;
            if (batch == 1 && destination.Storage.Graphemes.CopyCellFrom(target + copied, storage.Graphemes, offset + copied) != GhosttySnapshotGraphemeAddResult.Success) break;
            GhosttySnapshotSetAddResult result = destination.Storage.Styles.CopyCellsFrom(target + copied, storage.Styles,
                offset + copied, batch, ref cache, out int added);
            copied += added;
            if (result != GhosttySnapshotSetAddResult.Success) break;
        }
        if (copied == count) return true;
        // Page.cloneRowFrom may fail after copying a styled prefix. Roll back
        // that row's references, retaining dead IDs/probe history from the try.
        // No rehash or capacity growth is allowed on a preceding-page backfill.
        destination.Storage.Styles.ClearCells(target, count);
        destination.Storage.Graphemes.ClearCells(target, count);
        return false;
    }

    private static void Assign(Destination destination, Source source, int columns, uint foreground, uint background)
    {
        TerminalRow row = source.Row;
        int oldColumns = row.Columns;
        bool grew = columns > oldColumns;
        bool spacer = grew && row.ReadOnlyCells[^1].IsWideSpacerHead;
        row.Resize(columns, foreground, background);
        if (grew)
        {
            // clonePartialRowFrom copies a short source into a blank wider row.
            row.WrapsToNext = row.IsWrapContinuation = false;
            if (spacer)
            {
                ref TerminalCell cell = ref row[oldColumns - 1];
                cell.IsWideSpacerHead = false;
                cell.Width = 1;
            }
        }
        row.SnapshotAllocation = destination.Page;
        row.SnapshotAllocationRow = destination.NextSlot++;
        row.SnapshotAllocationUnmodified = false;
        destination.Rows.Add(row);
    }
}
