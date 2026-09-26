// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

// Page replacement clones every metadata allocator, even when only one grows.
// Keep ownership atomic without mixing the allocators' individual algorithms.
internal sealed class GhosttySnapshotPageStorage(GhosttySnapshotStyleStorage styles, GhosttySnapshotGraphemeStorage graphemes)
{
    internal GhosttySnapshotPageStorage(GhosttySnapshotPageCapacity capacity) : this(new(capacity.Styles), new(capacity.GraphemeBytes)) { }
    internal GhosttySnapshotStyleStorage Styles { get; } = styles;
    internal GhosttySnapshotGraphemeStorage Graphemes { get; } = graphemes;
    internal GhosttySnapshotPageStorage Copy() => new(Styles.Copy(), Graphemes.Copy());

    internal bool Rebuild(GhosttySnapshotPageCapacity capacity, bool restoreCursor, out GhosttySnapshotPageStorage? result)
    {
        result = null;
        if (Graphemes.Rebuild(capacity.GraphemeBytes, out GhosttySnapshotGraphemeStorage? graphemes) != GhosttySnapshotGraphemeAddResult.Success ||
            Styles.Rebuild(capacity.Styles, out GhosttySnapshotStyleStorage? styles) != GhosttySnapshotSetAddResult.Success ||
            restoreCursor && styles!.ChangeCursor(Styles.Cursor) != GhosttySnapshotSetAddResult.Success) return false;
        result = new(styles!, graphemes!);
        return true;
    }
}
