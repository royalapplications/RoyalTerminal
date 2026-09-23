// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal;

/// <summary>Image accumulation with capacity growth bounded by the configured image limit.</summary>
internal sealed class ManagedKittyImageBuffer
{
    private byte[] _buffer;
    private int _length;
    private readonly int _limit;

    internal ManagedKittyImageBuffer(int limit, ReadOnlyMemory<byte> initial = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initial.Length, limit);
        _limit = limit;
        if (MemoryMarshal.TryGetArray(initial, out ArraySegment<byte> segment) && segment.Offset == 0 && segment.Array is not null && segment.Array.Length <= limit)
            _buffer = segment.Array;
        else _buffer = initial.ToArray();
        _length = initial.Length;
    }

    internal ReadOnlyMemory<byte> Data => _buffer.AsMemory(0, _length);

    internal bool TryAppend(ReadOnlySpan<byte> data)
    {
        if (data.Length > _limit - _length) return false;
        int required = _length + data.Length;
        if (required > _buffer.Length)
        {
            int capacity = (int)Math.Min(_limit, Math.Max(required, Math.Max(256L, _buffer.Length * 2L)));
            Array.Resize(ref _buffer, capacity);
        }
        data.CopyTo(_buffer.AsSpan(_length));
        _length = required;
        return true;
    }

    internal byte[] Take(int length)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _length);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        byte[] data = _buffer;
        if (length != data.Length) Array.Resize(ref data, length);
        _buffer = [];
        _length = 0;
        return data;
    }
}
