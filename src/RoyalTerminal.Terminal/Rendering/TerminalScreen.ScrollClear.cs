// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    // snapshot/screen.zig installs both alternate limits at zero; a newly
    // created Terminal screen has only the byte limit. Preserve that lifetime
    // distinction across COW publication, but not alternate-screen disposal.
    private bool _snapshotAlternateLineLimit;

    private int ScrollAlternateViewportToHistory()
    {
        ScrollOffset = 0;
        int movedRows = GetNonEmptyViewportRowCount();
        if (movedRows > 0)
        {
            // ED22 uses PageList.grow even with Screen.no_scrollback. Keep its
            // incidental history under the alternate page-byte limit, not the
            // primary limit or the ordinary alternate line-feed row cap.
            // Tracking also preserves this geometry across buffer switches.
            _snapshotRowGeometry = true;
            GhosttySnapshotAllocation layout = SnapshotPageLayout();
            if (_rows[0].SnapshotAllocation is null)
                _ = GhosttySnapshotLiveAllocation.Measure(this, _rows, layout);
            GhosttySnapshotScrollbackQuota quota = AlternateSnapshotHistoryQuota();
            for (int index = 0; index < movedRows; index++) AddRowCore(int.MaxValue, quota);
        }
        ClearRasterGraphicsInViewportRectangle(0, ViewportRows - 1, 0, Columns - 1);
        ClearKittyGraphics();
        InvalidateAll();
        return movedRows;
    }

    private GhosttySnapshotScrollbackQuota AlternateSnapshotHistoryQuota()
    {
        _ = SnapshotPageLayout();
        return new()
        {
            PageAlignment = _snapshotPageAlignment,
            MaximumBytes = 0,
            MaximumRows = _snapshotAlternateLineLimit ? 0UL : null,
        };
    }
}
