// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    /// <summary>
    /// Creates history above a top-origin scrolling region, leaving rows below
    /// its bottom margin stationary. Caller holds the screen lock.
    /// </summary>
    internal TerminalRow AddRowAtActiveRow(int bottom)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bottom);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bottom, ViewportRows);
        TerminalRow blank = AddRow();
        int insertion = TotalRows - ViewportRows + bottom;
        // Like Ghostty cursorScrollAbove: rotate only the suffix below the
        // margin, not the history or cell arrays. Metadata and shared storage
        // remain attached to their logical rows.
        for (int row = TotalRows - 1; row > insertion; row--)
            _rows[row] = _rows[row - 1];
        _rows[insertion] = blank;

        // AddRow already accounted for history pruning. The suffix originally
        // below the margin occupies the insertion position before rotation.
        if (bottom < ViewportRows - 1)
        {
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
        return blank;
    }
}
