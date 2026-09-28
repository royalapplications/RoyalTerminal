// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    /// <summary>
    /// Rotates row ownership within one viewport page slice. The first row is
    /// recycled at the end without releasing its allocator slot. The processor
    /// owns cell clearing/cross-page copies and shifts anchors separately.
    /// Caller holds the screen lock.
    /// </summary>
    internal void RotateViewportRowsUp(int top, int bottom)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(top);
        ArgumentOutOfRangeException.ThrowIfLessThan(bottom, top);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bottom, ViewportRows);
        int first = GetAbsoluteRowForViewportRow(top), last = GetAbsoluteRowForViewportRow(bottom);
        TerminalRow recycled = _rows[first];
        for (int row = first; row < last; row++)
        {
            _rows[row] = _rows[row + 1];
            _rows[row].IsDirty = true;
        }
        _rows[last] = recycled;
        recycled.IsDirty = true;
    }

    /// <summary>
    /// Rotates row ownership down within one viewport page slice, retaining
    /// every row's allocator and physical cell slot. Caller holds the lock and
    /// owns boundary copies, clearing and anchor movement.
    /// </summary>
    internal void RotateViewportRowsDown(int top, int bottom)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(top);
        ArgumentOutOfRangeException.ThrowIfLessThan(bottom, top);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bottom, ViewportRows);
        int first = GetAbsoluteRowForViewportRow(top), last = GetAbsoluteRowForViewportRow(bottom);
        TerminalRow recycled = _rows[last];
        for (int row = last; row > first; row--)
        {
            _rows[row] = _rows[row - 1];
            _rows[row].IsDirty = true;
        }
        _rows[first] = recycled;
        recycled.IsDirty = true;
    }

    /// <summary>
    /// Keeps anchors below a top-origin history region stationary on screen
    /// after AddRow and before its suffix rotates down. History pruning was
    /// already accounted for by AddRow. Caller holds the screen lock.
    /// </summary>
    internal void ShiftAnchorsBelowHistoryMargin(int bottom)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bottom);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bottom, ViewportRows);
        if (bottom < ViewportRows - 1)
        {
            int insertion = GetAbsoluteRowForViewportRow(bottom);
            _anchorRevision++;
            foreach ((TerminalScreenAnchor token, TrackedCell original) in _trackedAnchors)
            {
                if (original.Alternate != _alternateBufferActive || original.Row < insertion) continue;
                CollectionsMarshal.GetValueRefOrNullRef(_trackedAnchors, token).Row++;
            }
            for (int i = 0; i < _rasterPlacements.Count; i++)
            {
                TerminalRasterImagePlacement placement = _rasterPlacements[i];
                if (placement.AnchorRow >= insertion)
                    _rasterPlacements[i] = placement.WithAnchorRow(placement.AnchorRow + 1);
            }
        }
    }
}
