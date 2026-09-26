// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Bounded, checksum-validating record reader. Buffered input is borrowed without a
/// payload copy; streamed input reuses one scratch allocation. Returned spans remain
/// valid until the next read or disposal. The source stream remains caller-owned.
/// </summary>
internal sealed class GhosttySnapshotRecordReader : IDisposable
{
    private readonly Stream? _source;
    private readonly ReadOnlyMemory<byte> _memory;
    private readonly int _maximumPayloadBytes;
    private byte[]? _scratch;
    private int _offset;
    private bool _envelopeRead;
    private bool _finished;
    private bool _failed;
    private bool _disposed;

    internal GhosttySnapshotRecordReader(Stream source, int maximumPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPayloadBytes);
        if (!source.CanRead) throw new ArgumentException("The snapshot source is not readable.", nameof(source));
        _source = source;
        _maximumPayloadBytes = maximumPayloadBytes;
    }

    internal GhosttySnapshotRecordReader(ReadOnlyMemory<byte> memory, int maximumPayloadBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPayloadBytes);
        _memory = memory;
        _maximumPayloadBytes = maximumPayloadBytes;
    }

    internal long SourceOffset { get; private set; }

    internal void ReadEnvelope()
    {
        EnsureUsable();
        if (_envelopeRead) throw new InvalidOperationException("The snapshot envelope was already read.");
        try
        {
            Span<byte> envelope = stackalloc byte[GhosttySnapshotFraming.HeaderLength];
            ReadExactly(envelope);
            if (!envelope.SequenceEqual(GhosttySnapshotFraming.Envelope))
                throw new InvalidDataException("Invalid Ghostty snapshot magic or unsupported version.");
            _envelopeRead = true;
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    internal GhosttySnapshotRecordTag ReadRecord(out ReadOnlySpan<byte> payload)
    {
        EnsureUsable();
        if (!_envelopeRead) throw new InvalidOperationException("Read the snapshot envelope first.");
        if (_finished) throw new InvalidOperationException("The snapshot FINISH record was already read.");
        try
        {
            Span<byte> header = stackalloc byte[GhosttySnapshotFraming.HeaderLength];
            ReadExactly(header);
            GhosttySnapshotRecordTag tag = (GhosttySnapshotRecordTag)BinaryPrimitives.ReadUInt16LittleEndian(header);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header[2..]);
            GhosttySnapshotFraming.ValidateHeader(tag, length, _maximumPayloadBytes);
            int payloadLength = checked((int)length);

            if (_source is null)
            {
                if (payloadLength > _memory.Length - _offset) throw new EndOfStreamException();
                payload = _memory.Span.Slice(_offset, payloadLength);
                _offset += payloadLength;
                SourceOffset += payloadLength;
            }
            else if (payloadLength == 0)
            {
                payload = ReadOnlySpan<byte>.Empty;
            }
            else
            {
                EnsureScratch(payloadLength);
                Span<byte> bytes = _scratch.AsSpan(0, payloadLength);
                ReadExactly(bytes);
                payload = bytes;
            }

            if (GhosttySnapshotFraming.ComputeChecksum(header[..6], payload) != BinaryPrimitives.ReadUInt32LittleEndian(header[6..]))
                throw new InvalidDataException("Ghostty snapshot record checksum mismatch.");
            _finished = tag == GhosttySnapshotRecordTag.Finish;
            return tag;
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    private void ReadExactly(Span<byte> destination)
    {
        if (_source is not null)
        {
            _source.ReadExactly(destination);
        }
        else
        {
            if (destination.Length > _memory.Length - _offset) throw new EndOfStreamException();
            _memory.Span.Slice(_offset, destination.Length).CopyTo(destination);
            _offset += destination.Length;
        }
        SourceOffset += destination.Length;
    }

    private void EnsureScratch(int length)
    {
        if (_scratch is not null && _scratch.Length >= length) return;
        byte[] replacement = ArrayPool<byte>.Shared.Rent(length);
        if (_scratch is not null) ArrayPool<byte>.Shared.Return(_scratch, clearArray: true);
        _scratch = replacement;
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) throw new InvalidOperationException("A failed snapshot reader cannot be resumed.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_scratch is not null)
        {
            ArrayPool<byte>.Shared.Return(_scratch, clearArray: true);
            _scratch = null;
        }
    }
}
