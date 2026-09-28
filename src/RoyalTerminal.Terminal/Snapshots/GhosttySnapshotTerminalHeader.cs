// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Validated terminal-wide snapshot v1 header, with upstream semantic normalization.</summary>
internal sealed class GhosttySnapshotTerminalHeader
{
    internal const int Length = 103;
    internal const ulong ModeMask = (1UL << 43) - 1;
    private readonly byte[] _bytes;

    private GhosttySnapshotTerminalHeader(byte[] bytes) => _bytes = bytes;

    internal int Columns => U16(0);
    internal int Rows => U16(2);
    internal uint PixelWidth => U32(4);
    internal uint PixelHeight => U32(8);
    internal int ScrollTop => U16(12);
    internal int ScrollBottom => U16(14);
    internal int ScrollLeft => U16(16);
    internal int ScrollRight => U16(18);
    internal byte StatusDisplay => _bytes[20];
    internal int ActiveScreenKey => U16(21);
    internal int ScreenCount => U16(23);
    internal uint? PreviousCodepoint => U32(25) is uint.MaxValue ? null : U32(25);
    internal bool CursorIsDefault => _bytes[29] == 1;
    // Values here are snapshot v1 registry values, not host UI enum values.
    internal byte CursorDefaultStyle => _bytes[30];
    internal bool? CursorDefaultBlink => OptionalBool(31);
    internal byte ShellRedraw => _bytes[32];
    internal bool ModifyOtherKeys2 => _bytes[33] == 1;
    internal byte MouseEvent => _bytes[34];
    internal byte MouseFormat => _bytes[35];
    internal bool? MouseShiftCapture => OptionalBool(36);
    internal byte MouseShape => _bytes[37];
    internal bool PasswordInput => _bytes[38] == 1;
    internal ulong CurrentModes => U64(39);
    internal ulong SavedModes => U64(47);
    internal ulong DefaultModes => U64(55);
    internal GhosttySnapshotDynamicColor Background => Color(63);
    internal GhosttySnapshotDynamicColor Foreground => Color(71);
    internal GhosttySnapshotDynamicColor CursorColor => Color(79);
    internal ulong? MaximumScrollbackBytes => OptionalLimit(87);
    internal ulong? MaximumScrollbackRows => OptionalLimit(95);

    internal static GhosttySnapshotTerminalHeader Read(ReadOnlySpan<byte> source, int maximumCells)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCells);
        if (source.Length < Length) throw new EndOfStreamException();
        int columns = BinaryPrimitives.ReadUInt16LittleEndian(source);
        int rows = BinaryPrimitives.ReadUInt16LittleEndian(source[2..]);
        int screens = BinaryPrimitives.ReadUInt16LittleEndian(source[23..]);
        if (columns == 0 || rows == 0 || (long)columns * rows > maximumCells)
            throw new InvalidDataException("Snapshot terminal dimensions exceed the configured limit.");
        if (screens is not (1 or 2)) throw new InvalidDataException("Invalid snapshot screen count.");

        byte[] bytes = source[..Length].ToArray();
        NormalizeAxis(bytes, 12, rows);
        NormalizeAxis(bytes, 16, columns);
        NormalizeEnum(bytes, 20, 1, 0);
        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(21)) >= screens)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(21), 0);
        uint previous = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(25));
        if (previous > 0x10FFFF || previous is >= 0xD800 and <= 0xDFFF)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(25), uint.MaxValue);
        NormalizeEnum(bytes, 29, 1, 1);
        NormalizeEnum(bytes, 30, 3, 1); // Unknown cursor style becomes block, not bar.
        NormalizeEnum(bytes, 31, 2, 0);
        NormalizeEnum(bytes, 32, 2, 0); // Shell redraw defaults to true (registry value zero).
        NormalizeEnum(bytes, 33, 1, 0);
        NormalizeEnum(bytes, 34, 4, 0);
        NormalizeEnum(bytes, 35, 4, 0);
        NormalizeEnum(bytes, 36, 2, 0);
        NormalizeEnum(bytes, 37, 33, 8); // Unknown mouse shape becomes text.
        NormalizeEnum(bytes, 38, 1, 0);
        for (int offset = 39; offset < 63; offset += 8)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset),
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset)) & ModeMask);
        for (int offset = 63; offset < 87; offset += 4)
            if (bytes[offset] != 1) bytes.AsSpan(offset, 4).Clear();
        return new(bytes);
    }

    internal void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Snapshot destination is not writable.", nameof(destination));
        destination.Write(_bytes);
    }

    private ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(offset));
    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(offset));
    private ulong U64(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(offset));
    private bool? OptionalBool(int offset) => _bytes[offset] switch { 1 => false, 2 => true, _ => null };
    private ulong? OptionalLimit(int offset) => U64(offset) is ulong.MaxValue ? null : U64(offset);
    private GhosttySnapshotDynamicColor Color(int offset) => new(Rgb(offset), Rgb(offset + 4));
    private uint? Rgb(int offset) => _bytes[offset] == 1
        ? (uint)(_bytes[offset + 1] << 16 | _bytes[offset + 2] << 8 | _bytes[offset + 3]) : null;

    private static void NormalizeAxis(Span<byte> bytes, int offset, int extent)
    {
        int start = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
        int end = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 2)..]);
        if (start <= end && end < extent) return;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[offset..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[(offset + 2)..], (ushort)(extent - 1));
    }

    private static void NormalizeEnum(Span<byte> bytes, int offset, byte maximum, byte fallback)
    {
        if (bytes[offset] > maximum) bytes[offset] = fallback;
    }
}

/// <summary>Optional default and override colors in 0xRRGGBB form; black is not absence.</summary>
internal readonly record struct GhosttySnapshotDynamicColor(uint? Default, uint? Override);
