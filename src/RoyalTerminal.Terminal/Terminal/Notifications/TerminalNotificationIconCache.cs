// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

// Session-local bounded LRU. Client names never become file paths or native IDs.
internal sealed class TerminalNotificationIconCache
{
    private readonly Dictionary<string, LinkedListNode<(string Id, byte[] Bytes)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Id, byte[] Bytes)> _lru = new();
    private int _bytes;

    internal void Put(string id, byte[] bytes)
    {
        if (_entries.Remove(id, out var previous)) Remove(previous);
        while (_lru.First is { } oldest && (_entries.Count >= 128 || _bytes + bytes.Length > 16 * 1024 * 1024))
        {
            _entries.Remove(oldest.Value.Id);
            Remove(oldest);
        }
        _entries.Add(id, _lru.AddLast((id, bytes)));
        _bytes += bytes.Length;
    }

    internal ReadOnlyMemory<byte> Get(string id)
    {
        if (!_entries.TryGetValue(id, out var entry)) return default;
        _lru.Remove(entry);
        _lru.AddLast(entry);
        return entry.Value.Bytes;
    }

    internal void Clear() { _entries.Clear(); _lru.Clear(); _bytes = 0; }

    private void Remove(LinkedListNode<(string Id, byte[] Bytes)> node)
    {
        _lru.Remove(node);
        _bytes -= node.Value.Bytes.Length;
    }
}
