// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

// PAGE tables own temporary references until all grid cells have been decoded.
// Style/link storage survives staging with its exact live/dead reference state. Raw
// tables stay separate so native pressure cannot corrupt lossless codec content.
internal sealed class GhosttySnapshotMetadataRestore
{
    private readonly HashSet<ushort> _seenLinks = [];
    private readonly HashSet<ushort> _styles = [];
    private readonly GhosttySnapshotStyleStorage _styleStorage;
    private readonly Dictionary<ushort, int> _styleIds = [];
    private readonly GhosttySnapshotHyperlinkStorage _linkStorage;
    private readonly Dictionary<ushort, int> _linkIds = [];

    internal GhosttySnapshotMetadataRestore(GhosttySnapshotPageCapacity capacity)
    {
        _styleStorage = new(capacity.Styles);
        _linkStorage = new(capacity.HyperlinkBytes, capacity.StringBytes);
    }

    internal IReadOnlySet<ushort> Styles => _styles;

    // The raw reader already enforces style-ID first-wins, validity and default.
    internal void ReadStyle(ushort id, GhosttySnapshotStyle style)
    {
        int native = _styleStorage.AddTableReference(style);
        if (native == 0) return;
        _styles.Add(id);
        _styleIds.Add(id, native);
    }

    // Every accepted wire table entry owns a separate temporary reference,
    // including equal values mapped to different wire IDs. Cells take their own
    // references before all temporary ones are surrendered. Unused styles remain
    // dead in the native table until a future insertion reclaims them.
    internal GhosttySnapshotStyleStorage FinishStyles(GhosttySnapshotGrid grid)
    {
        for (int i = 0; i < grid.Cells.Length; i++)
        {
            if (_styleIds.TryGetValue((ushort)(grid.Cells[i] >> 26), out int id))
                _styleStorage.AttachDecodedCell(i, id);
            ulong bits = grid.Cells[i];
            int kind = (int)(bits & 3);
            uint content = (uint)((bits >> 2) & 0xFFFFFF);
            if (kind >= 2)
                _styleStorage.ObserveInlineBackground(i, kind == 2
                    ? new(1, (byte)content, 0, 0) : new(2, (byte)content, (byte)(content >> 8), (byte)(content >> 16)));
        }
        foreach (int id in _styleIds.Values) _styleStorage.ReleaseTableReference(id);
        _styleIds.Clear();
        return _styleStorage;
    }

    internal void ReadHyperlink(ushort id, GhosttySnapshotHyperlink link, ReadOnlySpan<byte> encoded, byte[]? owned)
    {
        int native = _linkStorage.AddDecodedTableReference(link, encoded, owned);
        if (id == 0 || !_seenLinks.Add(id)) _linkStorage.ReleaseTableReference(native);
        else if (native != 0) _linkIds.Add(id, native);
    }

    internal GhosttySnapshotHyperlinkStorage FinishHyperlinks(GhosttySnapshotGrid grid)
    {
        for (int i = 0; i < grid.Cells.Length; i++)
            if (_linkIds.TryGetValue((ushort)(grid.Cells[i] >> 48), out int id))
                _ = _linkStorage.AttachDecodedCell(i, id);
        foreach (int id in _linkIds.Values) _linkStorage.ReleaseTableReference(id);
        _linkIds.Clear();
        return _linkStorage;
    }
}
