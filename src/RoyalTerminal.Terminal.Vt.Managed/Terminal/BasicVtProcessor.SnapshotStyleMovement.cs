// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    // Constant-time, allocation-free delivery of page-rebuild degradation. Do
    // not infer it from IsCurrent: a deliberate SGR/restore can be in flight.
    private byte ApplySnapshotCursorStyleDrops()
    {
        byte dropped = _screen.TakeSnapshotCursorStyleDrops();
        if ((dropped & 1) != 0) _snapshotPrimaryPen = default;
        if ((dropped & 2) != 0)
        {
            _snapshotAlternatePen = default;
            _alternateEraseBackground = default;
        }
        if ((dropped & (_inAltScreen ? 2 : 1)) != 0) ResetAttributes();
        return dropped;
    }

    private SnapshotCursorStyleScope TrackSnapshotCursorMovement()
        => _screen.TracksSnapshotMetadata ? new(this) : default;

    // Only semantic control boundaries call this, never each printable byte.
    // Same-page movement does not migrate references in Ghostty either.
    private void RecordSnapshotCursorStyle()
    {
        // Do not allocate or mask the original fatal row-copy error while
        // movement scopes unwind around a partially completed operation.
        if (!_screen.TracksSnapshotMetadata || _screen.SnapshotMutationFailed) return;
        RecordSnapshotCursorStyle(CaptureSnapshotPen());
    }

    private GhosttySnapshotStyle RecordSnapshotCursorStyle(GhosttySnapshotStyle pen)
    {
        int key = _inAltScreen ? 1 : 0;
        if ((ApplySnapshotCursorStyleDrops() & (1 << key)) != 0) pen = default;
        if (!_screen.SnapshotCursorStyleIsCurrent(key, _cursorRow, pen))
            pen = ChangeSnapshotStyle(key, _cursorRow, pen, pen);
        ref uint counter = ref (_inAltScreen ? ref _alternateHyperlinkImplicitCounter : ref _primaryHyperlinkImplicitCounter);
        _currentHyperlinkId = _screen.SnapshotHyperlinkChanged(key, _cursorRow, pen, _currentHyperlinkId, ref counter);
        if ((ApplySnapshotCursorStyleDrops() & (1 << key)) != 0) pen = default;
        return pen;
    }

    private GhosttySnapshotStyle ChangeSnapshotStyle(int key, int row, GhosttySnapshotStyle previous,
        GhosttySnapshotStyle current, bool restorePreviousOnFailure = false)
    {
        ref uint counter = ref (key == 0 ? ref _primaryHyperlinkImplicitCounter : ref _alternateHyperlinkImplicitCounter);
        if (!_screen.SnapshotStyleChanged(key, row, previous, current, ref counter))
        {
            // Screen.setAttribute restores the previous pen; DECRC and page
            // movement instead degrade to default on capacity failure. Screen
            // cursorCopy has a separate destination-cursor rollback boundary.
            current = restorePreviousOnFailure && _screen.SnapshotStyleChanged(key, row, default, previous, ref counter)
                ? previous : default;
            _screen.SnapshotStyleChanged(key, row, default, current, ref counter);
            if (key == (_inAltScreen ? 1 : 0))
            {
                TerminalCell pen = GhosttySnapshotLivePage.DecodeStyle(current, _theme);
                InstallSnapshotPen(in pen);
            }
            else if (key == 0) _snapshotPrimaryPen = current;
            else _snapshotAlternatePen = current;
        }
        if (key == (_inAltScreen ? 1 : 0))
            _currentHyperlinkId = _screen.SnapshotCursorHyperlinkToken(key, _currentHyperlinkId);
        else if (key == 0)
            _snapshotPrimaryHyperlink = _screen.SnapshotCursorHyperlinkToken(key, _snapshotPrimaryHyperlink);
        else _snapshotAlternateHyperlink = _screen.SnapshotCursorHyperlinkToken(key, _snapshotAlternateHyperlink);
        return current;
    }

    private readonly struct SnapshotCursorStyleScope(BasicVtProcessor? owner) : IDisposable
    {
        public void Dispose() => owner?.RecordSnapshotCursorStyle();
    }
}
