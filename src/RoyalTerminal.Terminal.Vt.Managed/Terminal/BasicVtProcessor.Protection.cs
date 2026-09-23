// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private enum CharacterProtectionMode : byte { Off, Iso, Dec }

    private bool _currentProtected;
    private CharacterProtectionMode _primaryProtectionMode;
    private CharacterProtectionMode _alternateProtectionMode;

    private ref CharacterProtectionMode ProtectionMode => ref (_inAltScreen ? ref _alternateProtectionMode : ref _primaryProtectionMode);

    private void SetCharacterProtection(CharacterProtectionMode mode)
    {
        _currentProtected = mode != CharacterProtectionMode.Off;
        // ISO erase behavior survives EPA; only a subsequent DEC selection changes it.
        if (_currentProtected) ProtectionMode = mode;
    }

    private void EraseProtectedDisplay(int mode)
    {
        if (mode == 0)
        {
            EraseProtectedLine(0);
            for (int row = _cursorRow + 1; row < _screen.ViewportRows; row++)
                ClearUnprotectedCells(row, 0, _screen.Columns);
        }
        else if (mode == 1)
        {
            EraseProtectedLine(1);
            for (int row = 0; row < _cursorRow; row++)
                ClearUnprotectedCells(row, 0, _screen.Columns);
        }
        else
        {
            for (int row = 0; row < _screen.ViewportRows; row++)
                ClearUnprotectedCells(row, 0, _screen.Columns);
            _kittyStore.ClearScreen(_screen, (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx());
            AdvanceKittyAnimations();
            PublishKittyGraphics();
        }
        ResetDelayedWrap();
    }

    private void EraseProtectedLine(int mode)
    {
        if (mode is < 0 or > 2) return;
        TerminalRow row = _screen.GetViewportRow(_cursorRow);
        int start = mode == 0 ? _cursorCol : 0;
        int end = mode == 1 ? _cursorCol + 1 : row.Columns;
        // EL includes both halves if its edge intersects a wide glyph.
        if (start > 0 && row.ReadOnlyCells[start].Width == 0 && !row.ReadOnlyCells[start].IsWideSpacerHead) start--;
        if (end < row.Columns && row.ReadOnlyCells[end - 1].Width == 2) end++;
        if (mode != 1) ResetProtectedRowWrap(row);
        ClearUnprotectedCells(_cursorRow, start, end);
        ResetDelayedWrap();
    }

    private void EraseProtectedCharacters(int count)
    {
        TerminalRow row = _screen.GetViewportRow(_cursorRow);
        int end = Math.Min(row.Columns, _cursorCol + count);
        if (end < row.Columns && row.ReadOnlyCells[end - 1].Width == 2) end++;
        // Match Ghostty eraseChars: split boundary pairs before considering ISO
        // protection. Upstream explicitly documents this protected-wide edge case.
        if (_cursorRow > 0 && _cursorCol <= 1 && row.ReadOnlyCells[0].Width == 2)
            ErasePreviousWideSpacerHead();
        if (_cursorCol > 0 && row.ReadOnlyCells[_cursorCol - 1].Width == 2)
            ClearCellAndWideArtifacts(row, _cursorCol);
        ResetProtectedRowWrap(row);
        ClearUnprotectedCells(_cursorRow, _cursorCol, end);
        ResetDelayedWrap();
    }

    private void ResetProtectedRowWrap(TerminalRow row)
    {
        // A spacer head cannot survive removing its row's wrap marker.
        if (row.ReadOnlyCells[^1].IsWideSpacerHead) row[row.Columns - 1] = CreateErasedCell();
        row.WrapsToNext = false;
    }

    private void ErasePreviousWideSpacerHead()
    {
        if (_cursorRow <= 0) return;
        TerminalRow previous = _screen.GetViewportRow(_cursorRow - 1);
        if (!previous.ReadOnlyCells[^1].IsWideSpacerHead) return;
        previous[previous.Columns - 1] = CreateErasedCell();
        previous.IsDirty = true;
    }

    private void ClearUnprotectedCells(int rowIndex, int start, int end)
    {
        TerminalRow row = _screen.GetViewportRow(rowIndex);
        ClearPreservedCellsForMutation(row);
        // Preserve protected cells and their complete style/link/grapheme data.
        // Batch only unprotected ranges so raster clearing also respects holes.
        int column = start;
        while (column < end)
        {
            if (row.ReadOnlyCells[column].IsProtected) { column++; continue; }
            int first = column;
            do { row[column++] = CreateErasedCell(); }
            while (column < end && !row.ReadOnlyCells[column].IsProtected);
            _screen.ClearRasterGraphicsInViewportRectangle(rowIndex, rowIndex, first, column - 1);
        }
        row.IsDirty = true;
    }
}
