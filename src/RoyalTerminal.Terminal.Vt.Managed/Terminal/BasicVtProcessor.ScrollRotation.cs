// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private void RotateHistorySuffixDownOneRow(TerminalRow added)
    {
        // Screen.cursorScrollAboveRotate first advances the cursor into its
        // destination page (done by the caller), then visits pages tail-first.
        // Keep each allocation contiguous: moving the new tail row directly
        // to the margin would change cursor ownership and bypass boundary-copy
        // pressure. Only one row per page boundary is cloned; same-page rows
        // keep their cell arrays, physical metadata slots and COW ownership.
        _screen.ShiftAnchorsBelowHistoryMargin(_scrollBottom);
        int end = _screen.ViewportRows - 1;
        while (end >= _scrollBottom)
        {
            TerminalRow last = _screen.GetViewportRow(end);
            if (_screen.TracksSnapshotMetadata && last.SnapshotAllocation is null)
            {
                using GhosttySnapshotPageTracker.RowEdit observed = _screen.EditSnapshotRowMetadata(last);
            }
            GhosttySnapshotPageAllocation? page = last.SnapshotAllocation;
            int start = end;
            while (start > _scrollBottom &&
                ReferenceEquals(_screen.GetViewportRow(start - 1).SnapshotAllocation, page)) start--;

            _screen.RotateViewportRowsDown(start, end);
            if (start > _scrollBottom)
                CopyRow(_screen.GetViewportRow(start - 1), last, preserveWrap: true);
            else
            {
                if (_screen.TracksSnapshotMetadata) ApplySnapshotCursorStyleDrops();
                // AddRow already initialized the fresh tail. The same-page
                // fast path must not clear an unchanged blank row twice.
                if (!ReferenceEquals(last, added) || _currentBgKind != SgrColorKind.Default)
                    ClearRow(last, _screen.DefaultForeground, _currentBg, CurrentBackgroundIdentity);
            }
            end = start - 1;
        }
    }

    private void RotateScrollRegionUpOneRow()
    {
        // Screen.cursorScrollRegionUp / PageList.eraseRow[Bounded] retain the
        // allocator and physical cell slots within each page. Only the first
        // row crossing each page boundary needs a clone. Do not implement this
        // as DL: DL clears wrap state and releases destination rows in a
        // different order, changing both rendering and metadata pressure.
        _screen.ShiftAnchorsInViewportRows(_scrollTop, _scrollBottom, -1);
        _screen.ShiftRasterGraphicsInViewportRows(_scrollTop, _scrollBottom, -1);
        int start = _scrollTop;
        while (start <= _scrollBottom)
        {
            TerminalRow first = _screen.GetViewportRow(start);
            if (_screen.TracksSnapshotMetadata && first.SnapshotAllocation is null)
            {
                using GhosttySnapshotPageTracker.RowEdit observed = _screen.EditSnapshotRowMetadata(first);
            }
            GhosttySnapshotPageAllocation? page = first.SnapshotAllocation;
            int end = start;
            while (end < _scrollBottom &&
                ReferenceEquals(_screen.GetViewportRow(end + 1).SnapshotAllocation, page)) end++;

            if (end == _scrollBottom)
            {
                if (_screen.TracksSnapshotMetadata) ApplySnapshotCursorStyleDrops();
                ClearRow(first, _currentFg, _currentBg, CurrentBackgroundIdentity);
            }
            _screen.RotateViewportRowsUp(start, end);
            if (end < _scrollBottom)
                CopyRow(_screen.GetViewportRow(end + 1), first, preserveWrap: true);
            start = end + 1;
        }
        _screen.InvalidateViewport();
    }
}
