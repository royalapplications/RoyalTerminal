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

    private GhosttySnapshotPage(byte[] header, GhosttySnapshotGrid grid,
        Dictionary<ushort, GhosttySnapshotStyle> styles, Dictionary<ushort, byte[]> hyperlinks)
    { _header = header; Grid = grid; _styles = styles; _hyperlinks = hyperlinks; }

    internal GhosttySnapshotGrid Grid { get; }
    internal int StyleCount => _styles.Count;
    internal int HyperlinkCount => _hyperlinks.Count;
    internal bool TryGetStyle(ushort id, out GhosttySnapshotStyle style) => _styles.TryGetValue(id, out style);
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
            if (id == 0 || !seen.Add(id) || !link.IsValid) continue;
            long added = (long)link.ExplicitId.Length + link.Uri.Length;
            if (added > maximumStringBytes - strings)
                throw new InvalidDataException("Snapshot PAGE strings exceed the configured byte limit.");
            strings += (int)added;
            hyperlinks.Add(id, encoded.ToArray());
        }
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.Read(remaining, columns, rows,
            maximumCells, maximumSuffixCodepoints, out int consumed);
        if (consumed != remaining.Length) throw new InvalidDataException("Snapshot PAGE has trailing payload bytes.");
        grid.ResolvePageIds(styles, hyperlinks);
        return new(payload[..20].ToArray(), grid, styles, hyperlinks);
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
