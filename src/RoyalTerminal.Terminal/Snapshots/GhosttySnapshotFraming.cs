// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Wire format: ghostty/src/terminal/snapshot/{envelope,record}.zig at22391ed6491f.

using System.Buffers.Binary;
using System.Numerics;

namespace RoyalTerminal.Terminal.Snapshots;

internal enum GhosttySnapshotRecordTag : ushort
{
    Terminal = 1,
    Screen = 2,
    Page = 3,
    History = 4,
    Ready = 5,
    Finish = 6,
    Continuation = 7,
}

/// <summary>Version1 envelope and record framing, independent of engine storage.</summary>
internal static class GhosttySnapshotFraming
{
    internal const int HeaderLength = 10;
    internal static ReadOnlySpan<byte> Envelope => "GHOSTSNP\x01\0"u8;

    internal static void WriteEnvelope(Stream destination) => destination.Write(Envelope);

    internal static void WriteRecord(Stream destination, GhosttySnapshotRecordTag tag, ReadOnlySpan<byte> payload)
    {
        ValidateHeader(tag, (uint)payload.Length, int.MaxValue);
        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)tag);
        BinaryPrimitives.WriteUInt32LittleEndian(header[2..], (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[6..], ComputeChecksum(header[..6], payload));
        destination.Write(header);
        destination.Write(payload);
    }

    internal static void ValidateHeader(GhosttySnapshotRecordTag tag, uint length, int maximumPayloadBytes)
    {
        if (tag < GhosttySnapshotRecordTag.Terminal || tag > GhosttySnapshotRecordTag.Continuation)
            throw new InvalidDataException("Unknown Ghostty snapshot record tag.");
        if (length > (uint)maximumPayloadBytes)
            throw new InvalidDataException("Ghostty snapshot record exceeds the configured byte limit.");
        if (tag is GhosttySnapshotRecordTag.Ready or GhosttySnapshotRecordTag.Finish && length != 0)
            throw new InvalidDataException("Ghostty snapshot checkpoint records must be empty.");
    }

    internal static uint ComputeChecksum(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> payload)
        => ~AppendCrc32C(AppendCrc32C(uint.MaxValue, prefix), payload);

    private static uint AppendCrc32C(uint crc, ReadOnlySpan<byte> bytes)
    {
        // BitOperations uses CRC32C hardware instructions where available and a
        // portable optimized fallback otherwise, matching upstream's CRC policy.
        while (bytes.Length >= sizeof(ulong))
        {
            crc = BitOperations.Crc32C(crc, BinaryPrimitives.ReadUInt64LittleEndian(bytes));
            bytes = bytes[sizeof(ulong)..];
        }
        if (bytes.Length >= sizeof(uint))
        {
            crc = BitOperations.Crc32C(crc, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            bytes = bytes[sizeof(uint)..];
        }
        foreach (byte value in bytes) crc = BitOperations.Crc32C(crc, value);
        return crc;
    }
}
