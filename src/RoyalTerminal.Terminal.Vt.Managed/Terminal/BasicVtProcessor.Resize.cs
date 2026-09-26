// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private int _savedAlternateCursorCol;
    private int _savedAlternateCursorRow;
    private bool _savedAlternateDelayedWrap;
    private int _tabStopColumns;

    private void ResizeInactiveScreen(int oldColumns, int oldRows, int columns, int rows,
        bool reflowOnResize, bool preserveViewportTopOnRowsIncrease)
    {
        using TerminalScreen.InactiveResizeScope scope = _screen.EnterInactiveResize(oldColumns, oldRows);
        if (!scope.Available) return;
        (int Column, int Row, bool PendingWrap) active = (_cursorCol, _cursorRow, _delayedWrap);
        bool alternate = _inAltScreen;
        _inAltScreen = !alternate;
        (_cursorCol, _cursorRow, _delayedWrap) = alternate
            ? (_savedMainCursorCol, _savedMainCursorRow, _savedMainDelayedWrap)
            : (_savedAlternateCursorCol, _savedAlternateCursorRow, _savedAlternateDelayedWrap);
        try
        {
            int hyperlink = ResizeActiveScreenBuffer(columns, rows, reflowOnResize,
                Span<TerminalGridPosition>.Empty, preserveViewportTopOnRowsIncrease,
                alternate ? _snapshotPrimaryPen : _snapshotAlternatePen,
                alternate ? _snapshotPrimaryHyperlink : _snapshotAlternateHyperlink);
            if (alternate)
            {
                (_savedMainCursorCol, _savedMainCursorRow, _savedMainDelayedWrap) = (_cursorCol, _cursorRow, _delayedWrap);
                _snapshotPrimaryHyperlink = hyperlink;
            }
            else
            {
                (_savedAlternateCursorCol, _savedAlternateCursorRow, _savedAlternateDelayedWrap) = (_cursorCol, _cursorRow, _delayedWrap);
                _snapshotAlternateHyperlink = hyperlink;
            }
        }
        finally
        {
            _inAltScreen = alternate;
            (_cursorCol, _cursorRow, _delayedWrap) = active;
        }
    }
}
