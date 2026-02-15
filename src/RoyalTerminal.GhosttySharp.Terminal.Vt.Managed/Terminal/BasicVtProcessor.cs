// Licensed under the MIT License.
// GhosttySharp.Avalonia — VT sequence processor for standalone/demo mode.
// Processes raw terminal output bytes into the TerminalScreen cell grid.
// Supports: printable characters, cursor movement, SGR colors (256 + truecolor),
// scrolling, scroll regions (DECSTBM), alternate screen buffer, DEC private modes,
// DEC line-drawing character set, erase, insert/delete lines & characters, and tabs.

using GhosttySharp.Avalonia.Rendering;
using GhosttySharp.Unicode;
using System.Text;

namespace GhosttySharp.Avalonia.Terminal;

/// <summary>
/// VT100/xterm escape sequence processor that writes terminal data directly
/// into a <see cref="TerminalScreen"/> cell grid.
///
/// This is used as the fallback VT processor when the Ghostty native terminal
/// (via <see cref="GhosttyVtProcessor"/>) is not available. It handles enough
/// of the VT protocol to render typical shell output and full-screen TUI
/// applications such as Midnight Commander, htop, vim, etc.
/// </summary>
public sealed class BasicVtProcessor : IVtProcessor
{
    private readonly TerminalScreen _screen;
    private int _cursorCol;
    private int _cursorRow;
    private uint _currentFg;
    private uint _currentBg;
    private CellAttributes _currentAttrs;

    // Parser state machine
    private ParserState _state = ParserState.Ground;
    private readonly List<int> _params = [];
    private int _currentParam;
    private bool _hasParam;
    private char _intermediateChar;

    // Saved cursor state (for DECSC/DECRC — ESC 7 / ESC 8)
    private int _savedCursorCol;
    private int _savedCursorRow;
    private uint _savedFg;
    private uint _savedBg;
    private CellAttributes _savedAttrs;
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
    private bool _bracketedPaste;      // Bracketed paste mode (mode 2004)

    // Tab stops
    private readonly HashSet<int> _tabStops = [];

    // UTF-8 multi-byte decoding state
    private int _utf8Codepoint;
    private int _utf8Remaining;

    private enum ParserState
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        OscString,
        DcsString,
    }

    /// <summary>Current cursor column.</summary>
    public int CursorCol => _cursorCol;

    /// <summary>Current cursor row.</summary>
    public int CursorRow => _cursorRow;

    /// <summary>Whether cursor should be visible.</summary>
    public bool CursorVisible => _cursorVisible;

    /// <summary>Whether application cursor key mode is active.</summary>
    public bool ApplicationCursorKeys => _applicationCursorKeys;

    /// <summary>Whether the alternate screen buffer is active.</summary>
    public bool AlternateScreen => _inAltScreen;

    /// <summary>Whether bracketed paste mode is active.</summary>
    public bool BracketedPaste => _bracketedPaste;

    /// <inheritdoc />
    public Action<byte[]>? ResponseCallback { get; set; }

    /// <inheritdoc />
    public Action? BellCallback { get; set; }

    /// <inheritdoc />
    public Action<string>? TitleCallback { get; set; }

    public BasicVtProcessor(TerminalScreen screen)
    {
        _screen = screen;
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
                case ParserState.DcsString:
                    ProcessDcsString(b);
                    break;
            }
        }
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

            case (byte)'\n': // LF
            case 0x0B:       // VT
            case 0x0C:       // FF
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

            case 0x07: // BEL
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
        cell.Width = (byte)width;

        if (width == 2 && _cursorCol + 1 < row.Columns)
        {
            ref TerminalCell spacer = ref row[_cursorCol + 1];
            spacer.Codepoint = 0;
            spacer.Grapheme = null;
            spacer.Foreground = _currentFg;
            spacer.Background = _currentBg;
            spacer.Attributes = _currentAttrs;
            spacer.Width = 0;
        }

        row.IsDirty = true;

        _cursorCol += width;
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
                _state = ParserState.CsiEntry;
                _params.Clear();
                _currentParam = 0;
                _hasParam = false;
                _intermediateChar = '\0';
                break;

            case (byte)']': // OSC
                _state = ParserState.OscString;
                break;

            case (byte)'P': // DCS
                _state = ParserState.DcsString;
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
                Reset();
                _state = ParserState.Ground;
                break;

            case (byte)'=': // DECKPAM — Application keypad
            case (byte)'>': // DECKPNM — Normal keypad
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
        else if (b == (byte)'?' || b == (byte)'>' || b == (byte)'!')
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
        if (b == 0x07) // BEL
            _state = ParserState.Ground;
        else if (b == 0x1B)
            _state = ParserState.Escape; // Potential ST (ESC \)
    }

    private void ProcessDcsString(byte b)
    {
        // Consume DCS until ST
        if (b == 0x1B)
            _state = ParserState.Escape;
        else if (b == 0x9C) // 8-bit ST
            _state = ParserState.Ground;
    }

    private void ExecuteCsi(char finalByte)
    {
        _state = ParserState.Ground;

        var p0 = _params.Count > 0 ? _params[0] : 0;
        var p1 = _params.Count > 1 ? _params[1] : 0;

        // DEC private modes: CSI ? ... h/l
        if (_intermediateChar == '?')
        {
            var set = (finalByte == 'h');
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
            return;
        }

        // CSI <space> q — Set cursor style (DECSCUSR)
        if (_intermediateChar == ' ' && finalByte == 'q')
            return;

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
                break;

            case 'l': // RM — Reset Mode
                break;

            case 'b': // REP — Repeat preceding graphic character
                break;

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
        }
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
        _savedUseLineDrawing = _useLineDrawing;
    }

    private void RestoreCursor()
    {
        _cursorCol = _savedCursorCol;
        _cursorRow = _savedCursorRow;
        _currentFg = _savedFg;
        _currentBg = _savedBg;
        _currentAttrs = _savedAttrs;
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
                case 4: _currentAttrs |= CellAttributes.Underline; break;
                case 5: _currentAttrs |= CellAttributes.Blink; break;
                case 7: _currentAttrs |= CellAttributes.Inverse; break;
                case 8: _currentAttrs |= CellAttributes.Hidden; break;
                case 9: _currentAttrs |= CellAttributes.Strikethrough; break;
                case 21: _currentAttrs |= CellAttributes.Underline; break; // double underline
                case 22: _currentAttrs &= ~(CellAttributes.Bold | CellAttributes.Dim); break;
                case 23: _currentAttrs &= ~CellAttributes.Italic; break;
                case 24: _currentAttrs &= ~CellAttributes.Underline; break;
                case 25: _currentAttrs &= ~CellAttributes.Blink; break;
                case 27: _currentAttrs &= ~CellAttributes.Inverse; break;
                case 28: _currentAttrs &= ~CellAttributes.Hidden; break;
                case 29: _currentAttrs &= ~CellAttributes.Strikethrough; break;

                // Standard foreground colors
                case >= 30 and <= 37:
                    _currentFg = StandardColor(p - 30);
                    break;
                case 39:
                    _currentFg = _screen.DefaultForeground;
                    break;

                // Standard background colors
                case >= 40 and <= 47:
                    _currentBg = StandardColor(p - 40);
                    break;
                case 49:
                    _currentBg = _screen.DefaultBackground;
                    break;

                // Bright foreground colors
                case >= 90 and <= 97:
                    _currentFg = BrightColor(p - 90);
                    break;

                // Bright background colors
                case >= 100 and <= 107:
                    _currentBg = BrightColor(p - 100);
                    break;

                // 256-color and truecolor
                case 38:
                    if (i + 1 < _params.Count)
                    {
                        if (_params[i + 1] == 5 && i + 2 < _params.Count)
                        {
                            _currentFg = Color256(_params[i + 2]);
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
                            _currentBg = Color256(_params[i + 2]);
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
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;
        _useLineDrawing = false;
        _g0IsLineDrawing = false;
        _g1IsLineDrawing = false;
        _shiftOut = false;
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

    #region Color Tables

    private static uint StandardColor(int index) => index switch
    {
        0 => 0xFF000000, // Black
        1 => 0xFFCC0000, // Red
        2 => 0xFF4E9A06, // Green
        3 => 0xFFC4A000, // Yellow
        4 => 0xFF3465A4, // Blue
        5 => 0xFF75507B, // Magenta
        6 => 0xFF06989A, // Cyan
        7 => 0xFFD3D7CF, // White
        _ => 0xFFD4D4D4,
    };

    private static uint BrightColor(int index) => index switch
    {
        0 => 0xFF555753, // Bright Black
        1 => 0xFFEF2929, // Bright Red
        2 => 0xFF8AE234, // Bright Green
        3 => 0xFFFCE94F, // Bright Yellow
        4 => 0xFF729FCF, // Bright Blue
        5 => 0xFFAD7FA8, // Bright Magenta
        6 => 0xFF34E2E2, // Bright Cyan
        7 => 0xFFEEEEEC, // Bright White
        _ => 0xFFFFFFFF,
    };

    private static uint Color256(int index)
    {
        if (index < 8) return StandardColor(index);
        if (index < 16) return BrightColor(index - 8);

        // 216-color cube (6x6x6): indices 16-231
        if (index < 232)
        {
            var i = index - 16;
            var r = (i / 36) * 51;
            var g = ((i / 6) % 6) * 51;
            var b = (i % 6) * 51;
            return 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
        }

        // Grayscale ramp: indices 232-255
        var gray = (index - 232) * 10 + 8;
        return 0xFF000000 | ((uint)gray << 16) | ((uint)gray << 8) | (uint)gray;
    }

    #endregion

    /// <summary>
    /// Resets the processor to initial state.
    /// </summary>
    public void Reset()
    {
        _cursorCol = 0;
        _cursorRow = 0;
        _state = ParserState.Ground;
        _params.Clear();
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;
        _inAltScreen = false;
        _savedMainBuffer = null;
        _autoWrap = true;
        _cursorVisible = true;
        _originMode = false;
        _applicationCursorKeys = false;
        _bracketedPaste = false;
        _useLineDrawing = false;
        _g0IsLineDrawing = false;
        _g1IsLineDrawing = false;
        _shiftOut = false;
        ResetAttributes();
        InitTabStops();

        for (var r = 0; r < _screen.ViewportRows; r++)
            _screen.GetViewportRow(r).Clear(_screen.DefaultForeground, _screen.DefaultBackground);
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
    /// BasicVtProcessor doesn't use pixel dimensions, so this delegates to the
    /// column/row overload.
    /// </summary>
    public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
    {
        NotifyResize(columns, rows);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No unmanaged resources to release.
    }
}
