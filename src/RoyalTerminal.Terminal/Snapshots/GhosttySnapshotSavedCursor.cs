// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Saved cursor state, clamped only when installed into terminal geometry.</summary>
internal readonly record struct GhosttySnapshotSavedCursor(
    ushort X, ushort Y, GhosttySnapshotStyle Pen, byte Flags, GhosttySnapshotCharset Charset)
{
    internal const int Length = 23;
    internal bool Protected => (Flags & 1) != 0;
    internal bool PendingWrap => (Flags & 2) != 0;
    internal bool Origin => (Flags & 4) != 0;

    internal static GhosttySnapshotSavedCursor Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Length) throw new EndOfStreamException();
        return new(BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]),
            GhosttySnapshotStyle.Read(bytes[4..]) ?? default, (byte)(bytes[20] & 7),
            GhosttySnapshotCharset.Read(BinaryPrimitives.ReadUInt16LittleEndian(bytes[21..])));
    }

    internal GhosttySnapshotSavedCursor Clamp(int columns, int rows)
    {
        ValidateExtent(columns, nameof(columns));
        ValidateExtent(rows, nameof(rows));
        ushort x = (ushort)Math.Min(X, columns - 1);
        return this with { X = x, Y = (ushort)Math.Min(Y, rows - 1),
            Flags = x == columns - 1 ? Flags : (byte)(Flags & ~2) };
    }

    internal void Write(Span<byte> destination)
    {
        if ((Flags & ~7) != 0 || !Pen.IsValid) throw new InvalidDataException("Invalid snapshot saved cursor.");
        if (destination.Length < Length) throw new ArgumentException("Saved cursor destination is too short.", nameof(destination));
        BinaryPrimitives.WriteUInt16LittleEndian(destination, X);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], Y);
        Pen.Write(destination[4..]);
        destination[20] = Flags;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[21..], Charset.Bits);
    }

    internal static void ValidateExtent(int value, string name)
    {
        if (value is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(name);
    }
}
