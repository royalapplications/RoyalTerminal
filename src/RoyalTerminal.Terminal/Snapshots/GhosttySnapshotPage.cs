// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Self-contained, validated PAGE payload. Allocation hints are retained as wire
/// metadata, never used to allocate. Only complete pages become visible to callers.
/// </summary>
internal sealed class GhosttySnapshotPage
{
    private readonly byte[] _header;
    private readonly Dictionary<ushort, GhosttySnapshotStyle> _styles;
    private readonly Dictionary<ushort, byte[]> _hyperlinks;
    private readonly IReadOnlyDictionary<int, uint[]>? _liveGraphemes;
    private readonly IReadOnlySet<ushort>? _liveStyles;
    private readonly GhosttySnapshotStyleStorage? _styleStorage;
    private readonly GhosttySnapshotGraphemeStorage? _graphemeStorage;
    private readonly GhosttySnapshotHyperlinkStorage _hyperlinkStorage;

    private GhosttySnapshotPage(byte[] header, GhosttySnapshotGrid grid,
        Dictionary<ushort, GhosttySnapshotStyle> styles, Dictionary<ushort, byte[]> hyperlinks,
        IReadOnlyDictionary<int, uint[]>? liveGraphemes = null, GhosttySnapshotMetadataRestore? metadata = null,
        GhosttySnapshotGraphemeStorage? graphemeStorage = null)
    {
        _header = header; Grid = grid; _styles = styles; _hyperlinks = hyperlinks; _liveGraphemes = liveGraphemes;
        _liveStyles = metadata?.Styles;
        _styleStorage = metadata?.FinishStyles(grid);
        _graphemeStorage = graphemeStorage;
        _hyperlinkStorage = metadata?.FinishHyperlinks(grid) ?? new(Capacity.HyperlinkBytes, Capacity.StringBytes);
    }

    internal GhosttySnapshotGrid Grid { get; }
    internal int StyleCount => _styles.Count;
    internal int HyperlinkCount => _hyperlinks.Count;
    internal GhosttySnapshotPageCapacity Capacity => GhosttySnapshotPageCapacity.Read(_header);

    // The identity owns a read-only restore seed. Each live owner obtains an
    // isolated mutable copy, while raw PAGE data and held COW frames stay stable.
    internal GhosttySnapshotPageAllocation CreateAllocationIdentity() => new(Capacity, _styleStorage,
        restoredGraphemes: _graphemeStorage, restoredHyperlinks: _hyperlinkStorage);

    internal bool TryGetLiveCellHyperlink(int index, out GhosttySnapshotHyperlink link)
        => _hyperlinkStorage.TryGetCell(index, out link);

    internal int LiveHyperlinkId(int index) => _hyperlinkStorage.CellId(index);

    // Raw codec output stays lossless; live cells follow native allocation
    // failure and suffix-bound semantics computed in original wire order.
    internal ReadOnlySpan<uint> LiveSuffix(int row, int column)
    {
        ReadOnlySpan<uint> suffix = _liveGraphemes is { } live
            ? live.TryGetValue(row * Grid.Columns + column, out uint[]? stored) ? stored : []
            : Grid.Suffix(row, column);
        return suffix[..Math.Min(suffix.Length, TerminalGraphemeStorage.MaximumSuffixCodepoints)];
    }

    internal static GhosttySnapshotPage FromOwnedGrid(GhosttySnapshotGrid grid,
        Dictionary<ushort, GhosttySnapshotStyle> styles, Dictionary<ushort, byte[]> hyperlinks)
    {
        byte[] header = new byte[20];
        BinaryPrimitives.WriteUInt16LittleEndian(header, checked((ushort)grid.Columns));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), checked((ushort)grid.Rows));
        // Ghostty's decoder uses these hints as fixed allocation capacities and
        // drops entries that do not fit. Derive them from owned data, never from
        // untrusted source hints (page.zig exactRowCapacity / RefCountedSet).
        int styleCapacity = SetCapacity(styles.Count);
        int linkedCells = 0;
        long graphemeBytes = 0;
        for (int row = 0; row < grid.Rows; row++)
        for (int column = 0; column < grid.Columns; column++)
        {
            if ((grid.Cells[row * grid.Columns + column] >> 48) != 0) linkedCells++;
            int suffixLength = grid.Suffix(row, column).Length;
            // Native appends suffix scalars incrementally. Reserve both old and
            // new 16-byte-aligned slices during growth, not just final storage.
            graphemeBytes += 2L * ((suffixLength * 4L + 15) & ~15L);
        }
        // A conservative upper bound for Set.Item across supported desktop
        // targets (48 bytes on 64-bit), plus the 16-cells-per-entry map budget.
        int linkBytes = Math.Max(SetCapacity(hyperlinks.Count), (linkedCells + 15) / 16) * 64;
        long stringBytes = 0;
        foreach (byte[] bytes in hyperlinks.Values)
        {
            GhosttySnapshotHyperlink link = GhosttySnapshotHyperlink.Read(bytes, out _);
            stringBytes += (link.Uri.Length + 31L) & ~31L;
            stringBytes += (link.ExplicitId.Length + 31L) & ~31L;
        }
        if (styleCapacity > ushort.MaxValue || linkBytes > ushort.MaxValue ||
            graphemeBytes > uint.MaxValue || stringBytes > uint.MaxValue)
            throw new InvalidDataException("Split the snapshot PAGE before its native capacity fields overflow.");
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)styleCapacity);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)linkBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)graphemeBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), (uint)stringBytes);
        GhosttySnapshotMetadataRestore? metadata = null;
        if (styles.Count != 0 || hyperlinks.Count != 0)
        {
            metadata = new(GhosttySnapshotPageCapacity.Read(header));
            foreach ((ushort id, GhosttySnapshotStyle style) in styles) metadata.ReadStyle(id, style);
            foreach ((ushort id, byte[] bytes) in hyperlinks)
                metadata.ReadHyperlink(id, GhosttySnapshotHyperlink.Read(bytes, out _), bytes, bytes);
        }
        return new(header, grid, styles, hyperlinks, metadata: metadata);
    }

    private static int SetCapacity(int count) => count == 0 ? 0 : ((count + 1) * 16 + 12) / 13;
    internal bool TryGetStyle(ushort id, out GhosttySnapshotStyle style) => _styles.TryGetValue(id, out style);
    internal bool TryGetLiveStyle(ushort id, out GhosttySnapshotStyle style)
    {
        if (_liveStyles is null || _liveStyles.Contains(id)) return TryGetStyle(id, out style);
        style = default;
        return false;
    }

    internal bool TryGetHyperlink(ushort id, out GhosttySnapshotHyperlink hyperlink)
    {
        if (_hyperlinks.TryGetValue(id, out byte[]? bytes))
        {
            hyperlink = GhosttySnapshotHyperlink.Read(bytes, out _);
            return true;
        }
        hyperlink = default;
        return false;
    }

    internal static GhosttySnapshotPage Read(ReadOnlySpan<byte> payload, int maximumCells,
        int maximumSuffixCodepoints, int maximumStringBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCells);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSuffixCodepoints);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumStringBytes);
        if (payload.Length < 20) throw new EndOfStreamException();
        int columns = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        int rows = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
        if (columns == 0 || rows == 0 || (long)columns * rows > maximumCells)
            throw new InvalidDataException("Snapshot PAGE dimensions exceed the configured limit.");
        int styleCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
        int hyperlinkCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
        ReadOnlySpan<byte> remaining = payload[20..];
        if (remaining.Length < (long)styleCount * 18 + (long)hyperlinkCount * 11 + (long)rows * 3 + 4)
            throw new EndOfStreamException();
        Dictionary<ushort, GhosttySnapshotStyle> styles = [];
        Dictionary<ushort, byte[]> hyperlinks = [];
        GhosttySnapshotMetadataRestore? metadata = styleCount != 0 || hyperlinkCount != 0
            ? new(GhosttySnapshotPageCapacity.Read(payload)) : null;
        // Invalid first entries must still reserve their IDs: a later valid
        // duplicate cannot revive an entry that decoded to the default.
        HashSet<ushort> seen = [];
        for (int index = 0; index < styleCount; index++)
        {
            if (remaining.Length < 18) throw new EndOfStreamException();
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(remaining);
            GhosttySnapshotStyle? style = GhosttySnapshotStyle.Read(remaining[2..]);
            remaining = remaining[18..];
            if (id == 0 || !seen.Add(id) || style is not { } valid || valid == default) continue;
            styles.Add(id, valid);
            metadata?.ReadStyle(id, valid);
        }
        seen.Clear();
        int strings = 0;
        for (int index = 0; index < hyperlinkCount; index++)
        {
            if (remaining.Length < 2) throw new EndOfStreamException();
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(remaining);
            remaining = remaining[2..];
            GhosttySnapshotHyperlink link = GhosttySnapshotHyperlink.Read(remaining, out int length);
            ReadOnlySpan<byte> encoded = remaining[..length];
            remaining = remaining[length..];
            byte[]? owned = null;
            if (id != 0 && seen.Add(id) && link.IsValid)
            {
                long added = (long)link.ExplicitId.Length + link.Uri.Length;
                if (added > maximumStringBytes - strings)
                    throw new InvalidDataException("Snapshot PAGE strings exceed the configured byte limit.");
                strings += (int)added;
                hyperlinks.Add(id, owned = encoded.ToArray());
            }
            // Native decodes even ignored/zero/duplicate IDs, then surrenders
            // their temporary references. That may retain dead string storage.
            metadata?.ReadHyperlink(id, link, encoded, owned);
        }
        GhosttySnapshotGraphemeRestore liveGraphemes = new(BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]), maximumSuffixCodepoints);
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.Read(remaining, columns, rows,
            maximumCells, maximumSuffixCodepoints, out int consumed, liveGraphemes);
        if (consumed != remaining.Length) throw new InvalidDataException("Snapshot PAGE has trailing payload bytes.");
        grid.ResolvePageIds(styles, hyperlinks);
        return new(payload[..20].ToArray(), grid, styles, hyperlinks, liveGraphemes.Suffixes, metadata, liveGraphemes.Storage);
    }

    internal void WritePayloadTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Snapshot destination is not writable.", nameof(destination));
        Span<byte> buffer = stackalloc byte[20];
        _header.CopyTo(buffer);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[4..], (ushort)_styles.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[6..], (ushort)_hyperlinks.Count);
        destination.Write(buffer);
        foreach ((ushort id, GhosttySnapshotStyle style) in _styles)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, id);
            style.Write(buffer[2..]);
            destination.Write(buffer[..18]);
        }
        foreach ((ushort id, byte[] hyperlink) in _hyperlinks)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, id);
            destination.Write(buffer[..2]);
            destination.Write(hyperlink);
        }
        Grid.WriteTo(destination);
    }
}
