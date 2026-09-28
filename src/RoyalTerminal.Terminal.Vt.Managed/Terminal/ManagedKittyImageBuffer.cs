// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal;

/// <summary>Single-owner image accumulation with capacity bounded by the configured image limit.</summary>
// Store inline in the loader. Active values must not be copied except to transfer ownership.
internal struct ManagedKittyImageBuffer
{
    private byte[]? _buffer;
    private int _length;
    private readonly int _limit;

    internal ManagedKittyImageBuffer(int limit, ReadOnlyMemory<byte> initial = default,
        Action<ManagedKittyImageAllocation>? allocationCheckpoint = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initial.Length, limit);
        _limit = limit;
        if (MemoryMarshal.TryGetArray(initial, out ArraySegment<byte> segment) && segment.Offset == 0 && segment.Array is not null && segment.Array.Length <= limit)
            _buffer = segment.Array;
        else
        {
            if (!initial.IsEmpty) allocationCheckpoint?.Invoke(ManagedKittyImageAllocation.BufferCopy);
            _buffer = initial.ToArray();
        }
        _length = initial.Length;
    }

    internal ReadOnlyMemory<byte> Data => _buffer.AsMemory(0, _length);

    internal bool TryAppend(ReadOnlySpan<byte> data, Action<ManagedKittyImageAllocation>? allocationCheckpoint = null)
    {
        if (data.Length > _limit - _length) return false;
        int required = _length + data.Length;
        int currentCapacity = _buffer?.Length ?? 0;
        if (required > currentCapacity)
        {
            int capacity = (int)Math.Min(_limit, Math.Max(required, Math.Max(256L, currentCapacity * 2L)));
            allocationCheckpoint?.Invoke(ManagedKittyImageAllocation.BufferGrowth);
            Array.Resize(ref _buffer, capacity);
        }
        data.CopyTo(_buffer.AsSpan(_length));
        _length = required;
        return true;
    }

    internal byte[] Take(int length, Action<ManagedKittyImageAllocation>? allocationCheckpoint = null)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _length);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        byte[] data = _buffer ?? [];
        if (length != data.Length)
        {
            if (length != 0) allocationCheckpoint?.Invoke(ManagedKittyImageAllocation.BufferTransfer);
            Array.Resize(ref data, length);
        }
        _buffer = [];
        _length = 0;
        return data;
    }
}
