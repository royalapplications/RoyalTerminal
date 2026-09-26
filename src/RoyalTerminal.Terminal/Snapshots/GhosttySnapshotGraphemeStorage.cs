// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Adapted from Ghostty Page.zig. See ../THIRD-PARTY-NOTICES.txt.

namespace RoyalTerminal.Terminal.Snapshots;

internal enum GhosttySnapshotGraphemeAddResult { Success, AllocatorFull, MapFull }

// Logical per-PAGE allocation ownership, not duplicate grapheme text storage.
// Sparse entries retain exact slice locations, lengths and bitmap search state.
// Cell keys use physical page stride; copies/rebuilds intentionally differ:
// a COW fork keeps fragmentation, while a native page clone repacks row-major.
internal sealed class GhosttySnapshotGraphemeStorage
{
    private readonly record struct Entry(GhosttySnapshotBitmap.Slice Slice, byte Length);
    private readonly GhosttySnapshotBitmap _bitmap;
    private readonly ulong _mapCapacity;
    private readonly Dictionary<int, Entry> _cells;

    internal GhosttySnapshotGraphemeStorage(uint capacityBytes)
    {
        _mapCapacity = GhosttySnapshotAllocation.GraphemeCellCapacity(capacityBytes);
        _bitmap = new(capacityBytes, 16);
        _cells = [];
    }

    private GhosttySnapshotGraphemeStorage(GhosttySnapshotGraphemeStorage source)
    {
        _mapCapacity = source._mapCapacity;
        _bitmap = source._bitmap.Copy();
        _cells = new(source._cells);
        AllocatedBytes = source.AllocatedBytes;
    }

    internal int Count => _cells.Count;
    internal ulong AllocatedBytes { get; private set; }
    internal int SuffixLength(int cell) => _cells.TryGetValue(cell, out Entry entry) ? entry.Length : 0;
    internal GhosttySnapshotGraphemeStorage Copy() => new(this);

    // Diagnostics expose values only, not writable bitmap/map state.
    internal bool TryGetAllocation(int cell, out GhosttySnapshotBitmap.Slice slice)
    {
        bool found = _cells.TryGetValue(cell, out Entry entry);
        slice = entry.Slice;
        return found;
    }

    // Page.appendGrapheme retains the old slice on allocation failure. A restore
    // caller separately clears the entire suffix; live Screen growth retries it.
    internal GhosttySnapshotGraphemeAddResult Append(int cell)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        if (!_cells.TryGetValue(cell, out Entry previous)) return Set(cell, 1);
        if (previous.Length >= TerminalGraphemeStorage.MaximumSuffixCodepoints) return GhosttySnapshotGraphemeAddResult.Success;
        byte length = (byte)(previous.Length + 1);
        if (previous.Length % 4 != 0)
        {
            _cells[cell] = previous with { Length = length };
            return GhosttySnapshotGraphemeAddResult.Success;
        }
        if (!_bitmap.TryAllocate(length * 4, out GhosttySnapshotBitmap.Slice next))
            return GhosttySnapshotGraphemeAddResult.AllocatorFull;
        // Both allocations coexist until the replacement has succeeded.
        _bitmap.Free(previous.Slice);
        _cells[cell] = new(next, length);
        AllocatedBytes += (ulong)(next.Chunks - previous.Slice.Chunks) * 16;
        return GhosttySnapshotGraphemeAddResult.Success;
    }

    // Snapshot decode feeds scalars one at a time. Skip only the in-chunk
    // increments, which cannot allocate; still visit every replacement boundary.
    internal GhosttySnapshotGraphemeAddResult AppendToLength(int cell, int codepoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        ArgumentOutOfRangeException.ThrowIfNegative(codepoints);
        int target = Math.Min(codepoints, TerminalGraphemeStorage.MaximumSuffixCodepoints);
        while (SuffixLength(cell) < target)
        {
            GhosttySnapshotGraphemeAddResult result = Append(cell);
            if (result != GhosttySnapshotGraphemeAddResult.Success) return result;
            Entry entry = _cells[cell];
            _cells[cell] = entry with { Length = (byte)Math.Min(target, entry.Slice.Chunks * 4) };
        }
        return GhosttySnapshotGraphemeAddResult.Success;
    }

    // Page.setGraphemes allocates the full suffix once, unlike input/restore
    // append. The destination must have been cleared by its owning row copy.
    internal GhosttySnapshotGraphemeAddResult Set(int cell, int codepoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        ArgumentOutOfRangeException.ThrowIfNegative(codepoints);
        if (_cells.ContainsKey(cell)) throw new InvalidOperationException("Grapheme clone destination must be empty.");
        int count = Math.Min(codepoints, TerminalGraphemeStorage.MaximumSuffixCodepoints);
        if (count == 0) return GhosttySnapshotGraphemeAddResult.Success;
        if (!_bitmap.TryAllocate(count * 4, out GhosttySnapshotBitmap.Slice slice))
            return GhosttySnapshotGraphemeAddResult.AllocatorFull;
        // Native allocates before inserting into the map. Even failed map
        // insertion must perform the same bitmap free/search-start transition.
        if ((ulong)_cells.Count >= _mapCapacity)
        {
            _bitmap.Free(slice);
            return GhosttySnapshotGraphemeAddResult.MapFull;
        }
        _cells.Add(cell, new(slice, (byte)count));
        AllocatedBytes += (ulong)slice.Chunks * 16;
        return GhosttySnapshotGraphemeAddResult.Success;
    }

    internal void Clear(int cell)
    {
        if (!_cells.Remove(cell, out Entry entry)) return;
        _bitmap.Free(entry.Slice);
        AllocatedBytes -= (ulong)entry.Slice.Chunks * 16;
    }

    internal void ClearCells(int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        int end = checked(start + count);
        // Dictionary.Remove is supported during enumeration on our .NET target.
        // Visit sparse grapheme cells rather than every cell in a wide blank row.
        foreach (int cell in _cells.Keys)
            if (cell >= start && cell < end) Clear(cell);
    }

    // Same-page ownership permutations allocate no new bitmap slices.
    internal void Swap(int left, int right)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(left);
        ArgumentOutOfRangeException.ThrowIfNegative(right);
        if (left == right) return;
        bool hasLeft = _cells.Remove(left, out Entry a), hasRight = _cells.Remove(right, out Entry b);
        if (hasLeft) _cells.Add(right, a);
        if (hasRight) _cells.Add(left, b);
    }

    internal GhosttySnapshotGraphemeAddResult CopyCellFrom(int destination, GhosttySnapshotGraphemeStorage source, int index)
        => Set(destination, source.SuffixLength(index));

    internal void RetainRows(IReadOnlySet<int> rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        foreach (int cell in _cells.Keys)
            if (!rows.Contains(cell / columns)) Clear(cell);
    }

    internal GhosttySnapshotGraphemeAddResult Rebuild(uint capacityBytes, out GhosttySnapshotGraphemeStorage? rebuilt)
    {
        GhosttySnapshotGraphemeStorage result = new(capacityBytes);
        List<int> cells = new(_cells.Keys);
        cells.Sort(); // Page.cloneFrom visits physical rows/cells, not old insertion order.
        foreach (int cell in cells)
        {
            GhosttySnapshotGraphemeAddResult status = result.Set(cell, _cells[cell].Length);
            if (status == GhosttySnapshotGraphemeAddResult.Success) continue;
            rebuilt = null;
            return status;
        }
        rebuilt = result;
        return GhosttySnapshotGraphemeAddResult.Success;
    }
}
