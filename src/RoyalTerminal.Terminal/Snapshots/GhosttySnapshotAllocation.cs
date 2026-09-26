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

    internal int InitialRows(int columns) => InitialCapacity(columns).Rows;

    internal GhosttySnapshotPageCapacity InitialCapacity(int columns)
    {
        if (columns is <= 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(columns));
        return TryAdjustColumns(StandardCapacity, columns, out GhosttySnapshotPageCapacity adjusted)
            ? adjusted : StandardCapacity with { Columns = (ushort)columns };
    }

    // Page.Capacity.adjust retains the layout's size, not its pooled allocation
    // charge. Small restored/compacted pages must not acquire the pool's spare
    // bytes when their grid is adjusted during reflow.
    internal bool TryAdjustColumns(GhosttySnapshotPageCapacity capacity, int columns,
        out GhosttySnapshotPageCapacity adjusted)
    {
        if (columns is <= 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(columns));
        ulong total = LayoutBytes(capacity);
        ulong available = total - MetadataBytes(capacity);
        int rows = (int)Math.Min(ushort.MaxValue, available / ((ulong)columns * 8 + 8));
        while (rows > 0)
        {
            adjusted = capacity with { Columns = (ushort)columns, Rows = (ushort)rows };
            if (LayoutBytes(adjusted) <= total) return true;
            rows--;
        }
        adjusted = default;
        return false;
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

    // PageList.increaseCapacity: zero resumes at the Capacity default, otherwise
    // double, saturating once at the field maximum. Style/grapheme growth can
    // project the current density over all reserved rows with 25% headroom,
    // bounded to 32 times the old request and the native four-GiB page ceiling.
    internal bool TryIncreaseCapacity(GhosttySnapshotPageCapacity original, GhosttySnapshotCapacityDimension dimension,
        ulong used, int liveRows, out GhosttySnapshotPageCapacity increased)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(liveRows);
        uint old = GetDimension(original, dimension);
        uint maximum = dimension is GhosttySnapshotCapacityDimension.Styles or GhosttySnapshotCapacityDimension.HyperlinkBytes
            ? ushort.MaxValue : uint.MaxValue;
        increased = original;
        if (old == maximum) return false;
        uint next = old == 0 ? dimension switch
        {
            GhosttySnapshotCapacityDimension.Styles => 16,
            GhosttySnapshotCapacityDimension.GraphemeBytes => 1024,
            GhosttySnapshotCapacityDimension.HyperlinkBytes => 192,
            GhosttySnapshotCapacityDimension.StringBytes => 2048,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
        } : (uint)Math.Min((ulong)maximum, (ulong)old * 2);
        GhosttySnapshotPageCapacity doubled = SetDimension(original, dimension, next);
        if (LayoutBytes(doubled) > uint.MaxValue) return false;
        increased = doubled;
        if (used == 0 || liveRows == 0 || old == 0 ||
            dimension is not (GhosttySnapshotCapacityDimension.Styles or GhosttySnapshotCapacityDimension.GraphemeBytes)) return true;

        ulong scaled = used > ulong.MaxValue / original.Rows ? ulong.MaxValue : used * original.Rows;
        ulong density = scaled / (ulong)liveRows;
        ulong projected = density > ulong.MaxValue - density / 4 ? ulong.MaxValue : density + density / 4;
        projected = Math.Min(projected, Math.Min((ulong)old * 32, maximum));
        if (projected > next)
        {
            GhosttySnapshotPageCapacity candidate = SetDimension(original, dimension, (uint)projected);
            if (LayoutBytes(candidate) <= uint.MaxValue) increased = candidate;
        }
        return true;
    }

    internal bool TryFitMetadata(GhosttySnapshotPageCapacity original, in GhosttySnapshotMetadataUsage usage,
        int liveRows, out GhosttySnapshotPageCapacity capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(liveRows);
        capacity = original;
        while (true)
        {
            GhosttySnapshotCapacityDimension dimension;
            ulong used;
            if (usage.Styles > SetItemCapacity(capacity.Styles))
            {
                dimension = GhosttySnapshotCapacityDimension.Styles;
                used = Math.Min(usage.Styles, SetItemCapacity(capacity.Styles));
            }
            else if (usage.GraphemeCells > GraphemeCellCapacity(capacity.GraphemeBytes) ||
                     usage.GraphemeBytes > BitmapDataBytes(capacity.GraphemeBytes, 16) ||
                     usage.GraphemeTemporaryBytes > BitmapDataBytes(capacity.GraphemeBytes, 16) - usage.GraphemeBytes)
            {
                dimension = GhosttySnapshotCapacityDimension.GraphemeBytes;
                used = Math.Min(usage.GraphemeBytes, BitmapDataBytes(capacity.GraphemeBytes, 16));
            }
            else if (usage.Hyperlinks > SetItemCapacity(capacity.HyperlinkBytes / 48UL) ||
                     usage.HyperlinkCells > MapItemCapacity(capacity.HyperlinkBytes / 48UL * 16, 80))
            {
                dimension = GhosttySnapshotCapacityDimension.HyperlinkBytes;
                used = 0;
            }
            else if (usage.StringBytes > BitmapDataBytes(capacity.StringBytes, 32))
            {
                dimension = GhosttySnapshotCapacityDimension.StringBytes;
                used = 0;
            }
            else return true;

            // This remains checkpoint-driven: the live mutation tracker must
            // eventually supply exact occupancy at each allocation failure.
            // Do not replace a failed growth with a wrapped/truncated capacity.
            if (!TryIncreaseCapacity(capacity, dimension, used, liveRows, out capacity)) return false;
        }
    }

    internal static ulong SetItemCapacity(ulong requested)
    {
        ulong items = PowerOfTwo(requested) * 13 / 16;
        return items == 0 ? 0 : items - 1; // Native ID zero is reserved.
    }

    internal static ulong GraphemeCellCapacity(uint bytes) => PowerOfTwo(((ulong)bytes + 15) / 16);
    internal static ulong BitmapDataBytes(ulong bytes, ulong chunk) => Align((bytes + chunk - 1) / chunk, 64) * chunk;
    internal static ulong MapItemCapacity(ulong requested, ulong load) => MapSlotCapacity(requested, load) * load / 100;

    private static uint GetDimension(GhosttySnapshotPageCapacity capacity, GhosttySnapshotCapacityDimension dimension) => dimension switch
    {
        GhosttySnapshotCapacityDimension.Styles => capacity.Styles,
        GhosttySnapshotCapacityDimension.GraphemeBytes => capacity.GraphemeBytes,
        GhosttySnapshotCapacityDimension.HyperlinkBytes => capacity.HyperlinkBytes,
        GhosttySnapshotCapacityDimension.StringBytes => capacity.StringBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    private static GhosttySnapshotPageCapacity SetDimension(GhosttySnapshotPageCapacity capacity,
        GhosttySnapshotCapacityDimension dimension, uint value) => dimension switch
    {
        GhosttySnapshotCapacityDimension.Styles => capacity with { Styles = (ushort)value },
        GhosttySnapshotCapacityDimension.GraphemeBytes => capacity with { GraphemeBytes = value },
        GhosttySnapshotCapacityDimension.HyperlinkBytes => capacity with { HyperlinkBytes = (ushort)value },
        GhosttySnapshotCapacityDimension.StringBytes => capacity with { StringBytes = value },
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

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
        ulong capacity = MapSlotCapacity(requested, load);
        ulong keysStart = Align(4 + capacity, 4); // mutable u32 header + u8 metadata.
        ulong valuesStart = Align(keysStart + capacity * 4, valueAlignment);
        return Align(valuesStart + capacity * valueBytes, Math.Max(4, valueAlignment));
    }

    private static ulong BitmapBytes(ulong bytes, ulong chunkBytes)
    {
        ulong chunks = Align((bytes + chunkBytes - 1) / chunkBytes, 64);
        return chunks / 64 * 8 + chunks * chunkBytes;
    }

    private static ulong MapSlotCapacity(ulong requested, ulong load)
        => requested == 0 ? 0 : Math.Min(1UL << 31, PowerOfTwo((requested * 100 + load - 1) / load));

    private static ulong PowerOfTwo(ulong value) => value == 0 ? 0 : BitOperations.RoundUpToPowerOf2(value);
    private static ulong Align(ulong value, ulong alignment) => (value + alignment - 1) & ~(alignment - 1);
}
