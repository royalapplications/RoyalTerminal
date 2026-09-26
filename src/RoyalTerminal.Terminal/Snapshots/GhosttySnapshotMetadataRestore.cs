// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

// PAGE tables own temporary references until all grid cells have been decoded.
// Only the accepted wire IDs survive this staging helper. Raw tables are kept
// separately so native pressure does not corrupt lossless codec operations.
internal sealed class GhosttySnapshotMetadataRestore
{
    private readonly HashSet<ushort> _seenLinks = [];
    private readonly HashSet<ushort> _styles = [], _links = [];
    private readonly GhosttySnapshotRefCountedSet<GhosttySnapshotStyle> _styleSet;
    private readonly GhosttySnapshotRefCountedSet<LinkValue> _linkSet;
    private readonly LinkContext _linkContext;

    internal GhosttySnapshotMetadataRestore(GhosttySnapshotPageCapacity capacity)
    {
        _styleSet = new(capacity.Styles, new StyleContext());
        _linkContext = new(new(capacity.StringBytes, 32));
        _linkSet = new((ushort)(capacity.HyperlinkBytes / 48), _linkContext);
    }

    internal IReadOnlySet<ushort> Styles => _styles;
    internal IReadOnlySet<ushort> Links => _links;

    // The raw reader already enforces style-ID first-wins, validity and default.
    internal void ReadStyle(ushort id, GhosttySnapshotStyle style)
    {
        if (_styleSet.Add(style) != 0) _styles.Add(id);
    }

    internal void ReadHyperlink(ushort id, GhosttySnapshotHyperlink link, ReadOnlySpan<byte> encoded, byte[]? owned)
    {
        int native = DecodeHyperlink(link, encoded, owned);
        if (id == 0 || !_seenLinks.Add(id)) _linkSet.Release(native);
        else if (native != 0) _links.Add(id);
    }

    private int DecodeHyperlink(GhosttySnapshotHyperlink link, ReadOnlySpan<byte> encoded, byte[]? owned)
    {
        if (!link.IsValid) return 0;
        GhosttySnapshotBitmap.Slice id = default;
        if (link.HasExplicitId && !_linkContext.Bitmap.TryAllocate(link.ExplicitId.Length, out id)) return 0;
        if (!_linkContext.Bitmap.TryAllocate(link.Uri.Length, out GhosttySnapshotBitmap.Slice uri))
        {
            _linkContext.Bitmap.Free(id);
            return 0;
        }
        // Even duplicate values allocate their strings before set lookup. A
        // released entry keeps its old strings until add trims/reuses its ID.
        LinkValue value = new(owned ?? encoded.ToArray(), id, uri, GhosttySnapshotMetadataHash.Hyperlink(link));
        int result = _linkSet.Add(value);
        if (result == 0) _linkContext.Deleted(value);
        return result;
    }

    private sealed class StyleContext : IGhosttySnapshotSetContext<GhosttySnapshotStyle>
    {
        public ulong Hash(GhosttySnapshotStyle value) => GhosttySnapshotMetadataHash.Style(value);
        public bool Equal(GhosttySnapshotStyle left, GhosttySnapshotStyle right) => left == right;
        public void Deleted(GhosttySnapshotStyle value) { }
    }

    private sealed record LinkValue(byte[] Encoded, GhosttySnapshotBitmap.Slice Id, GhosttySnapshotBitmap.Slice Uri, ulong Hash);

    private sealed class LinkContext(GhosttySnapshotBitmap bitmap) : IGhosttySnapshotSetContext<LinkValue>
    {
        internal GhosttySnapshotBitmap Bitmap => bitmap;
        public ulong Hash(LinkValue value) => value.Hash;
        public bool Equal(LinkValue left, LinkValue right) => left.Encoded.AsSpan().SequenceEqual(right.Encoded);
        public void Deleted(LinkValue value) { bitmap.Free(value.Id); bitmap.Free(value.Uri); }
    }
}
