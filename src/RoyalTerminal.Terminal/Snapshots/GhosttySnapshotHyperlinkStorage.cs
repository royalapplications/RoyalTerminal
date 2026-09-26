// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Adapted from Ghostty Page.zig / hyperlink.zig. See ../THIRD-PARTY-NOTICES.txt.

namespace RoyalTerminal.Terminal.Snapshots;

internal enum GhosttySnapshotHyperlinkAddResult { Success, InvalidEntry, StringsFull, SetFull, SetNeedsRehash, MapFull }

// Logical page-owned strings, refcounted links and cell map. Immutable encoded
// values are shared by COW forks; bitmap locations, dead entries, cursor and cell
// references are not. No CLR allocation is sized from native capacity hints.
internal sealed class GhosttySnapshotHyperlinkStorage
{
    private sealed record Entry(byte[] Encoded, GhosttySnapshotBitmap.Slice Id, GhosttySnapshotBitmap.Slice Uri, ulong Hash);
    private readonly GhosttySnapshotRefCountedSet<Entry> _links;
    private readonly StringContext _strings;
    private readonly Dictionary<int, int> _cells;
    private readonly ulong _mapCapacity;
    private int _cursorId;

    internal GhosttySnapshotHyperlinkStorage(ushort hyperlinkBytes, uint stringBytes)
    {
        _strings = new(new(stringBytes, 32));
        _links = new((ushort)(hyperlinkBytes / 48), _strings);
        _cells = [];
        _mapCapacity = GhosttySnapshotAllocation.MapItemCapacity(hyperlinkBytes / 48UL * 16, 80);
    }

    private GhosttySnapshotHyperlinkStorage(GhosttySnapshotHyperlinkStorage source)
    {
        _strings = source._strings.Copy();
        _links = source._links.Copy(_strings);
        _cells = new(source._cells);
        _mapCapacity = source._mapCapacity;
        _cursorId = source._cursorId;
    }

    internal int Count => _links.Count;
    internal int CellCount => _cells.Count;
    internal ulong StringBytes => _strings.AllocatedBytes;
    internal int CursorId => _cursorId;
    internal ReadOnlySpan<byte> CursorEncoding => _cursorId == 0 ? default : _links.Get(_cursorId).Encoded;
    internal GhosttySnapshotHyperlinkStorage Copy() => new(this);
    internal int CellId(int index) => _cells.TryGetValue(index, out int id) ? id : 0;
    internal int ReferenceCount(int id) => _links.ReferenceCount(id);

    internal bool TryGetCell(int index, out GhosttySnapshotHyperlink link)
    {
        int id = CellId(index);
        link = id == 0 ? default : GhosttySnapshotHyperlink.Read(_links.Get(id).Encoded, out _);
        return id != 0;
    }

    internal bool TryGetAllocation(int id, out GhosttySnapshotBitmap.Slice explicitId, out GhosttySnapshotBitmap.Slice uri)
    {
        if (_links.ReferenceCount(id) == 0) { explicitId = uri = default; return false; }
        Entry entry = _links.Get(id);
        explicitId = entry.Id; uri = entry.Uri;
        return true;
    }

    // PAGE decode owns one reference per accepted wire table entry, not per
    // distinct value. Decode allocates explicit ID before URI, even for an
    // ultimately ignored zero/duplicate wire ID. Owned bytes must be immutable.
    internal int AddDecodedTableReference(GhosttySnapshotHyperlink link, ReadOnlySpan<byte> encoded, byte[]? owned = null)
    {
        if (Allocate(link, encoded, owned, idFirst: true, out Entry? entry) != GhosttySnapshotHyperlinkAddResult.Success) return 0;
        GhosttySnapshotSetAddResult result = _links.TryAdd(entry!, out int id);
        if (result != GhosttySnapshotSetAddResult.Success) _strings.Deleted(entry!);
        return id;
    }

    internal void ReleaseTableReference(int id) => _links.Release(id);

    internal GhosttySnapshotHyperlinkAddResult AttachDecodedCell(int index, int id)
    {
        if (id == 0) return GhosttySnapshotHyperlinkAddResult.Success;
        return AssignCell(index, id);
    }

    // startHyperlinkOnce ends the old cursor even for an equal explicit link.
    // Unlike cloning, normal insertion allocates strings BEFORE deduplication.
    internal GhosttySnapshotHyperlinkAddResult StartCursor(ReadOnlySpan<byte> encoded, byte[]? owned = null)
    {
        GhosttySnapshotHyperlink link = GhosttySnapshotHyperlink.Read(encoded, out _);
        EndCursor();
        GhosttySnapshotHyperlinkAddResult allocation = Allocate(link, encoded, owned, idFirst: false, out Entry? entry);
        if (allocation != GhosttySnapshotHyperlinkAddResult.Success) return allocation;
        GhosttySnapshotSetAddResult result = _links.TryAdd(entry!, out _cursorId);
        if (result != GhosttySnapshotSetAddResult.Success) _strings.Deleted(entry!);
        return Convert(result);
    }

    internal void EndCursor()
    {
        _links.Release(_cursorId);
        _cursorId = 0;
    }

    internal GhosttySnapshotHyperlinkAddResult WriteCursorToCell(int index)
    {
        if (_cursorId == 0) { Clear(index); return GhosttySnapshotHyperlinkAddResult.Success; }
        return AssignCell(index, _cursorId);
    }

    private GhosttySnapshotHyperlinkAddResult AssignCell(int index, int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        int previous = CellId(index);
        if (previous == 0 && (ulong)_cells.Count >= _mapCapacity) return GhosttySnapshotHyperlinkAddResult.MapFull;
        // Each caller still owns a cursor/table reference. Increase before
        // releasing the cell so replacing the last equal link cannot kill it.
        _links.Use(id);
        _links.Release(previous);
        _cells[index] = id;
        return GhosttySnapshotHyperlinkAddResult.Success;
    }

    internal void Clear(int index)
    {
        if (_cells.Remove(index, out int id)) _links.Release(id);
    }

    internal void ClearCells(int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        int end = checked(start + count);
        if (count <= _cells.Count)
        {
            for (int index = start; index < end; index++) Clear(index);
        }
        else foreach (int index in _cells.Keys)
            if (index >= start && index < end) Clear(index);
    }

    internal void Swap(int left, int right)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(left);
        ArgumentOutOfRangeException.ThrowIfNegative(right);
        if (left == right) return;
        bool a = _cells.Remove(left, out int first), b = _cells.Remove(right, out int second);
        if (a) _cells.Add(right, first);
        if (b) _cells.Add(left, second);
    }

    internal void RetainRows(IReadOnlySet<int> rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        foreach (int index in _cells.Keys)
            if (!rows.Contains(index / columns)) Clear(index);
    }

    // Page.clonePartialRowFrom checks map capacity, looks up the value BEFORE
    // allocating strings, then clones URI before explicit ID and prefers the
    // source native ID. The destination must have been cleared by its owner.
    internal GhosttySnapshotHyperlinkAddResult CopyCellFrom(int destination, GhosttySnapshotHyperlinkStorage source, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(destination);
        if (CellId(destination) != 0) throw new InvalidOperationException("Hyperlink clone destination must be empty.");
        int sourceId = source.CellId(index);
        if (sourceId == 0) return GhosttySnapshotHyperlinkAddResult.Success;
        if ((ulong)_cells.Count >= _mapCapacity) return GhosttySnapshotHyperlinkAddResult.MapFull;
        if (ReferenceEquals(this, source)) return AssignCell(destination, sourceId);
        Entry value = source._links.Get(sourceId);
        int found = _links.Lookup(value);
        if (found != 0) return AssignCell(destination, found);

        GhosttySnapshotHyperlink link = GhosttySnapshotHyperlink.Read(value.Encoded, out _);
        GhosttySnapshotHyperlinkAddResult allocation = Allocate(link, value.Encoded, value.Encoded, idFirst: false, out Entry? copied);
        if (allocation != GhosttySnapshotHyperlinkAddResult.Success) return allocation;
        GhosttySnapshotSetAddResult result = _links.TryAddWithId(copied!, sourceId, out int id);
        // Pinned Page.clonePartialRowFrom does NOT free the copied strings on
        // set failure. Preserve that pressure until the owning page rebuilds;
        // normal insertion and decode, in contrast, explicitly roll them back.
        if (result != GhosttySnapshotSetAddResult.Success) return Convert(result);
        _cells.Add(destination, id); // The insertion reference belongs to the cell.
        return GhosttySnapshotHyperlinkAddResult.Success;
    }

    // cursorSetHyperlink reserves an extra URI on map failure before growing
    // hyperlink capacity. Successful scratch is intentionally not freed: native
    // immediately replaces the page. The owner must rebuild after this call.
    internal bool TryReserveCursorUri()
        => _cursorId == 0 || _strings.TryAllocate(GhosttySnapshotHyperlink.Read(CursorEncoding, out _).Uri.Length, out _);

    // Clone only cell-owned references in physical order. The Screen owner
    // separately restores style then hyperlink cursor state, which can itself
    // fail because cursor insertion allocates temporary duplicate strings.
    internal GhosttySnapshotHyperlinkAddResult Rebuild(ushort hyperlinkBytes, uint stringBytes, out GhosttySnapshotHyperlinkStorage? rebuilt)
    {
        GhosttySnapshotHyperlinkStorage candidate = new(hyperlinkBytes, stringBytes);
        List<int> cells = new(_cells.Keys);
        cells.Sort();
        foreach (int index in cells)
        {
            GhosttySnapshotHyperlinkAddResult result = candidate.CopyCellFrom(index, this, index);
            if (result != GhosttySnapshotHyperlinkAddResult.Success) { rebuilt = null; return result; }
        }
        rebuilt = candidate;
        return GhosttySnapshotHyperlinkAddResult.Success;
    }

    private GhosttySnapshotHyperlinkAddResult Allocate(GhosttySnapshotHyperlink link, ReadOnlySpan<byte> encoded,
        byte[]? owned, bool idFirst, out Entry? entry)
    {
        entry = null;
        if (link.HasExplicitId && link.ExplicitId.IsEmpty) return GhosttySnapshotHyperlinkAddResult.InvalidEntry;
        GhosttySnapshotBitmap.Slice id = default, uri = default;
        if (idFirst && link.HasExplicitId && !_strings.TryAllocate(link.ExplicitId.Length, out id))
            return GhosttySnapshotHyperlinkAddResult.StringsFull;
        if (link.Uri.IsEmpty)
        {
            _strings.Free(id);
            return GhosttySnapshotHyperlinkAddResult.InvalidEntry;
        }
        if (!_strings.TryAllocate(link.Uri.Length, out uri))
        {
            _strings.Free(id);
            return GhosttySnapshotHyperlinkAddResult.StringsFull;
        }
        if (!idFirst && link.HasExplicitId && !_strings.TryAllocate(link.ExplicitId.Length, out id))
        {
            _strings.Free(uri);
            return GhosttySnapshotHyperlinkAddResult.StringsFull;
        }
        entry = new(owned ?? encoded.ToArray(), id, uri, GhosttySnapshotMetadataHash.Hyperlink(link));
        return GhosttySnapshotHyperlinkAddResult.Success;
    }

    private static GhosttySnapshotHyperlinkAddResult Convert(GhosttySnapshotSetAddResult result) => result switch
    {
        GhosttySnapshotSetAddResult.Success => GhosttySnapshotHyperlinkAddResult.Success,
        GhosttySnapshotSetAddResult.NeedsRehash => GhosttySnapshotHyperlinkAddResult.SetNeedsRehash,
        _ => GhosttySnapshotHyperlinkAddResult.SetFull,
    };

    private sealed class StringContext(GhosttySnapshotBitmap bitmap) : IGhosttySnapshotSetContext<Entry>
    {
        internal ulong AllocatedBytes;
        internal StringContext Copy() => new(bitmap.Copy()) { AllocatedBytes = AllocatedBytes };
        internal bool TryAllocate(int bytes, out GhosttySnapshotBitmap.Slice slice)
        {
            if (!bitmap.TryAllocate(bytes, out slice)) return false;
            AllocatedBytes += (ulong)slice.Chunks * 32;
            return true;
        }
        internal void Free(GhosttySnapshotBitmap.Slice slice)
        {
            bitmap.Free(slice);
            AllocatedBytes -= (ulong)slice.Chunks * 32;
        }
        public ulong Hash(Entry value) => value.Hash;
        public bool Equal(Entry left, Entry right) => left.Encoded.AsSpan().SequenceEqual(right.Encoded);
        public void Deleted(Entry value) { Free(value.Id); Free(value.Uri); }
    }
}
