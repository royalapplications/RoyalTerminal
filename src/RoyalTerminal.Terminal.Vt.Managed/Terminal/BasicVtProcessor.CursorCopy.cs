// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    // The screen switch has committed, but Screen.cursorCopy can still fail
    // while loading the entering pen at the destination's old position. Copy
    // cursor-owned registers only after that operation succeeds. Charset and
    // mode/clear changes belong to the switch, not this fallible cursor copy.
    private void CopyEnteringScreenCursor(int key, bool destinationWasCleared = false)
    {
        int row = key == 0 ? _savedMainCursorRow : _savedAlternateCursorRow;
        GhosttySnapshotStyle previous = key == 0 ? _snapshotPrimaryPen : _snapshotAlternatePen;
        ref uint counter = ref (key == 0 ? ref _primaryHyperlinkImplicitCounter : ref _alternateHyperlinkImplicitCounter);
        if (_screen.TracksSnapshotMetadata &&
            !_screen.SnapshotStyleChanged(key, row, previous, CaptureSnapshotPen(), ref counter))
        {
            // The failed insertion released the old style reference. Reacquire
            // it in the surviving allocation (which may have grown/split), not
            // by restoring an obsolete page-local numeric ID. If that also
            // fails, the normal style fallback leaves a consistent default pen.
            GhosttySnapshotStyle restored = ChangeSnapshotStyle(key, row, default, previous);
            TerminalCell pen = GhosttySnapshotLivePage.DecodeStyle(restored, _theme);
            InstallSnapshotPen(in pen);
            _currentProtected = key == 0 ? _snapshotPrimaryProtected : _snapshotAlternateProtected;
            _cursorCol = key == 0 ? _savedMainCursorCol : _savedAlternateCursorCol;
            _cursorRow = row;
            _delayedWrap = !destinationWasCleared && (key == 0 ? _savedMainDelayedWrap : _savedAlternateDelayedWrap);
            return;
        }

        if (key == 0)
        {
            _primaryCursorStyle = _alternateCursorStyle;
            _primarySemanticPen = _alternateSemanticPen;
            _primaryHyperlinkImplicitCounter = _alternateHyperlinkImplicitCounter;
        }
        else
        {
            _alternateCursorStyle = _primaryCursorStyle;
            _alternateSemanticPen = _primarySemanticPen;
            _alternateHyperlinkImplicitCounter = _primaryHyperlinkImplicitCounter;
        }
    }
}
