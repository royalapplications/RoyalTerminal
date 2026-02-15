// Licensed under the MIT License.
// RoyalTerminal.GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace RoyalTerminal.GhosttySharp.Terminal;

/// <summary>
/// High-performance ring buffer for terminal data.
/// Uses <see cref="ArrayPool{T}"/> for memory management and provides
/// <see cref="Span{T}"/> access for zero-copy reads and writes.
/// Thread-safe for single-producer/single-consumer scenarios.
/// </summary>
public sealed class TerminalBuffer : IDisposable
{
    private byte[] _buffer;
    private int _head;
    private int _tail;
    private int _count;
    private bool _disposed;
    private readonly object _lock = new();

    /// <summary>Current number of bytes in the buffer.</summary>
    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            lock (_lock) return _count;
        }
    }

    /// <summary>Total capacity of the buffer.</summary>
    public int Capacity => _buffer.Length;

    /// <summary>Available space for writing.</summary>
    public int Available
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            lock (_lock) return _buffer.Length - _count;
        }
    }

    /// <summary>Creates a new terminal buffer with the specified capacity.</summary>
    /// <param name="capacity">Initial buffer capacity. Will be rounded up to pool bucket size.</param>
    public TerminalBuffer(int capacity = 65536)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _buffer = ArrayPool<byte>.Shared.Rent(capacity);
    }

    /// <summary>
    /// Writes data from the source span into the buffer.
    /// If the buffer is full, oldest data is overwritten.
    /// </summary>
    /// <param name="data">Source data to write.</param>
    /// <returns>Number of bytes actually written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return 0;

        lock (_lock)
        {
            var length = data.Length;
            var bufLen = _buffer.Length;

            if (length >= bufLen)
            {
                // Data is larger than buffer, only keep the tail
                data[^bufLen..].CopyTo(_buffer);
                _head = 0;
                _tail = 0;
                _count = bufLen;
                return bufLen;
            }

            // Write data, potentially wrapping around
            var firstPart = Math.Min(length, bufLen - _tail);
            data[..firstPart].CopyTo(_buffer.AsSpan(_tail, firstPart));

            if (firstPart < length)
            {
                data[firstPart..].CopyTo(_buffer.AsSpan(0, length - firstPart));
            }

            var oldTail = _tail;
            _tail = (_tail + length) % bufLen;
            _count = Math.Min(_count + length, bufLen);

            // If we've wrapped past the head, move head forward
            if (_count == bufLen && oldTail != _head)
            {
                _head = _tail;
            }

            return length;
        }
    }

    /// <summary>
    /// Reads up to <paramref name="maxLength"/> bytes from the buffer into the destination.
    /// Data is consumed (removed from buffer).
    /// </summary>
    /// <param name="destination">Destination span.</param>
    /// <param name="maxLength">Maximum number of bytes to read.</param>
    /// <returns>Number of bytes actually read.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<byte> destination, int maxLength = int.MaxValue)
    {
        lock (_lock)
        {
            var toRead = Math.Min(Math.Min(_count, maxLength), destination.Length);
            if (toRead == 0) return 0;

            var bufLen = _buffer.Length;
            var firstPart = Math.Min(toRead, bufLen - _head);

            _buffer.AsSpan(_head, firstPart).CopyTo(destination);
            if (firstPart < toRead)
            {
                _buffer.AsSpan(0, toRead - firstPart).CopyTo(destination[firstPart..]);
            }

            _head = (_head + toRead) % bufLen;
            _count -= toRead;
            return toRead;
        }
    }

    /// <summary>
    /// Peeks at data in the buffer without consuming it.
    /// The returned span is valid only until the next write.
    /// </summary>
    /// <param name="destination">Destination span for peeked data.</param>
    /// <param name="offset">Offset from the read position.</param>
    /// <returns>Number of bytes peeked.</returns>
    public int Peek(Span<byte> destination, int offset = 0)
    {
        lock (_lock)
        {
            if (offset >= _count) return 0;
            var available = _count - offset;
            var toPeek = Math.Min(available, destination.Length);
            if (toPeek == 0) return 0;

            var bufLen = _buffer.Length;
            var startPos = (_head + offset) % bufLen;
            var firstPart = Math.Min(toPeek, bufLen - startPos);

            _buffer.AsSpan(startPos, firstPart).CopyTo(destination);
            if (firstPart < toPeek)
            {
                _buffer.AsSpan(0, toPeek - firstPart).CopyTo(destination[firstPart..]);
            }

            return toPeek;
        }
    }

    /// <summary>Clears all data from the buffer.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// Grows the buffer to at least the specified capacity.
    /// Existing data is preserved.
    /// </summary>
    public void EnsureCapacity(int minCapacity)
    {
        lock (_lock)
        {
            if (_buffer.Length >= minCapacity) return;

            var newBuffer = ArrayPool<byte>.Shared.Rent(minCapacity);
            var count = _count;

            if (count > 0)
            {
                var bufLen = _buffer.Length;
                var firstPart = Math.Min(count, bufLen - _head);
                _buffer.AsSpan(_head, firstPart).CopyTo(newBuffer);
                if (firstPart < count)
                {
                    _buffer.AsSpan(0, count - firstPart).CopyTo(newBuffer.AsSpan(firstPart));
                }
            }

            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
            _head = 0;
            _tail = count;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }
}
