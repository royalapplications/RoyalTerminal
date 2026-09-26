// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

// Page.cloneFrom visits the logical Row array, not sorted physical cell blocks.
// Snapshot the address translation before a replacement commits new row slots.
// Only retained host rows/columns are represented, never the declared PAGE size.
internal sealed class GhosttySnapshotPageRemap
{
    internal readonly record struct Row(int Source, int Destination, int Length);
    internal readonly record struct Cell(int Source, int Destination) : IComparable<Cell>
    {
        public int CompareTo(Cell other) => Destination.CompareTo(other.Destination);
    }

    private readonly int _sourceColumns;
    private readonly Dictionary<int, Row> _slots;
    internal Row[] Rows { get; }

    internal GhosttySnapshotPageRemap(int sourceColumns, int destinationColumns, IReadOnlyList<TerminalRow> rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceColumns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(destinationColumns, 1);
        _sourceColumns = sourceColumns;
        _slots = new(rows.Count);
        Rows = new Row[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            TerminalRow row = rows[i];
            Row copy = new(checked(row.SnapshotAllocationRow * sourceColumns), checked(i * destinationColumns),
                Math.Min(row.PreservedColumns, Math.Min(sourceColumns, destinationColumns)));
            _slots.Add(row.SnapshotAllocationRow, copy);
            Rows[i] = copy;
        }
    }

    internal bool TryMap(int source, out int destination)
    {
        destination = 0;
        if (source < 0 || !_slots.TryGetValue(source / _sourceColumns, out Row row)) return false;
        int column = source - row.Source;
        if (column >= row.Length) return false;
        destination = checked(row.Destination + column);
        return true;
    }

    internal static List<Cell> OrderCells(ICollection<int> occupied, GhosttySnapshotPageRemap? remap)
    {
        List<Cell> cells = new(occupied.Count);
        foreach (int source in occupied)
        {
            int destination = source;
            if (remap is null || remap.TryMap(source, out destination)) cells.Add(new(source, destination));
        }
        cells.Sort();
        return cells;
    }
}
