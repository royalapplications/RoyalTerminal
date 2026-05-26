// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal — VT sequence processor for standalone/demo mode.
// Processes raw terminal output bytes into the TerminalScreen cell grid.
// Supports: printable characters, cursor movement, SGR colors (256 + truecolor),
// scrolling, scroll regions (DECSTBM), alternate screen buffer, DEC private modes,
// DEC line-drawing character set, erase, insert/delete lines & characters, and tabs.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Sixel;
using RoyalTerminal.Terminal.Theming;
using RoyalTerminal.Unicode;
using System.Globalization;
using System.Net;
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
public sealed class BasicVtProcessor : IVtProcessor,
    ITerminalThemeSink,
    IKittyKeyboardStateSource,
    ITerminalCursorStyleSource,
    ITerminalFocusEventModeSource,
    ITerminalSelectionExportSource,
    ITerminalPasteSequenceEncoderSource,
    ITerminalSnapshotExportSource,
    ITerminalPointerSequenceEncoderSource,
    ITerminalMouseReportingStateSource,
    ITerminalSixelOptionsSink
{
    private const int MaxOscBufferBytes = 4096;
    private const int MaxDcsBufferBytes = 4096;
    private const int KittyKeyboardFlagMask = 0x1F;
    private const int KittyKeyboardMaxStackDepth = 32;
    private const int ZeroWidthJoinerCodepoint = 0x200D;
    private const int CombiningKeycapCodepoint = 0x20E3;
    private static readonly int[] ExtendedDecModes =
    [
        3,
        4,
        5,
        8,
        9,
        12,
        40,
        45,
        69,
        1000,
        1002,
        1003,
        1004,
        1005,
        1006,
        1007,
        1015,
        1016,
        1035,
        1036,
        1039,
        1045,
        2026,
        2027,
        2031,
        2048,
    ];
    private static readonly int[] ExtendedDecModesEnabledByDefault =
    [
        1007,
        1035,
        1036,
    ];

    private readonly TerminalScreen _screen;
    private int _cursorCol;
    private int _cursorRow;
    private uint _currentFg;
    private uint _currentBg;
    private CellAttributes _currentAttrs;
    private TerminalUnderlineStyle _currentUnderlineStyle;
    private uint _currentUnderlineColor;
    private bool _currentHasUnderlineColor;
    private CellDecorations _currentDecorations;
    private int _currentHyperlinkId;
    private TerminalTheme _theme;

    // Parser state machine
    private ParserState _state = ParserState.Ground;
    private readonly List<int> _params = [];
    private int _currentParam;
    private bool _hasParam;
    private char _csiPrivateMarker;
    private char _intermediateChar;
    private readonly List<byte> _oscBuffer = [];
    private readonly List<byte> _dcsBuffer = [];
    private readonly SixelDecoder _sixelDecoder;
    private readonly BasicVtProcessorOptions _options;
    private bool _isDiscardingOscPayload;
    private bool _isDiscardingDcsPayload;
    private bool _sixelGraphicsEnabled;
    private bool _sixelDisplayMode;

    // Saved cursor state (for DECSC/DECRC — ESC 7 / ESC 8)
    private int _savedCursorCol;
    private int _savedCursorRow;
    private uint _savedFg;
    private uint _savedBg;
    private CellAttributes _savedAttrs;
    private TerminalUnderlineStyle _savedUnderlineStyle;
    private uint _savedUnderlineColor;
    private bool _savedHasUnderlineColor;
    private CellDecorations _savedDecorations;
    private int _savedHyperlinkId;
    private bool _savedUseLineDrawing;
    private bool _savedDelayedWrap;

    // Scroll region (DECSTBM)
    private int _scrollTop;    // 0-based inclusive
    private int _scrollBottom; // 0-based inclusive (ViewportRows - 1 at init)

    // Alternate screen buffer
    private int _savedMainCursorCol;
    private int _savedMainCursorRow;
    private bool _savedMainDelayedWrap;
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
    private bool _backarrowKeyMode;    // DECBKM (mode 67)
    private bool _saveCursorMode;      // DECSC/DECRC via mode 1048
    private bool _bracketedPaste;      // Bracketed paste mode (mode 2004)
    private bool _win32InputMode;      // Win32 input mode (mode 9001)
    private bool _delayedWrap;         // Last-column flag: wrap on the next printable cell.
    private bool _keyboardLocked;      // KAM (ANSI mode 2)
    private bool _sendReceiveMode = true; // SRM (ANSI mode 12)
    private bool _insertMode;          // IRM (ANSI mode 4)
    private bool _lineFeedNewLineMode; // LNM (ANSI mode 20)
    private int _cursorStyle = 1;      // DECSCUSR (CSI Ps SP q), default blinking block
    private int _widthPx;
    private int _heightPx;
    private int _kittyKeyboardFlagsMain;
    private int _kittyKeyboardFlagsAlt;
    private readonly List<int> _kittyKeyboardStackMain = [];
    private readonly List<int> _kittyKeyboardStackAlt = [];
    private readonly HashSet<int> _extendedDecModesEnabled = [];

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
    public bool Win32InputMode => _win32InputMode;

    /// <inheritdoc />
    public bool FocusEventsEnabled => _extendedDecModesEnabled.Contains(1004);

    /// <inheritdoc />
    public bool MouseReportingEnabled => MouseModeState.IsMouseReportingEnabled;

    /// <inheritdoc />
    public TerminalCursorStyle CursorStyle => MapCursorStyle(_cursorStyle);

    /// <inheritdoc />
    public bool CursorBlinking => IsCursorStyleBlinking(_cursorStyle);

    /// <inheritdoc />
    public int KittyKeyboardFlags => _inAltScreen ? _kittyKeyboardFlagsAlt : _kittyKeyboardFlagsMain;

    /// <inheritdoc />
    public TerminalModeState ModeState => new(
        CursorVisible,
        ApplicationCursorKeys,
        ApplicationKeypad,
        AlternateScreen,
        BracketedPaste,
        Win32InputMode,
        _backarrowKeyMode);

    private TerminalMouseModeState MouseModeState => new(
        GetMouseTrackingMode(),
        GetMouseEncodingMode());

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

    /// <inheritdoc />
    public bool SixelGraphicsEnabled
    {
        get => _sixelGraphicsEnabled;
        set
        {
            if (_sixelGraphicsEnabled == value)
            {
                return;
            }

            _sixelGraphicsEnabled = value;
            if (!value)
            {
                _screen.ClearRasterGraphics();
                _sixelDisplayMode = false;
            }
        }
    }

    public BasicVtProcessor(TerminalScreen screen)
        : this(screen, null)
    {
    }

    /// <summary>
    /// Creates a managed VT processor with explicit options.
    /// </summary>
    public BasicVtProcessor(TerminalScreen screen, BasicVtProcessorOptions? options)
    {
        _screen = screen;
        _options = options ?? BasicVtProcessorOptions.Default;
        _sixelDecoder = new SixelDecoder(_options.SixelDecoderOptions);
        _sixelGraphicsEnabled = _options.SixelGraphicsEnabled;
        _theme = screen.Theme;
        _currentFg = screen.DefaultForeground;
        _currentBg = screen.DefaultBackground;
        _scrollBottom = screen.ViewportRows - 1;
        ResetExtendedDecModesToDefaults();
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
        if (data.IsEmpty)
        {
            return;
        }

        TerminalModeState before = ModeState;

        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];

            if (TryHandleAnywhereCancelControl(b))
            {
                continue;
            }

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

    /// <inheritdoc />
    public bool TryEncodePointer(
        in TerminalPointerEvent pointerEvent,
        in TerminalPointerEncodingContext context,
        out byte[] sequence)
    {
        sequence = [];
        if (context.CellWidthPx <= 0 || context.CellHeightPx <= 0)
        {
            return false;
        }

        double contentX = pointerEvent.X - context.PaddingLeftPx;
        double contentY = pointerEvent.Y - context.PaddingTopPx;
        int column = Math.Clamp((int)Math.Floor(contentX / context.CellWidthPx) + 1, 1, Math.Max(1, _screen.Columns));
        int row = Math.Clamp((int)Math.Floor(contentY / context.CellHeightPx) + 1, 1, Math.Max(1, _screen.ViewportRows));
        int pixelX = Math.Max(1, (int)Math.Floor(contentX) + 1);
        int pixelY = Math.Max(1, (int)Math.Floor(contentY) + 1);

        return TerminalMouseProtocolEncoder.TryEncode(
            pointerEvent,
            MouseModeState,
            column,
            row,
            pixelX,
            pixelY,
            out sequence);
    }

    /// <inheritdoc />
    public string? ReadSelection(in TerminalSelectionRange selection)
    {
        TerminalSelectionRange normalized = selection.Normalize();
        int startCol = normalized.StartColumn;
        int startRow = normalized.StartRow;
        int endCol = normalized.EndColumn;
        int endRow = normalized.EndRow;

        if (startRow > endRow || _screen.ViewportRows <= 0 || _screen.Columns <= 0)
        {
            return null;
        }

        StringBuilder builder = new();
        for (int row = startRow; row <= endRow; row++)
        {
            if (row < 0 || row >= _screen.ViewportRows)
            {
                continue;
            }

            TerminalRow terminalRow = _screen.GetViewportRow(row);
            if (!TryGetSelectionColumnRange(normalized, row, out int rowStart, out int rowEnd))
            {
                continue;
            }

            for (int col = rowStart; col <= rowEnd; col++)
            {
                ref TerminalCell cell = ref terminalRow[col];
                if (cell.Width == 0)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(cell.Grapheme))
                {
                    builder.Append(cell.Grapheme);
                }
                else if (cell.Codepoint != 0)
                {
                    builder.Append(char.ConvertFromUtf32(cell.Codepoint));
                }
            }

            if (row < endRow)
            {
                builder.AppendLine();
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <inheritdoc />
    public bool IsPasteSafe(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TerminalPasteEncoder.IsSafe(text);
    }

    /// <inheritdoc />
    public bool TryEncodePaste(string text, bool bracketedPaste, out byte[] sequence)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            sequence = [];
            return false;
        }

        sequence = TerminalPasteEncoder.Encode(text, bracketedPaste);
        return sequence.Length > 0;
    }

    /// <inheritdoc />
    public bool SupportsSnapshotFormat(TerminalSnapshotExportFormat format)
    {
        return format switch
        {
            TerminalSnapshotExportFormat.PlainText => true,
            TerminalSnapshotExportFormat.StyledVt => true,
            TerminalSnapshotExportFormat.Html => true,
            _ => false,
        };
    }

    /// <inheritdoc />
    public bool TryExportSnapshot(
        TerminalSnapshotExportFormat format,
        in TerminalSnapshotExportOptions options,
        out string snapshot)
    {
        if (!SupportsSnapshotFormat(format))
        {
            snapshot = string.Empty;
            return false;
        }

        snapshot = format switch
        {
            TerminalSnapshotExportFormat.PlainText => ExportPlainSnapshot(options),
            TerminalSnapshotExportFormat.StyledVt => ExportStyledVtSnapshot(options),
            TerminalSnapshotExportFormat.Html => ExportHtmlSnapshot(options),
            _ => string.Empty,
        };

        return snapshot.Length > 0;
    }

    private string ExportPlainSnapshot(in TerminalSnapshotExportOptions options)
    {
        if (_screen.TotalRows <= 0 || _screen.Columns <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();

        if (options.Selection is TerminalSelectionRange selection)
        {
            TerminalSelectionRange normalized = selection.Normalize();
            int viewportTopAbsoluteRow = GetViewportTopAbsoluteRow();
            bool unwrapRows = options.Unwrap && !normalized.Rectangle;
            for (int viewportRow = normalized.StartRow; viewportRow <= normalized.EndRow; viewportRow++)
            {
                int absoluteRow = viewportTopAbsoluteRow + viewportRow;
                if ((uint)absoluteRow >= (uint)_screen.TotalRows)
                {
                    continue;
                }

                if (!TryGetSelectionColumnRange(normalized, viewportRow, out int rowStart, out int rowEnd))
                {
                    continue;
                }

                TerminalRow row = _screen.GetRow(absoluteRow);
                AppendRowPlainText(row, rowStart, rowEnd, options.TrimTrailingWhitespace, builder);

                if (ShouldAppendSnapshotLineBreak(
                    row,
                    unwrapRows,
                    viewportRow,
                    normalized.EndRow))
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        int lastRowIndex = options.TrimTrailingWhitespace
            ? GetSnapshotLastRowIndex(visual: false)
            : _screen.TotalRows - 1;
        if (lastRowIndex < 0)
        {
            return string.Empty;
        }

        for (int absoluteRow = 0; absoluteRow <= lastRowIndex; absoluteRow++)
        {
            TerminalRow row = _screen.GetRow(absoluteRow);
            AppendRowPlainText(row, 0, row.Columns - 1, options.TrimTrailingWhitespace, builder);

            if (ShouldAppendSnapshotLineBreak(row, options.Unwrap, absoluteRow, lastRowIndex))
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private string ExportStyledVtSnapshot(in TerminalSnapshotExportOptions options)
    {
        if (_screen.TotalRows <= 0 || _screen.Columns <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        builder.Append("\x1b[0m");

        SnapshotCellStyleKey? currentStyle = null;
        string? currentHyperlink = null;

        if (options.Selection is TerminalSelectionRange selection)
        {
            TerminalSelectionRange normalized = selection.Normalize();
            int viewportTopAbsoluteRow = GetViewportTopAbsoluteRow();
            bool unwrapRows = options.Unwrap && !normalized.Rectangle;
            for (int viewportRow = normalized.StartRow; viewportRow <= normalized.EndRow; viewportRow++)
            {
                int absoluteRow = viewportTopAbsoluteRow + viewportRow;
                if ((uint)absoluteRow >= (uint)_screen.TotalRows)
                {
                    continue;
                }

                if (!TryGetSelectionColumnRange(normalized, viewportRow, out int rowStart, out int rowEnd))
                {
                    continue;
                }

                AppendStyledSnapshotRow(
                    builder,
                    _screen.GetRow(absoluteRow),
                    rowStart,
                    rowEnd,
                    options,
                    ref currentStyle,
                    ref currentHyperlink);

                if (ShouldAppendSnapshotLineBreak(
                    _screen.GetRow(absoluteRow),
                    unwrapRows,
                    viewportRow,
                    normalized.EndRow))
                {
                    builder.AppendLine();
                }
            }
        }
        else
        {
            int lastRowIndex = options.TrimTrailingWhitespace
                ? GetSnapshotLastRowIndex(visual: true)
                : _screen.TotalRows - 1;
            if (lastRowIndex < 0)
            {
                return string.Empty;
            }

            for (int absoluteRow = 0; absoluteRow <= lastRowIndex; absoluteRow++)
            {
                TerminalRow row = _screen.GetRow(absoluteRow);
                AppendStyledSnapshotRow(
                    builder,
                    row,
                    0,
                    row.Columns - 1,
                    options,
                    ref currentStyle,
                    ref currentHyperlink);

                if (ShouldAppendSnapshotLineBreak(row, options.Unwrap, absoluteRow, lastRowIndex))
                {
                    builder.AppendLine();
                }
            }
        }

        CloseStyledHyperlink(builder, ref currentHyperlink);
        builder.Append("\x1b[0m");
        AppendStyledVtExtras(builder, options);
        return builder.ToString();
    }

    private string ExportHtmlSnapshot(in TerminalSnapshotExportOptions options)
    {
        if (_screen.TotalRows <= 0 || _screen.Columns <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        builder.Append("<!DOCTYPE html><html><body style=\"margin:0;\">");
        builder.Append("<pre class=\"terminal-snapshot\" style=\"margin:0;white-space:pre;\">");

        if (options.Selection is TerminalSelectionRange selection)
        {
            TerminalSelectionRange normalized = selection.Normalize();
            int viewportTopAbsoluteRow = GetViewportTopAbsoluteRow();
            bool unwrapRows = options.Unwrap && !normalized.Rectangle;
            for (int viewportRow = normalized.StartRow; viewportRow <= normalized.EndRow; viewportRow++)
            {
                int absoluteRow = viewportTopAbsoluteRow + viewportRow;
                if ((uint)absoluteRow >= (uint)_screen.TotalRows)
                {
                    continue;
                }

                if (!TryGetSelectionColumnRange(normalized, viewportRow, out int rowStart, out int rowEnd))
                {
                    continue;
                }

                TerminalRow row = _screen.GetRow(absoluteRow);
                AppendHtmlSnapshotRow(builder, row, rowStart, rowEnd, options);
                if (ShouldAppendSnapshotLineBreak(row, unwrapRows, viewportRow, normalized.EndRow))
                {
                    builder.Append('\n');
                }
            }
        }
        else
        {
            int lastRowIndex = options.TrimTrailingWhitespace
                ? GetSnapshotLastRowIndex(visual: true)
                : _screen.TotalRows - 1;
            if (lastRowIndex < 0)
            {
                return string.Empty;
            }

            for (int absoluteRow = 0; absoluteRow <= lastRowIndex; absoluteRow++)
            {
                TerminalRow row = _screen.GetRow(absoluteRow);
                AppendHtmlSnapshotRow(builder, row, 0, row.Columns - 1, options);
                if (ShouldAppendSnapshotLineBreak(row, options.Unwrap, absoluteRow, lastRowIndex))
                {
                    builder.Append('\n');
                }
            }
        }

        builder.Append("</pre></body></html>");
        return builder.ToString();
    }

    private static void AppendRowPlainText(
        TerminalRow terminalRow,
        int startColumn,
        int endColumn,
        bool trimTrailingWhitespace,
        StringBuilder builder)
    {
        int originalLength = builder.Length;
        int clampedStart = Math.Max(0, startColumn);
        int clampedEnd = Math.Min(terminalRow.Columns - 1, endColumn);
        if (clampedEnd < clampedStart)
        {
            return;
        }

        for (int col = clampedStart; col <= clampedEnd; col++)
        {
            ref TerminalCell cell = ref terminalRow[col];
            if (cell.Width == 0)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                builder.Append(cell.Grapheme);
            }
            else if (cell.Codepoint != 0)
            {
                builder.Append(char.ConvertFromUtf32(cell.Codepoint));
            }
        }

        if (!trimTrailingWhitespace)
        {
            return;
        }

        int end = builder.Length - 1;
        while (end >= originalLength && char.IsWhiteSpace(builder[end]) && builder[end] is not '\r' and not '\n')
        {
            builder.Length--;
            end--;
        }
    }

    private bool TryGetSelectionColumnRange(
        in TerminalSelectionRange selection,
        int row,
        out int rowStart,
        out int rowEnd)
    {
        if (selection.Rectangle)
        {
            rowStart = Math.Min(selection.StartColumn, selection.EndColumn);
            rowEnd = Math.Max(selection.StartColumn, selection.EndColumn);
        }
        else
        {
            rowStart = row == selection.StartRow ? selection.StartColumn : 0;
            rowEnd = row == selection.EndRow ? selection.EndColumn : _screen.Columns - 1;
        }

        if (rowEnd < 0 || rowStart >= _screen.Columns)
        {
            return false;
        }

        rowStart = Math.Max(0, rowStart);
        rowEnd = Math.Min(_screen.Columns - 1, rowEnd);
        return rowEnd >= rowStart;
    }

    private int GetViewportTopAbsoluteRow()
    {
        return Math.Max(0, _screen.TotalRows - _screen.ViewportRows - _screen.ScrollOffset);
    }

    private static bool ShouldAppendSnapshotLineBreak(
        TerminalRow row,
        bool unwrap,
        int rowIndex,
        int lastRowIndex)
    {
        return rowIndex < lastRowIndex && (!unwrap || !row.WrapsToNext);
    }

    private void AppendStyledSnapshotRow(
        StringBuilder builder,
        TerminalRow row,
        int startColumn,
        int endColumn,
        in TerminalSnapshotExportOptions options,
        ref SnapshotCellStyleKey? currentStyle,
        ref string? currentHyperlink)
    {
        int exportEnd = GetSnapshotRowEndColumn(row, startColumn, endColumn, options.TrimTrailingWhitespace, visual: true);
        if (exportEnd < startColumn)
        {
            return;
        }

        for (int col = Math.Max(0, startColumn); col <= exportEnd; col++)
        {
            ref TerminalCell cell = ref row[col];
            if (cell.Width == 0)
            {
                continue;
            }

            string text = GetSnapshotCellText(cell, preserveEmptyCells: true);
            if (text.Length == 0)
            {
                continue;
            }

            string? desiredHyperlink = ResolveSnapshotHyperlink(cell, options.Extras.IncludeHyperlinks);
            if (!string.Equals(currentHyperlink, desiredHyperlink, StringComparison.Ordinal))
            {
                CloseStyledHyperlink(builder, ref currentHyperlink);
                if (!string.IsNullOrEmpty(desiredHyperlink))
                {
                    builder.Append("\x1b]8;;").Append(desiredHyperlink).Append("\x1b\\");
                    currentHyperlink = desiredHyperlink;
                }
            }

            SnapshotCellStyleKey style = CreateSnapshotStyleKey(cell);
            if (currentStyle is null || currentStyle.Value != style)
            {
                builder.Append(BuildStyledSgrSequence(cell));
                currentStyle = style;
            }

            builder.Append(text);
        }
    }

    private void AppendHtmlSnapshotRow(
        StringBuilder builder,
        TerminalRow row,
        int startColumn,
        int endColumn,
        in TerminalSnapshotExportOptions options)
    {
        int exportEnd = GetSnapshotRowEndColumn(row, startColumn, endColumn, options.TrimTrailingWhitespace, visual: true);
        if (exportEnd < startColumn)
        {
            return;
        }

        for (int col = Math.Max(0, startColumn); col <= exportEnd; col++)
        {
            ref TerminalCell cell = ref row[col];
            if (cell.Width == 0)
            {
                continue;
            }

            string text = GetSnapshotCellText(cell, preserveEmptyCells: true);
            if (text.Length == 0)
            {
                continue;
            }

            string encodedText = WebUtility.HtmlEncode(text);
            string style = BuildHtmlCellStyle(cell);
            string? hyperlink = ResolveSnapshotHyperlink(cell, options.Extras.IncludeHyperlinks);

            if (!string.IsNullOrEmpty(hyperlink))
            {
                builder.Append("<a href=\"")
                    .Append(WebUtility.HtmlEncode(hyperlink))
                    .Append("\" style=\"color:inherit;text-decoration:inherit;\">");
            }

            if (style.Length > 0)
            {
                builder.Append("<span style=\"")
                    .Append(style)
                    .Append("\">");
            }

            builder.Append(encodedText);

            if (style.Length > 0)
            {
                builder.Append("</span>");
            }

            if (!string.IsNullOrEmpty(hyperlink))
            {
                builder.Append("</a>");
            }
        }
    }

    private int GetSnapshotRowEndColumn(
        TerminalRow row,
        int startColumn,
        int endColumn,
        bool trimTrailingWhitespace,
        bool visual)
    {
        int clampedStart = Math.Max(0, startColumn);
        int clampedEnd = Math.Min(row.Columns - 1, endColumn);
        if (clampedEnd < clampedStart || !trimTrailingWhitespace)
        {
            return clampedEnd;
        }

        for (int col = clampedEnd; col >= clampedStart; col--)
        {
            TerminalCell cell = row[col];
            if (cell.Width == 0)
            {
                continue;
            }

            if (visual ? IsVisualSnapshotCell(cell) : IsPlainSnapshotCell(cell))
            {
                return col;
            }
        }

        return clampedStart - 1;
    }

    private int GetSnapshotLastRowIndex(bool visual)
    {
        for (int rowIndex = _screen.TotalRows - 1; rowIndex >= 0; rowIndex--)
        {
            TerminalRow row = _screen.GetRow(rowIndex);
            if (GetSnapshotRowEndColumn(row, 0, row.Columns - 1, trimTrailingWhitespace: true, visual) >= 0)
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private static bool IsPlainSnapshotCell(TerminalCell cell)
    {
        if (!cell.HasContent)
        {
            return false;
        }

        string text = GetSnapshotCellText(cell, preserveEmptyCells: false);
        return !string.IsNullOrEmpty(text) && !string.IsNullOrWhiteSpace(text);
    }

    private bool IsVisualSnapshotCell(TerminalCell cell)
    {
        if (cell.HasContent || cell.HyperlinkId > 0)
        {
            return true;
        }

        if (cell.Attributes != CellAttributes.None ||
            cell.UnderlineStyle != TerminalUnderlineStyle.None ||
            cell.HasUnderlineColor ||
            cell.Decorations != CellDecorations.None)
        {
            return true;
        }

        return cell.Foreground != _screen.DefaultForeground ||
               cell.Background != _screen.DefaultBackground ||
               !cell.HasBackground;
    }

    private static string GetSnapshotCellText(TerminalCell cell, bool preserveEmptyCells)
    {
        if (!string.IsNullOrEmpty(cell.Grapheme))
        {
            return cell.Grapheme;
        }

        if (cell.Codepoint != 0 && Rune.IsValid(cell.Codepoint))
        {
            return char.ConvertFromUtf32(cell.Codepoint);
        }

        return preserveEmptyCells ? " " : string.Empty;
    }

    private string? ResolveSnapshotHyperlink(TerminalCell cell, bool includeHyperlinks)
    {
        if (!includeHyperlinks || cell.HyperlinkId <= 0)
        {
            return null;
        }

        return _screen.TryGetHyperlinkUrl(cell.HyperlinkId, out string? url)
            ? url
            : null;
    }

    private string BuildStyledSgrSequence(TerminalCell cell)
    {
        List<int> parameters = [0];

        if ((cell.Attributes & CellAttributes.Bold) != 0) parameters.Add(1);
        if ((cell.Attributes & CellAttributes.Dim) != 0) parameters.Add(2);
        if ((cell.Attributes & CellAttributes.Italic) != 0) parameters.Add(3);

        TerminalUnderlineStyle underlineStyle = GetEffectiveUnderlineStyle(cell);
        if (underlineStyle == TerminalUnderlineStyle.Double)
        {
            parameters.Add(21);
        }
        else if (underlineStyle != TerminalUnderlineStyle.None)
        {
            parameters.Add(4);
        }

        if ((cell.Attributes & CellAttributes.Blink) != 0) parameters.Add(5);
        if ((cell.Attributes & CellAttributes.Inverse) != 0) parameters.Add(7);
        if ((cell.Attributes & CellAttributes.Hidden) != 0) parameters.Add(8);
        if ((cell.Attributes & CellAttributes.Strikethrough) != 0) parameters.Add(9);
        if ((cell.Decorations & CellDecorations.Overline) != 0) parameters.Add(53);

        AppendRgbParameters(parameters, foreground: true, cell.Foreground);
        AppendRgbParameters(parameters, foreground: false, cell.Background);

        if (cell.HasUnderlineColor)
        {
            parameters.Add(58);
            parameters.Add(2);
            parameters.Add((int)((cell.UnderlineColor >> 16) & 0xFF));
            parameters.Add((int)((cell.UnderlineColor >> 8) & 0xFF));
            parameters.Add((int)(cell.UnderlineColor & 0xFF));
        }

        return $"\x1b[{string.Join(';', parameters)}m";
    }

    private void AppendStyledVtExtras(StringBuilder builder, in TerminalSnapshotExportOptions options)
    {
        if (options.Extras.IncludePalette)
        {
            AppendPaletteSnapshot(builder);
        }

        if (options.Extras.IncludeModes)
        {
            AppendModeSnapshot(builder);
        }

        if (options.Extras.IncludeScrollingRegion)
        {
            builder.Append("\x1b[")
                .Append(_scrollTop + 1)
                .Append(';')
                .Append(_scrollBottom + 1)
                .Append('r');
        }

        if (options.Extras.IncludeTabstops)
        {
            AppendTabstopSnapshot(builder);
        }

        if (options.Extras.IncludeCharsets)
        {
            builder.Append(_g0IsLineDrawing ? "\x1b(0" : "\x1b(B");
            builder.Append(_g1IsLineDrawing ? "\x1b)0" : "\x1b)B");
            builder.Append(_shiftOut ? "\x0E" : "\x0F");
        }

        if (options.Extras.IncludeKittyKeyboard)
        {
            builder.Append("\x1b[=")
                .Append(KittyKeyboardFlags.ToString(CultureInfo.InvariantCulture))
                .Append('u');
        }

        if (options.Extras.IncludeKeyboardModes)
        {
            AppendKeyboardModeSnapshot(builder);
        }

        if (options.Extras.IncludeCursor)
        {
            builder.Append("\x1b[")
                .Append(_cursorRow + 1)
                .Append(';')
                .Append(_cursorCol + 1)
                .Append('H');
        }

        if (options.Extras.IncludeStyle)
        {
            builder.Append("\x1b[")
                .Append(BuildCurrentSgrState())
                .Append('m');
        }
    }

    private void AppendPaletteSnapshot(StringBuilder builder)
    {
        builder.Append("\x1b]10;")
            .Append(ToOscRgb(_screen.DefaultForeground))
            .Append("\x1b\\");
        builder.Append("\x1b]11;")
            .Append(ToOscRgb(_screen.DefaultBackground))
            .Append("\x1b\\");
        builder.Append("\x1b]12;")
            .Append(ToOscRgb(_theme.CursorColor))
            .Append("\x1b\\");

        for (int index = 0; index < 256; index++)
        {
            builder.Append("\x1b]4;")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .Append(';')
                .Append(ToOscRgb(_theme.Palette[index]))
                .Append("\x1b\\");
        }
    }

    private void AppendModeSnapshot(StringBuilder builder)
    {
        AppendMode(builder, ansi: false, 6, _originMode);
        AppendMode(builder, ansi: false, 7, _autoWrap);
        AppendMode(builder, ansi: false, 25, _cursorVisible);
        if (_sixelGraphicsEnabled)
        {
            AppendMode(builder, ansi: false, 80, _sixelDisplayMode);
        }

        AppendMode(builder, ansi: false, 1049, _inAltScreen);
        AppendMode(builder, ansi: false, 1004, FocusEventsEnabled);
        AppendMode(builder, ansi: false, 2004, _bracketedPaste);
        AppendMode(builder, ansi: false, 2031, _extendedDecModesEnabled.Contains(2031));
        AppendMode(builder, ansi: false, 2048, _extendedDecModesEnabled.Contains(2048));
        AppendMode(builder, ansi: false, 9001, _win32InputMode);

        for (int i = 0; i < ExtendedDecModes.Length; i++)
        {
            int mode = ExtendedDecModes[i];
            if (mode is 1004 or 2004 or 2031 or 2048)
            {
                continue;
            }

            AppendMode(builder, ansi: false, mode, _extendedDecModesEnabled.Contains(mode));
        }
    }

    private void AppendKeyboardModeSnapshot(StringBuilder builder)
    {
        AppendMode(builder, ansi: false, 1, _applicationCursorKeys);
        builder.Append(_applicationKeypad ? "\x1b=" : "\x1b>");
        AppendMode(builder, ansi: false, 67, _backarrowKeyMode);
        AppendMode(builder, ansi: true, 2, _keyboardLocked);
        AppendMode(builder, ansi: true, 4, _insertMode);
        AppendMode(builder, ansi: true, 12, _sendReceiveMode);
        AppendMode(builder, ansi: true, 20, _lineFeedNewLineMode);
    }

    private static void AppendMode(StringBuilder builder, bool ansi, int value, bool enabled)
    {
        builder.Append("\x1b[");
        if (!ansi)
        {
            builder.Append('?');
        }

        builder.Append(value.ToString(CultureInfo.InvariantCulture))
            .Append(enabled ? 'h' : 'l');
    }

    private void AppendTabstopSnapshot(StringBuilder builder)
    {
        builder.Append("\x1b[3g");
        if (_tabStops.Count == 0)
        {
            return;
        }

        int[] orderedTabStops = new int[_tabStops.Count];
        _tabStops.CopyTo(orderedTabStops);
        Array.Sort(orderedTabStops);
        for (int i = 0; i < orderedTabStops.Length; i++)
        {
            int tabStop = orderedTabStops[i];
            builder.Append("\x1b[")
                .Append(tabStop + 1)
                .Append('G')
                .Append("\x1bH");
        }
    }

    private string BuildHtmlCellStyle(TerminalCell cell)
    {
        StringBuilder builder = new();

        GetEffectiveHtmlColors(cell, out uint foreground, out uint background);
        builder.Append("color:")
            .Append(ToCssColor(foreground))
            .Append(';');

        if (cell.HasBackground || background != _screen.DefaultBackground || (cell.Attributes & CellAttributes.Inverse) != 0)
        {
            builder.Append("background-color:")
                .Append(ToCssColor(background))
                .Append(';');
        }

        if ((cell.Attributes & CellAttributes.Bold) != 0)
        {
            builder.Append("font-weight:bold;");
        }

        if ((cell.Attributes & CellAttributes.Italic) != 0)
        {
            builder.Append("font-style:italic;");
        }

        if ((cell.Attributes & CellAttributes.Dim) != 0)
        {
            builder.Append("opacity:0.7;");
        }

        if ((cell.Attributes & CellAttributes.Hidden) != 0)
        {
            builder.Append("visibility:hidden;");
        }

        AppendHtmlTextDecorations(builder, cell);
        return builder.ToString();
    }

    private void AppendHtmlTextDecorations(StringBuilder builder, TerminalCell cell)
    {
        List<string> lines = [];
        TerminalUnderlineStyle underlineStyle = GetEffectiveUnderlineStyle(cell);
        if (underlineStyle != TerminalUnderlineStyle.None)
        {
            lines.Add("underline");
        }

        if ((cell.Attributes & CellAttributes.Strikethrough) != 0)
        {
            lines.Add("line-through");
        }

        if ((cell.Decorations & CellDecorations.Overline) != 0)
        {
            lines.Add("overline");
        }

        if (lines.Count == 0)
        {
            return;
        }

        builder.Append("text-decoration-line:")
            .Append(string.Join(' ', lines))
            .Append(';');

        if (underlineStyle != TerminalUnderlineStyle.None)
        {
            builder.Append("text-decoration-style:")
                .Append(underlineStyle switch
                {
                    TerminalUnderlineStyle.Double => "double",
                    TerminalUnderlineStyle.Curly => "wavy",
                    TerminalUnderlineStyle.Dotted => "dotted",
                    TerminalUnderlineStyle.Dashed => "dashed",
                    _ => "solid",
                })
                .Append(';');
        }

        if (cell.HasUnderlineColor)
        {
            builder.Append("text-decoration-color:")
                .Append(ToCssColor(cell.UnderlineColor))
                .Append(';');
        }
    }

    private void GetEffectiveHtmlColors(TerminalCell cell, out uint foreground, out uint background)
    {
        foreground = cell.Foreground;
        background = cell.HasBackground ? cell.Background : _screen.DefaultBackground;

        if ((cell.Attributes & CellAttributes.Inverse) != 0)
        {
            (foreground, background) = (background, foreground);
        }
    }

    private static TerminalUnderlineStyle GetEffectiveUnderlineStyle(TerminalCell cell)
    {
        if (cell.UnderlineStyle != TerminalUnderlineStyle.None)
        {
            return cell.UnderlineStyle;
        }

        return (cell.Attributes & CellAttributes.Underline) != 0
            ? TerminalUnderlineStyle.Single
            : TerminalUnderlineStyle.None;
    }

    private static void CloseStyledHyperlink(StringBuilder builder, ref string? currentHyperlink)
    {
        if (string.IsNullOrEmpty(currentHyperlink))
        {
            return;
        }

        builder.Append("\x1b]8;;\x1b\\");
        currentHyperlink = null;
    }

    private static void AppendRgbParameters(List<int> parameters, bool foreground, uint argb)
    {
        parameters.Add(foreground ? 38 : 48);
        parameters.Add(2);
        parameters.Add((int)((argb >> 16) & 0xFF));
        parameters.Add((int)((argb >> 8) & 0xFF));
        parameters.Add((int)(argb & 0xFF));
    }

    private static string ToCssColor(uint argb)
    {
        return string.Create(
            7,
            argb,
            static (span, color) =>
            {
                span[0] = '#';
                byte r = (byte)((color >> 16) & 0xFF);
                byte g = (byte)((color >> 8) & 0xFF);
                byte b = (byte)(color & 0xFF);
                r.TryFormat(span[1..3], out _, "X2", CultureInfo.InvariantCulture);
                g.TryFormat(span[3..5], out _, "X2", CultureInfo.InvariantCulture);
                b.TryFormat(span[5..7], out _, "X2", CultureInfo.InvariantCulture);
            });
    }

    private static string ToOscRgb(uint argb)
    {
        return string.Create(
            12,
            argb,
            static (span, color) =>
            {
                span[0] = 'r';
                span[1] = 'g';
                span[2] = 'b';
                span[3] = ':';
                byte r = (byte)((color >> 16) & 0xFF);
                byte g = (byte)((color >> 8) & 0xFF);
                byte b = (byte)(color & 0xFF);
                r.TryFormat(span[4..6], out _, "X2", CultureInfo.InvariantCulture);
                span[6] = '/';
                g.TryFormat(span[7..9], out _, "X2", CultureInfo.InvariantCulture);
                span[9] = '/';
                b.TryFormat(span[10..12], out _, "X2", CultureInfo.InvariantCulture);
            });
    }

    private readonly record struct SnapshotCellStyleKey(
        uint Foreground,
        uint Background,
        CellAttributes Attributes,
        TerminalUnderlineStyle UnderlineStyle,
        uint UnderlineColor,
        bool HasUnderlineColor,
        CellDecorations Decorations,
        bool HasBackground);

    private static SnapshotCellStyleKey CreateSnapshotStyleKey(TerminalCell cell)
    {
        return new SnapshotCellStyleKey(
            cell.Foreground,
            cell.Background,
            cell.Attributes,
            cell.UnderlineStyle,
            cell.UnderlineColor,
            cell.HasUnderlineColor,
            cell.Decorations,
            cell.HasBackground);
    }

    private void EnterCsiState()
    {
        _state = ParserState.CsiEntry;
        _params.Clear();
        _currentParam = 0;
        _hasParam = false;
        _csiPrivateMarker = '\0';
        _intermediateChar = '\0';
    }

    private void EnterOscState()
    {
        _state = ParserState.OscString;
        _oscBuffer.Clear();
        _isDiscardingOscPayload = false;
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
                ResetDelayedWrap();
                if (_lineFeedNewLineMode)
                {
                    _cursorCol = 0;
                }
                LineFeed(wrapForced: false);
                break;

            case (byte)'\r': // CR
                ResetDelayedWrap();
                _cursorCol = 0;
                break;

            case 0x08: // BS — Backspace
                ResetDelayedWrap();
                if (_cursorCol > 0) _cursorCol--;
                break;

            case (byte)'\t': // HT — Horizontal Tab
                ResetDelayedWrap();
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
        bool shouldAttemptGraphemeAppend = ShouldAttemptGraphemeAppend(codepoint);
        if (_delayedWrap &&
            shouldAttemptGraphemeAppend &&
            TryAppendToPreviousCellGrapheme(codepoint))
        {
            return;
        }

        if (ConsumeDelayedWrapBeforePrint())
        {
            ClampCursor();
        }

        if (shouldAttemptGraphemeAppend && TryAppendToPreviousCellGrapheme(codepoint))
        {
            return;
        }

        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;
        if (_cursorCol < 0 || _cursorCol >= _screen.Columns) return;

        TerminalRow row = _screen.GetViewportRow(_cursorRow);
        if (_cursorCol >= row.Columns) return;
        ClearPreservedCellsForMutation(row);

        int width = TerminalCellWidthCalculator.GetCellWidth(codepoint);
        width = width <= 1 ? 1 : 2;

        if (width == 2 && _cursorCol == _screen.Columns - 1)
        {
            if (_autoWrap)
            {
                ResetDelayedWrap();
                _cursorCol = 0;
                LineFeed(wrapForced: true);
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
            ClearPreservedCellsForMutation(row);
        }

        if (_insertMode)
        {
            InsertCharacters(width);
            row = _screen.GetViewportRow(_cursorRow);
            if (_cursorCol < 0 || _cursorCol >= row.Columns)
            {
                return;
            }

            ClearPreservedCellsForMutation(row);
        }

        ClearRasterGraphicsForTextMutation(_cursorRow, _cursorCol, width);
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
        cell.UnderlineColor = _currentUnderlineColor;
        cell.HasUnderlineColor = _currentHasUnderlineColor;
        cell.Decorations = _currentDecorations;
        cell.HasBackground = true;
        cell.HyperlinkId = _currentHyperlinkId;
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
            spacer.UnderlineColor = _currentUnderlineColor;
            spacer.HasUnderlineColor = _currentHasUnderlineColor;
            spacer.Decorations = _currentDecorations;
            spacer.HasBackground = true;
            spacer.HyperlinkId = _currentHyperlinkId;
            spacer.Width = 0;
        }

        row.IsDirty = true;

        AdvanceCursorAfterGraphic(width);
        _lastGraphicCodepoint = codepoint;
    }

    private static bool ShouldAttemptGraphemeAppend(int codepoint)
    {
        if (!Rune.IsValid(codepoint))
        {
            return false;
        }

        if (codepoint == ZeroWidthJoinerCodepoint ||
            codepoint == CombiningKeycapCodepoint ||
            IsVariationSelector(codepoint) ||
            IsEmojiModifier(codepoint) ||
            IsRegionalIndicator(codepoint) ||
            IsEmojiTag(codepoint) ||
            IsDefaultEmojiPresentation(codepoint) ||
            IsLegacyEmojiPresentation(codepoint))
        {
            return true;
        }

        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(codepoint);
        return category is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark or
            UnicodeCategory.Format;
    }

    private static bool IsVariationSelector(int codepoint)
        => (codepoint >= 0xFE00 && codepoint <= 0xFE0F) ||
           (codepoint >= 0xE0100 && codepoint <= 0xE01EF);

    private static bool IsEmojiModifier(int codepoint)
        => codepoint >= 0x1F3FB && codepoint <= 0x1F3FF;

    private static bool IsRegionalIndicator(int codepoint)
        => codepoint >= 0x1F1E6 && codepoint <= 0x1F1FF;

    private static bool IsEmojiTag(int codepoint)
        => codepoint >= 0xE0020 && codepoint <= 0xE007F;

    private static bool IsDefaultEmojiPresentation(int codepoint)
        => codepoint >= 0x1F300 && codepoint <= 0x1FAFF;

    private static bool IsLegacyEmojiPresentation(int codepoint)
        => codepoint is 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x2328 or 0x23CF or 0x24C2 or
               0x25B6 or 0x25C0 or 0x3030 or 0x303D or 0x3297 or 0x3299 ||
           (codepoint >= 0x2194 && codepoint <= 0x21AA) ||
           (codepoint >= 0x231A && codepoint <= 0x231B) ||
           (codepoint >= 0x23E9 && codepoint <= 0x23F3) ||
           (codepoint >= 0x23F8 && codepoint <= 0x23FA) ||
           (codepoint >= 0x25AA && codepoint <= 0x25AB) ||
           (codepoint >= 0x25FB && codepoint <= 0x25FE) ||
           (codepoint >= 0x2600 && codepoint <= 0x27BF) ||
           (codepoint >= 0x2934 && codepoint <= 0x2935) ||
           (codepoint >= 0x2B05 && codepoint <= 0x2B55);

    private bool TryAppendToPreviousCellGrapheme(int codepoint)
    {
        if (!Rune.IsValid(codepoint))
        {
            return false;
        }

        int targetRowIndex = _cursorRow;
        int targetColIndex = _delayedWrap ? _cursorCol : _cursorCol - 1;

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

        ClearPreservedCellsForMutation(targetRow);
        ClearRasterGraphicsForTextMutation(targetRowIndex, targetColIndex, Math.Max(oldWidth, newWidth));

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
            spacer.UnderlineColor = targetCell.UnderlineColor;
            spacer.HasUnderlineColor = targetCell.HasUnderlineColor;
            spacer.Decorations = targetCell.Decorations;
            spacer.HasBackground = targetCell.HasBackground;
            spacer.HyperlinkId = targetCell.HyperlinkId;
            spacer.Width = 0;
        }

        if (oldWidth == 2 && newWidth == 1 && targetColIndex + 1 < targetRow.Columns)
        {
            targetRow[targetColIndex + 1] = TerminalCell.Empty(targetCell.Foreground, targetCell.Background);
        }

        if (!_delayedWrap && targetRowIndex == _cursorRow && targetColIndex + oldWidth == _cursorCol)
        {
            _cursorCol = targetColIndex + newWidth;
        }

        targetRow.IsDirty = true;
        return true;
    }

    private bool ConsumeDelayedWrapBeforePrint()
    {
        if (!_delayedWrap)
        {
            return false;
        }

        _delayedWrap = false;
        if (!_autoWrap)
        {
            return false;
        }

        _cursorCol = 0;
        LineFeed(wrapForced: true);
        return true;
    }

    private void AdvanceCursorAfterGraphic(int width)
    {
        int nextColumn = _cursorCol + Math.Max(1, width);
        if (_autoWrap && nextColumn >= _screen.Columns)
        {
            _cursorCol = Math.Max(0, _screen.Columns - 1);
            _delayedWrap = true;
            return;
        }

        _cursorCol = Math.Min(nextColumn, Math.Max(0, _screen.Columns - 1));
        _delayedWrap = false;
    }

    private void ResetDelayedWrap()
    {
        _delayedWrap = false;
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

    private void LineFeed(bool wrapForced)
    {
        if (_cursorRow >= 0 && _cursorRow < _screen.ViewportRows)
        {
            _screen.GetViewportRow(_cursorRow).WrapsToNext = wrapForced;
        }

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
            _screen.InvalidateViewport();
        }
        else
        {
            // Scroll within region: shift rows up, insert blank at bottom of region
            _screen.ShiftRasterGraphicsInViewportRows(_scrollTop, _scrollBottom, rowDelta: -1);
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
            _screen.InvalidateViewport();
        }
    }

    private void ScrollDownInRegion()
    {
        // Shift rows down within the scroll region, insert blank at top of region
        _screen.ShiftRasterGraphicsInViewportRows(_scrollTop, _scrollBottom, rowDelta: 1);
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
        _screen.InvalidateViewport();
    }

    private void CopyRow(TerminalRow src, TerminalRow dst)
    {
        dst.CopyActiveFrom(src, _screen.DefaultForeground, _screen.DefaultBackground);
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
                ResetDelayedWrap();
                LineFeed(wrapForced: false);
                _state = ParserState.Ground;
                break;

            case (byte)'E': // NEL — Next line
                ResetDelayedWrap();
                _cursorCol = 0;
                LineFeed(wrapForced: false);
                _state = ParserState.Ground;
                break;

            case (byte)'M': // RI — Reverse index
                ResetDelayedWrap();
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
            _csiPrivateMarker = (char)b;
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

        if (TryAbortBrokenControlStringOnControl(b))
        {
            return;
        }

        AppendOscByteOrDiscard(b);
    }

    private void ProcessOscEscape(byte b)
    {
        if (b == (byte)'\\')
        {
            HandleOscString();
            _state = ParserState.Ground;
            return;
        }

        if (TryAbortBrokenControlStringOnControl(b))
        {
            return;
        }

        // False alarm: preserve ESC as payload and continue OSC parsing.
        AppendOscByteOrDiscard(0x1B);

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

        AppendOscByteOrDiscard(b);
        _state = ParserState.OscString;
    }

    private void HandleOscString()
    {
        if (_isDiscardingOscPayload)
        {
            _oscBuffer.Clear();
            _isDiscardingOscPayload = false;
            return;
        }

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

            case 8:
                HandleOscHyperlink(value);
                break;
        }
    }

    private void HandleOscHyperlink(string value)
    {
        int separator = value.IndexOf(';');
        if (separator < 0)
        {
            return;
        }

        string uri = separator + 1 < value.Length
            ? value[(separator + 1)..]
            : string.Empty;
        if (string.IsNullOrEmpty(uri))
        {
            _currentHyperlinkId = 0;
            return;
        }

        _currentHyperlinkId = _screen.RegisterHyperlink(uri);
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

        if (TryAbortBrokenControlStringOnControl(b))
        {
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

        if (TryAbortBrokenControlStringOnControl(b))
        {
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

        byte[] payloadBytes = _dcsBuffer.ToArray();
        _dcsBuffer.Clear();

        if (_sixelGraphicsEnabled && IsSixelDcsPayload(payloadBytes))
        {
            HandleSixelDcsPayload(payloadBytes);
            return;
        }

        string payload = Encoding.ASCII.GetString(payloadBytes);

        // DCS $ q Pt ST — DECRQSS request.
        if (payload.StartsWith("$q", StringComparison.Ordinal))
        {
            string request = payload.Length > 2 ? payload[2..] : string.Empty;
            HandleDecRequestStatusString(request);
        }
    }

    private void HandleSixelDcsPayload(ReadOnlySpan<byte> payload)
    {
        SixelDecodeResult result = _sixelDecoder.Decode(payload);
        if (!result.Success || result.Image is null)
        {
            return;
        }

        int cellWidthPx = GetEffectiveCellWidthPx();
        int cellHeightPx = GetEffectiveCellHeightPx();
        int anchorColumn = _cursorCol;
        int anchorViewportRow = _cursorRow;
        int overflowRows = 0;

        if (_sixelDisplayMode)
        {
            anchorColumn = 0;
            anchorViewportRow = 0;
        }
        else
        {
            int imageRows = Math.Max(1, DivideRoundUp(result.Image.Height, cellHeightPx));
            overflowRows = Math.Max(0, _cursorRow + imageRows - 1 - _scrollBottom);
            for (int i = 0; i < overflowRows; i++)
            {
                ScrollUpInRegion();
            }

            anchorViewportRow = Math.Clamp(_cursorRow - overflowRows, _scrollTop, _scrollBottom);
        }

        int anchorAbsoluteRow = _screen.GetAbsoluteRowForViewportRow(anchorViewportRow);
        int imageId = _screen.AllocateRasterImageId();
        TerminalRasterImageSource source = new(
            imageId,
            TerminalRasterImageProtocol.Sixel,
            result.Image.Width,
            result.Image.Height,
            result.Image.RgbaPixels);
        TerminalRasterImagePlacement placement = new(
            imageId,
            TerminalRasterImageLayer.BelowText,
            anchorColumn,
            anchorAbsoluteRow,
            xOffsetPx: 0,
            yOffsetPx: 0,
            widthPx: result.Image.Width,
            heightPx: result.Image.Height,
            sourceX: 0,
            sourceY: 0,
            sourceWidth: result.Image.Width,
            sourceHeight: result.Image.Height,
            cellWidthPx,
            cellHeightPx);

        _screen.ReplaceRasterImage(source, placement);
        if (!_sixelDisplayMode)
        {
            AdvanceCursorAfterSixel(anchorViewportRow, result.FinalCursorX, result.FinalCursorY, cellWidthPx, cellHeightPx);
        }
    }

    private void ClearRasterGraphicsForTextMutation(int viewportRow, int startColumn, int width)
    {
        if (!_screen.HasRasterGraphics || width <= 0)
        {
            return;
        }

        int endColumn = Math.Min(_screen.Columns - 1, startColumn + width - 1);
        if (viewportRow < 0 || viewportRow >= _screen.ViewportRows || startColumn > endColumn)
        {
            return;
        }

        _screen.ClearRasterGraphicsInViewportRectangle(
            viewportRow,
            viewportRow,
            startColumn,
            endColumn);
    }

    private void AdvanceCursorAfterSixel(
        int anchorViewportRow,
        int finalCursorX,
        int finalCursorY,
        int cellWidthPx,
        int cellHeightPx)
    {
        int columnAdvance = DivideRoundUp(finalCursorX, cellWidthPx);
        int rowAdvance = Math.Max(0, finalCursorY / cellHeightPx);
        int nextColumn = _cursorCol + columnAdvance;
        int nextRow = anchorViewportRow + rowAdvance;

        while (nextColumn >= _screen.Columns)
        {
            nextColumn -= _screen.Columns;
            nextRow++;
        }

        while (nextRow > _scrollBottom)
        {
            ScrollUpInRegion();
            nextRow--;
        }

        _cursorCol = Math.Clamp(nextColumn, 0, Math.Max(0, _screen.Columns - 1));
        _cursorRow = Math.Clamp(nextRow, 0, Math.Max(0, _screen.ViewportRows - 1));
    }

    private int GetEffectiveCellWidthPx()
    {
        return _screen.Columns > 0 && _widthPx > 0
            ? Math.Max(1, _widthPx / _screen.Columns)
            : 1;
    }

    private int GetEffectiveCellHeightPx()
    {
        return _screen.ViewportRows > 0 && _heightPx > 0
            ? Math.Max(1, _heightPx / _screen.ViewportRows)
            : 1;
    }

    private static int DivideRoundUp(int value, int divisor)
    {
        if (value <= 0)
        {
            return 0;
        }

        return ((value - 1) / Math.Max(1, divisor)) + 1;
    }

    private static bool IsSixelDcsPayload(ReadOnlySpan<byte> payload)
    {
        int index = 0;
        while (index < payload.Length && payload[index] is >= 0x30 and <= 0x3F)
        {
            index++;
        }

        bool hasIntermediate = false;
        while (index < payload.Length && payload[index] is >= 0x20 and <= 0x2F)
        {
            hasIntermediate = true;
            index++;
        }

        return index < payload.Length &&
            payload[index] == (byte)'q' &&
            !hasIntermediate;
    }

    private void AppendDcsByteOrDiscard(byte b)
    {
        if (_isDiscardingDcsPayload)
        {
            return;
        }

        int maxDcsBytes = _sixelGraphicsEnabled
            ? Math.Max(MaxDcsBufferBytes, _options.SixelDecoderOptions.MaxInputBytes)
            : MaxDcsBufferBytes;
        if (_dcsBuffer.Count >= maxDcsBytes)
        {
            _dcsBuffer.Clear();
            _isDiscardingDcsPayload = true;
            return;
        }

        _dcsBuffer.Add(b);
    }

    private void AppendOscByteOrDiscard(byte b)
    {
        if (_isDiscardingOscPayload)
        {
            return;
        }

        if (_oscBuffer.Count >= MaxOscBufferBytes)
        {
            _oscBuffer.Clear();
            _isDiscardingOscPayload = true;
            return;
        }

        _oscBuffer.Add(b);
    }

    private bool TryHandleAnywhereCancelControl(byte b)
    {
        if (b is not 0x18 and not 0x1A)
        {
            return false;
        }

        AbortActiveControlString();
        return true;
    }

    private bool TryAbortBrokenControlStringOnControl(byte b)
    {
        if (_state is not ParserState.OscString and
            not ParserState.OscEscape and
            not ParserState.DcsString and
            not ParserState.DcsEscape)
        {
            return false;
        }

        if (_state is ParserState.DcsString or ParserState.DcsEscape &&
            IsIgnorableSixelControl(b) &&
            IsCurrentDcsSixelPayload())
        {
            return false;
        }

        if (!IsBrokenStringTerminatorControl(b))
        {
            return false;
        }

        // Recover from malformed OSC/DCS strings in binary streams so shell
        // prompt control bytes can return the parser to ground.
        AbortActiveControlString();
        ProcessGround(b);
        return true;
    }

    private bool IsCurrentDcsSixelPayload()
    {
        int index = 0;
        while (index < _dcsBuffer.Count && _dcsBuffer[index] is >= 0x30 and <= 0x3F)
        {
            index++;
        }

        bool hasIntermediate = false;
        while (index < _dcsBuffer.Count && _dcsBuffer[index] is >= 0x20 and <= 0x2F)
        {
            hasIntermediate = true;
            index++;
        }

        return index < _dcsBuffer.Count &&
            _dcsBuffer[index] == (byte)'q' &&
            !hasIntermediate;
    }

    private void AbortActiveControlString()
    {
        _params.Clear();
        _currentParam = 0;
        _hasParam = false;
        _csiPrivateMarker = '\0';
        _intermediateChar = '\0';
        _utf8Codepoint = 0;
        _utf8Remaining = 0;
        _oscBuffer.Clear();
        _dcsBuffer.Clear();
        _isDiscardingOscPayload = false;
        _isDiscardingDcsPayload = false;
        _state = ParserState.Ground;
    }

    private static bool IsBrokenStringTerminatorControl(byte b)
    {
        return b is 0x08 or // BS
            0x09 or // HT
            0x0A or // LF
            0x0B or // VT
            0x0C or // FF
            0x0D or // CR
            0x0E or // SO
            0x0F;   // SI
    }

    private static bool IsIgnorableSixelControl(byte b)
    {
        return b is 0x09 or // HT
            0x0A or // LF
            0x0B or // VT
            0x0C or // FF
            0x0D;   // CR
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

        // DEC private mode families: CSI ? ...
        if (_csiPrivateMarker == '?')
        {
            if (_intermediateChar == '$' && finalByte == 'p')
            {
                HandleDecModeQuery();
                return;
            }

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
        if (_csiPrivateMarker == '!' && finalByte == 'p')
        {
            SoftReset();
            return;
        }

        // CSI > ... c — Secondary DA
        if (_csiPrivateMarker == '>')
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
        if (_csiPrivateMarker == '=')
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
        if (_csiPrivateMarker == '<')
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

        // ANSI mode query family: CSI Ps $ p
        if (_csiPrivateMarker == '\0' && _intermediateChar == '$' && finalByte == 'p')
        {
            HandleAnsiModeQuery();
            return;
        }

        ResetDelayedWrapForCsi(finalByte);

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
                    ResponseCallback?.Invoke(GetPrimaryDeviceAttributesResponse());
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

    private void ResetDelayedWrapForCsi(char finalByte)
    {
        switch (finalByte)
        {
            case 'A': // CUU
            case 'B': // CUD
            case 'C': // CUF
            case 'D': // CUB
            case 'E': // CNL
            case 'F': // CPL
            case 'G': // CHA
            case 'H': // CUP
            case 'f': // HVP
            case 'J': // ED
            case 'K': // EL
            case 'L': // IL
            case 'M': // DL
            case 'P': // DCH
            case 'X': // ECH
            case '@': // ICH
            case 'S': // SU
            case 'T': // SD
            case 'Z': // CBT
            case 'I': // CHT
            case 'd': // VPA
            case 'e': // VPR
            case 'a': // HPR
            case 'g': // TBC
            case 'r': // DECSTBM
                ResetDelayedWrap();
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

    private void HandleDecModeQuery()
    {
        if (_params.Count == 0)
        {
            EmitDecModeQueryResponse(mode: 0, status: 0);
            return;
        }

        for (int i = 0; i < _params.Count; i++)
        {
            int mode = _params[i];
            int status = GetDecPrivateModeReportStatus(mode);
            EmitDecModeQueryResponse(mode, status);
        }
    }

    private void HandleAnsiModeQuery()
    {
        if (_params.Count == 0)
        {
            EmitAnsiModeQueryResponse(mode: 0, status: 0);
            return;
        }

        for (int i = 0; i < _params.Count; i++)
        {
            int mode = _params[i];
            int status = GetAnsiModeReportStatus(mode);
            EmitAnsiModeQueryResponse(mode, status);
        }
    }

    private int GetAnsiModeReportStatus(int mode)
    {
        return mode switch
        {
            2 => _keyboardLocked ? 1 : 2,
            4 => _insertMode ? 1 : 2,
            12 => _sendReceiveMode ? 1 : 2,
            20 => _lineFeedNewLineMode ? 1 : 2,
            _ => 0,
        };
    }

    private byte[] GetPrimaryDeviceAttributesResponse()
    {
        return _sixelGraphicsEnabled
            ? "\x1b[?62;1;4;6;22c"u8.ToArray()
            : "\x1b[?62;1;6;22c"u8.ToArray();
    }

    private int GetDecPrivateModeReportStatus(int mode)
    {
        if (mode == 80)
        {
            return _sixelGraphicsEnabled
                ? (_sixelDisplayMode ? 1 : 2)
                : 0;
        }

        int status = mode switch
        {
            1 => _applicationCursorKeys ? 1 : 2,
            6 => _originMode ? 1 : 2,
            7 => _autoWrap ? 1 : 2,
            25 => _cursorVisible ? 1 : 2,
            66 => _applicationKeypad ? 1 : 2,
            67 => _backarrowKeyMode ? 1 : 2,
            47 => _inAltScreen ? 1 : 2,
            1047 => _inAltScreen ? 1 : 2,
            1048 => _saveCursorMode ? 1 : 2,
            1049 => _inAltScreen ? 1 : 2,
            2004 => _bracketedPaste ? 1 : 2,
            9001 => _win32InputMode ? 1 : 2,
            _ => 0,
        };

        if (status != 0)
        {
            return status;
        }

        if (TryGetExtendedDecMode(mode, out bool enabled))
        {
            return enabled ? 1 : 2;
        }

        return 0;
    }

    private void EmitDecModeQueryResponse(int mode, int status)
    {
        string response = $"\x1b[?{Math.Max(0, mode)};{status}$y";
        ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
    }

    private void EmitAnsiModeQueryResponse(int mode, int status)
    {
        string response = $"\x1b[{Math.Max(0, mode)};{status}$y";
        ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
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

    private static bool IsExtendedDecModeSupported(int mode)
    {
        for (int i = 0; i < ExtendedDecModes.Length; i++)
        {
            if (ExtendedDecModes[i] == mode)
            {
                return true;
            }
        }

        return false;
    }

    private void SetExtendedDecMode(int mode, bool enabled)
    {
        if (!IsExtendedDecModeSupported(mode))
        {
            return;
        }

        if (enabled)
        {
            _extendedDecModesEnabled.Add(mode);
        }
        else
        {
            _extendedDecModesEnabled.Remove(mode);
        }
    }

    private bool TryGetExtendedDecMode(int mode, out bool enabled)
    {
        if (!IsExtendedDecModeSupported(mode))
        {
            enabled = false;
            return false;
        }

        enabled = _extendedDecModesEnabled.Contains(mode);
        return true;
    }

    private TerminalMouseTrackingMode GetMouseTrackingMode()
    {
        if (_extendedDecModesEnabled.Contains(1003))
        {
            return TerminalMouseTrackingMode.AnyMotion;
        }

        if (_extendedDecModesEnabled.Contains(1002))
        {
            return TerminalMouseTrackingMode.ButtonMotion;
        }

        if (_extendedDecModesEnabled.Contains(1000))
        {
            return TerminalMouseTrackingMode.PressRelease;
        }

        if (_extendedDecModesEnabled.Contains(9))
        {
            return TerminalMouseTrackingMode.X10Press;
        }

        return TerminalMouseTrackingMode.None;
    }

    private TerminalMouseEncoding GetMouseEncodingMode()
    {
        if (_extendedDecModesEnabled.Contains(1016))
        {
            return TerminalMouseEncoding.SgrPixels;
        }

        if (_extendedDecModesEnabled.Contains(1006))
        {
            return TerminalMouseEncoding.Sgr;
        }

        if (_extendedDecModesEnabled.Contains(1015))
        {
            return TerminalMouseEncoding.Urxvt;
        }

        if (_extendedDecModesEnabled.Contains(1005))
        {
            return TerminalMouseEncoding.Utf8;
        }

        return TerminalMouseEncoding.Default;
    }

    private void ResetExtendedDecModesToDefaults()
    {
        _extendedDecModesEnabled.Clear();
        for (int i = 0; i < ExtendedDecModesEnabledByDefault.Length; i++)
        {
            _extendedDecModesEnabled.Add(ExtendedDecModesEnabledByDefault[i]);
        }
    }

    private void HandleAnsiMode(int mode, bool set)
    {
        switch (mode)
        {
            case 2: // KAM — Keyboard action mode (keyboard locked).
                _keyboardLocked = set;
                break;

            case 4: // IRM — Insert/replace mode.
                _insertMode = set;
                break;

            case 12: // SRM — Send/receive mode.
                _sendReceiveMode = set;
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

    private static TerminalCursorStyle MapCursorStyle(int styleParameter)
    {
        return styleParameter switch
        {
            3 or 4 => TerminalCursorStyle.Underline,
            5 or 6 => TerminalCursorStyle.Bar,
            _ => TerminalCursorStyle.Block,
        };
    }

    private static bool IsCursorStyleBlinking(int styleParameter)
    {
        return styleParameter is 1 or 3 or 5;
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
                ResetDelayedWrap();
                _originMode = set;
                _cursorCol = 0;
                _cursorRow = _originMode ? _scrollTop : 0;
                break;

            case 7: // DECAWM — Auto-wrap mode
                _autoWrap = set;
                if (!set)
                {
                    ResetDelayedWrap();
                }
                break;

            case 66: // DECNKM — Application keypad keys
                _applicationKeypad = set;
                break;

            case 67: // DECBKM — Backarrow key mode
                _backarrowKeyMode = set;
                break;

            case 25: // DECTCEM — Show/hide cursor
                _cursorVisible = set;
                break;

            case 80: // DECSDM — Sixel display mode.
                if (_sixelGraphicsEnabled)
                {
                    _sixelDisplayMode = set;
                }
                break;

            case 47: // Use alternate screen buffer (no clear)
                if (set && !_inAltScreen)
                    SwitchToAltScreen(clearAlt: false);
                else if (!set && _inAltScreen)
                    SwitchToMainScreen();
                break;

            case 3: // 132-column mode
            case 4: // Smooth scroll
            case 5: // Reverse video
            case 8: // Auto-repeat
            case 9: // X10 mouse tracking
            case 12: // Cursor blinking
            case 40: // Allow 80/132 mode
            case 45: // Reverse wraparound
            case 69: // Left/right margin mode
            case 1000: // Mouse tracking normal
            case 1002: // Mouse button-event tracking
            case 1003: // Mouse any-event tracking
            case 1004: // Focus event reporting
            case 1005: // Mouse UTF-8 encoding
            case 1006: // Mouse SGR encoding
            case 1007: // Alternate scroll mode
            case 1015: // Mouse urxvt encoding
            case 1016: // Mouse pixel mode
            case 1035: // Ignore keypad with numlock
            case 1036: // Meta sends escape prefix
            case 1039: // Alt sends escape
            case 1045: // Reverse wrap extended
            case 2026: // Synchronized output
            case 2027: // Grapheme cluster mode
            case 2031: // Report color scheme mode
            case 2048: // In-band size reports
                SetExtendedDecMode(mode, set);
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
                {
                    SaveCursor();
                    _saveCursorMode = true;
                }
                else
                {
                    RestoreCursor();
                    _saveCursorMode = false;
                }
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

            case 9001: // Win32 input mode
                _win32InputMode = set;
                break;
        }
    }

    #endregion

    #region Alternate Screen Buffer

    private void SwitchToAltScreen(bool clearAlt)
    {
        if (_inAltScreen) return;

        if (_screen.ScrollOffset != 0)
        {
            _screen.ScrollOffset = 0;
        }

        _savedMainCursorCol = _cursorCol;
        _savedMainCursorRow = _cursorRow;
        _savedMainDelayedWrap = _delayedWrap;
        _inAltScreen = true;
        _screen.SwitchToAlternateBuffer(clearAlt);
        if (clearAlt)
        {
            ResetDelayedWrap();
            _cursorCol = 0;
            _cursorRow = 0;
        }

        // Reset scroll region
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;
    }

    private void SwitchToMainScreen()
    {
        if (!_inAltScreen) return;

        if (_screen.ScrollOffset != 0)
        {
            _screen.ScrollOffset = 0;
        }

        _screen.SwitchToPrimaryBuffer();

        _cursorCol = _savedMainCursorCol;
        _cursorRow = _savedMainCursorRow;
        _delayedWrap = _savedMainDelayedWrap;
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
        _savedUnderlineColor = _currentUnderlineColor;
        _savedHasUnderlineColor = _currentHasUnderlineColor;
        _savedDecorations = _currentDecorations;
        _savedHyperlinkId = _currentHyperlinkId;
        _savedUseLineDrawing = _useLineDrawing;
        _savedDelayedWrap = _delayedWrap;
    }

    private void RestoreCursor()
    {
        _cursorCol = _savedCursorCol;
        _cursorRow = _savedCursorRow;
        _delayedWrap = _savedDelayedWrap;
        _currentFg = _savedFg;
        _currentBg = _savedBg;
        _currentAttrs = _savedAttrs;
        _currentUnderlineStyle = _savedUnderlineStyle;
        _currentUnderlineColor = _savedUnderlineColor;
        _currentHasUnderlineColor = _savedHasUnderlineColor;
        _currentDecorations = _savedDecorations;
        _currentHyperlinkId = _savedHyperlinkId;
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

                case 58:
                    if (i + 1 < _params.Count)
                    {
                        if (_params[i + 1] == 5 && i + 2 < _params.Count)
                        {
                            _currentUnderlineColor = PaletteColor(_params[i + 2]);
                            _currentHasUnderlineColor = true;
                            i += 2;
                        }
                        else if (_params[i + 1] == 2 && i + 4 < _params.Count)
                        {
                            _currentUnderlineColor = 0xFF000000 |
                                                     ((uint)_params[i + 2] << 16) |
                                                     ((uint)_params[i + 3] << 8) |
                                                     (uint)_params[i + 4];
                            _currentHasUnderlineColor = true;
                            i += 4;
                        }
                    }
                    break;

                case 59:
                    _currentUnderlineColor = 0;
                    _currentHasUnderlineColor = false;
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
        _currentUnderlineColor = 0;
        _currentHasUnderlineColor = false;
        _currentDecorations = CellDecorations.None;
    }

    #endregion

    #region Erase / Insert / Delete Operations

    private void ClearScreen()
    {
        for (var r = 0; r < _screen.ViewportRows; r++)
            _screen.GetViewportRow(r).Clear(_screen.DefaultForeground, _screen.DefaultBackground);
        ResetDelayedWrap();
        _cursorCol = 0;
        _cursorRow = 0;
        _screen.ClearRasterGraphics();
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
                if (_cursorRow + 1 < _screen.ViewportRows)
                {
                    _screen.ClearRasterGraphicsInViewportRectangle(
                        _cursorRow + 1,
                        _screen.ViewportRows - 1,
                        0,
                        _screen.Columns - 1);
                }
                break;

            case 1: // From start to cursor
                for (var r = 0; r < _cursorRow && r < _screen.ViewportRows; r++)
                    _screen.GetViewportRow(r).Clear(_currentFg, _currentBg);
                if (_cursorRow > 0)
                {
                    _screen.ClearRasterGraphicsInViewportRectangle(
                        0,
                        _cursorRow - 1,
                        0,
                        _screen.Columns - 1);
                }
                if (_cursorRow >= 0 && _cursorRow < _screen.ViewportRows)
                {
                    var rowToCursor = _screen.GetViewportRow(_cursorRow);
                    ClearPreservedCellsForMutation(rowToCursor);
                    for (var c = 0; c <= _cursorCol && c < _screen.Columns; c++)
                        rowToCursor[c] = TerminalCell.Empty(_currentFg, _currentBg);
                    NormalizeRowWideCells(rowToCursor);
                    rowToCursor.IsDirty = true;
                }
                break;

            case 2: // Entire display
                for (var r = 0; r < _screen.ViewportRows; r++)
                    _screen.GetViewportRow(r).Clear(_currentFg, _currentBg);
                _screen.ClearRasterGraphics();
                break;

            case 3: // Entire display + scrollback
                for (var r = 0; r < _screen.ViewportRows; r++)
                    _screen.GetViewportRow(r).Clear(_currentFg, _currentBg);
                _screen.ClearRasterGraphics();
                break;
        }

        _screen.InvalidateAll();
    }

    private void EraseInLine(int mode)
    {
        ClampCursor();
        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;

        var row = _screen.GetViewportRow(_cursorRow);
        ClearPreservedCellsForMutation(row);
        switch (mode)
        {
            case 0: // From cursor to end of line
                for (var c = Math.Max(0, _cursorCol); c < _screen.Columns; c++)
                    row[c] = TerminalCell.Empty(_currentFg, _currentBg);
                row.WrapsToNext = false;
                _screen.ClearRasterGraphicsInViewportRectangle(
                    _cursorRow,
                    _cursorRow,
                    _cursorCol,
                    _screen.Columns - 1);
                break;

            case 1: // From start to cursor
                for (var c = 0; c <= _cursorCol && c < _screen.Columns; c++)
                    row[c] = TerminalCell.Empty(_currentFg, _currentBg);
                _screen.ClearRasterGraphicsInViewportRectangle(
                    _cursorRow,
                    _cursorRow,
                    0,
                    _cursorCol);
                break;

            case 2: // Entire line
                row.Clear(_currentFg, _currentBg);
                _screen.ClearRasterGraphicsInViewportRectangle(
                    _cursorRow,
                    _cursorRow,
                    0,
                    _screen.Columns - 1);
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
            _screen.ShiftRasterGraphicsInViewportRows(_cursorRow, _scrollBottom, rowDelta: 1);
            // Shift rows down from cursor to scroll bottom
            for (var r = _scrollBottom; r > _cursorRow; r--)
            {
                if (r < _screen.ViewportRows && r - 1 >= 0)
                    CopyRow(_screen.GetViewportRow(r - 1), _screen.GetViewportRow(r));
            }
            // Clear the line at cursor
            if (_cursorRow < _screen.ViewportRows)
            {
                _screen.GetViewportRow(_cursorRow).Clear(_currentFg, _currentBg);
                _screen.ClearRasterGraphicsInViewportRectangle(
                    _cursorRow,
                    _cursorRow,
                    0,
                    _screen.Columns - 1);
            }
        }
        _screen.InvalidateAll();
    }

    private void DeleteLines(int count)
    {
        ClampCursor();
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;

        for (var n = 0; n < count; n++)
        {
            _screen.ShiftRasterGraphicsInViewportRows(_cursorRow, _scrollBottom, rowDelta: -1);
            // Shift rows up from cursor to scroll bottom
            for (var r = _cursorRow; r < _scrollBottom; r++)
            {
                if (r >= 0 && r + 1 < _screen.ViewportRows)
                    CopyRow(_screen.GetViewportRow(r + 1), _screen.GetViewportRow(r));
            }
            // Clear the bottom row of the scroll region
            if (_scrollBottom < _screen.ViewportRows)
            {
                _screen.GetViewportRow(_scrollBottom).Clear(_currentFg, _currentBg);
                _screen.ClearRasterGraphicsInViewportRectangle(
                    _scrollBottom,
                    _scrollBottom,
                    0,
                    _screen.Columns - 1);
            }
        }
        _screen.InvalidateAll();
    }

    private void InsertCharacters(int count)
    {
        ClampCursor();
        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;

        var row = _screen.GetViewportRow(_cursorRow);
        ClearPreservedCellsForMutation(row);
        for (var c = _screen.Columns - 1; c >= _cursorCol + count; c--)
            row[c] = row[c - count];
        for (var c = _cursorCol; c < _cursorCol + count && c < _screen.Columns; c++)
            row[c] = TerminalCell.Empty(_currentFg, _currentBg);
        NormalizeRowWideCells(row);
        row.IsDirty = true;
        _screen.ClearRasterGraphicsInViewportRectangle(
            _cursorRow,
            _cursorRow,
            _cursorCol,
            _screen.Columns - 1);
    }

    private void DeleteCharacters(int count)
    {
        ClampCursor();
        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;

        var row = _screen.GetViewportRow(_cursorRow);
        ClearPreservedCellsForMutation(row);
        for (var c = _cursorCol; c + count < _screen.Columns; c++)
            row[c] = row[c + count];
        for (var c = Math.Max(_cursorCol, _screen.Columns - count); c < _screen.Columns; c++)
            row[c] = TerminalCell.Empty(_currentFg, _currentBg);
        NormalizeRowWideCells(row);
        row.IsDirty = true;
        _screen.ClearRasterGraphicsInViewportRectangle(
            _cursorRow,
            _cursorRow,
            _cursorCol,
            _screen.Columns - 1);
    }

    private void EraseCharacters(int count)
    {
        ClampCursor();
        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;

        var row = _screen.GetViewportRow(_cursorRow);
        ClearPreservedCellsForMutation(row);
        for (var c = _cursorCol; c < _cursorCol + count && c < _screen.Columns; c++)
            row[c] = TerminalCell.Empty(_currentFg, _currentBg);
        NormalizeRowWideCells(row);
        row.IsDirty = true;
        _screen.ClearRasterGraphicsInViewportRectangle(
            _cursorRow,
            _cursorRow,
            _cursorCol,
            Math.Min(_screen.Columns - 1, _cursorCol + count - 1));
    }

    private void ClearPreservedCellsForMutation(TerminalRow row)
    {
        row.MarkContentMutation();

        if (row.PreservedColumns > row.Columns)
        {
            row.ClearPreservedCellsFrom(row.Columns, _screen.DefaultForeground, _screen.DefaultBackground);
        }
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
                trailing.UnderlineColor = cell.UnderlineColor;
                trailing.HasUnderlineColor = cell.HasUnderlineColor;
                trailing.Decorations = cell.Decorations;
                trailing.HasBackground = cell.HasBackground;
                trailing.HyperlinkId = cell.HyperlinkId;
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
        _backarrowKeyMode = false;
        _saveCursorMode = false;
        _bracketedPaste = false;
        _win32InputMode = false;
        _keyboardLocked = false;
        _sendReceiveMode = true;
        ResetExtendedDecModesToDefaults();
        _sixelDisplayMode = false;
        _insertMode = false;
        _lineFeedNewLineMode = false;
        _cursorStyle = 1;
        ResetDelayedWrap();
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;
        _useLineDrawing = false;
        _g0IsLineDrawing = false;
        _g1IsLineDrawing = false;
        _shiftOut = false;
        _lastGraphicCodepoint = 0;
        _oscBuffer.Clear();
        _isDiscardingOscPayload = false;
        _dcsBuffer.Clear();
        _isDiscardingDcsPayload = false;
        _savedUnderlineStyle = TerminalUnderlineStyle.None;
        _savedUnderlineColor = 0;
        _savedHasUnderlineColor = false;
        _savedDecorations = CellDecorations.None;
        _savedHyperlinkId = 0;
        _currentHyperlinkId = 0;
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
        ResetDelayedWrap();
        _state = ParserState.Ground;
        _params.Clear();
        _csiPrivateMarker = '\0';
        _intermediateChar = '\0';
        _oscBuffer.Clear();
        _isDiscardingOscPayload = false;
        _dcsBuffer.Clear();
        _isDiscardingDcsPayload = false;
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;
        if (_inAltScreen || _screen.AlternateBufferActive)
        {
            _screen.SwitchToPrimaryBuffer();
        }

        _screen.DiscardInactiveAlternateBuffer();

        _inAltScreen = false;
        _autoWrap = true;
        _cursorVisible = true;
        _originMode = false;
        _applicationCursorKeys = false;
        _applicationKeypad = false;
        _backarrowKeyMode = false;
        _saveCursorMode = false;
        _bracketedPaste = false;
        _win32InputMode = false;
        _keyboardLocked = false;
        _sendReceiveMode = true;
        ResetExtendedDecModesToDefaults();
        _sixelDisplayMode = false;
        _insertMode = false;
        _lineFeedNewLineMode = false;
        _cursorStyle = 1;
        _useLineDrawing = false;
        _g0IsLineDrawing = false;
        _g1IsLineDrawing = false;
        _shiftOut = false;
        _lastGraphicCodepoint = 0;
        _savedUnderlineStyle = TerminalUnderlineStyle.None;
        _savedUnderlineColor = 0;
        _savedHasUnderlineColor = false;
        _savedDecorations = CellDecorations.None;
        _savedHyperlinkId = 0;
        _currentHyperlinkId = 0;
        _kittyKeyboardFlagsMain = 0;
        _kittyKeyboardFlagsAlt = 0;
        _kittyKeyboardStackMain.Clear();
        _kittyKeyboardStackAlt.Clear();
        ResetAttributes();
        InitTabStops();

        for (var r = 0; r < _screen.ViewportRows; r++)
            _screen.GetViewportRow(r).Clear(_screen.DefaultForeground, _screen.DefaultBackground);

        _screen.ClearRasterGraphics();

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
        ApplyResizeState(columns, rows);
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

    /// <summary>
    /// Resizes the associated screen buffer and remaps the managed cursor through any row reflow.
    /// </summary>
    public void ResizeScreen(
        int columns,
        int rows,
        int widthPx,
        int heightPx,
        bool reflowOnResize,
        bool preserveViewportTopOnRowsIncrease = false)
    {
        ResizeScreen(
            columns,
            rows,
            widthPx,
            heightPx,
            reflowOnResize,
            Span<TerminalGridPosition>.Empty,
            preserveViewportTopOnRowsIncrease);
    }

    /// <summary>
    /// Resizes the associated screen buffer and remaps the managed cursor plus absolute grid anchors through row reflow.
    /// </summary>
    public void ResizeScreen(
        int columns,
        int rows,
        int widthPx,
        int heightPx,
        bool reflowOnResize,
        Span<TerminalGridPosition> trackedAbsolutePositions,
        bool preserveViewportTopOnRowsIncrease = false)
    {
        _widthPx = Math.Max(0, widthPx);
        _heightPx = Math.Max(0, heightPx);

        bool alternateScreen = _inAltScreen;
        int previousCursorCol = _cursorCol;
        int previousCursorRow = _cursorRow;
        bool previousDelayedWrap = _delayedWrap;
        int resizeCursorCol = _delayedWrap ? _screen.Columns : _cursorCol;
        int alternateViewportTop = alternateScreen
            ? Math.Max(0, _screen.TotalRows - _screen.ViewportRows)
            : 0;
        int restoreScrollOffset = alternateScreen ? 0 : _screen.ScrollOffset;
        if (_screen.ScrollOffset != 0)
        {
            _screen.ScrollOffset = 0;
        }

        TerminalGridPosition mappedCursor;
        try
        {
            mappedCursor = _screen.Resize(
                columns,
                rows,
                reflowOnResize && !alternateScreen,
                alternateScreen ? null : new TerminalGridPosition(resizeCursorCol, _cursorRow),
                trackedAbsolutePositions,
                preserveViewportTopOnRowsIncrease && !alternateScreen);
        }
        finally
        {
            if (!alternateScreen && restoreScrollOffset != 0)
            {
                _screen.ScrollOffset = restoreScrollOffset;
            }
        }

        if (alternateScreen)
        {
            _screen.PadBottomViewportToPreserveTop(alternateViewportTop);
            _cursorCol = previousCursorCol;
            _cursorRow = previousCursorRow;
            _delayedWrap = previousDelayedWrap;
        }
        else
        {
            SetCursorFromMappedResize(columns, mappedCursor, previousDelayedWrap);
            _cursorRow = mappedCursor.Row;
        }

        ApplyResizeState(columns, rows);
    }

    private void ApplyResizeState(int columns, int rows)
    {
        int safeColumns = Math.Max(1, columns);
        int safeRows = Math.Max(1, rows);

        _scrollBottom = safeRows - 1;
        if (_scrollTop >= safeRows)
        {
            _scrollTop = 0;
        }

        if (_cursorRow >= safeRows)
        {
            _cursorRow = safeRows - 1;
        }

        if (_cursorCol >= safeColumns)
        {
            bool preserveDelayedWrap = _delayedWrap;
            _cursorCol = safeColumns - 1;
            _delayedWrap = preserveDelayedWrap;
        }

        if (_cursorRow < 0)
        {
            _cursorRow = 0;
        }

        if (_cursorCol < 0)
        {
            _cursorCol = 0;
        }

        InitTabStops();
    }

    private void SetCursorFromMappedResize(int columns, TerminalGridPosition mappedCursor, bool restoreDelayedWrapAtEnd)
    {
        int safeColumns = Math.Max(1, columns);
        if (mappedCursor.Column >= safeColumns)
        {
            _cursorCol = safeColumns - 1;
            _delayedWrap = restoreDelayedWrapAtEnd;
            return;
        }

        _cursorCol = Math.Clamp(mappedCursor.Column, 0, safeColumns - 1);
        _delayedWrap = false;
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
