// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    // Ghostty Terminal.insertColumns/deleteColumns preserve the cursor and
    // reuse character editing across the vertical scrolling region.
    private void EditColumns(int count, bool insert)
    {
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom ||
            !CursorInsideHorizontalMargins) return;

        int row = _cursorRow;
        int column = _cursorCol;
        try
        {
            ResetDelayedWrap();
            for (int y = _scrollTop; y <= _scrollBottom; y++)
            {
                _cursorRow = y;
                _cursorCol = column;
                if (insert) InsertCharacters(count);
                else DeleteCharacters(count);
            }
        }
        finally
        {
            _cursorRow = row;
            _cursorCol = column;
        }
    }

    private void HorizontalIndex(bool forward)
    {
        ResetDelayedWrap();
        int column = _cursorCol;
        if (column == (forward ? RightMargin : _scrollLeft))
        {
            try
            {
                if (forward) _cursorCol = _scrollLeft;
                EditColumns(1, insert: !forward);
            }
            finally
            {
                _cursorCol = column;
            }
        }
        else
        {
            _cursorCol = Math.Clamp(column + (forward ? 1 : -1), 0, _screen.Columns - 1);
        }
    }
}
