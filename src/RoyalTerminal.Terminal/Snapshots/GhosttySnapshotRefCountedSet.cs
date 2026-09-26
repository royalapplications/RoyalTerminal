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
    private readonly int _itemCapacity = (int)(BitOperations.RoundUpToPowerOf2((uint)requested) * 13 / 16);
    private readonly Dictionary<int, int> _buckets = [];
    private readonly List<Item?> _items = [null];
    private readonly int[] _probes = new int[32];
    private int _nextId = 1, _maximumProbe;

    internal int Add(T value)
    {
        while (_nextId > 1 && _items[_nextId - 1] is not { References: > 0 }) Delete(--_nextId);
        int existing = Lookup(value);
        if (existing != 0)
        {
            context.Deleted(value);
            _items[existing]!.References++;
            return existing;
        }
        // Both native OutOfMemory and NeedsRehash degrade a snapshot entry.
        if (_probes[31] > 0 || _nextId >= _itemCapacity) return 0;
        Item fresh = new(value), held = fresh;
        int chosen = _nextId, heldId = _nextId;
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
                if (id < _nextId) chosen = id;
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
        if (chosen == _nextId) _nextId++;
        return chosen;
    }

    internal void Release(int id)
    {
        if (id != 0) _items[id]!.References--;
    }

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
