// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Numerics;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Native PAGE capacities, used for accounting only, never allocation requests.</summary>
internal readonly record struct GhosttySnapshotPageCapacity(
    ushort Columns, ushort Rows, ushort Styles, ushort HyperlinkBytes, uint GraphemeBytes, uint StringBytes)
{
    internal static GhosttySnapshotPageCapacity Read(ReadOnlySpan<byte> header)
    {
        if (header.Length < 20) throw new EndOfStreamException();
        return new(BinaryPrimitives.ReadUInt16LittleEndian(header), BinaryPrimitives.ReadUInt16LittleEndian(header[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[8..]), BinaryPrimitives.ReadUInt16LittleEndian(header[10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[12..]), BinaryPrimitives.ReadUInt32LittleEndian(header[16..]));
    }
}

/// <summary>
/// Ghostty Page.layout / PageList.Limits accounting for the supported 64-bit
/// desktop ABIs. This models native bytes, not CLR object sizes. Page alignment
/// is a target property (Zig page_size_min), not the OS runtime page size.
/// </summary>
internal sealed class GhosttySnapshotAllocation
{
    private readonly ulong _pageAlignment;
    private static GhosttySnapshotPageCapacity StandardCapacity => new(215, 215, 128, 192, 8192, 2048);

    internal GhosttySnapshotAllocation(int pageAlignment)
    {
        if (pageAlignment is not (4096 or 16384)) throw new ArgumentOutOfRangeException(nameof(pageAlignment));
        _pageAlignment = (ulong)pageAlignment;
        StandardPageBytes = LayoutBytes(StandardCapacity);
    }

    internal ulong StandardPageBytes { get; }

    // createPageExt uses a full pooled item for every non-exact allocation that
    // fits. Snapshot PAGE records always use this path, even a one-cell page.
    internal ulong AllocatedBytes(GhosttySnapshotPageCapacity capacity) => Math.Max(StandardPageBytes, LayoutBytes(capacity));

    internal ulong LayoutBytes(GhosttySnapshotPageCapacity capacity)
    {
        if (capacity.Columns == 0 || capacity.Rows == 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        ulong cellsStart = Align((ulong)capacity.Rows * 8, 128); // Zig atomic.cache_line: x64/arm64.
        ulong metaStart = Align(cellsStart + (ulong)capacity.Rows * capacity.Columns * 8, 8);
        return Align(metaStart + MetadataBytes(capacity), _pageAlignment);
    }

    internal int InitialRows(int columns)
    {
        if (columns is <= 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(columns));
        GhosttySnapshotPageCapacity standard = StandardCapacity;
        ulong available = StandardPageBytes - MetadataBytes(standard);
        int rows = checked((int)(available / ((ulong)columns * 8 + 8)));
        while (rows > 0)
        {
            if (LayoutBytes(standard with { Columns = (ushort)columns, Rows = checked((ushort)rows) }) <= StandardPageBytes) return rows;
            rows--;
        }
        // Upstream initialCapacity falls back to the unadjusted standard row
        // count with the requested columns when even one row cannot fit.
        return standard.Rows;
    }

    internal (ulong Bytes, ulong Rows) MinimumLimits(int columns, int rows)
    {
        if (rows is <= 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(rows));
        ulong capacityRows = (ulong)InitialRows(columns);
        ulong pages = ((ulong)rows + capacityRows - 1) / capacityRows;
        return (StandardPageBytes * (pages + 1), capacityRows);
    }

    internal bool Fits(int columns, int rows, ulong allocatedBytes, ulong totalRows, ulong? maximumBytes, ulong? maximumRows)
    {
        (ulong minimumBytes, ulong minimumRows) = MinimumLimits(columns, rows);
        return allocatedBytes <= Math.Max(maximumBytes ?? ulong.MaxValue, minimumBytes) &&
            (totalRows <= (ulong)rows || totalRows - (ulong)rows <= Math.Max(maximumRows ?? ulong.MaxValue, minimumRows));
    }

    private static ulong MetadataBytes(GhosttySnapshotPageCapacity capacity)
    {
        // RGB is a packed u24 with four-byte ABI alignment. Each tagged color
        // therefore occupies eight bytes; Style is 28 and Set.Item is 36.
        ulong end = SetBytes(capacity.Styles, 36, 4);
        end = Align(end, 8) + BitmapBytes(capacity.GraphemeBytes, 16);
        ulong graphemes = PowerOfTwo(((ulong)capacity.GraphemeBytes + 15) / 16);
        // Offset(Cell): u32; Offset(u21).Slice: 16 bytes, aligned to eight.
        end = Align(end, 8) + MapBytes(graphemes, 16, 8, 100);
        end = Align(end, 8) + BitmapBytes(capacity.StringBytes, 32);
        ulong links = capacity.HyperlinkBytes / 48UL;
        end = Align(end, 8) + SetBytes(links, 48, 8);
        // Hyperlink cell map uses u16 IDs and 80% maximum load.
        return Align(end, 4) + MapBytes(links * 16, 2, 2, 80);
    }

    private static ulong SetBytes(ulong requested, ulong itemBytes, ulong alignment)
    {
        if (requested == 0) return 0;
        ulong table = PowerOfTwo(requested);
        return Align(table * 2, alignment) + (table * 13 / 16) * itemBytes;
    }

    private static ulong MapBytes(ulong requested, ulong valueBytes, ulong valueAlignment, ulong load)
    {
        ulong capacity = requested == 0 ? 0 : Math.Min(1UL << 31, PowerOfTwo((requested * 100 + load - 1) / load));
        ulong keysStart = Align(4 + capacity, 4); // mutable u32 header + u8 metadata.
        ulong valuesStart = Align(keysStart + capacity * 4, valueAlignment);
        return Align(valuesStart + capacity * valueBytes, Math.Max(4, valueAlignment));
    }

    private static ulong BitmapBytes(ulong bytes, ulong chunkBytes)
    {
        ulong chunks = Align((bytes + chunkBytes - 1) / chunkBytes, 64);
        return chunks / 64 * 8 + chunks * chunkBytes;
    }

    private static ulong PowerOfTwo(ulong value) => value == 0 ? 0 : BitOperations.RoundUpToPowerOf2(value);
    private static ulong Align(ulong value, ulong alignment) => (value + alignment - 1) & ~(alignment - 1);
}
