// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;

namespace RoyalTerminal.Terminal.Snapshots;

// One record's scratch, retained between records. The owner supplies bounded
// stack storage; only larger payloads rent memory. Never copy an owning instance.
internal ref struct GhosttySnapshotRecordBuffer : IGhosttySnapshotWriter, IDisposable
{
    private readonly ArrayPool<byte> _pool;
    private Span<byte> _buffer;
    private byte[]? _rented;
    private int _length;
    private int _maximumLength;
    private bool _disposed;

    internal GhosttySnapshotRecordBuffer(Span<byte> initialBuffer, int maximumLength,
        ArrayPool<byte>? pool = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);
        _buffer = initialBuffer;
        _maximumLength = maximumLength;
        _pool = pool ?? ArrayPool<byte>.Shared;
    }

    // Borrowed until the next mutation/disposal, like record.Writer upstream.
    internal readonly ReadOnlySpan<byte> WrittenSpan
    {
        get { EnsureUsable(); return _buffer[.._length]; }
    }

    public void Write(scoped ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(bytes.Length);
        bytes.CopyTo(_buffer[_length..]);
        _length += bytes.Length;
    }

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    internal void Reset(int maximumLength)
    {
        EnsureUsable();
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);
        _length = 0;
        _maximumLength = maximumLength;
    }

    private void EnsureCapacity(int additional)
    {
        EnsureUsable();
        if (additional > _maximumLength - _length)
            throw new InvalidDataException("Snapshot exceeds the payload byte limit.");
        int needed = _length + additional;
        if (needed <= _buffer.Length) return;
        int capacity = (int)Math.Min(_maximumLength, Math.Max(needed, (long)_buffer.Length * 2));
        byte[] replacement = _pool.Rent(capacity);
        _buffer[.._length].CopyTo(replacement);
        if (_rented is { } previous) _pool.Return(previous, clearArray: true);
        else _buffer.Clear();
        _rented = replacement;
        _buffer = replacement;
    }

    private readonly void EnsureUsable()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GhosttySnapshotRecordBuffer));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_rented is { } rented) _pool.Return(rented, clearArray: true);
        else _buffer.Clear();
        _rented = null;
        _buffer = [];
        _length = 0;
    }
}
