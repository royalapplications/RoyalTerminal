// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    internal void InstallSnapshot(GhosttySnapshotReadyState ready, TerminalTheme theme, bool retainContinuation)
    {
        InstallSnapshotColors(ready.Terminal, theme);
        InstallSnapshotModes(ready.Terminal.Header);
        InstallSnapshotCursorPolicy(ready.Terminal.Header);
        InstallSnapshotGeometry(ready.Terminal);
        foreach (GhosttySnapshotScreen screen in ready.Screens) InstallSnapshotScreenState(screen.State);
        InstallSnapshotMetadata(ready.Terminal);
        PasswordInput = ready.Terminal.Header.PasswordInput;
        if (!ready.Continuation.IsEmpty) Process(ready.Continuation);
        if (!_continuation.GetBytes().SequenceEqual(ready.Continuation))
            throw new InvalidDataException("Snapshot continuation did not recreate the same parser state.");
        if (!retainContinuation) _continuation.Disable();
    }

    internal GhosttySnapshotHistoryProgress ApplySnapshotHistory(GhosttySnapshotHistoryApplication application,
        in GhosttySnapshotHistoryPage history)
    {
        // Use the live COW screen during output holds, not the frozen published view.
        // Admission policy belongs to the host, not the frozen terminal frame.
        _screen.SnapshotScrollbackQuota = _publishedScreen.SnapshotScrollbackQuota;
        TerminalRowBuffer? rows = _screen.GetSnapshotRows(history.Key);
        long historyRows = rows is null ? long.MaxValue : (long)rows.Count - _screen.ViewportRows;
        int limit = history.Key == 0 ? _publishedScreen.ScrollbackLimit : 0;
        bool fits = historyRows <= (long)limit - history.Page.Grid.Rows &&
            _screen.FitsSnapshotHistoryQuota(history.Key, history.Page);
        GhosttySnapshotHistoryProgress progress = application.Apply(_screen, history, fits);
        if (progress.ContainsPrompt)
        {
            if (history.Key == 0) _primaryPromptPolicy.Seen = true;
            else _alternatePromptPolicy.Seen = true;
        }
        return progress;
    }

    // Assembly primitives for a fresh, unpublished processor whose screen was
    // staged by GhosttySnapshotLiveScreen. They do not replay VT or publish state.
    // Public restoration composes these before publishing its owned result.
    internal void InstallSnapshotGeometry(GhosttySnapshotTerminalState terminal)
    {
        GhosttySnapshotTerminalHeader header = terminal.Header;
        if (_screen.Columns != header.Columns || _screen.ViewportRows != header.Rows ||
            _screen.AlternateBufferActive != (header.ActiveScreenKey == 1))
            throw new InvalidOperationException("Snapshot processor geometry must match its staged screen.");
        int tabCount = 0;
        for (int column = 0; column < header.Columns; column++)
            if (terminal.IsTabStop(column)) tabCount++;
        _tabStops.EnsureCapacity(tabCount);
        _tabStops.Clear();
        for (int column = 0; column < header.Columns; column++)
            if (terminal.IsTabStop(column)) _tabStops.Add(column);
        _tabStopColumns = header.Columns;
        _widthPx = header.PixelWidth;
        _heightPx = header.PixelHeight;
        UpdateReportCellSize(header.Columns, header.Rows, _widthPx, _heightPx);
        _scrollTop = header.ScrollTop;
        _scrollBottom = header.ScrollBottom;
        _scrollLeft = header.ScrollLeft;
        _scrollRight = header.ScrollRight;
        _lastGraphicCodepoint = header.PreviousCodepoint is { } cp ? (int)cp : -1;
        _promptRedraw = (TerminalPromptRedraw)header.ShellRedraw;
        _inAltScreen = header.ActiveScreenKey == 1;
        _kittyStore = _inAltScreen
            ? _alternateKittyStore ??= new ManagedKittyGraphicsStore(_options.KittyGraphicsStorageLimitBytes)
            : _primaryKittyStore;
    }

    internal void InstallSnapshotScreenState(GhosttySnapshotScreenState state)
    {
        TerminalRowBuffer rows = _screen.GetSnapshotRows(state.Key)
            ?? throw new InvalidOperationException("Snapshot screen rows must be staged before cursor state.");
        int row = Math.Min(state.CursorY, _screen.ViewportRows - 1);
        TerminalRow cursorRow = rows[rows.Count - _screen.ViewportRows + row];
        (int x, int y, bool wrap) = state.GetCursorPosition(cursorRow.Columns, _screen.ViewportRows);
        TerminalCell pen = GhosttySnapshotLivePage.DecodeStyle(state.Pen, _theme);
        _screen.SnapshotStyleChanged(state.Key, row, default, state.Pen);
        SavedCursorState? saved = DecodeSnapshotSavedCursor(state.SavedCursor);
        ManagedCharsetState charset = ManagedCharsetState.FromSnapshot(state.Charset);
        SemanticPen semantic = new() { Content = (TerminalSemanticContent)state.SemanticContent, ClearAtEndOfLine = state.SemanticContentClearEol };
        PromptPolicy prompt = new()
        {
            Seen = semantic.Content == TerminalSemanticContent.Prompt || SnapshotRowsContainPrompt(rows),
            Click = state.SemanticClickKind switch
            {
                1 => state.SemanticClickValue == 0 ? TerminalPromptClick.Absolute : TerminalPromptClick.Relative,
                2 => (TerminalPromptClick)(state.SemanticClickValue + (byte)TerminalPromptClick.Line),
                _ => TerminalPromptClick.None,
            },
        };
        if (state.Key == 0)
        {
            _primarySavedCursor = saved;
            _primaryCharsets = charset;
            _primarySemanticPen = semantic;
            _primaryPromptPolicy = prompt;
            _primaryProtectionMode = (CharacterProtectionMode)state.ProtectedMode;
            _primaryHyperlinkImplicitCounter = state.HyperlinkImplicitCounter;
            (_savedMainCursorCol, _savedMainCursorRow, _savedMainDelayedWrap) = (x, y, wrap);
        }
        else
        {
            _alternateSavedCursor = saved;
            _alternateCharsets = charset;
            _alternateSemanticPen = semantic;
            _alternatePromptPolicy = prompt;
            _alternateProtectionMode = (CharacterProtectionMode)state.ProtectedMode;
            _alternateHyperlinkImplicitCounter = state.HyperlinkImplicitCounter;
            (_savedAlternateCursorCol, _savedAlternateCursorRow, _savedAlternateDelayedWrap) = (x, y, wrap);
            _alternateEraseBackground = pen.BackgroundIdentity;
        }
        InstallSnapshotCursorStyle(state);
        InstallSnapshotKittyKeyboard(state);
        int linkToken = state.TryGetHyperlink(out GhosttySnapshotHyperlink link)
            ? _screen.RegisterHyperlink(link.Uri, link.ExplicitId, link.ImplicitId) : 0;
        ref uint counter = ref (state.Key == 0 ? ref _primaryHyperlinkImplicitCounter : ref _alternateHyperlinkImplicitCounter);
        linkToken = _screen.SnapshotHyperlinkChanged(state.Key, row, state.Pen, linkToken, ref counter, restart: true);
        if (state.Key == 0)
        {
            _snapshotPrimaryPen = state.Pen; _snapshotPrimaryProtected = state.Protected; _snapshotPrimaryHyperlink = linkToken;
        }
        else
        {
            _snapshotAlternatePen = state.Pen; _snapshotAlternateProtected = state.Protected; _snapshotAlternateHyperlink = linkToken;
        }
        if ((state.Key == 1) != _inAltScreen) return;
        (_cursorCol, _cursorRow, _delayedWrap) = (x, y, wrap);
        _currentProtected = state.Protected;
        _charsets = charset;
        InstallSnapshotPen(in pen);
        _currentHyperlinkId = linkToken;
    }

    private SavedCursorState? DecodeSnapshotSavedCursor(GhosttySnapshotSavedCursor? source)
    {
        if (source is not { } value) return null;
        value = value.Clamp(_screen.Columns, _screen.ViewportRows);
        TerminalCell pen = GhosttySnapshotLivePage.DecodeStyle(value.Pen, _theme);
        return new(value.X, value.Y, pen.ForegroundIdentity, pen.BackgroundIdentity,
            pen.UnderlineIdentity, pen.HasUnderlineColor, pen.Attributes, pen.UnderlineStyle,
            pen.Decorations, ManagedCharsetState.FromSnapshot(value.Charset), value.Protected,
            value.PendingWrap, value.Origin);
    }

    private void InstallSnapshotPen(in TerminalCell pen)
    {
        _currentFg = pen.Foreground; _currentBg = pen.Background;
        _currentFgKind = ToSgrKind(pen.ForegroundIdentity); _currentBgKind = ToSgrKind(pen.BackgroundIdentity);
        _currentFgPaletteIndex = pen.ForegroundIdentity.Kind == TerminalColorKind.Palette ? (int)pen.ForegroundIdentity.Value : 0;
        _currentBgPaletteIndex = pen.BackgroundIdentity.Kind == TerminalColorKind.Palette ? (int)pen.BackgroundIdentity.Value : 0;
        _currentAttrs = pen.Attributes;
        _currentUnderlineStyle = pen.UnderlineStyle;
        _currentUnderlineIdentity = pen.UnderlineIdentity;
        _currentHasUnderlineColor = pen.HasUnderlineColor;
        _currentUnderlineColor = pen.UnderlineColor;
        _currentDecorations = pen.Decorations;
    }

    private static bool SnapshotRowsContainPrompt(TerminalRowBuffer rows)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            TerminalRow row = rows[index];
            if (row.SemanticPrompt != TerminalSemanticPrompt.None) return true;
            foreach (ref readonly TerminalCell cell in row.ReadOnlyCells)
                if (cell.SemanticContent == TerminalSemanticContent.Prompt) return true;
        }
        return false;
    }

    internal ushort GetSnapshotCharset(int key) => key switch
    {
        0 => (_inAltScreen ? _primaryCharsets : _charsets).Bits,
        1 => (_inAltScreen ? _charsets : _alternateCharsets).Bits,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    internal (uint Width, uint Height, int Top, int Bottom, int Left, int Right, int Previous) SnapshotGeometry
        => (_widthPx, _heightPx, _scrollTop, _scrollBottom, _scrollLeft, RightMargin, _lastGraphicCodepoint);

    internal bool IsSnapshotTabStop(int column) => _tabStops.Contains(column);
}
