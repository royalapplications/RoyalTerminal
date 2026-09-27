// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    // Ghostty Terminal.cursorLeft (including #14390) is authoritative for both
    // BS and CUB. xterm.js only reverses soft wraps for BS and clears their wrap
    // marker; Windows Terminal clamps CUB. We retain Ghostty's wrap markers and
    // extended-mode margin cycling so the two RoyalTerminal engines agree.
    private void CursorBackward(int count)
    {
        bool extended = _extendedDecModes.Contains(ManagedDecModeFlag.ReverseWrapExtended);
        if (!_autoWrap || (!extended && !_extendedDecModes.Contains(ManagedDecModeFlag.ReverseWrap)))
        {
            _cursorCol = Math.Max(0, _cursorCol - count);
            ResetDelayedWrap();
            return;
        }

        if (_delayedWrap)
        {
            ResetDelayedWrap();
            if (--count == 0) return; // Do not relocate a restored cursor above the top margin.
        }

        int left = CursorLeftLimit;
        if (!extended && _cursorCol == left && _cursorRow <= _scrollTop)
        {
            _cursorRow = _scrollTop;
            return;
        }

        while (count > 0)
        {
            int amount = Math.Min(_cursorCol - left, count);
            _cursorCol -= amount;
            count -= amount;
            if (count == 0) return;

            if (_cursorRow == _scrollTop)
            {
                if (!extended) return;
                _cursorRow = _scrollBottom;
            }
            else
            {
                // Above the region, extended mode stops at physical row zero.
                // Neither mode can reverse into scrollback or scroll the screen.
                if (_cursorRow == 0 || (!extended && !_screen.GetViewportRow(_cursorRow - 1).WrapsToNext)) return;
                _cursorRow--;
            }
            _cursorCol = RightMargin;
            count--;
        }
    }
}
