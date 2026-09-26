// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

// Logical style allocator for a native PAGE. It retains dead slots and cell/cursor
// references, not just distinct visible values. A stored restore seed is immutable
// by ownership; callers fork before changing it. No screen/row objects are retained.
internal sealed class GhosttySnapshotStyleStorage
{
    private const int ChunkShift = 8;
    private const int ChunkSize = 1 << ChunkShift;
    private sealed class CellChunk
    {
        internal readonly ushort[] Ids = new ushort[ChunkSize];
        internal int Count;
        internal CellChunk Copy()
        {
            CellChunk copy = new() { Count = Count };
            Ids.CopyTo(copy.Ids, 0);
            return copy;
        }
    }

    private readonly GhosttySnapshotRefCountedSet<GhosttySnapshotStyle> _styles;
    // Materialize only chunks containing styled cells. Dense styled rows cost
    // about two bytes per cell, not one dictionary entry/object per character.
    private readonly Dictionary<int, CellChunk> _cells;
    private int _cursorId, _cellCount;

    internal GhosttySnapshotStyleStorage(ushort capacity)
        : this(new(capacity, new StyleContext()), []) { }

    private GhosttySnapshotStyleStorage(GhosttySnapshotRefCountedSet<GhosttySnapshotStyle> styles,
        Dictionary<int, CellChunk> cells)
    { _styles = styles; _cells = cells; }

    internal int Count => _styles.Count;
    internal int CellCount => _cellCount;
    internal GhosttySnapshotStyle Cursor => _cursorId == 0 ? default : _styles.Get(_cursorId);

    internal GhosttySnapshotStyleStorage Copy()
    {
        Dictionary<int, CellChunk> cells = new(_cells.Count);
        foreach ((int index, CellChunk chunk) in _cells) cells.Add(index, chunk.Copy());
        return new(_styles.Copy(new StyleContext()), cells) { _cursorId = _cursorId, _cellCount = _cellCount };
    }

    internal int AddTableReference(GhosttySnapshotStyle value) => value == default ? 0 : _styles.Add(value);
    internal void ReleaseTableReference(int id) => _styles.Release(id);

    internal void AttachDecodedCell(int index, int id)
    {
        if (id == 0) return;
        StoreCell(index, id);
        _styles.Use(id);
    }

    internal GhosttySnapshotStyle CellStyle(int index)
    {
        int id = CellId(index);
        return id == 0 ? default : _styles.Get(id);
    }

    // Screen.manualStyleUpdate releases the old cursor before insertion, even
    // when no character is printed. Failure leaves the pen unallocated/default.
    // Capacity growth/rehash/splitting is the owning page lifecycle's decision.
    internal GhosttySnapshotSetAddResult ChangeCursor(GhosttySnapshotStyle value)
    {
        if (Cursor == value) return GhosttySnapshotSetAddResult.Success;
        _styles.Release(_cursorId);
        _cursorId = 0;
        return value == default ? GhosttySnapshotSetAddResult.Success : _styles.TryAdd(value, out _cursorId);
    }

    internal void WriteCursorToCell(int index)
    {
        if (CellId(index) == _cursorId) return;
        ClearCell(index);
        if (_cursorId == 0) return;
        _styles.Use(_cursorId);
        StoreCell(index, _cursorId);
    }

    internal void ClearCell(int index)
    {
        if (!_cells.TryGetValue(index >> ChunkShift, out CellChunk? chunk)) return;
        ref ushort previous = ref chunk.Ids[index & (ChunkSize - 1)];
        if (previous == 0) return;
        _styles.Release(previous);
        previous = 0;
        _cellCount--;
        if (--chunk.Count == 0) _cells.Remove(index >> ChunkShift);
    }

    private int CellId(int index) => _cells.TryGetValue(index >> ChunkShift, out CellChunk? chunk)
        ? chunk.Ids[index & (ChunkSize - 1)] : 0;

    private void StoreCell(int index, int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (!_cells.TryGetValue(index >> ChunkShift, out CellChunk? chunk))
            _cells.Add(index >> ChunkShift, chunk = new());
        ref ushort cell = ref chunk.Ids[index & (ChunkSize - 1)];
        if (cell != 0) throw new InvalidOperationException("Style cell already owns a reference.");
        cell = checked((ushort)id);
        chunk.Count++;
        _cellCount++;
    }

    // A native page rebuild drops dead entries and copies cell references in
    // physical row/column order, preserving IDs where possible. The cursor is
    // owned by Screen, so the caller reinstalls it after successful replacement.
    // The source remains unchanged if any insertion fails.
    internal GhosttySnapshotSetAddResult Rebuild(ushort capacity, out GhosttySnapshotStyleStorage? rebuilt)
    {
        GhosttySnapshotStyleStorage candidate = new(capacity);
        int[] indices = new int[_cells.Count];
        _cells.Keys.CopyTo(indices, 0);
        Array.Sort(indices);
        foreach (int chunkIndex in indices)
        {
            ReadOnlySpan<ushort> cells = _cells[chunkIndex].Ids;
            for (int offset = 0; offset < cells.Length; offset++)
            {
                int previous = cells[offset];
                if (previous == 0) continue;
                GhosttySnapshotSetAddResult result = candidate._styles.TryAddWithId(_styles.Get(previous), previous, out int id);
                if (result != GhosttySnapshotSetAddResult.Success) { rebuilt = null; return result; }
                candidate.StoreCell((chunkIndex << ChunkShift) + offset, id);
            }
        }
        rebuilt = candidate;
        return GhosttySnapshotSetAddResult.Success;
    }

    private sealed class StyleContext : IGhosttySnapshotSetContext<GhosttySnapshotStyle>
    {
        public ulong Hash(GhosttySnapshotStyle value) => GhosttySnapshotMetadataHash.Style(value);
        public bool Equal(GhosttySnapshotStyle left, GhosttySnapshotStyle right) => left == right;
        public void Deleted(GhosttySnapshotStyle value) { }
    }
}
