// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private ManagedCharsetState _charsets = new();
    private SavedCursorState? _primarySavedCursor;
    private SavedCursorState? _alternateSavedCursor;
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
        if (SavedCursor is not { } saved)
        {
            _cursorCol = _cursorRow = 0;
            _delayedWrap = _originMode = _currentProtected = false;
            _charsets = new();
            ResetAttributes();
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
    }

    private static SgrColorKind ToSgrKind(TerminalColorIdentity identity) => identity.Kind switch
    {
        TerminalColorKind.Palette => SgrColorKind.Palette,
        TerminalColorKind.Rgb => SgrColorKind.Rgb,
        _ => SgrColorKind.Default,
    };
}
