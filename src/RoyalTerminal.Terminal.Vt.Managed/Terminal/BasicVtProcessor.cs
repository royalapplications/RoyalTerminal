// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal — VT sequence processor for standalone/demo mode.
// Processes raw terminal output bytes into the TerminalScreen cell grid.
// Supports: printable characters, cursor movement, SGR colors (256 + truecolor),
// scrolling, scroll regions (DECSTBM), alternate screen buffer, DEC private modes,
// DEC line-drawing character set, erase, insert/delete lines & characters, and tabs.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Theming;
using RoyalTerminal.Unicode;
using System.Globalization;
using System.Text;

namespace RoyalTerminal.Terminal;

/// <summary>
/// VT100/xterm escape sequence processor that writes terminal data directly
/// into a <see cref="TerminalScreen"/> cell grid.
///
/// This is used as the fallback VT processor when the Ghostty native terminal
/// (via <see cref="GhosttyVtProcessor"/>) is not available. It handles enough
/// of the VT protocol to render typical shell output and full-screen TUI
/// applications such as Midnight Commander, htop, vim, etc.
/// </summary>
public sealed class BasicVtProcessor : IVtProcessor, ITerminalThemeSink, IKittyKeyboardStateSource
{
    private const int MaxDcsBufferBytes = 4096;
    private const int KittyKeyboardFlagMask = 0x1F;
    private const int KittyKeyboardMaxStackDepth = 32;

    private readonly TerminalScreen _screen;
    private int _cursorCol;
    private int _cursorRow;
    private uint _currentFg;
    private uint _currentBg;
    private CellAttributes _currentAttrs;
    private TerminalUnderlineStyle _currentUnderlineStyle;
    private CellDecorations _currentDecorations;
    private TerminalTheme _theme;

    // Parser state machine
    private ParserState _state = ParserState.Ground;
    private readonly List<int> _params = [];
    private int _currentParam;
    private bool _hasParam;
    private char _intermediateChar;
    private readonly List<byte> _oscBuffer = [];
    private readonly List<byte> _dcsBuffer = [];
    private bool _isDiscardingDcsPayload;

    // Saved cursor state (for DECSC/DECRC — ESC 7 / ESC 8)
    private int _savedCursorCol;
    private int _savedCursorRow;
    private uint _savedFg;
    private uint _savedBg;
    private CellAttributes _savedAttrs;
    private TerminalUnderlineStyle _savedUnderlineStyle;
    private CellDecorations _savedDecorations;
    private bool _savedUseLineDrawing;

    // Scroll region (DECSTBM)
    private int _scrollTop;    // 0-based inclusive
    private int _scrollBottom; // 0-based inclusive (ViewportRows - 1 at init)

    // Alternate screen buffer
    private TerminalRow[]? _savedMainBuffer;
    private int _savedMainCursorCol;
    private int _savedMainCursorRow;
    private bool _inAltScreen;

    // DEC line-drawing character set
    private bool _useLineDrawing; // true when active charset is DEC Special Graphics
    private bool _g0IsLineDrawing;
    private bool _g1IsLineDrawing;
    private bool _shiftOut; // SO/SI for G1/G0 switching

    // DEC private modes
    private bool _autoWrap = true;     // DECAWM (mode 7)
    private bool _cursorVisible = true; // DECTCEM (mode 25)
    private bool _originMode;          // DECOM (mode 6)
    private bool _applicationCursorKeys; // DECCKM (mode 1)
    private bool _applicationKeypad;   // DECKPAM/DECKPNM
    private bool _bracketedPaste;      // Bracketed paste mode (mode 2004)
    private bool _insertMode;          // IRM (ANSI mode 4)
    private bool _lineFeedNewLineMode; // LNM (ANSI mode 20)
    private int _cursorStyle = 1;      // DECSCUSR (CSI Ps SP q), default blinking block
    private int _widthPx;
    private int _heightPx;
    private int _kittyKeyboardFlagsMain;
    private int _kittyKeyboardFlagsAlt;
    private readonly List<int> _kittyKeyboardStackMain = [];
    private readonly List<int> _kittyKeyboardStackAlt = [];

    // Tab stops
    private readonly HashSet<int> _tabStops = [];

    // UTF-8 multi-byte decoding state
    private int _utf8Codepoint;
    private int _utf8Remaining;
    private int _lastGraphicCodepoint;
    private byte[]? _enquiryResponse;

    private enum ParserState
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        OscString,
        OscEscape,
        DcsString,
        DcsEscape,
    }

    /// <summary>Current cursor column.</summary>
    public int CursorCol => _cursorCol;

    /// <summary>Current cursor row.</summary>
    public int CursorRow => _cursorRow;

    /// <summary>Whether cursor should be visible.</summary>
    public bool CursorVisible => _cursorVisible;

    /// <summary>Whether application cursor key mode is active.</summary>
    public bool ApplicationCursorKeys => _applicationCursorKeys;

    /// <summary>Whether application keypad mode is active.</summary>
    public bool ApplicationKeypad => _applicationKeypad;

    /// <summary>Whether the alternate screen buffer is active.</summary>
    public bool AlternateScreen => _inAltScreen;

    /// <summary>Whether bracketed paste mode is active.</summary>
    public bool BracketedPaste => _bracketedPaste;

    /// <inheritdoc />
    public int KittyKeyboardFlags => _inAltScreen ? _kittyKeyboardFlagsAlt : _kittyKeyboardFlagsMain;

    /// <inheritdoc />
    public TerminalModeState ModeState => new(
        CursorVisible,
        ApplicationCursorKeys,
        ApplicationKeypad,
        AlternateScreen,
        BracketedPaste);

    /// <inheritdoc />
    public event EventHandler<TerminalModeState>? ModeChanged;

    /// <inheritdoc />
    public Action<byte[]>? ResponseCallback { get; set; }

    /// <inheritdoc />
    public Action? BellCallback { get; set; }

    /// <inheritdoc />
    public Action<string>? TitleCallback { get; set; }

    /// <summary>
    /// Optional ENQ (0x05) answerback payload. When null or empty, ENQ is acknowledged
    /// but no response is emitted (secure default).
    /// </summary>
    public byte[]? EnquiryResponse
    {
        get => _enquiryResponse?.ToArray();
        set => _enquiryResponse = value is null ? null : value.ToArray();
    }

    public BasicVtProcessor(TerminalScreen screen)
    {
        _screen = screen;
        _theme = screen.Theme;
        _currentFg = screen.DefaultForeground;
        _currentBg = screen.DefaultBackground;
        _scrollBottom = screen.ViewportRows - 1;
        InitTabStops();
    }

    private void InitTabStops()
    {
        _tabStops.Clear();
        for (var i = 0; i < _screen.Columns; i += 8)
            _tabStops.Add(i);
    }

    /// <summary>
    /// Processes a span of raw terminal output bytes.
    /// </summary>
    public void Process(ReadOnlySpan<byte> data)
    {
        TerminalModeState before = ModeState;

        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];

            switch (_state)
            {
                case ParserState.Ground:
                    ProcessGround(b);
                    break;
                case ParserState.Escape:
                    ProcessEscape(b);
                    break;
                case ParserState.EscapeIntermediate:
                    ProcessEscapeIntermediate(b);
                    break;
                case ParserState.CsiEntry:
                    ProcessCsiEntry(b);
                    break;
                case ParserState.CsiParam:
                    ProcessCsiParam(b);
                    break;
                case ParserState.CsiIntermediate:
                    ProcessCsiIntermediate(b);
                    break;
                case ParserState.OscString:
                    ProcessOscString(b);
                    break;
                case ParserState.OscEscape:
                    ProcessOscEscape(b);
                    break;
                case ParserState.DcsString:
                    ProcessDcsString(b);
                    break;
                case ParserState.DcsEscape:
                    ProcessDcsEscape(b);
                    break;
            }
        }

        RaiseModeChangedIfNeeded(before);
    }

    private void EnterCsiState()
    {
        _state = ParserState.CsiEntry;
        _params.Clear();
        _currentParam = 0;
        _hasParam = false;
        _intermediateChar = '\0';
    }

    private void EnterOscState()
    {
        _state = ParserState.OscString;
        _oscBuffer.Clear();
    }

    private void EnterDcsState()
    {
        _state = ParserState.DcsString;
        _dcsBuffer.Clear();
        _isDiscardingDcsPayload = false;
    }

    #region Ground State

    private void ProcessGround(byte b)
    {
        // Handle UTF-8 continuation bytes first
        if (_utf8Remaining > 0)
        {
            if ((b & 0xC0) == 0x80) // Valid continuation byte
            {
                _utf8Codepoint = (_utf8Codepoint << 6) | (b & 0x3F);
                _utf8Remaining--;
                if (_utf8Remaining == 0)
                    PutChar(_utf8Codepoint);
            }
            else
            {
                // Invalid continuation — reset and process this byte normally
                _utf8Remaining = 0;
                ProcessGround(b);
            }
            return;
        }

        switch (b)
        {
            case 0x1B: // ESC
                _state = ParserState.Escape;
                break;

            case 0x9B: // C1 CSI
                EnterCsiState();
                break;

            case 0x9D: // C1 OSC
                EnterOscState();
                break;

            case 0x90: // C1 DCS
                EnterDcsState();
                break;

            case 0x9C: // C1 ST
                _state = ParserState.Ground;
                break;

            case (byte)'\n': // LF
            case 0x0B:       // VT
            case 0x0C:       // FF
                if (_lineFeedNewLineMode)
                {
                    _cursorCol = 0;
                }
                LineFeed();
                break;

            case (byte)'\r': // CR
                _cursorCol = 0;
                break;

            case 0x08: // BS — Backspace
                if (_cursorCol > 0) _cursorCol--;
                break;

            case (byte)'\t': // HT — Horizontal Tab
                TabForward();
                break;

            case 0x05: // ENQ
                if (_enquiryResponse is { Length: > 0 } enquiryResponse)
                {
                    ResponseCallback?.Invoke(enquiryResponse.ToArray());
                }
                break;

            case 0x07: // BEL
                BellCallback?.Invoke();
                break;

            case 0x0E: // SO — Shift Out (activate G1)
                _shiftOut = true;
                _useLineDrawing = _g1IsLineDrawing;
                break;

            case 0x0F: // SI — Shift In (activate G0)
                _shiftOut = false;
                _useLineDrawing = _g0IsLineDrawing;
                break;

            default:
                if (b < 0x20)
                {
                    // Other C0 control characters — ignore
                }
                else if (b < 0x80)
                {
                    // ASCII printable — check line-drawing mapping
                    if (_useLineDrawing)
                        PutChar(MapLineDrawing((char)b));
                    else
                        PutChar(b);
                }
                else if ((b & 0xE0) == 0xC0)
                {
                    _utf8Codepoint = b & 0x1F;
                    _utf8Remaining = 1;
                }
                else if ((b & 0xF0) == 0xE0)
                {
                    _utf8Codepoint = b & 0x0F;
                    _utf8Remaining = 2;
                }
                else if ((b & 0xF8) == 0xF0)
                {
                    _utf8Codepoint = b & 0x07;
                    _utf8Remaining = 3;
                }
                else
                {
                }
                break;
        }
    }

    private void PutChar(int codepoint)
    {
        ClampCursor();

        if (!Rune.IsValid(codepoint))
        {
            return;
        }

        // If we're sitting at wrapped EOL, a combining/emoji continuation should
        // still be allowed to merge into the previous cell before forcing a wrap.
        if (_cursorCol >= _screen.Columns && TryAppendToPreviousCellGrapheme(codepoint))
        {
            return;
        }

        if (WrapAtEndOfLineIfNeeded())
        {
            ClampCursor();
        }

        if (TryAppendToPreviousCellGrapheme(codepoint))
        {
            return;
        }

        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;
        if (_cursorCol < 0 || _cursorCol >= _screen.Columns) return;

        TerminalRow row = _screen.GetViewportRow(_cursorRow);
        if (_cursorCol >= row.Columns) return;

        int width = TerminalCellWidthCalculator.GetCellWidth(codepoint);
        width = width <= 1 ? 1 : 2;

        if (width == 2 && _cursorCol == _screen.Columns - 1)
        {
            if (_autoWrap)
            {
                _cursorCol = 0;
                LineFeed();
                ClampCursor();
            }
            else
            {
                width = 1;
            }

            if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;
            if (_cursorCol < 0 || _cursorCol >= _screen.Columns) return;
            row = _screen.GetViewportRow(_cursorRow);
            if (_cursorCol >= row.Columns) return;
        }

        if (_insertMode)
        {
            InsertCharacters(width);
            row = _screen.GetViewportRow(_cursorRow);
            if (_cursorCol < 0 || _cursorCol >= row.Columns)
            {
                return;
            }
        }

        ClearCellAndWideArtifacts(row, _cursorCol);
        if (width == 2 && _cursorCol + 1 < row.Columns)
        {
            ClearCellAndWideArtifacts(row, _cursorCol + 1);
        }

        ref var cell = ref row[_cursorCol];
        cell.Codepoint = codepoint;
        cell.Grapheme = null;
        cell.Foreground = _currentFg;
        cell.Background = _currentBg;
        cell.Attributes = _currentAttrs;
        cell.UnderlineStyle = _currentUnderlineStyle;
        cell.Decorations = _currentDecorations;
        cell.Width = (byte)width;

        if (width == 2 && _cursorCol + 1 < row.Columns)
        {
            ref TerminalCell spacer = ref row[_cursorCol + 1];
            spacer.Codepoint = 0;
            spacer.Grapheme = null;
            spacer.Foreground = _currentFg;
            spacer.Background = _currentBg;
            spacer.Attributes = _currentAttrs;
            spacer.UnderlineStyle = _currentUnderlineStyle;
            spacer.Decorations = _currentDecorations;
            spacer.Width = 0;
        }

        row.IsDirty = true;

        _cursorCol += width;
        _lastGraphicCodepoint = codepoint;
    }

    private bool TryAppendToPreviousCellGrapheme(int codepoint)
    {
        if (!Rune.IsValid(codepoint))
        {
            return false;
        }

        int targetRowIndex = _cursorRow;
        int targetColIndex = _cursorCol - 1;

        if (targetColIndex < 0)
        {
            targetRowIndex--;
            targetColIndex = _screen.Columns - 1;
        }

        if (targetRowIndex < 0 || targetRowIndex >= _screen.ViewportRows)
        {
            return false;
        }

        TerminalRow targetRow = _screen.GetViewportRow(targetRowIndex);
        while (targetColIndex >= 0 && targetRow[targetColIndex].Width == 0)
        {
            targetColIndex--;
        }

        if (targetColIndex < 0 || targetColIndex >= targetRow.Columns)
        {
            return false;
        }

        ref TerminalCell targetCell = ref targetRow[targetColIndex];
        if (!targetCell.HasContent)
        {
            return false;
        }

        string currentText;
        if (string.IsNullOrEmpty(targetCell.Grapheme))
        {
            if (!Rune.IsValid(targetCell.Codepoint))
            {
                return false;
            }

            currentText = char.ConvertFromUtf32(targetCell.Codepoint);
        }
        else
        {
            currentText = targetCell.Grapheme;
        }

        string nextText = string.Concat(currentText, char.ConvertFromUtf32(codepoint));
        if (!TerminalCellWidthCalculator.IsSingleGrapheme(nextText))
        {
            return false;
        }

        int oldWidth = targetCell.Width <= 0 ? 1 : targetCell.Width;
        int newWidth = TerminalCellWidthCalculator.GetCellWidth(nextText);
        newWidth = newWidth <= 1 ? 1 : 2;

        if (newWidth == 2 && targetColIndex >= targetRow.Columns - 1)
        {
            return false;
        }

        targetCell.Codepoint = targetCell.Codepoint != 0
            ? targetCell.Codepoint
            : codepoint;
        targetCell.Grapheme = nextText;
        targetCell.Width = (byte)newWidth;

        if (newWidth == 2)
        {
            ref TerminalCell spacer = ref targetRow[targetColIndex + 1];
            spacer.Codepoint = 0;
            spacer.Grapheme = null;
            spacer.Foreground = targetCell.Foreground;
            spacer.Background = targetCell.Background;
            spacer.Attributes = targetCell.Attributes;
            spacer.UnderlineStyle = targetCell.UnderlineStyle;
            spacer.Decorations = targetCell.Decorations;
            spacer.Width = 0;
        }

        if (oldWidth == 2 && newWidth == 1 && targetColIndex + 1 < targetRow.Columns)
        {
            targetRow[targetColIndex + 1] = TerminalCell.Empty(targetCell.Foreground, targetCell.Background);
        }

        if (targetRowIndex == _cursorRow && targetColIndex + oldWidth == _cursorCol)
        {
            _cursorCol = targetColIndex + newWidth;
        }

        targetRow.IsDirty = true;
        return true;
    }

    private bool WrapAtEndOfLineIfNeeded()
    {
        if (_cursorCol < _screen.Columns)
        {
            return false;
        }

        if (_autoWrap)
        {
            _cursorCol = 0;
            LineFeed();
            return true;
        }

        _cursorCol = _screen.Columns - 1;
        return false;
    }

    private void ClearCellAndWideArtifacts(TerminalRow row, int column)
    {
        if (column < 0 || column >= row.Columns)
        {
            return;
        }

        TerminalCell existing = row[column];
        if (existing.Width == 0 && column > 0)
        {
            ref TerminalCell left = ref row[column - 1];
            if (left.Width == 2)
            {
                left = TerminalCell.Empty(_currentFg, _currentBg);
            }
        }
        else if (existing.Width == 2 && column + 1 < row.Columns)
        {
            ref TerminalCell right = ref row[column + 1];
            if (right.Width == 0)
            {
                right = TerminalCell.Empty(_currentFg, _currentBg);
            }
        }

        row[column] = TerminalCell.Empty(_currentFg, _currentBg);
    }

    private void ClampCursor()
    {
        if (_cursorCol < 0) _cursorCol = 0;
        if (_cursorRow < 0) _cursorRow = 0;
        if (_cursorRow >= _screen.ViewportRows)
        {
            while (_cursorRow >= _screen.ViewportRows)
            {
                ScrollUpInRegion();
                _cursorRow = _screen.ViewportRows - 1;
            }
        }
    }

    private void LineFeed()
    {
        if (_cursorRow == _scrollBottom)
        {
            // At bottom of scroll region — scroll the region up
            ScrollUpInRegion();
        }
        else if (_cursorRow < _screen.ViewportRows - 1)
        {
            _cursorRow++;
        }
        else
        {
            // Below scroll region — scroll the whole screen
            ScrollUpInRegion();
        }
    }

    private void ReverseIndex()
    {
        if (_cursorRow == _scrollTop)
        {
            // At top of scroll region — scroll region down
            ScrollDownInRegion();
        }
        else if (_cursorRow > 0)
        {
            _cursorRow--;
        }
    }

    private void TabForward()
    {
        for (var c = _cursorCol + 1; c < _screen.Columns; c++)
        {
            if (_tabStops.Contains(c))
            {
                _cursorCol = c;
                return;
            }
        }
        _cursorCol = _screen.Columns - 1;
    }

    #endregion

    #region Scroll Region Operations

    private void ScrollUpInRegion()
    {
        if (_scrollTop == 0 && _scrollBottom == _screen.ViewportRows - 1 && !_inAltScreen)
        {
            // Whole-screen scroll — push to scrollback
            _screen.AddRow();
            _screen.InvalidateAll();
        }
        else
        {
            // Scroll within region: shift rows up, insert blank at bottom of region
            for (var r = _scrollTop; r < _scrollBottom && r < _screen.ViewportRows - 1; r++)
            {
                var src = _screen.GetViewportRow(r + 1);
                var dst = _screen.GetViewportRow(r);
                CopyRow(src, dst);
                dst.IsDirty = true;
            }
            if (_scrollBottom < _screen.ViewportRows)
            {
                _screen.GetViewportRow(_scrollBottom).Clear(_currentFg, _currentBg);
            }
            _screen.InvalidateAll();
        }
    }

    private void ScrollDownInRegion()
    {
        // Shift rows down within the scroll region, insert blank at top of region
        for (var r = _scrollBottom; r > _scrollTop && r > 0; r--)
        {
            var src = _screen.GetViewportRow(r - 1);
            var dst = _screen.GetViewportRow(r);
            CopyRow(src, dst);
            dst.IsDirty = true;
        }
        if (_scrollTop < _screen.ViewportRows)
        {
            _screen.GetViewportRow(_scrollTop).Clear(_currentFg, _currentBg);
        }
        _screen.InvalidateAll();
    }

    private void CopyRow(TerminalRow src, TerminalRow dst)
    {
        var count = Math.Min(src.Columns, dst.Columns);
        src.ReadOnlyCells[..count].CopyTo(dst.Cells);
        NormalizeRowWideCells(dst);
    }

    #endregion

    #region Escape Sequences

    private void ProcessEscape(byte b)
    {
        switch (b)
        {
            case (byte)'[': // CSI
                EnterCsiState();
                break;

            case (byte)']': // OSC
                EnterOscState();
                break;

            case (byte)'P': // DCS
                EnterDcsState();
                break;

            case (byte)'(': // Designate G0 charset
                _intermediateChar = '(';
                _state = ParserState.EscapeIntermediate;
                break;

            case (byte)')': // Designate G1 charset
                _intermediateChar = ')';
                _state = ParserState.EscapeIntermediate;
                break;

            case (byte)'*': // Designate G2 charset
            case (byte)'+': // Designate G3 charset
                _state = ParserState.EscapeIntermediate;
                break;

            case (byte)'7': // DECSC — Save cursor
                SaveCursor();
                _state = ParserState.Ground;
                break;

            case (byte)'8': // DECRC — Restore cursor
                RestoreCursor();
                _state = ParserState.Ground;
                break;

            case (byte)'D': // IND — Index (move cursor down, scroll if at bottom)
                LineFeed();
                _state = ParserState.Ground;
                break;

            case (byte)'E': // NEL — Next line
                _cursorCol = 0;
                LineFeed();
                _state = ParserState.Ground;
                break;

            case (byte)'M': // RI — Reverse index
                ReverseIndex();
                _state = ParserState.Ground;
                break;

            case (byte)'H': // HTS — Horizontal Tab Set
                _tabStops.Add(_cursorCol);
                _state = ParserState.Ground;
                break;

            case (byte)'c': // RIS — Full reset
                ResetInternal(raiseModeChanged: false);
                _state = ParserState.Ground;
                break;

            case (byte)'=': // DECKPAM — Application keypad
                _applicationKeypad = true;
                _state = ParserState.Ground;
                break;
            case (byte)'>': // DECKPNM — Normal keypad
                _applicationKeypad = false;
                _state = ParserState.Ground;
                break;

            case (byte)'\\': // ST — String Terminator (end of OSC/DCS/etc.)
                _state = ParserState.Ground;
                break;

            default:
                _state = ParserState.Ground;
                break;
        }
    }

    private void ProcessEscapeIntermediate(byte b)
    {
        // Designate character set
        if (_intermediateChar == '(')
        {
            // G0
            _g0IsLineDrawing = (b == (byte)'0');
            if (!_shiftOut)
                _useLineDrawing = _g0IsLineDrawing;
        }
        else if (_intermediateChar == ')')
        {
            // G1
            _g1IsLineDrawing = (b == (byte)'0');
            if (_shiftOut)
                _useLineDrawing = _g1IsLineDrawing;
        }

        _state = ParserState.Ground;
    }

    #endregion

    #region CSI Processing

    private void ProcessCsiEntry(byte b)
    {
        if (b is >= (byte)'0' and <= (byte)'9')
        {
            _currentParam = b - '0';
            _hasParam = true;
            _state = ParserState.CsiParam;
        }
        else if (b == (byte)';')
        {
            _params.Add(0);
            _state = ParserState.CsiParam;
        }
        else if (b == (byte)'?' || b == (byte)'>' || b == (byte)'!' || b == (byte)'=' || b == (byte)'<')
        {
            _intermediateChar = (char)b;
            _state = ParserState.CsiParam;
        }
        else if (b is >= 0x40 and <= 0x7E)
        {
            ExecuteCsi((char)b);
        }
        else
        {
            _state = ParserState.Ground;
        }
    }

    private void ProcessCsiParam(byte b)
    {
        if (b is >= (byte)'0' and <= (byte)'9')
        {
            _currentParam = _currentParam * 10 + (b - '0');
            _hasParam = true;
        }
        else if (b == (byte)';')
        {
            _params.Add(_hasParam ? _currentParam : 0);
            _currentParam = 0;
            _hasParam = false;
        }
        else if (b is >= 0x20 and <= 0x2F)
        {
            if (_hasParam)
            {
                _params.Add(_currentParam);
                _currentParam = 0;
                _hasParam = false;
            }
            _intermediateChar = (char)b;
            _state = ParserState.CsiIntermediate;
        }
        else if (b is >= 0x40 and <= 0x7E)
        {
            if (_hasParam)
                _params.Add(_currentParam);
            ExecuteCsi((char)b);
        }
        else
        {
            _state = ParserState.Ground;
        }
    }

    private void ProcessCsiIntermediate(byte b)
    {
        if (b is >= 0x40 and <= 0x7E)
        {
            ExecuteCsi((char)b);
        }
        else if (b is >= 0x20 and <= 0x2F)
        {
            // More intermediates — ignore
        }
        else
        {
            _state = ParserState.Ground;
        }
    }

    private void ProcessOscString(byte b)
    {
        if (b == 0x07) // BEL terminator
        {
            HandleOscString();
            _state = ParserState.Ground;
            return;
        }

        if (b == 0x9C) // 8-bit ST
        {
            HandleOscString();
            _state = ParserState.Ground;
            return;
        }

        if (b == 0x1B) // Potential ST (ESC \)
        {
            _state = ParserState.OscEscape;
            return;
        }

        _oscBuffer.Add(b);
    }

    private void ProcessOscEscape(byte b)
    {
        if (b == (byte)'\\')
        {
            HandleOscString();
            _state = ParserState.Ground;
            return;
        }

        // False alarm: preserve ESC as payload and continue OSC parsing.
        _oscBuffer.Add(0x1B);

        if (b == 0x1B)
        {
            _state = ParserState.OscEscape;
            return;
        }

        if (b == 0x07)
        {
            HandleOscString();
            _state = ParserState.Ground;
            return;
        }

        if (b == 0x9C)
        {
            HandleOscString();
            _state = ParserState.Ground;
            return;
        }

        _oscBuffer.Add(b);
        _state = ParserState.OscString;
    }

    private void HandleOscString()
    {
        if (_oscBuffer.Count == 0)
        {
            return;
        }

        string oscPayload = Encoding.UTF8.GetString(_oscBuffer.ToArray());
        _oscBuffer.Clear();

        int separator = oscPayload.IndexOf(';');
        if (separator < 0)
        {
            return;
        }

        ReadOnlySpan<char> selector = oscPayload.AsSpan(0, separator);
        string value = separator + 1 < oscPayload.Length
            ? oscPayload[(separator + 1)..]
            : string.Empty;

        if (!int.TryParse(selector, out int selectorCode))
        {
            return;
        }

        switch (selectorCode)
        {
            case 0:
            case 1:
            case 2:
                TitleCallback?.Invoke(value);
                break;

            case 4:
                HandleOscPalette(value);
                break;

            case 10:
                HandleOscDynamicColor(selectorCode, value);
                break;

            case 11:
                HandleOscDynamicColor(selectorCode, value);
                break;

            case 12:
                HandleOscDynamicColor(selectorCode, value);
                break;
        }
    }

    private void HandleOscPalette(string value)
    {
        string[] parts = value.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return;
        }

        TerminalTheme nextTheme = _theme;
        bool changed = false;
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (!int.TryParse(parts[i], out int colorIndex))
            {
                continue;
            }

            colorIndex = Math.Clamp(colorIndex, 0, 255);
            string colorSpec = parts[i + 1];
            if (colorSpec == "?")
            {
                uint color = PaletteColor(colorIndex);
                string rgb = FormatRgbColor(color);
                string response = $"\x1b]4;{colorIndex};{rgb}\x1b\\";
                ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
                continue;
            }

            if (!TryParseOscColorSpec(colorSpec, out uint parsedColor))
            {
                continue;
            }

            nextTheme = nextTheme.WithPaletteColor(colorIndex, parsedColor, explicitOverride: true);
            changed = true;
        }

        if (changed)
        {
            ApplyTheme(nextTheme);
        }
    }

    private void HandleOscDynamicColor(int selectorCode, string value)
    {
        if (value == "?")
        {
            uint queryColor = selectorCode switch
            {
                10 => _screen.DefaultForeground,
                11 => _screen.DefaultBackground,
                12 => _theme.CursorColor,
                _ => _screen.DefaultForeground,
            };
            SendOscColorResponse(selectorCode.ToString(), queryColor);
            return;
        }

        if (!TryParseOscColorSpec(value, out uint parsedColor))
        {
            return;
        }

        TerminalTheme nextTheme = selectorCode switch
        {
            10 => _theme.WithDefaultForeground(parsedColor),
            11 => _theme.WithDefaultBackground(parsedColor),
            12 => _theme.WithCursorColor(parsedColor),
            _ => _theme,
        };
        ApplyTheme(nextTheme);
    }

    private void SendOscColorResponse(string selector, uint argbColor)
    {
        string rgb = FormatRgbColor(argbColor);
        string response = $"\x1b]{selector};{rgb}\x1b\\";
        ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
    }

    private string FormatRgbColor(uint argbColor)
    {
        int red = (int)((argbColor >> 16) & 0xFF);
        int green = (int)((argbColor >> 8) & 0xFF);
        int blue = (int)(argbColor & 0xFF);

        return _theme.OscColorReportFormat == TerminalOscColorReportFormat.Bit8
            ? $"rgb:{red:X2}/{green:X2}/{blue:X2}"
            : $"rgb:{red * 0x101:X4}/{green * 0x101:X4}/{blue * 0x101:X4}";
    }

    private void ProcessDcsString(byte b)
    {
        if (b == 0x1B)
        {
            _state = ParserState.DcsEscape;
            return;
        }

        if (b == 0x9C) // 8-bit ST
        {
            HandleDcsString();
            _state = ParserState.Ground;
            return;
        }

        AppendDcsByteOrDiscard(b);
    }

    private void ProcessDcsEscape(byte b)
    {
        if (b == (byte)'\\')
        {
            HandleDcsString();
            _state = ParserState.Ground;
            return;
        }

        // False alarm: preserve ESC as payload and continue DCS parsing.
        AppendDcsByteOrDiscard(0x1B);

        if (b == 0x1B)
        {
            _state = ParserState.DcsEscape;
            return;
        }

        if (b == 0x9C)
        {
            HandleDcsString();
            _state = ParserState.Ground;
            return;
        }

        AppendDcsByteOrDiscard(b);
        _state = ParserState.DcsString;
    }

    private void HandleDcsString()
    {
        if (_isDiscardingDcsPayload)
        {
            _dcsBuffer.Clear();
            _isDiscardingDcsPayload = false;
            return;
        }

        if (_dcsBuffer.Count == 0)
        {
            return;
        }

        string payload = Encoding.ASCII.GetString(_dcsBuffer.ToArray());
        _dcsBuffer.Clear();

        // DCS $ q Pt ST — DECRQSS request.
        if (payload.StartsWith("$q", StringComparison.Ordinal))
        {
            string request = payload.Length > 2 ? payload[2..] : string.Empty;
            HandleDecRequestStatusString(request);
        }
    }

    private void AppendDcsByteOrDiscard(byte b)
    {
        if (_isDiscardingDcsPayload)
        {
            return;
        }

        if (_dcsBuffer.Count >= MaxDcsBufferBytes)
        {
            _dcsBuffer.Clear();
            _isDiscardingDcsPayload = true;
            return;
        }

        _dcsBuffer.Add(b);
    }

    private void HandleDecRequestStatusString(string request)
    {
        string? responsePayload = request switch
        {
            "m" => $"{BuildCurrentSgrState()}m",
            "r" => $"{_scrollTop + 1};{_scrollBottom + 1}r",
            " q" => $"{_cursorStyle} q",
            _ => null,
        };

        if (responsePayload is null)
        {
            // Unsupported request.
            ResponseCallback?.Invoke("\x1bP0$r\x1b\\"u8.ToArray());
            return;
        }

        string response = $"\x1bP1$r{responsePayload}\x1b\\";
        ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
    }

    private string BuildCurrentSgrState()
    {
        List<int> parameters = [];

        if ((_currentAttrs & CellAttributes.Bold) != 0) parameters.Add(1);
        if ((_currentAttrs & CellAttributes.Dim) != 0) parameters.Add(2);
        if ((_currentAttrs & CellAttributes.Italic) != 0) parameters.Add(3);
        if (_currentUnderlineStyle == TerminalUnderlineStyle.Double)
        {
            parameters.Add(21);
        }
        else if (_currentUnderlineStyle != TerminalUnderlineStyle.None ||
                 (_currentAttrs & CellAttributes.Underline) != 0)
        {
            parameters.Add(4);
        }
        if ((_currentAttrs & CellAttributes.Blink) != 0) parameters.Add(5);
        if ((_currentAttrs & CellAttributes.Inverse) != 0) parameters.Add(7);
        if ((_currentAttrs & CellAttributes.Hidden) != 0) parameters.Add(8);
        if ((_currentAttrs & CellAttributes.Strikethrough) != 0) parameters.Add(9);
        if ((_currentDecorations & CellDecorations.Overline) != 0) parameters.Add(53);

        if (_currentFg != _screen.DefaultForeground)
        {
            parameters.Add(38);
            parameters.Add(2);
            parameters.Add((int)((_currentFg >> 16) & 0xFF));
            parameters.Add((int)((_currentFg >> 8) & 0xFF));
            parameters.Add((int)(_currentFg & 0xFF));
        }

        if (_currentBg != _screen.DefaultBackground)
        {
            parameters.Add(48);
            parameters.Add(2);
            parameters.Add((int)((_currentBg >> 16) & 0xFF));
            parameters.Add((int)((_currentBg >> 8) & 0xFF));
            parameters.Add((int)(_currentBg & 0xFF));
        }

        if (parameters.Count == 0)
        {
            return "0";
        }

        return string.Join(';', parameters);
    }

    private void ExecuteCsi(char finalByte)
    {
        _state = ParserState.Ground;

        var p0 = _params.Count > 0 ? _params[0] : 0;
        var p1 = _params.Count > 1 ? _params[1] : 0;

        // DEC private modes: CSI ? ... h/l
        if (_intermediateChar == '?')
        {
            if (finalByte == 'u')
            {
                HandleKittyKeyboardQuery();
                return;
            }

            var set = finalByte == 'h';
            if (finalByte is 'h' or 'l')
            {
                foreach (var p in _params)
                    HandleDecMode(p, set);
            }
            return;
        }

        // CSI ! p — Soft terminal reset (DECSTR)
        if (_intermediateChar == '!' && finalByte == 'p')
        {
            SoftReset();
            return;
        }

        // CSI > ... c — Secondary DA
        if (_intermediateChar == '>')
        {
            if (finalByte == 'c')
            {
                // DA2 — Secondary Device Attributes
                ResponseCallback?.Invoke("\x1b[>1;10;0c"u8.ToArray());
            }
            else if (finalByte == 'u')
            {
                HandleKittyKeyboardPush();
            }
            return;
        }

        // CSI = ... c — Tertiary DA
        if (_intermediateChar == '=')
        {
            if (finalByte == 'c')
            {
                // DA3 response payload mirrors native wrapper behavior.
                ResponseCallback?.Invoke("\x1bP!|464F4F\x1b\\"u8.ToArray());
            }
            else if (finalByte == 'u')
            {
                HandleKittyKeyboardSet();
            }
            return;
        }

        // CSI < ... u — kitty keyboard pop mode
        if (_intermediateChar == '<')
        {
            if (finalByte == 'u')
            {
                HandleKittyKeyboardPop();
            }
            return;
        }

        // CSI <space> q — Set cursor style (DECSCUSR)
        if (_intermediateChar == ' ' && finalByte == 'q')
        {
            SetCursorStyle(Math.Max(0, p0));
            return;
        }

        switch (finalByte)
        {
            case 'A': // CUU — Cursor Up
                _cursorRow = Math.Max(_scrollTop, _cursorRow - Math.Max(1, p0));
                break;

            case 'B': // CUD — Cursor Down
                _cursorRow = Math.Min(_scrollBottom, _cursorRow + Math.Max(1, p0));
                break;

            case 'C': // CUF — Cursor Forward
                _cursorCol = Math.Min(_screen.Columns - 1, _cursorCol + Math.Max(1, p0));
                break;

            case 'D': // CUB — Cursor Back
                _cursorCol = Math.Max(0, _cursorCol - Math.Max(1, p0));
                break;

            case 'E': // CNL — Cursor Next Line
                _cursorCol = 0;
                _cursorRow = Math.Min(_scrollBottom, _cursorRow + Math.Max(1, p0));
                break;

            case 'F': // CPL — Cursor Previous Line
                _cursorCol = 0;
                _cursorRow = Math.Max(_scrollTop, _cursorRow - Math.Max(1, p0));
                break;

            case 'G': // CHA — Cursor Horizontal Absolute
                _cursorCol = Math.Clamp(Math.Max(1, p0) - 1, 0, _screen.Columns - 1);
                break;

            case 'H': // CUP — Cursor Position
            case 'f': // HVP — same as CUP
            {
                var row = Math.Max(1, p0) - 1;
                var col = Math.Max(1, p1) - 1;
                if (_originMode)
                {
                    row += _scrollTop;
                    row = Math.Clamp(row, _scrollTop, _scrollBottom);
                }
                else
                {
                    row = Math.Clamp(row, 0, _screen.ViewportRows - 1);
                }
                _cursorRow = row;
                _cursorCol = Math.Clamp(col, 0, _screen.Columns - 1);
                break;
            }

            case 'J': // ED — Erase in Display
                EraseInDisplay(p0);
                break;

            case 'K': // EL — Erase in Line
                EraseInLine(p0);
                break;

            case 'L': // IL — Insert Lines
                InsertLines(Math.Max(1, p0));
                break;

            case 'M': // DL — Delete Lines
                DeleteLines(Math.Max(1, p0));
                break;

            case 'P': // DCH — Delete Characters
                DeleteCharacters(Math.Max(1, p0));
                break;

            case 'X': // ECH — Erase Characters
                EraseCharacters(Math.Max(1, p0));
                break;

            case 'd': // VPA — Vertical line Position Absolute
                _cursorRow = Math.Clamp(Math.Max(1, p0) - 1, 0, _screen.ViewportRows - 1);
                break;

            case 'e': // VPR — Vertical Position Relative
                _cursorRow = Math.Min(_screen.ViewportRows - 1, _cursorRow + Math.Max(1, p0));
                break;

            case 'a': // HPR — Horizontal Position Relative
                _cursorCol = Math.Min(_screen.Columns - 1, _cursorCol + Math.Max(1, p0));
                break;

            case 'm': // SGR — Select Graphic Rendition
                ProcessSgr();
                break;

            case 'r': // DECSTBM — Set Top and Bottom Margins
            {
                var top = _params.Count > 0 && _params[0] > 0 ? _params[0] - 1 : 0;
                var bottom = _params.Count > 1 && _params[1] > 0 ? _params[1] - 1 : _screen.ViewportRows - 1;
                top = Math.Clamp(top, 0, _screen.ViewportRows - 1);
                bottom = Math.Clamp(bottom, 0, _screen.ViewportRows - 1);
                if (top < bottom)
                {
                    _scrollTop = top;
                    _scrollBottom = bottom;
                }
                // After setting margins, cursor moves to home
                _cursorCol = 0;
                _cursorRow = _originMode ? _scrollTop : 0;
                break;
            }

            case 's': // SCP — Save Cursor Position (ANSI.SYS)
                _savedCursorCol = _cursorCol;
                _savedCursorRow = _cursorRow;
                break;

            case 'u': // RCP — Restore Cursor Position (ANSI.SYS)
                _cursorCol = _savedCursorCol;
                _cursorRow = _savedCursorRow;
                break;

            case '@': // ICH — Insert Characters
                InsertCharacters(Math.Max(1, p0));
                break;

            case 'S': // SU — Scroll Up
                for (var i = 0; i < Math.Max(1, p0); i++)
                    ScrollUpInRegion();
                break;

            case 'T': // SD — Scroll Down
                for (var i = 0; i < Math.Max(1, p0); i++)
                    ScrollDownInRegion();
                break;

            case 'g': // TBC — Tab Clear
                if (p0 == 0)
                    _tabStops.Remove(_cursorCol);
                else if (p0 == 3)
                    _tabStops.Clear();
                break;

            case 'n': // DSR — Device Status Report
                if (p0 == 5)
                {
                    // Operating status report — terminal OK
                    ResponseCallback?.Invoke("\x1b[0n"u8.ToArray());
                }
                else if (p0 == 6)
                {
                    // CPR — Cursor Position Report (1-based)
                    var cpr = $"\x1b[{_cursorRow + 1};{_cursorCol + 1}R";
                    ResponseCallback?.Invoke(System.Text.Encoding.ASCII.GetBytes(cpr));
                }
                break;

            case 'c': // DA — Device Attributes
                if (p0 == 0 || !_hasParam)
                {
                    // DA1 — Primary Device Attributes (VT220 + ANSI color)
                    ResponseCallback?.Invoke("\x1b[?62;22c"u8.ToArray());
                }
                break;

            case 'h': // SM — Set Mode (ANSI modes, non-private)
            {
                if (_params.Count == 0)
                {
                    break;
                }

                for (int i = 0; i < _params.Count; i++)
                {
                    HandleAnsiMode(_params[i], set: true);
                }
                break;
            }

            case 'l': // RM — Reset Mode
            {
                if (_params.Count == 0)
                {
                    break;
                }

                for (int i = 0; i < _params.Count; i++)
                {
                    HandleAnsiMode(_params[i], set: false);
                }
                break;
            }

            case 'b': // REP — Repeat preceding graphic character
            {
                if (_lastGraphicCodepoint == 0)
                {
                    break;
                }

                int count = Math.Max(1, p0);
                for (int i = 0; i < count; i++)
                {
                    PutChar(_lastGraphicCodepoint);
                }
                break;
            }

            case 'Z': // CBT — Cursor Backward Tabulation
            {
                var count = Math.Max(1, p0);
                for (var n = 0; n < count; n++)
                {
                    var found = false;
                    for (var c = _cursorCol - 1; c >= 0; c--)
                    {
                        if (_tabStops.Contains(c))
                        {
                            _cursorCol = c;
                            found = true;
                            break;
                        }
                    }
                    if (!found) _cursorCol = 0;
                }
                break;
            }

            case 'I': // CHT — Cursor Horizontal Forward Tabulation
                for (var n = 0; n < Math.Max(1, p0); n++)
                    TabForward();
                break;

            case 't': // XTWINOPS reports
                HandleWindowReport(Math.Max(0, p0));
                break;
        }
    }

    private void HandleWindowReport(int reportCode)
    {
        switch (reportCode)
        {
            case 14: // CSI 14 t — text area size in pixels
            {
                string response = $"\x1b[4;{Math.Max(0, _heightPx)};{Math.Max(0, _widthPx)}t";
                ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
                break;
            }

            case 16: // CSI 16 t — cell size in pixels
            {
                int cellWidth = _screen.Columns > 0 && _widthPx > 0
                    ? _widthPx / _screen.Columns
                    : 8;
                int cellHeight = _screen.ViewportRows > 0 && _heightPx > 0
                    ? _heightPx / _screen.ViewportRows
                    : 16;

                if (cellWidth <= 0) cellWidth = 8;
                if (cellHeight <= 0) cellHeight = 16;

                string response = $"\x1b[6;{cellHeight};{cellWidth}t";
                ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
                break;
            }

            case 18: // CSI 18 t — text area size in characters
            {
                string response = $"\x1b[8;{_screen.ViewportRows};{_screen.Columns}t";
                ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
                break;
            }

            case 21: // CSI 21 t — report window title
                ResponseCallback?.Invoke("\x1b]l\x1b\\"u8.ToArray());
                break;
        }
    }

    private void HandleKittyKeyboardQuery()
    {
        string response = $"\x1b[?{KittyKeyboardFlags}u";
        ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
    }

    private void HandleKittyKeyboardSet()
    {
        int flags = _params.Count > 0 ? _params[0] : 0;
        int operation = _params.Count > 1 ? _params[1] : 1;
        flags = NormalizeKittyKeyboardFlags(flags);

        switch (operation)
        {
            case 2: // OR
                SetKittyKeyboardFlags(KittyKeyboardFlags | flags);
                break;
            case 3: // AND NOT
                SetKittyKeyboardFlags(KittyKeyboardFlags & ~flags);
                break;
            case 1:
            default: // SET
                SetKittyKeyboardFlags(flags);
                break;
        }
    }

    private void HandleKittyKeyboardPush()
    {
        List<int> stack = GetActiveKittyKeyboardStack();
        if (stack.Count >= KittyKeyboardMaxStackDepth)
        {
            stack.RemoveAt(0);
        }

        stack.Add(KittyKeyboardFlags);
        int flags = _params.Count > 0 ? _params[0] : 0;
        SetKittyKeyboardFlags(flags);
    }

    private void HandleKittyKeyboardPop()
    {
        int count = _params.Count > 0 ? Math.Max(1, _params[0]) : 1;
        List<int> stack = GetActiveKittyKeyboardStack();

        for (int i = 0; i < count; i++)
        {
            if (stack.Count == 0)
            {
                break;
            }

            int topIndex = stack.Count - 1;
            int flags = stack[topIndex];
            stack.RemoveAt(topIndex);
            SetKittyKeyboardFlags(flags);
        }
    }

    private List<int> GetActiveKittyKeyboardStack()
    {
        return _inAltScreen ? _kittyKeyboardStackAlt : _kittyKeyboardStackMain;
    }

    private void SetKittyKeyboardFlags(int flags)
    {
        int normalized = NormalizeKittyKeyboardFlags(flags);
        if (_inAltScreen)
        {
            _kittyKeyboardFlagsAlt = normalized;
        }
        else
        {
            _kittyKeyboardFlagsMain = normalized;
        }
    }

    private static int NormalizeKittyKeyboardFlags(int flags)
    {
        return Math.Max(0, flags) & KittyKeyboardFlagMask;
    }

    private void HandleAnsiMode(int mode, bool set)
    {
        switch (mode)
        {
            case 4: // IRM — Insert/replace mode.
                _insertMode = set;
                break;

            case 20: // LNM — Linefeed/newline mode.
                _lineFeedNewLineMode = set;
                break;
        }
    }

    private void SetCursorStyle(int styleParameter)
    {
        _cursorStyle = styleParameter switch
        {
            <= 0 => 1,
            > 6 => 6,
            _ => styleParameter,
        };
    }

    #endregion

    #region DEC Private Modes

    private void HandleDecMode(int mode, bool set)
    {
        switch (mode)
        {
            case 1: // DECCKM — Application cursor keys
                _applicationCursorKeys = set;
                break;

            case 6: // DECOM — Origin mode
                _originMode = set;
                _cursorCol = 0;
                _cursorRow = _originMode ? _scrollTop : 0;
                break;

            case 7: // DECAWM — Auto-wrap mode
                _autoWrap = set;
                break;

            case 12: // Blinking cursor (att610) — ignore
                break;

            case 25: // DECTCEM — Show/hide cursor
                _cursorVisible = set;
                break;

            case 47: // Use alternate screen buffer (no clear)
                if (set && !_inAltScreen)
                    SwitchToAltScreen(clearAlt: false);
                else if (!set && _inAltScreen)
                    SwitchToMainScreen();
                break;

            case 1000: // Mouse tracking — ignore
            case 1002:
            case 1003:
            case 1005:
            case 1006:
            case 1015:
                break;

            case 1047: // Use alternate screen buffer
                if (set && !_inAltScreen)
                    SwitchToAltScreen(clearAlt: true);
                else if (!set && _inAltScreen)
                {
                    ClearScreen();
                    SwitchToMainScreen();
                }
                break;

            case 1048: // Save/restore cursor (for 1049)
                if (set)
                    SaveCursor();
                else
                    RestoreCursor();
                break;

            case 1049: // Save cursor + switch to alt screen + clear
                if (set)
                {
                    SaveCursor();
                    SwitchToAltScreen(clearAlt: true);
                }
                else
                {
                    SwitchToMainScreen();
                    RestoreCursor();
                }
                break;

            case 2004: // Bracketed paste mode
                _bracketedPaste = set;
                break;
        }
    }

    #endregion

    #region Alternate Screen Buffer

    private void SwitchToAltScreen(bool clearAlt)
    {
        if (_inAltScreen) return;

        // Save current main screen content
        _savedMainBuffer = new TerminalRow[_screen.ViewportRows];
        for (var r = 0; r < _screen.ViewportRows; r++)
        {
            var srcRow = _screen.GetViewportRow(r);
            var saved = new TerminalRow(_screen.Columns, _screen.DefaultForeground, _screen.DefaultBackground);
            CopyRow(srcRow, saved);
            _savedMainBuffer[r] = saved;
        }
        _savedMainCursorCol = _cursorCol;
        _savedMainCursorRow = _cursorRow;
        _inAltScreen = true;

        if (clearAlt)
            ClearScreen();

        // Reset scroll region
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;
    }

    private void SwitchToMainScreen()
    {
        if (!_inAltScreen) return;

        // Restore main screen content
        if (_savedMainBuffer is not null)
        {
            for (var r = 0; r < _screen.ViewportRows && r < _savedMainBuffer.Length; r++)
            {
                var dst = _screen.GetViewportRow(r);
                CopyRow(_savedMainBuffer[r], dst);
                dst.IsDirty = true;
            }
            _savedMainBuffer = null;
        }

        _cursorCol = _savedMainCursorCol;
        _cursorRow = _savedMainCursorRow;
        _inAltScreen = false;

        // Reset scroll region
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;

        _screen.InvalidateAll();
    }

    #endregion

    #region Cursor Save/Restore

    private void SaveCursor()
    {
        _savedCursorCol = _cursorCol;
        _savedCursorRow = _cursorRow;
        _savedFg = _currentFg;
        _savedBg = _currentBg;
        _savedAttrs = _currentAttrs;
        _savedUnderlineStyle = _currentUnderlineStyle;
        _savedDecorations = _currentDecorations;
        _savedUseLineDrawing = _useLineDrawing;
    }

    private void RestoreCursor()
    {
        _cursorCol = _savedCursorCol;
        _cursorRow = _savedCursorRow;
        _currentFg = _savedFg;
        _currentBg = _savedBg;
        _currentAttrs = _savedAttrs;
        _currentUnderlineStyle = _savedUnderlineStyle;
        _currentDecorations = _savedDecorations;
        _useLineDrawing = _savedUseLineDrawing;
    }

    #endregion

    #region SGR Processing

    private void ProcessSgr()
    {
        if (_params.Count == 0)
        {
            ResetAttributes();
            return;
        }

        for (var i = 0; i < _params.Count; i++)
        {
            var p = _params[i];

            switch (p)
            {
                case 0: ResetAttributes(); break;
                case 1: _currentAttrs |= CellAttributes.Bold; break;
                case 2: _currentAttrs |= CellAttributes.Dim; break;
                case 3: _currentAttrs |= CellAttributes.Italic; break;
                case 4:
                    _currentAttrs |= CellAttributes.Underline;
                    _currentUnderlineStyle = TerminalUnderlineStyle.Single;
                    break;
                case 5: _currentAttrs |= CellAttributes.Blink; break;
                case 7: _currentAttrs |= CellAttributes.Inverse; break;
                case 8: _currentAttrs |= CellAttributes.Hidden; break;
                case 9: _currentAttrs |= CellAttributes.Strikethrough; break;
                case 21:
                    _currentAttrs |= CellAttributes.Underline;
                    _currentUnderlineStyle = TerminalUnderlineStyle.Double;
                    break;
                case 22: _currentAttrs &= ~(CellAttributes.Bold | CellAttributes.Dim); break;
                case 23: _currentAttrs &= ~CellAttributes.Italic; break;
                case 24:
                    _currentAttrs &= ~CellAttributes.Underline;
                    _currentUnderlineStyle = TerminalUnderlineStyle.None;
                    break;
                case 25: _currentAttrs &= ~CellAttributes.Blink; break;
                case 27: _currentAttrs &= ~CellAttributes.Inverse; break;
                case 28: _currentAttrs &= ~CellAttributes.Hidden; break;
                case 29: _currentAttrs &= ~CellAttributes.Strikethrough; break;
                case 53:
                    _currentDecorations |= CellDecorations.Overline;
                    break;
                case 55:
                    _currentDecorations &= ~CellDecorations.Overline;
                    break;

                // Standard foreground colors
                case >= 30 and <= 37:
                    _currentFg = PaletteColor(p - 30);
                    break;
                case 39:
                    _currentFg = _screen.DefaultForeground;
                    break;

                // Standard background colors
                case >= 40 and <= 47:
                    _currentBg = PaletteColor(p - 40);
                    break;
                case 49:
                    _currentBg = _screen.DefaultBackground;
                    break;

                // Bright foreground colors
                case >= 90 and <= 97:
                    _currentFg = PaletteColor(p - 82);
                    break;

                // Bright background colors
                case >= 100 and <= 107:
                    _currentBg = PaletteColor(p - 92);
                    break;

                // 256-color and truecolor
                case 38:
                    if (i + 1 < _params.Count)
                    {
                        if (_params[i + 1] == 5 && i + 2 < _params.Count)
                        {
                            _currentFg = PaletteColor(_params[i + 2]);
                            i += 2;
                        }
                        else if (_params[i + 1] == 2 && i + 4 < _params.Count)
                        {
                            _currentFg = 0xFF000000 | ((uint)_params[i + 2] << 16) |
                                         ((uint)_params[i + 3] << 8) | (uint)_params[i + 4];
                            i += 4;
                        }
                    }
                    break;

                case 48:
                    if (i + 1 < _params.Count)
                    {
                        if (_params[i + 1] == 5 && i + 2 < _params.Count)
                        {
                            _currentBg = PaletteColor(_params[i + 2]);
                            i += 2;
                        }
                        else if (_params[i + 1] == 2 && i + 4 < _params.Count)
                        {
                            _currentBg = 0xFF000000 | ((uint)_params[i + 2] << 16) |
                                         ((uint)_params[i + 3] << 8) | (uint)_params[i + 4];
                            i += 4;
                        }
                    }
                    break;
            }
        }
    }

    private void ResetAttributes()
    {
        _currentFg = _screen.DefaultForeground;
        _currentBg = _screen.DefaultBackground;
        _currentAttrs = CellAttributes.None;
        _currentUnderlineStyle = TerminalUnderlineStyle.None;
        _currentDecorations = CellDecorations.None;
    }

    #endregion

    #region Erase / Insert / Delete Operations

    private void ClearScreen()
    {
        for (var r = 0; r < _screen.ViewportRows; r++)
            _screen.GetViewportRow(r).Clear(_screen.DefaultForeground, _screen.DefaultBackground);
        _cursorCol = 0;
        _cursorRow = 0;
        _screen.InvalidateAll();
    }

    private void EraseInDisplay(int mode)
    {
        ClampCursor();

        switch (mode)
        {
            case 0: // From cursor to end
                EraseInLine(0);
                for (var r = _cursorRow + 1; r < _screen.ViewportRows; r++)
                    _screen.GetViewportRow(r).Clear(_currentFg, _currentBg);
                break;

            case 1: // From start to cursor
                for (var r = 0; r < _cursorRow && r < _screen.ViewportRows; r++)
                    _screen.GetViewportRow(r).Clear(_currentFg, _currentBg);
                if (_cursorRow >= 0 && _cursorRow < _screen.ViewportRows)
                {
                    var rowToCursor = _screen.GetViewportRow(_cursorRow);
                    for (var c = 0; c <= _cursorCol && c < _screen.Columns; c++)
                        rowToCursor[c] = TerminalCell.Empty(_currentFg, _currentBg);
                    NormalizeRowWideCells(rowToCursor);
                    rowToCursor.IsDirty = true;
                }
                break;

            case 2: // Entire display
                for (var r = 0; r < _screen.ViewportRows; r++)
                    _screen.GetViewportRow(r).Clear(_currentFg, _currentBg);
                break;

            case 3: // Entire display + scrollback
                for (var r = 0; r < _screen.ViewportRows; r++)
                    _screen.GetViewportRow(r).Clear(_currentFg, _currentBg);
                break;
        }

        _screen.InvalidateAll();
    }

    private void EraseInLine(int mode)
    {
        ClampCursor();
        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;

        var row = _screen.GetViewportRow(_cursorRow);
        switch (mode)
        {
            case 0: // From cursor to end of line
                for (var c = Math.Max(0, _cursorCol); c < _screen.Columns; c++)
                    row[c] = TerminalCell.Empty(_currentFg, _currentBg);
                break;

            case 1: // From start to cursor
                for (var c = 0; c <= _cursorCol && c < _screen.Columns; c++)
                    row[c] = TerminalCell.Empty(_currentFg, _currentBg);
                break;

            case 2: // Entire line
                row.Clear(_currentFg, _currentBg);
                break;
        }

        NormalizeRowWideCells(row);
        row.IsDirty = true;
    }

    private void InsertLines(int count)
    {
        ClampCursor();
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;

        for (var n = 0; n < count; n++)
        {
            // Shift rows down from cursor to scroll bottom
            for (var r = _scrollBottom; r > _cursorRow; r--)
            {
                if (r < _screen.ViewportRows && r - 1 >= 0)
                    CopyRow(_screen.GetViewportRow(r - 1), _screen.GetViewportRow(r));
            }
            // Clear the line at cursor
            if (_cursorRow < _screen.ViewportRows)
                _screen.GetViewportRow(_cursorRow).Clear(_currentFg, _currentBg);
        }
        _screen.InvalidateAll();
    }

    private void DeleteLines(int count)
    {
        ClampCursor();
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;

        for (var n = 0; n < count; n++)
        {
            // Shift rows up from cursor to scroll bottom
            for (var r = _cursorRow; r < _scrollBottom; r++)
            {
                if (r >= 0 && r + 1 < _screen.ViewportRows)
                    CopyRow(_screen.GetViewportRow(r + 1), _screen.GetViewportRow(r));
            }
            // Clear the bottom row of the scroll region
            if (_scrollBottom < _screen.ViewportRows)
                _screen.GetViewportRow(_scrollBottom).Clear(_currentFg, _currentBg);
        }
        _screen.InvalidateAll();
    }

    private void InsertCharacters(int count)
    {
        ClampCursor();
        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;

        var row = _screen.GetViewportRow(_cursorRow);
        for (var c = _screen.Columns - 1; c >= _cursorCol + count; c--)
            row[c] = row[c - count];
        for (var c = _cursorCol; c < _cursorCol + count && c < _screen.Columns; c++)
            row[c] = TerminalCell.Empty(_currentFg, _currentBg);
        NormalizeRowWideCells(row);
        row.IsDirty = true;
    }

    private void DeleteCharacters(int count)
    {
        ClampCursor();
        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;

        var row = _screen.GetViewportRow(_cursorRow);
        for (var c = _cursorCol; c + count < _screen.Columns; c++)
            row[c] = row[c + count];
        for (var c = Math.Max(_cursorCol, _screen.Columns - count); c < _screen.Columns; c++)
            row[c] = TerminalCell.Empty(_currentFg, _currentBg);
        NormalizeRowWideCells(row);
        row.IsDirty = true;
    }

    private void EraseCharacters(int count)
    {
        ClampCursor();
        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;

        var row = _screen.GetViewportRow(_cursorRow);
        for (var c = _cursorCol; c < _cursorCol + count && c < _screen.Columns; c++)
            row[c] = TerminalCell.Empty(_currentFg, _currentBg);
        NormalizeRowWideCells(row);
        row.IsDirty = true;
    }

    private static void NormalizeRowWideCells(TerminalRow row)
    {
        for (int col = 0; col < row.Columns; col++)
        {
            ref TerminalCell cell = ref row[col];
            if (cell.Width == 2)
            {
                if (col + 1 >= row.Columns)
                {
                    row[col] = TerminalCell.Empty(cell.Foreground, cell.Background);
                    continue;
                }

                ref TerminalCell trailing = ref row[col + 1];
                if (trailing.Width != 0 || trailing.HasContent)
                {
                    row[col] = TerminalCell.Empty(cell.Foreground, cell.Background);
                    continue;
                }

                trailing.Codepoint = 0;
                trailing.Grapheme = null;
                trailing.Foreground = cell.Foreground;
                trailing.Background = cell.Background;
                trailing.Attributes = cell.Attributes;
                trailing.UnderlineStyle = cell.UnderlineStyle;
                trailing.Decorations = cell.Decorations;
                trailing.Width = 0;
                col++;
                continue;
            }

            if (cell.Width == 0)
            {
                bool hasWideLeader = col > 0 && row[col - 1].Width == 2;
                if (!hasWideLeader)
                {
                    row[col] = TerminalCell.Empty(cell.Foreground, cell.Background);
                }

                continue;
            }

            if (cell.Width != 1)
            {
                cell.Width = 1;
            }
        }
    }

    #endregion

    #region Soft Reset

    private void SoftReset()
    {
        _cursorVisible = true;
        _originMode = false;
        _autoWrap = true;
        _applicationCursorKeys = false;
        _applicationKeypad = false;
        _insertMode = false;
        _lineFeedNewLineMode = false;
        _cursorStyle = 1;
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;
        _useLineDrawing = false;
        _g0IsLineDrawing = false;
        _g1IsLineDrawing = false;
        _shiftOut = false;
        _lastGraphicCodepoint = 0;
        _dcsBuffer.Clear();
        _isDiscardingDcsPayload = false;
        _savedUnderlineStyle = TerminalUnderlineStyle.None;
        _savedDecorations = CellDecorations.None;
        _kittyKeyboardFlagsMain = 0;
        _kittyKeyboardFlagsAlt = 0;
        _kittyKeyboardStackMain.Clear();
        _kittyKeyboardStackAlt.Clear();
        ResetAttributes();
        InitTabStops();
    }

    #endregion

    #region DEC Line Drawing Character Set

    /// <summary>
    /// Maps ASCII characters to DEC Special Graphics set codepoints.
    /// Used for drawing borders and lines in TUI applications.
    /// </summary>
    private static int MapLineDrawing(char ch) => ch switch
    {
        'j' => 0x2518, // ┘ Bottom-right corner
        'k' => 0x2510, // ┐ Top-right corner
        'l' => 0x250C, // ┌ Top-left corner
        'm' => 0x2514, // └ Bottom-left corner
        'n' => 0x253C, // ┼ Crossing lines
        'q' => 0x2500, // ─ Horizontal line
        't' => 0x251C, // ├ Left tee
        'u' => 0x2524, // ┤ Right tee
        'v' => 0x2534, // ┴ Bottom tee
        'w' => 0x252C, // ┬ Top tee
        'x' => 0x2502, // │ Vertical line
        'a' => 0x2592, // ▒ Checkerboard
        'f' => 0x00B0, // ° Degree symbol
        'g' => 0x00B1, // ± Plus/minus
        'h' => 0x2592, // ▒ Board of squares (NL)
        'o' => 0x23BA, // ⎺ Scan line 1
        'p' => 0x23BB, // ⎻ Scan line 3
        'r' => 0x23BC, // ⎼ Scan line 7
        's' => 0x23BD, // ⎽ Scan line 9
        '`' => 0x25C6, // ◆ Diamond
        '~' => 0x00B7, // · Bullet (middle dot)
        '_' => 0x0020, // (blank)
        '0' => 0x2588, // █ Solid block
        'y' => 0x2264, // ≤ Less-than-or-equal
        'z' => 0x2265, // ≥ Greater-than-or-equal
        '{' => 0x03C0, // π Pi
        '|' => 0x2260, // ≠ Not equal
        '}' => 0x00A3, // £ Pound sign
        _ => ch,
    };

    #endregion

    #region Colors

    private uint PaletteColor(int index)
    {
        index = Math.Clamp(index, 0, 255);
        return _theme.Palette[index];
    }

    private static bool TryParseOscColorSpec(string value, out uint color)
    {
        color = 0;
        string token = value.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        if (token.StartsWith('#'))
        {
            return TerminalThemeParser.TryParseColor(token, out color);
        }

        if (token.StartsWith("rgb:", StringComparison.OrdinalIgnoreCase))
        {
            return TerminalThemeParser.TryParseColor(token, out color);
        }

        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed))
        {
            color = (packed & 0x00FFFFFFu) | 0xFF000000u;
            return true;
        }

        return false;
    }

    #endregion

    /// <summary>
    /// Resets the processor to initial state.
    /// </summary>
    public void Reset()
    {
        ResetInternal(raiseModeChanged: true);
    }

    private void ResetInternal(bool raiseModeChanged)
    {
        TerminalModeState before = ModeState;

        _cursorCol = 0;
        _cursorRow = 0;
        _state = ParserState.Ground;
        _params.Clear();
        _oscBuffer.Clear();
        _dcsBuffer.Clear();
        _isDiscardingDcsPayload = false;
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;
        _inAltScreen = false;
        _savedMainBuffer = null;
        _autoWrap = true;
        _cursorVisible = true;
        _originMode = false;
        _applicationCursorKeys = false;
        _applicationKeypad = false;
        _bracketedPaste = false;
        _insertMode = false;
        _lineFeedNewLineMode = false;
        _cursorStyle = 1;
        _useLineDrawing = false;
        _g0IsLineDrawing = false;
        _g1IsLineDrawing = false;
        _shiftOut = false;
        _lastGraphicCodepoint = 0;
        _savedUnderlineStyle = TerminalUnderlineStyle.None;
        _savedDecorations = CellDecorations.None;
        _kittyKeyboardFlagsMain = 0;
        _kittyKeyboardFlagsAlt = 0;
        _kittyKeyboardStackMain.Clear();
        _kittyKeyboardStackAlt.Clear();
        ResetAttributes();
        InitTabStops();

        for (var r = 0; r < _screen.ViewportRows; r++)
            _screen.GetViewportRow(r).Clear(_screen.DefaultForeground, _screen.DefaultBackground);

        if (raiseModeChanged)
        {
            RaiseModeChangedIfNeeded(before);
        }
    }

    /// <summary>
    /// Notify the processor that the screen has been resized.
    /// Updates the scroll region to match the new dimensions.
    /// </summary>
    public void NotifyResize(int columns, int rows)
    {
        _scrollBottom = rows - 1;
        if (_scrollTop >= rows)
            _scrollTop = 0;
        if (_cursorRow >= rows)
            _cursorRow = rows - 1;
        if (_cursorCol >= columns)
            _cursorCol = columns - 1;
        InitTabStops();
    }

    /// <summary>
    /// Notify the processor that the screen has been resized with pixel dimensions.
    /// Pixel dimensions are used to answer CSI 14t/16t size reports.
    /// </summary>
    public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
    {
        _widthPx = Math.Max(0, widthPx);
        _heightPx = Math.Max(0, heightPx);
        NotifyResize(columns, rows);
    }

    /// <inheritdoc />
    public void ApplyTheme(TerminalTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TerminalTheme previousTheme = _theme;
        Dictionary<uint, uint> colorRemap = BuildColorRemap(previousTheme, theme);

        _theme = theme;
        _screen.ApplyTheme(theme, invalidateRows: true);

        _currentFg = RemapColor(_currentFg, colorRemap);
        _currentBg = RemapColor(_currentBg, colorRemap);
        _savedFg = RemapColor(_savedFg, colorRemap);
        _savedBg = RemapColor(_savedBg, colorRemap);
    }

    private static Dictionary<uint, uint> BuildColorRemap(TerminalTheme previousTheme, TerminalTheme nextTheme)
    {
        Dictionary<uint, uint> remap = new(capacity: 258);
        HashSet<uint> ambiguousSources = new();

        AddColorRemap(remap, ambiguousSources, previousTheme.DefaultForeground, nextTheme.DefaultForeground);
        AddColorRemap(remap, ambiguousSources, previousTheme.DefaultBackground, nextTheme.DefaultBackground);

        for (int i = 0; i < 256; i++)
        {
            AddColorRemap(remap, ambiguousSources, previousTheme.Palette[i], nextTheme.Palette[i]);
        }

        return remap;
    }

    private static void AddColorRemap(
        IDictionary<uint, uint> remap,
        ISet<uint> ambiguousSources,
        uint source,
        uint target)
    {
        if (source == target || ambiguousSources.Contains(source))
        {
            return;
        }

        if (!remap.TryGetValue(source, out uint existing))
        {
            remap[source] = target;
            return;
        }

        if (existing != target)
        {
            remap.Remove(source);
            ambiguousSources.Add(source);
        }
    }

    private static uint RemapColor(uint color, IReadOnlyDictionary<uint, uint> remap)
    {
        return remap.TryGetValue(color, out uint mapped) ? mapped : color;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No unmanaged resources to release.
    }

    private void RaiseModeChangedIfNeeded(TerminalModeState before)
    {
        TerminalModeState current = ModeState;
        if (before != current)
        {
            ModeChanged?.Invoke(this, current);
        }
    }
}
