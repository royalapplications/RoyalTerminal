// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

// Page replacement clones every metadata allocator, even when only one grows.
// Keep ownership atomic without mixing the allocators' individual algorithms.
internal sealed class GhosttySnapshotPageStorage(GhosttySnapshotStyleStorage styles, GhosttySnapshotGraphemeStorage graphemes,
    GhosttySnapshotHyperlinkStorage hyperlinks)
{
    internal GhosttySnapshotPageStorage(GhosttySnapshotPageCapacity capacity)
        : this(new(capacity.Styles), new(capacity.GraphemeBytes), new(capacity.HyperlinkBytes, capacity.StringBytes)) { }
    internal GhosttySnapshotStyleStorage Styles { get; } = styles;
    internal GhosttySnapshotGraphemeStorage Graphemes { get; } = graphemes;
    internal GhosttySnapshotHyperlinkStorage Hyperlinks { get; } = hyperlinks;
    internal GhosttySnapshotPageStorage Copy() => new(Styles.Copy(), Graphemes.Copy(), Hyperlinks.Copy());

    internal ulong Usage(GhosttySnapshotCapacityDimension? dimension) => dimension switch
    {
        GhosttySnapshotCapacityDimension.GraphemeBytes => Graphemes.AllocatedBytes,
        GhosttySnapshotCapacityDimension.HyperlinkBytes => (ulong)Hyperlinks.Count,
        GhosttySnapshotCapacityDimension.StringBytes => Hyperlinks.StringBytes,
        _ => (ulong)Styles.Count,
    };

    internal bool Rebuild(GhosttySnapshotPageCapacity capacity, bool restoreCursor, out GhosttySnapshotPageStorage? result)
    {
        result = null;
        if (Graphemes.Rebuild(capacity.GraphemeBytes, out GhosttySnapshotGraphemeStorage? graphemes) != GhosttySnapshotGraphemeAddResult.Success ||
            Hyperlinks.Rebuild(capacity.HyperlinkBytes, capacity.StringBytes, out GhosttySnapshotHyperlinkStorage? hyperlinks) != GhosttySnapshotHyperlinkAddResult.Success ||
            Styles.Rebuild(capacity.Styles, out GhosttySnapshotStyleStorage? styles) != GhosttySnapshotSetAddResult.Success ||
            restoreCursor && styles!.ChangeCursor(Styles.Cursor) != GhosttySnapshotSetAddResult.Success) return false;
        // Screen.increaseCapacity restores style first, then tries the cursor
        // hyperlink once. Duplicate string scratch can fail even when all cells
        // cloned successfully; native drops that cursor link without retrying.
        if (restoreCursor) _ = Hyperlinks.CopyCursorTo(hyperlinks!);
        result = new(styles!, graphemes!, hyperlinks!);
        return true;
    }
}
