// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Unresolved snapshot color, preserving default/palette/RGB identity.</summary>
internal readonly record struct GhosttySnapshotColor(byte Kind, byte First, byte Second, byte Third)
{
    internal bool IsValid => Kind switch
    {
        0 => (First | Second | Third) == 0,
        1 => (Second | Third) == 0,
        2 => true,
        _ => false,
    };

    internal static GhosttySnapshotColor Read(ReadOnlySpan<byte> bytes)
        => new(bytes[0], bytes[1], bytes[2], bytes[3]);

    internal void Write(Span<byte> bytes)
    {
        bytes[0] = Kind;
        bytes[1] = First;
        bytes[2] = Second;
        bytes[3] = Third;
    }
}

/// <summary>The fixed 16-byte style representation from snapshot/style.zig.</summary>
internal readonly record struct GhosttySnapshotStyle(
    GhosttySnapshotColor Foreground,
    GhosttySnapshotColor Background,
    GhosttySnapshotColor UnderlineColor,
    ushort Flags)
{
    internal const int EncodedLength = 16;

    internal bool IsValid => Foreground.IsValid && Background.IsValid && UnderlineColor.IsValid &&
        (Flags & 0xF800) == 0 && ((Flags >> 8) & 7) <= 5;

    // Structural truncation is fatal. Complete entries with invalid semantic
    // values degrade to the default style in their containing PAGE record.
    internal static GhosttySnapshotStyle? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < EncodedLength) throw new EndOfStreamException();
        GhosttySnapshotStyle style = new(
            GhosttySnapshotColor.Read(bytes),
            GhosttySnapshotColor.Read(bytes[4..]),
            GhosttySnapshotColor.Read(bytes[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]));
        return style.IsValid && BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]) == 0 ? style : null;
    }

    internal void Write(Span<byte> bytes)
    {
        if (!IsValid) throw new InvalidDataException("Invalid Ghostty snapshot style.");
        if (bytes.Length < EncodedLength) throw new ArgumentException("The style output buffer is too short.", nameof(bytes));
        Foreground.Write(bytes);
        Background.Write(bytes[4..]);
        UnderlineColor.Write(bytes[8..]);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[12..], Flags);
        bytes[14] = bytes[15] = 0;
    }
}
