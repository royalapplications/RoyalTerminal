// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Owned, bounded snapshot grid in the version-one wire layout. Page-local style
/// and hyperlink IDs remain unresolved until the containing PAGE is decoded.
/// Row transport widths and invalid semantic values are normalized on input.
/// </summary>
internal sealed class GhosttySnapshotGrid
{
    private const ulong ContentMask = 0xFFFFFFUL << 2;
    private const ulong WidthMask = 3UL << 42;
    private const ulong HyperlinkFlag = 1UL << 45;
    private readonly byte[] _rows;
    private readonly ulong[] _cells;
    private readonly Dictionary<int, uint[]> _suffixes;

    private GhosttySnapshotGrid(int columns, byte[] rows, ulong[] cells, Dictionary<int, uint[]> suffixes)
    { Columns = columns; _rows = rows; _cells = cells; _suffixes = suffixes; }

    internal int Columns { get; }
    internal int Rows => _rows.Length;
    internal ReadOnlySpan<byte> RowFlags => _rows;
    internal ReadOnlySpan<ulong> Cells => _cells;

    // Adapter-created arrays are already validated and exclusively owned.
    internal static GhosttySnapshotGrid FromOwnedCells(int columns, byte[] rows, ulong[] cells,
        Dictionary<int, uint[]> suffixes) => new(columns, rows, cells, suffixes);
    internal ReadOnlySpan<uint> Suffix(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
        return _suffixes.TryGetValue(row * Columns + column, out uint[]? value) ? value : [];
    }

    internal void ResolvePageIds(IReadOnlyDictionary<ushort, GhosttySnapshotStyle> styles,
        IReadOnlyDictionary<ushort, byte[]> hyperlinks)
    {
        for (int i = 0; i < _cells.Length; i++)
        {
            ulong cell = _cells[i];
            if (cell == 0) continue;
            ushort style = (ushort)(cell >> 26);
            if (style != 0 && !styles.ContainsKey(style)) cell &= ~(0xFFFFUL << 26);
            ushort link = (ushort)(cell >> 48);
            if (link != 0 && !hyperlinks.ContainsKey(link)) cell &= ~((0xFFFFUL << 48) | HyperlinkFlag);
            _cells[i] = cell;
        }
    }

    internal static GhosttySnapshotGrid Read(ReadOnlySpan<byte> data, int columns, int rows,
        int maximumCells, int maximumSuffixCodepoints, out int consumed,
        GhosttySnapshotGraphemeRestore? liveGraphemes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCells);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSuffixCodepoints);
        if (columns is < 1 or > ushort.MaxValue || rows is < 0 or > ushort.MaxValue)
            throw new InvalidDataException("Invalid snapshot grid dimensions.");
        long count = (long)columns * rows;
        if (count > maximumCells) throw new InvalidDataException("Snapshot grid exceeds the configured cell limit.");
        if (data.Length < (long)rows * 3 + 4) throw new EndOfStreamException();
        ReadOnlySpan<byte> remaining = data;
        byte[] flags = new byte[rows];
        ulong[] cells = new ulong[(int)count];
        for (int row = 0; row < rows; row++)
        {
            ReadOnlySpan<byte> header = Take(ref remaining, 3);
            byte rawFlags = header[0];
            flags[row] = (byte)(rawFlags & ((rawFlags & 12) == 12 ? 3 : 15));
            int encodedCount = BinaryPrimitives.ReadUInt16LittleEndian(header[1..]);
            if (encodedCount > columns) throw new InvalidDataException("Snapshot row cell count exceeds its width.");
            int width = 1 << ((rawFlags >> 4) & 3);
            ReadOnlySpan<byte> words = Take(ref remaining, encodedCount * width);
            Span<ulong> target = cells.AsSpan(row * columns, columns);
            bool hasWide = GhosttySnapshotGridCodec.Decode(words, target[..encodedCount], width);
            // Include elided defaults: a final encoded wide marker can have
            // no tail when the next (unencoded) cell is the implicit default.
            for (int column = 0; hasWide && column < columns; column++)
            {
                int kind = Width(target[column]);
                if (column > 0 && Width(target[column - 1]) == 1 && kind != 2)
                    target[column - 1] &= ~WidthMask;
                if ((kind == 1 && column + 1 == columns) ||
                    (kind == 2 && (column == 0 || Width(target[column - 1]) != 1)) ||
                    (kind == 3 && (column + 1 != columns || (flags[row] & 1) == 0)))
                    target[column] &= ~WidthMask;
            }
        }

        Dictionary<int, uint[]> suffixes = [];
        uint entries = BinaryPrimitives.ReadUInt32LittleEndian(Take(ref remaining, 4));
        // Every entry needs a six-byte header, even when it will be dropped.
        if (entries > (uint)(remaining.Length / 6)) throw new EndOfStreamException();
        int acceptedCodepoints = 0;
        for (uint entry = 0; entry < entries; entry++)
        {
            ReadOnlySpan<byte> header = Take(ref remaining, 6);
            int row = BinaryPrimitives.ReadUInt16LittleEndian(header);
            int column = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
            ReadOnlySpan<byte> codepoints = Take(ref remaining, length * 4);
            if (row >= rows || column >= columns) continue;
            int index = row * columns + column;
            ulong cell = cells[index];
            if ((cell & 3) > 1 || (cell & ContentMask) == 0) continue;
            if (suffixes.ContainsKey(index))
            {
                liveGraphemes?.Read(index, codepoints, null);
                continue;
            }
            int valid = 0;
            for (int i = 0; i < length; i++)
                if (ValidSuffix(BinaryPrimitives.ReadUInt32LittleEndian(codepoints[(i * 4)..]))) valid++;
            if (valid == 0) continue;
            if (valid > maximumSuffixCodepoints - acceptedCodepoints)
                throw new InvalidDataException("Snapshot graphemes exceed the configured codepoint limit.");
            uint[] suffix = new uint[valid];
            int next = 0;
            for (int i = 0; i < length; i++)
            {
                uint cp = BinaryPrimitives.ReadUInt32LittleEndian(codepoints[(i * 4)..]);
                if (ValidSuffix(cp)) suffix[next++] = cp;
            }
            suffixes.Add(index, suffix);
            cells[index] |= 1; // Canonical kind-one cell, now backed by a suffix.
            acceptedCodepoints += valid;
            liveGraphemes?.Read(index, codepoints, suffix);
        }
        consumed = data.Length - remaining.Length;
        return new(columns, flags, cells, suffixes);
    }

    internal void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Snapshot destination is not writable.", nameof(destination));
        GhosttySnapshotStreamWriter writer = new(destination);
        WriteTo(ref writer);
    }

    internal void WriteTo<TWriter>(ref TWriter destination)
        where TWriter : IGhosttySnapshotWriter, allows ref struct
    {
        Span<byte> scratch = stackalloc byte[4096];
        for (int row = 0; row < Rows; row++)
        {
            ReadOnlySpan<ulong> cells = _cells.AsSpan(row * Columns, Columns);
            (int count, int selector) = GhosttySnapshotGridCodec.Classify(cells);
            int width = 1 << selector;
            scratch[0] = (byte)(_rows[row] | (selector << 4));
            BinaryPrimitives.WriteUInt16LittleEndian(scratch[1..], (ushort)count);
            destination.Write(scratch[..3]);
            for (int start = 0; start < count;)
            {
                int batch = Math.Min(count - start, scratch.Length / width);
                GhosttySnapshotGridCodec.Encode(cells.Slice(start, batch), scratch, width);
                destination.Write(scratch[..(batch * width)]);
                start += batch;
            }
        }
        BinaryPrimitives.WriteUInt32LittleEndian(scratch, (uint)_suffixes.Count);
        destination.Write(scratch[..4]);
        if (_suffixes.Count == 0) return;
        // Canonical suffix order is row-major, independent of input order.
        // Small entries share the existing bounded scratch buffer and one write
        // per flush. Oversized entries retain bounded streaming chunks.
        int used = 0;
        for (int index = 0; index < _cells.Length; index++)
        {
            if ((_cells[index] & 3) != 1) continue;
            uint[] suffix = _suffixes[index];
            if (suffix.Length > ushort.MaxValue) throw new InvalidDataException("Snapshot grapheme entry exceeds the wire limit.");
            int needed = 6 + suffix.Length * 4;
            if (needed > scratch.Length - used)
            {
                if (used > 0) destination.Write(scratch[..used]);
                used = 0;
            }
            BinaryPrimitives.WriteUInt16LittleEndian(scratch[used..], (ushort)(index / Columns));
            BinaryPrimitives.WriteUInt16LittleEndian(scratch[(used + 2)..], (ushort)(index % Columns));
            BinaryPrimitives.WriteUInt16LittleEndian(scratch[(used + 4)..], (ushort)suffix.Length);
            if (needed <= scratch.Length)
            {
                WriteCodepoints(suffix, scratch[(used + 6)..]);
                used += needed;
                continue;
            }
            destination.Write(scratch[..6]);
            for (int start = 0; start < suffix.Length;)
            {
                int batch = Math.Min(suffix.Length - start, scratch.Length / 4);
                WriteCodepoints(suffix.AsSpan(start, batch), scratch);
                destination.Write(scratch[..(batch * 4)]);
                start += batch;
            }
        }
        if (used > 0) destination.Write(scratch[..used]);
    }

    private static void WriteCodepoints(ReadOnlySpan<uint> codepoints, Span<byte> destination)
    {
        if (BitConverter.IsLittleEndian)
            MemoryMarshal.AsBytes(codepoints).CopyTo(destination);
        else
            for (int i = 0; i < codepoints.Length; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(destination[(i * 4)..], codepoints[i]);
    }

    private static int Width(ulong cell) => (int)((cell >> 42) & 3);
    private static bool ValidScalar(uint cp) => cp <= 0x10FFFF && cp is not (>= 0xD800 and <= 0xDFFF);
    private static bool ValidSuffix(uint cp) => cp != 0 && ValidScalar(cp);

    private static ReadOnlySpan<byte> Take(ref ReadOnlySpan<byte> remaining, int count)
    {
        if (count > remaining.Length) throw new EndOfStreamException();
        ReadOnlySpan<byte> result = remaining[..count];
        remaining = remaining[count..];
        return result;
    }
}
