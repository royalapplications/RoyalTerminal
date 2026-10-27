// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    internal int GetSnapshotViewportTop(int key)
    {
        TerminalRowBuffer rows = GetSnapshotRows(key) ?? throw new InvalidDataException("Missing Kitty snapshot screen.");
        int offset = key == 0 ? _alternateBufferActive ? _primaryScrollOffset : _viewportTop : 0;
        return Math.Max(0, rows.Count - ViewportRows - offset);
    }

    // Snapshot import/export must not switch the active screen merely to access
    // a placement on the other buffer. Both buffers share the anchor owner.
    internal TerminalScreenAnchor CreateSnapshotAnchor(int key, int row, int column)
    {
        TerminalRowBuffer rows = GetSnapshotRows(key) ?? throw new InvalidDataException("Missing Kitty snapshot screen.");
        if ((uint)row >= (uint)rows.Count || (uint)column >= (uint)rows[row].Columns)
            throw new ArgumentOutOfRangeException(nameof(row));
        TerminalScreenAnchor token = new();
        _trackedAnchors.Add(token, new(row, column, key == 1));
        _anchorRevision++;
        return token;
    }

    internal bool TryResolveSnapshotAnchor(TerminalScreenAnchor token, int key, out TerminalGridPosition position)
    {
        if (_trackedAnchors.TryGetValue(token, out TrackedCell cell) && cell.Alternate == (key == 1) && cell.Row >= 0)
        {
            position = new(cell.Column, cell.Row); return true;
        }
        position = default; return false;
    }
}
