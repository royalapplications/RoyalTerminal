// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private ManagedCharsetState _primaryCharsets = new();
    private ManagedCharsetState _alternateCharsets = new();
    // The active register stays direct in the printer hot path. Dormant copies
    // are updated only at screen switches and raw snapshot installation.
    private ManagedCharsetState _charsets = new();
    private SavedCursorState? _primarySavedCursor;
    private SavedCursorState? _alternateSavedCursor;
    // 1049 clears using the dormant alternate pen before copying the entering
    // primary cursor. Retain its logical background across screen switches.
    private TerminalColorIdentity _alternateEraseBackground;

    private void ClearAlternateBeforeCursorCopy()
    {
        uint background = _currentBg;
        SgrColorKind kind = _currentBgKind;
        int paletteIndex = _currentBgPaletteIndex;
        _currentBg = ResolveColorIdentity(_alternateEraseBackground, _screen.DefaultBackground);
        _currentBgKind = ToSgrKind(_alternateEraseBackground);
        _currentBgPaletteIndex = (int)_alternateEraseBackground.Value;
        try
        {
            EraseInDisplay(2);
        }
        finally
        {
            _currentBg = background;
            _currentBgKind = kind;
            _currentBgPaletteIndex = paletteIndex;
        }
    }
    private ref SavedCursorState? SavedCursor => ref (_inAltScreen ? ref _alternateSavedCursor : ref _primarySavedCursor);

    // Hyperlinks deliberately are not saved: Ghostty leaves the current link
    // untouched on DECSC/DECRC. Resolve logical colors against the current theme
    // on restore instead of eagerly recoloring two dormant pens on every theme edit.
    private readonly record struct SavedCursorState(int Column, int Row,
        TerminalColorIdentity Foreground, TerminalColorIdentity Background,
        TerminalColorIdentity Underline, bool HasUnderline, CellAttributes Attributes,
        TerminalUnderlineStyle UnderlineStyle, CellDecorations Decorations,
        ManagedCharsetState Charsets, bool Protected, bool PendingWrap, bool Origin);

    private void SaveCursor()
    {
        SavedCursor = new(_cursorCol, _cursorRow,
            GetColorIdentity(_currentFgKind, _currentFgPaletteIndex, _currentFg),
            CurrentBackgroundIdentity, _currentUnderlineIdentity, _currentHasUnderlineColor,
            _currentAttrs, _currentUnderlineStyle, _currentDecorations,
            _charsets, _currentProtected, _delayedWrap, _originMode);
    }

    private void RestoreCursor()
    {
        using SnapshotCursorStyleScope snapshotCursor = TrackSnapshotCursorMovement();
        int departingRow = _cursorRow;
        GhosttySnapshotStyle previous = _screen.TracksSnapshotMetadata ? CaptureSnapshotPen() : default;
        if (SavedCursor is not { } saved)
        {
            _cursorCol = _cursorRow = 0;
            _delayedWrap = _originMode = _currentProtected = false;
            _charsets = new();
            ResetAttributes();
            _screen.SnapshotStyleChanged(_inAltScreen ? 1 : 0, departingRow, previous, default);
            return;
        }
        _cursorCol = Math.Clamp(saved.Column, 0, _screen.Columns - 1);
        _cursorRow = Math.Clamp(saved.Row, 0, _screen.ViewportRows - 1);
        _delayedWrap = saved.PendingWrap;
        _originMode = saved.Origin;
        _currentProtected = saved.Protected;
        _charsets = saved.Charsets;
        _currentFg = ResolveColorIdentity(saved.Foreground, _screen.DefaultForeground);
        _currentBg = ResolveColorIdentity(saved.Background, _screen.DefaultBackground);
        _currentFgKind = ToSgrKind(saved.Foreground);
        _currentBgKind = ToSgrKind(saved.Background);
        _currentFgPaletteIndex = saved.Foreground.Kind == TerminalColorKind.Palette ? (int)saved.Foreground.Value : 0;
        _currentBgPaletteIndex = saved.Background.Kind == TerminalColorKind.Palette ? (int)saved.Background.Value : 0;
        _currentAttrs = saved.Attributes;
        _currentUnderlineStyle = saved.UnderlineStyle;
        _currentUnderlineIdentity = saved.Underline;
        _currentHasUnderlineColor = saved.HasUnderline;
        _currentUnderlineColor = saved.HasUnderline ? ResolveColorIdentity(saved.Underline, _currentFg) : 0;
        _currentDecorations = saved.Decorations;
        // Ghostty restoreCursor applies the saved style before cursorAbsolute.
        // Accounting at only the destination would miss growth at the old page.
        if (_screen.TracksSnapshotMetadata)
            _screen.SnapshotStyleChanged(_inAltScreen ? 1 : 0, departingRow, previous, CaptureSnapshotPen());
    }

    // Ghostty Screen.resize temporarily tracks the saved cursor's actual cell,
    // not its next-print position. It does not track DECSC across ordinary output.
    private TerminalScreenAnchor? TrackSavedCursorForResize()
    {
        if (SavedCursor is not { } saved ||
            (uint)saved.Column >= (uint)_screen.Columns ||
            (uint)saved.Row >= (uint)_screen.ViewportRows)
        {
            return null;
        }

        return _screen.CreateAnchor(
            _screen.TotalRows - _screen.ViewportRows + saved.Row, saved.Column);
    }

    private void RemapSavedCursorAfterResize(TerminalScreenAnchor anchor)
    {
        if (SavedCursor is not { } saved) return;
        int activeTop = _screen.TotalRows - _screen.ViewportRows;
        if (!_screen.TryResolveAnchor(anchor, out TerminalGridPosition position) ||
            position.Row < activeTop || position.Row >= _screen.TotalRows)
        {
            SavedCursor = saved with { Column = 0, Row = 0, PendingWrap = false };
            return;
        }

        bool advance = saved.PendingWrap && position.Column != _screen.Columns - 1;
        SavedCursor = saved with
        {
            Column = position.Column + (advance ? 1 : 0),
            Row = position.Row - activeTop,
            PendingWrap = saved.PendingWrap && !advance,
        };
    }

    private static SgrColorKind ToSgrKind(TerminalColorIdentity identity) => identity.Kind switch
    {
        TerminalColorKind.Palette => SgrColorKind.Palette,
        TerminalColorKind.Rgb => SgrColorKind.Rgb,
        _ => SgrColorKind.Default,
    };
}
