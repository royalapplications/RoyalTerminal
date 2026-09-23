// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Routes a newest-to-oldest PAGE sequence after READY; zero pages is valid.</summary>
internal readonly record struct GhosttySnapshotHistoryHeader(ushort Key, uint PageCount)
{
    internal const int Length = 6;

    internal static GhosttySnapshotHistoryHeader Read(ReadOnlySpan<byte> payload, int maximumPages)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPages);
        if (payload.Length < Length) throw new EndOfStreamException();
        if (payload.Length != Length) throw new InvalidDataException("Snapshot HISTORY has trailing payload bytes.");
        ushort key = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(payload[2..]);
        if (key > 1) throw new InvalidDataException("Invalid snapshot history key.");
        if (count > maximumPages) throw new InvalidDataException("Snapshot history page count exceeds the configured limit.");
        return new(key, count);
    }

    internal void Write(Span<byte> destination)
    {
        if (Key > 1) throw new InvalidDataException("Invalid snapshot history key.");
        if (destination.Length < Length) throw new ArgumentException("History destination is too short.", nameof(destination));
        BinaryPrimitives.WriteUInt16LittleEndian(destination, Key);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[2..], PageCount);
    }
}
