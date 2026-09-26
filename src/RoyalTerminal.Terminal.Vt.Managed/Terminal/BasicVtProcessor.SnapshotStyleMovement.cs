// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private SnapshotCursorStyleScope TrackSnapshotCursorMovement()
        => _screen.TracksSnapshotStyles ? new(this) : default;

    // Only semantic control boundaries call this, never each printable byte.
    // Same-page movement does not migrate references in Ghostty either.
    private void RecordSnapshotCursorStyle()
    {
        if (!_screen.TracksSnapshotStyles) return;
        int key = _inAltScreen ? 1 : 0;
        GhosttySnapshotStyle pen = CaptureSnapshotPen();
        if (!_screen.SnapshotCursorStyleIsCurrent(key, _cursorRow, pen))
            _screen.SnapshotStyleChanged(key, _cursorRow, pen, pen);
    }

    private readonly struct SnapshotCursorStyleScope(BasicVtProcessor? owner) : IDisposable
    {
        public void Dispose() => owner?.RecordSnapshotCursorStyle();
    }
}
