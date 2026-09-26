// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Adapted from Ghostty ref_counted_set.zig. See ../THIRD-PARTY-NOTICES.txt.

using System.Numerics;

namespace RoyalTerminal.Terminal.Snapshots;

internal interface IGhosttySnapshotSetContext<T>
{
    ulong Hash(T value);
    bool Equal(T left, T right);
    void Deleted(T value);
}

internal enum GhosttySnapshotSetAddResult { Success, OutOfMemory, NeedsRehash }

// The native Robin Hood table has observable failure/reclamation behavior.
// Sparse buckets and lazily created item IDs avoid trusting capacity hints as
// CLR allocation sizes. ID zero is reserved, as in ref_counted_set.zig.
internal sealed class GhosttySnapshotRefCountedSet<T>(ushort requested, IGhosttySnapshotSetContext<T> context)
{
    private sealed class Item(T value)
    {
        internal T Value = value;
        internal int References, Probe, Bucket = -1;
    }

    private readonly int _tableCapacity = (int)BitOperations.RoundUpToPowerOf2((uint)requested);
    private readonly ushort _requested = requested;
    private readonly int _itemCapacity = (int)(BitOperations.RoundUpToPowerOf2((uint)requested) * 13 / 16);
    private readonly Dictionary<int, int> _buckets = [];
    private readonly List<Item?> _items = [null];
    private readonly int[] _probes = new int[32];
    private int _nextId = 1, _maximumProbe, _living;

    internal int Count => _living;

    // The replacement context owns any external allocation model. In particular,
    // a copied hyperlink set must free strings in its own copied bitmap, never
    // the publication's allocator. T values must be immutable/shared ownership.
    internal GhosttySnapshotRefCountedSet<T> Copy(IGhosttySnapshotSetContext<T> replacementContext)
    {
        GhosttySnapshotRefCountedSet<T> copy = new(_requested, replacementContext)
        { _nextId = _nextId, _maximumProbe = _maximumProbe, _living = _living };
        foreach ((int bucket, int id) in _buckets) copy._buckets.Add(bucket, id);
        for (int id = 1; id < _items.Count; id++)
            copy._items.Add(_items[id] is { } item
                ? new(item.Value) { References = item.References, Probe = item.Probe, Bucket = item.Bucket }
                : null);
        _probes.CopyTo(copy._probes, 0);
        return copy;
    }

    internal int Add(T value)
    {
        _ = TryAdd(value, out int id);
        return id;
    }

    internal GhosttySnapshotSetAddResult TryAdd(T value, out int id)
    {
        id = 0;
        while (_nextId > 1 && _items[_nextId - 1] is not { References: > 0 }) Delete(--_nextId);
        int existing = Lookup(value);
        if (existing != 0)
        {
            context.Deleted(value);
            Use(existing);
            id = existing;
            return GhosttySnapshotSetAddResult.Success;
        }
        // Snapshot decode drops either failure, but live mutation must distinguish
        // native's same-capacity rehash from a capacity increase. Match its f64
        // threshold (including truncation) rather than rounding to a percentage.
        if (_probes[31] > 0) return GhosttySnapshotSetAddResult.OutOfMemory;
        if (_nextId >= _itemCapacity)
            return _living < (int)(_itemCapacity * 0.9)
                ? GhosttySnapshotSetAddResult.NeedsRehash : GhosttySnapshotSetAddResult.OutOfMemory;
        id = Insert(value, _nextId);
        if (id == _nextId) _nextId++;
        return GhosttySnapshotSetAddResult.Success;
    }

    // Page.cloneFrom prefers the original ID while rebuilding row by row. This
    // is not a bulk rehash in old-ID order: probe placement and reference counts
    // influence subsequent reuse and therefore pressure-driven growth.
    internal GhosttySnapshotSetAddResult TryAddWithId(T value, int preferredId, out int id)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(preferredId, 1);
        id = 0;
        if (preferredId < _nextId)
        {
            Item? item = _items[preferredId];
            if (item is null || item.References == 0)
            {
                int existing = Lookup(value);
                if (existing != 0)
                {
                    context.Deleted(value);
                    Use(existing);
                    id = existing;
                    return GhosttySnapshotSetAddResult.Success;
                }
                if (_probes[31] > 0) return GhosttySnapshotSetAddResult.OutOfMemory;
                Delete(preferredId);
                id = Insert(value, preferredId);
                return GhosttySnapshotSetAddResult.Success;
            }
            if (context.Equal(value, item.Value))
            {
                context.Deleted(value);
                Use(preferredId);
                id = preferredId;
                return GhosttySnapshotSetAddResult.Success;
            }
        }
        return TryAdd(value, out id);
    }

    private int Insert(T value, int newId)
    {
        Item fresh = new(value), held = fresh;
        int chosen = newId, heldId = newId;
        ulong hash = context.Hash(value);
        for (int i = 0; i < _tableCapacity - 1; i++)
        {
            int bucket = (int)(unchecked(hash + (ulong)i) & (ulong)(_tableCapacity - 1));
            int id = Bucket(bucket);
            if (id == 0) { Place(bucket, heldId, held); break; }
            Item item = _items[id]!;
            if (item.References == 0)
            {
                context.Deleted(item.Value);
                _probes[item.Probe]--;
                _items[id] = null;
                if (id < newId) chosen = id;
                Place(bucket, heldId, held);
                break;
            }
            if (item.Probe < held.Probe || item.Probe == held.Probe && item.References < held.References)
            {
                Place(bucket, heldId, held);
                heldId = id;
                held = item;
                _probes[item.Probe]--;
            }
            held.Probe++;
        }
        _buckets[fresh.Bucket] = chosen;
        while (_items.Count <= chosen) _items.Add(null);
        _items[chosen] = fresh;
        fresh.References = 1;
        _living++;
        return chosen;
    }

    internal void Release(int id)
    {
        if (id == 0) return;
        Item item = LiveItem(id);
        if (--item.References == 0) _living--;
    }

    internal void ReleaseMultiple(int id, int count)
    {
        if (id == 0 || count == 0) return;
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Item item = LiveItem(id);
        if (count > item.References) throw new InvalidOperationException("Native snapshot set reference underflow.");
        item.References -= count;
        if (item.References == 0) _living--;
    }

    internal void Use(int id)
    {
        Item item = LiveItem(id);
        item.References = checked(item.References + 1);
    }

    internal void UseMultiple(int id, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (id == 0 || count == 0) return;
        Item item = LiveItem(id);
        item.References = checked(item.References + count);
    }
    internal T Get(int id) => LiveItem(id).Value;
    internal int ReferenceCount(int id) => (uint)id < (uint)_items.Count ? _items[id]?.References ?? 0 : 0;

    private Item LiveItem(int id) => id > 0 && id < _nextId && _items[id] is { References: > 0 } item
        ? item : throw new InvalidOperationException("Native snapshot set reference is not live.");

    private int Lookup(T value)
    {
        if (_tableCapacity == 0) return 0;
        ulong hash = context.Hash(value);
        for (int probe = 0; probe <= _maximumProbe; probe++)
        {
            int id = Bucket((int)(unchecked(hash + (ulong)probe) & (ulong)(_tableCapacity - 1)));
            if (id == 0) return 0;
            Item item = _items[id]!;
            if (item.Probe < probe) return 0;
            if (item.Probe == probe && item.References > 0 && context.Equal(value, item.Value)) return id;
        }
        return 0;
    }

    private void Delete(int id)
    {
        if (_items[id] is not { } item) return;
        context.Deleted(item.Value);
        _probes[item.Probe]--;
        int previous = item.Bucket, next = (previous + 1) & (_tableCapacity - 1);
        _buckets.Remove(previous);
        _items[id] = null;
        while (Bucket(next) is var movedId && movedId != 0 && _items[movedId]!.Probe > 0)
        {
            Item moved = _items[movedId]!;
            moved.Bucket = previous;
            _probes[moved.Probe]--;
            _probes[--moved.Probe]++;
            _buckets[previous] = movedId;
            previous = next;
            next = (next + 1) & (_tableCapacity - 1);
        }
        _buckets.Remove(previous);
        while (_maximumProbe > 0 && _probes[_maximumProbe] == 0) _maximumProbe--;
    }

    private void Place(int bucket, int id, Item item)
    {
        _buckets[bucket] = id;
        item.Bucket = bucket;
        _probes[item.Probe]++;
        _maximumProbe = Math.Max(_maximumProbe, item.Probe);
    }

    private int Bucket(int bucket) => _buckets.TryGetValue(bucket, out int id) ? id : 0;
}
