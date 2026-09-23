// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal — VT sequence processor for standalone/demo mode.
// Processes raw terminal output bytes into the TerminalScreen cell grid.
// Supports: printable characters, cursor movement, SGR colors (256 + truecolor),
// scrolling, scroll regions (DECSTBM), alternate screen buffer, DEC private modes,
// DEC line-drawing character set, erase, insert/delete lines & characters, and tabs.

using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Sixel;
using RoyalTerminal.Terminal.Theming;
using RoyalTerminal.Unicode;

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
public sealed partial class BasicVtProcessor : IVtProcessor,
    ITerminalThemeSink,
    ITerminalModeDefaults,
    IKittyKeyboardStateSource,
    ITerminalCursorStyleSource,
    ITerminalCursorDefaults,
    ITerminalFocusEventModeSource,
    ITerminalSessionHistoryController,
    ITerminalSelectionExportSource,
    ITerminalPasteSequenceEncoderSource,
    ITerminalSnapshotExportSource,
    ITerminalPointerSequenceEncoderSource,
    ITerminalMouseReportingStateSource,
    ITerminalSixelOptionsSink,
    ITerminalEraseDisplayOptionsSink,
    ITerminalShellIntegrationEventSource,
    ITerminalEffectSource,
    ITerminalUnicodeWidthProvider,
    ITerminalTimedRefreshSource
{
    private const int MaxOscBufferBytes = 8 * 1024 * 1024;
    private const int MaxGraphemeCodepoints = 65;
    private const int MaxDcsQueryBytes = 1024 * 1024;
    private const int MaxUnknownSequenceBytes = 4096;
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
        2033,
        2048,
        5522,
    ];
    private static readonly int[] ExtendedDecModesEnabledByDefault =
    [
        1007,
        1035,
        1036,
    ];

    private TerminalScreen _screen;
    private readonly TerminalScreen _publishedScreen;
    private RenderHoldState? _renderHold;
    private int _cursorCol;
    private int _cursorRow;
    private uint _currentFg;
    private uint _currentBg;
    private SgrColorKind _currentFgKind;
    private SgrColorKind _currentBgKind;
    private int _currentFgPaletteIndex;
    private int _currentBgPaletteIndex;
    private CellAttributes _currentAttrs;
    private TerminalUnderlineStyle _currentUnderlineStyle;
    private uint _currentUnderlineColor;
    private TerminalColorIdentity _currentUnderlineIdentity;
    private bool _currentHasUnderlineColor;
    private CellDecorations _currentDecorations;
    private int _currentHyperlinkId;
    private uint _primaryHyperlinkImplicitCounter;
    private uint _alternateHyperlinkImplicitCounter;
    private TerminalTheme _theme;
    private readonly ManagedTerminalColors _colors;
    private readonly ManagedVtContinuation _continuation;
    private readonly ManagedKittyGraphicsStore _primaryKittyStore;
    private ManagedKittyGraphicsStore? _alternateKittyStore;
    private ManagedKittyGraphicsStore _kittyStore;

    // Parser state machine
    private ParserState _state = ParserState.Ground;
    private readonly List<int> _params = new(24);
    private uint _csiColonSeparators;
    private int _intermediateCount;
    private int _currentParam;
    private bool _hasParam;
    private char _csiPrivateMarker;
    private char _intermediateChar;
    private readonly List<byte> _oscBuffer = [];
    private readonly List<byte> _dcsBuffer = [];
    private readonly List<byte> _apcBuffer = [];
    private bool _glyphProtocolEnabled;
    private bool _apcGlyphRecognized;
    private bool _apcGlyphEnabled;
    private readonly SixelDecoder _sixelDecoder;
    private readonly BasicVtProcessorOptions _options;
    private readonly TerminalShellIntegrationParser _shellIntegrationParser = new();
    private readonly KittyClipboardProtocol _kittyClipboardProtocol;
    private bool _isDiscardingOscPayload;
    private bool _isDiscardingDcsPayload;
    private bool _apcTruncated;
    private bool _sixelGraphicsEnabled;
    private bool _sixelDisplayMode;

    // Scroll region (DECSTBM)
    private int _scrollTop;    // 0-based inclusive
    private int _scrollBottom; // 0-based inclusive (ViewportRows - 1 at init)

    // Alternate screen buffer
    private int _savedMainCursorCol;
    private int _savedMainCursorRow;
    private bool _savedMainDelayedWrap;
    private bool _inAltScreen;

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
    private int _widthPx;
    private int _heightPx;
    private int _reportCellWidthPx;
    private int _reportCellHeightPx;
    private readonly HashSet<int> _extendedDecModesEnabled = [];
    private TerminalMouseModeState _mouseModeState;

    // Tab stops
    private readonly HashSet<int> _tabStops = [];

    // UTF-8 multi-byte decoding state
    private int _utf8Codepoint;
    private int _utf8Remaining;
    private byte _utf8NextMinimum;
    private byte _utf8NextMaximum;
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
        CsiIgnore,
        OscString,
        DcsEntry,
        DcsParam,
        DcsIntermediate,
        DcsIgnore,
        DcsString,
        ApcString,
    }

    private enum SgrColorKind : byte
    {
        Default,
        Palette,
        Rgb,
    }

    private enum SessionScreenResetMode
    {
        ClearViewport,
        ClearAll,
        PreserveScrollback,
    }

    /// <summary>Current cursor column.</summary>
    public int CursorCol => _renderHold?.CursorColumn ?? _cursorCol;

    /// <summary>Current cursor row.</summary>
    public int CursorRow => _renderHold?.CursorRow ?? _cursorRow;

    /// <summary>Whether cursor should be visible.</summary>
    public bool CursorVisible => _renderHold?.CursorVisible ?? _cursorVisible;

    /// <summary>Whether application cursor key mode is active.</summary>
    public bool ApplicationCursorKeys => _applicationCursorKeys;

    /// <summary>Whether application keypad mode is active.</summary>
    public bool ApplicationKeypad => _applicationKeypad;

    /// <summary>Whether the alternate screen buffer is active.</summary>
    public bool AlternateScreen => _renderHold?.AlternateScreen ?? _inAltScreen;

    /// <summary>Whether bracketed paste mode is active.</summary>
    public bool BracketedPaste => _bracketedPaste;

    /// <inheritdoc />
    public bool Win32InputMode => _win32InputMode;

    /// <inheritdoc />
    public bool FocusEventsEnabled => _extendedDecModesEnabled.Contains(1004);

    /// <inheritdoc />
    public bool MouseReportingEnabled => MouseModeState.IsMouseReportingEnabled;

    /// <inheritdoc />
    public TerminalCursorStyle CursorStyle => _renderHold?.CursorStyle ?? ActiveCursorStyle;

    /// <inheritdoc />
    public bool CursorBlinking => _renderHold?.CursorBlinking ?? _extendedDecModesEnabled.Contains(12);

    /// <inheritdoc />
    public int KittyKeyboardFlags => ActiveKittyKeyboard.Current;

    /// <inheritdoc />
    public TerminalModeState ModeState => new(
        CursorVisible,
        ApplicationCursorKeys,
        ApplicationKeypad,
        AlternateScreen,
        BracketedPaste,
        Win32InputMode,
        _backarrowKeyMode);

    private TerminalMouseModeState MouseModeState => _mouseModeState;

    /// <inheritdoc />
    public event EventHandler<TerminalModeState>? ModeChanged;

    /// <inheritdoc />
    public event EventHandler<TerminalShellIntegrationEventArgs>? ShellIntegrationEventReceived
    {
        add => _shellIntegrationParser.EventReceived += value;
        remove => _shellIntegrationParser.EventReceived -= value;
    }

    /// <inheritdoc />
    public Func<TerminalClipboardWrite, TerminalClipboardWriteResult>? ClipboardWriteCallback { get; set; }

    /// <inheritdoc />
    public Func<TerminalClipboardWrite, TerminalClipboardWriteReply>? ClipboardWriteRequestCallback { get; set; }

    /// <inheritdoc />
    public Func<TerminalClipboardRead, TerminalClipboardReadReply>? ClipboardReadCallback { get; set; }

    /// <inheritdoc />
    public Action<TerminalUnknownSequence>? UnknownSequenceCallback { get; set; }

    /// <inheritdoc />
    public Action<TerminalDesktopNotification>? DesktopNotificationCallback { get; set; }

    /// <inheritdoc />
    public Action<TerminalProgressReport>? ProgressReportCallback { get; set; }

    /// <inheritdoc />
    public Action<string>? WorkingDirectoryCallback { get; set; }

    /// <inheritdoc />
    public byte GetCodepointWidth(uint codepoint)
        => checked((byte)TerminalCellWidthCalculator.GetCodepointWidth(
            codepoint > int.MaxValue ? -1 : (int)codepoint));

    /// <inheritdoc />
    public nuint GetGraphemeWidth(ReadOnlySpan<uint> codepoints, out byte width)
    {
        int consumed = TerminalCellWidthCalculator.GetFirstGraphemeWidth(
            codepoints,
            out int measuredWidth);
        width = checked((byte)measuredWidth);
        return checked((nuint)consumed);
    }

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

    /// <summary>
    /// Enables future glyph APC commands. Disabling clears session registrations;
    /// an already-identified command retains its original enablement, as in libvt.
    /// </summary>
    public bool GlyphProtocolEnabled
    {
        get => _glyphProtocolEnabled;
        set
        {
            if (_glyphProtocolEnabled == value) return;
            _glyphProtocolEnabled = value;
            if (!value)
            {
                _screen.ClearRegisteredGlyphs();
            }
        }
    }

    /// <inheritdoc />
    public bool ScrollOnEraseInDisplay { get; set; }

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
        _publishedScreen = screen;
        _options = options ?? BasicVtProcessorOptions.Default;
        ArgumentNullException.ThrowIfNull(_options.TimeProvider);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.ClipboardWriteLimitBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.ContinuationMaxBytes);
        _continuation = new ManagedVtContinuation(_options.ContinuationMaxBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.KittyGraphicsStorageLimitBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.KittyGraphicsMaxApcBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.KittyGraphicsMaxImageBytes);
        _primaryKittyStore = new ManagedKittyGraphicsStore(_options.KittyGraphicsStorageLimitBytes);
        _kittyStore = _primaryKittyStore;
        _kittyClipboardProtocol = new KittyClipboardProtocol(_options.ClipboardWriteLimitBytes);
        _sixelDecoder = new SixelDecoder(_options.SixelDecoderOptions);
        _sixelGraphicsEnabled = _options.SixelGraphicsEnabled;
        _glyphProtocolEnabled = _options.GlyphProtocolEnabled;
        ScrollOnEraseInDisplay = _options.ScrollOnEraseInDisplay;
        _theme = screen.Theme;
        _colors = new ManagedTerminalColors(_theme);
        _currentFg = screen.DefaultForeground;
        _currentBg = screen.DefaultBackground;
        _scrollBottom = screen.ViewportRows - 1;
        ResetExtendedDecModesToDefaults();
        InitTabStops();
    }

    private void InitTabStops()
    {
        _tabStopColumns = _screen.Columns;
        _tabStops.Clear();
        for (var i = 0; i < _screen.Columns; i += 8)
            _tabStops.Add(i);
    }

    /// <summary>
    /// Processes a span of raw terminal output bytes.
    /// </summary>
    public void Process(ReadOnlySpan<byte> data)
        => ProcessCore(data, stopAtGround: false, out _);

    private void ProcessCore(ReadOnlySpan<byte> data, bool stopAtGround, out int consumed)
    {
        consumed = 0;
        RefreshTimedState();
        if (data.IsEmpty || (stopAtGround && IsParserGround))
        {
            return;
        }

        TerminalModeState before = ModeState;
        int continuationStart = -1;
        int continuationSegmentStart = 0;

        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];

            // Only the final unfinished fragment needs retaining. Record starts
            // without rescanning ordinary text or copying completed commands.
            if (IsParserGround || (_state == ParserState.Ground && _utf8Remaining > 0 && !IsUtf8Continuation(b)))
            {
                continuationStart = i;
            }

            // Strings commit when ESC arrives, not after a subsequent backslash.
            // Only the new ESC operation belongs to a replayable continuation.
            if (_state is ParserState.OscString or ParserState.ApcString or ParserState.DcsString or
                ParserState.DcsEntry or ParserState.DcsParam or ParserState.DcsIntermediate or ParserState.DcsIgnore && b == 0x1B)
                continuationStart = i;

            if (_state == ParserState.ApcString && b < 0x80 && b is not (0x18 or 0x1A or 0x1B))
            {
                ReadOnlySpan<byte> remaining = data[i..];
                int end = remaining.IndexOfAny((byte)0x18, (byte)0x1A, (byte)0x1B);
                if (end < 0) end = remaining.Length;
                int high = remaining[..end].IndexOfAnyInRange((byte)0x80, byte.MaxValue);
                if (high >= 0) end = high;
                AppendApcPayload(remaining[..end]);
                i += end - 1;
                continue;
            }

            if (_state is ParserState.OscString or ParserState.DcsString &&
                b >= 0x20 && !(_state == ParserState.DcsString && b == 0x7F))
            {
                ReadOnlySpan<byte> remaining = data[i..];
                int count = remaining.IndexOfAnyInRange((byte)0, (byte)0x1F);
                if (count < 0) count = remaining.Length;
                if (_state == ParserState.DcsString)
                {
                    int delete = remaining[..count].IndexOf((byte)0x7F);
                    if (delete >= 0) count = delete;
                }
                AppendControlStringPayload(remaining[..count]);
                i += count - 1;
                continue;
            }

            // Match Ghostty's print-slice fast path: keep contiguous printable
            // ASCII out of the parser state switch and UTF-8 decoder.
            if (_state == ParserState.Ground &&
                _utf8Remaining == 0 &&
                b is >= 0x20 and < 0x7F)
            {
                int end = i + 1;
                while (end < data.Length && data[end] is >= 0x20 and < 0x7F)
                {
                    end++;
                }

                ProcessPrintableAscii(data[i..end]);
                i = end - 1;
                continue;
            }

            // Finish/reject a partial scalar before interpreting controls, including
            // CAN/SUB. Ghostty emits U+FFFD for the interrupted scalar first.
            if (_state == ParserState.Ground && _utf8Remaining > 0)
            {
                ProcessGround(b);
                if (stopAtGround && IsParserGround) { consumed = i + 1; break; }
                continue;
            }

            if (_state == ParserState.ApcString && b is 0x90 or 0x9B or 0x9D)
            {
                ProcessApcString(b);
                // Exiting APC can commit a Kitty command. Do not replay it;
                // canonicalize the new C1 introduction into a ground-safe ESC.
                ReadOnlySpan<byte> prefix = b switch
                {
                    0x90 => "\u001bP"u8,
                    0x9B => "\u001b["u8,
                    _ => "\u001b]"u8,
                };
                _continuation.Track(prefix, 0, ground: false);
                continuationSegmentStart = i + 1;
                continuationStart = -1;
                continue;
            }

            if (TryHandleAnywhereCancelControl(b))
            {
                if (stopAtGround)
                {
                    consumed = i + 1;
                    break;
                }

                continue;
            }

            if (_state is ParserState.Escape or ParserState.EscapeIntermediate or
                ParserState.CsiEntry or ParserState.CsiParam or ParserState.CsiIntermediate or ParserState.CsiIgnore)
            {
                if (b == 0x1B)
                {
                    EnterCsiState(); // Clear stale parameters/intermediates, then start ESC.
                    _state = ParserState.Escape;
                    continuationStart = i;
                    continue;
                }
                if (b == 0x7F) continue;
                if (b is >= 0x80 and <= 0x9F)
                {
                    ProcessC1(b);
                    if (stopAtGround && IsParserGround) { consumed = i + 1; break; }
                    continue;
                }
                if (b < 0x20)
                {
                    ProcessGround(b); // Immediate C0 effect; preserve the unfinished parser state.
                    // Its effect is already captured. Retain the preceding
                    // segment but omit this byte, without an unbounded index list.
                    _continuation.Track(data[continuationSegmentStart..i],
                        continuationStart < continuationSegmentStart ? -1 : continuationStart - continuationSegmentStart,
                        ground: false);
                    continuationSegmentStart = i + 1;
                    if (stopAtGround && IsParserGround) { consumed = i + 1; break; }
                    continue;
                }
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
                case ParserState.CsiIgnore:
                    if (b is >= 0x40 and <= 0x7E) _state = ParserState.Ground;
                    break;
                case ParserState.OscString:
                    ProcessOscString(b);
                    break;
                case ParserState.DcsString:
                    ProcessDcsString(b);
                    break;
                case ParserState.DcsEntry:
                case ParserState.DcsParam:
                case ParserState.DcsIntermediate:
                case ParserState.DcsIgnore:
                    ProcessDcsHeader(b);
                    break;
                case ParserState.ApcString:
                    ProcessApcString(b);
                    break;
            }

            if (stopAtGround && IsParserGround)
            {
                consumed = i + 1;
                break;
            }
        }

        if (consumed == 0) consumed = data.Length;
        _continuation.Track(data[continuationSegmentStart..consumed],
            continuationStart < continuationSegmentStart ? -1 : continuationStart - continuationSegmentStart, IsParserGround);
        RaiseModeChangedIfNeeded(before);
    }

    private void ProcessPrintableAscii(ReadOnlySpan<byte> data)
    {
        for (int index = 0; index < data.Length; index++)
        {
            PutChar(data[index]);
        }
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

        return _kittyClipboardProtocol.TryEncodePaste(
            text,
            bracketedPaste,
            _extendedDecModesEnabled.Contains(5522),
            out sequence);
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
        // Screen selection may restore a saved cursor or clear the alternate
        // screen. Perform it before content and before restoring origin mode.
        if (options.Extras.IncludeModes) AppendMode(builder, ansi: false, 1049, _inAltScreen);
        builder.Append("\x1b[0m");

        SnapshotCellStyleKey? currentStyle = null;
        int currentHyperlink = 0;

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
                    builder.Append("\r\n");
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
                    builder.Append("\r\n");
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
        ref int currentHyperlink)
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

            int desiredHyperlink = options.Extras.IncludeHyperlinks ? cell.HyperlinkId : 0;
            if (currentHyperlink != desiredHyperlink)
            {
                CloseStyledHyperlink(builder, ref currentHyperlink);
                if (desiredHyperlink != 0)
                {
                    AppendStyledHyperlink(builder, desiredHyperlink);
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
            if (HasHorizontalMargins)
                builder.Append("\x1b[").Append(_scrollLeft + 1).Append(';').Append(RightMargin + 1).Append('s');
        }

        if (options.Extras.IncludeTabstops)
        {
            AppendTabstopSnapshot(builder);
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
            AppendCursorSnapshot(builder, options);
        }

        // A pending-wrap cursor is restored by reprinting its final cell. Restore
        // the application's character set and pen after that synthetic print.
        if (options.Extras.IncludeCharsets)
        {
            _charsets.AppendRestoreSequence(builder);
        }

        if (options.Extras.IncludeStyle)
        {
            builder.Append("\x1b[")
                .Append(BuildCurrentSgrState())
                .Append('m');
        }

        if (options.Extras.IncludeHyperlinks)
        {
            AppendStyledHyperlink(builder, _currentHyperlinkId);
        }
    }

    private void AppendCursorSnapshot(StringBuilder builder, in TerminalSnapshotExportOptions options)
    {
        int cursorColumn = _cursorCol;
        TerminalRow row = _screen.GetRow(Math.Max(0, _screen.TotalRows - _screen.ViewportRows) + _cursorRow);
        bool restoreWrap = _delayedWrap && cursorColumn == CursorRightLimit;
        if (restoreWrap && cursorColumn > 0 && row[cursorColumn].Width == 0 && row[cursorColumn - 1].Width == 2)
        {
            cursorColumn--;
        }

        builder.Append("\x1b[")
            .Append(_cursorRow - (options.Extras.IncludeModes && _originMode ? _scrollTop : 0) + 1)
            .Append(';')
            .Append(cursorColumn - (options.Extras.IncludeModes && _originMode ? _scrollLeft : 0) + 1)
            .Append('H');

        if (!restoreWrap)
        {
            return;
        }

        // CUP clears pending wrap; printing the complete edge cell restores it,
        // including wide characters and graphemes, using normal parser behavior.
        builder.Append("\x1b(B\x0F");
        SnapshotCellStyleKey? style = null;
        int hyperlink = 0;
        AppendStyledSnapshotRow(builder, row, cursorColumn, CursorRightLimit,
            options with { TrimTrailingWhitespace = false }, ref style, ref hyperlink);
        CloseStyledHyperlink(builder, ref hyperlink);
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

        builder.Append("\x1b[H");
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

    private void AppendStyledHyperlink(StringBuilder builder, int token)
    {
        builder.Append("\x1b]8;");
        if (_screen.TryGetHyperlink(token, out TerminalHyperlink? link) && link!.IsExplicit)
            builder.Append("id=").Append(Encoding.UTF8.GetString(link.ExplicitId));
        builder.Append(';');
        if (_screen.TryGetHyperlinkUrl(token, out string? url)) builder.Append(url);
        builder.Append("\x1b\\");
    }

    private static void CloseStyledHyperlink(StringBuilder builder, ref int currentHyperlink)
    {
        if (currentHyperlink == 0)
        {
            return;
        }

        builder.Append("\x1b]8;;\x1b\\");
        currentHyperlink = 0;
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
        _intermediateCount = 0;
        _csiColonSeparators = 0;
    }

    private void EnterOscState()
    {
        _state = ParserState.OscString;
        _oscBuffer.Clear();
        _isDiscardingOscPayload = false;
    }

    private void EnterDcsState()
    {
        EnterCsiState(); // Parameter/intermediate storage is shared, never concurrent.
        _state = ParserState.DcsEntry;
        _dcsBuffer.Clear();
        _dcsBufferLimit = 0;
        _isDiscardingDcsPayload = false;
    }

    private void EnterApcState()
    {
        _state = ParserState.ApcString;
        _apcBuffer.Clear();
        _apcTruncated = false;
        _apcGlyphRecognized = false;
        _apcGlyphEnabled = false;
    }

    private void ProcessC1(byte value)
    {
        switch (value)
        {
            case 0x90: EnterDcsState(); break;
            case 0x98: case 0x9E: case 0x9F: EnterApcState(); break;
            case 0x9B: EnterCsiState(); break;
            case 0x9D: EnterOscState(); break;
            case 0x9C: _state = ParserState.Ground; break;
            default: ProcessEscape((byte)(value - 0x40)); break;
        }
    }

    #region Ground State

    private void ProcessGround(byte b)
    {
        // Handle UTF-8 continuation bytes first
        if (_utf8Remaining > 0)
        {
            if (IsUtf8Continuation(b))
            {
                _utf8Codepoint = (_utf8Codepoint << 6) | (b & 0x3F);
                _utf8Remaining--;
                _utf8NextMinimum = 0x80;
                _utf8NextMaximum = 0xBF;
                if (_utf8Remaining == 0)
                {
                    // UTF-8 encoded C1 controls are ignored in ground state. This
                    // matches Ghostty; raw high bytes in ground are UTF-8 input.
                    if (_utf8Codepoint is < 0x80 or > 0x9F)
                    {
                        PutChar(_utf8Codepoint);
                    }
                }
            }
            else
            {
                // Replace the invalid prefix and retry this byte as UTF-8.
                _utf8Remaining = 0;
                PutChar(0xFFFD);
                if (b >= 0x80) ProcessUtf8Lead(b);
                else ProcessGround(b);
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
                ResetDelayedWrap();
                LineFeed(wrapForced: false);
                if (_lineFeedNewLineMode)
                {
                    CarriageReturn();
                }
                break;

            case (byte)'\r': // CR
                ResetDelayedWrap();
                CarriageReturn();
                break;

            case 0x08: // BS — Backspace
                ResetDelayedWrap();
                _cursorCol = Math.Max(0, _cursorCol - 1);
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
                _charsets.Invoke(1);
                break;

            case 0x0F: // SI — Shift In (activate G0)
                _charsets.Invoke(0);
                break;

            default:
                if (b < 0x20)
                {
                    // Other C0 control characters — ignore
                }
                else if (b < 0x80)
                {
                    PutChar(b);
                }
                else
                {
                    ProcessUtf8Lead(b);
                }
                break;
        }
    }

    private bool IsUtf8Continuation(byte value) => value >= _utf8NextMinimum && value <= _utf8NextMaximum;

    private void ProcessUtf8Lead(byte value)
    {
        _utf8NextMinimum = 0x80;
        _utf8NextMaximum = 0xBF;
        if (value is >= 0xC2 and <= 0xDF)
        {
            _utf8Codepoint = value & 0x1F;
            _utf8Remaining = 1;
        }
        else if (value is >= 0xE0 and <= 0xEF)
        {
            _utf8Codepoint = value & 0x0F;
            _utf8Remaining = 2;
            if (value == 0xE0) _utf8NextMinimum = 0xA0; // No overlong scalars.
            if (value == 0xED) _utf8NextMaximum = 0x9F; // No UTF-16 surrogates.
        }
        else if (value is >= 0xF0 and <= 0xF4)
        {
            _utf8Codepoint = value & 7;
            _utf8Remaining = 3;
            if (value == 0xF0) _utf8NextMinimum = 0x90;
            if (value == 0xF4) _utf8NextMaximum = 0x8F; // U+10FFFF upper bound.
        }
        else
        {
            _utf8Codepoint = 0;
            PutChar(0xFFFD);
        }
    }

    private void PutChar(int codepoint)
    {
        ClampCursor();

        if (!Rune.IsValid(codepoint))
        {
            return;
        }

        bool graphemeClusters = _extendedDecModesEnabled.Contains(2027);
        // The byte-sized fast path never joins a grapheme in Ghostty's printer.
        // Zero-width and cluster continuations must be considered before wrapping.
        if (graphemeClusters && codepoint > 255 && _cursorCol > 0 &&
            ShouldAttemptGraphemeAppend(codepoint) && TryAppendToPreviousCellGrapheme(codepoint, useBoundaries: true))
        {
            return;
        }

        int width = codepoint <= 255 ? 1 : TerminalCellWidthCalculator.GetCodepointWidth(codepoint);
        if (width == 0)
        {
            if (!graphemeClusters) TryAppendToPreviousCellGrapheme(codepoint, useBoundaries: false);
            return;
        }

        // Ghostty prints an empty narrow cell when a terminal cannot fit any
        // wide glyph. Preserve normal delayed-wrap behavior for that cell.
        if (width == 2 && _screen.Columns == 1)
        {
            codepoint = 0;
            width = 1;
        }

        if (ConsumeDelayedWrapBeforePrint())
        {
            ClampCursor();
        }

        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;
        if (_cursorCol < 0 || _cursorCol >= _screen.Columns) return;

        TerminalRow row = _screen.GetViewportRow(_cursorRow);
        if (_cursorCol >= row.Columns) return;
        ClearPreservedCellsForMutation(row);

        if (width == 2 && _cursorCol == CursorRightLimit)
        {
            if (_autoWrap)
            {
                ClearRasterGraphicsForTextMutation(_cursorRow, _cursorCol, 1);
                ClearCellAndWideArtifacts(row, _cursorCol);
                bool atScreenEdge = _cursorCol == _screen.Columns - 1;
                WriteCellFromPen(ref row[_cursorCol], 0, atScreenEdge ? (byte)0 : (byte)1);
                row[_cursorCol].IsWideSpacerHead = atScreenEdge;
                row.IsDirty = true;
                ResetDelayedWrap();
                LineFeed(wrapForced: atScreenEdge, softWrap: true);
                _cursorCol = _scrollLeft;
                ClampCursor();
            }
            else
            {
                return;
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
        if (_cursorCol <= 1 && row.ReadOnlyCells[0].Width == 2 &&
            !(_cursorCol == 0 && width == 2)) ClearPreviousWideSpacerHead();
        ClearCellAndWideArtifacts(row, _cursorCol);
        if (width == 2 && _cursorCol + 1 < row.Columns)
        {
            ClearCellAndWideArtifacts(row, _cursorCol + 1);
        }

        WriteCellFromPen(ref row[_cursorCol], codepoint, (byte)width);
        if (width == 2 && _cursorCol + 1 < row.Columns)
        {
            WriteCellFromPen(ref row[_cursorCol + 1], 0, 0);
        }

        row.IsDirty = true;
        AdvanceCursorAfterGraphic(width);
        _lastGraphicCodepoint = codepoint;
    }

    private void WriteCellFromPen(ref TerminalCell cell, int codepoint, byte width)
    {
        cell.Codepoint = _charsets.MapPrintedCell(codepoint);
        cell.Grapheme = null;
        cell.Foreground = _currentFg;
        cell.Background = _currentBg;
        cell.ForegroundIdentity = GetColorIdentity(_currentFgKind, _currentFgPaletteIndex, _currentFg);
        cell.BackgroundIdentity = GetColorIdentity(_currentBgKind, _currentBgPaletteIndex, _currentBg);
        cell.UnderlineIdentity = _currentUnderlineIdentity;
        cell.Attributes = _currentAttrs;
        cell.UnderlineStyle = _currentUnderlineStyle;
        cell.UnderlineColor = _currentUnderlineColor;
        cell.HasUnderlineColor = _currentHasUnderlineColor;
        cell.Decorations = _currentDecorations;
        cell.HasBackground = true;
        cell.HyperlinkId = _currentHyperlinkId;
        cell.Width = width;
        cell.IsWideSpacerHead = false;
        cell.IsProtected = _currentProtected;
        cell.SemanticContent = CurrentSemanticPen.Content;
    }

    private bool ShouldAttemptGraphemeAppend(int codepoint)
    {
        if (!Rune.IsValid(codepoint)) return false;
        Codepoint value = new((uint)codepoint);
        if (value.GraphemeBreakClass is not (GraphemeBreakClass.Other or
            GraphemeBreakClass.Control or GraphemeBreakClass.CR or GraphemeBreakClass.LF) ||
            value.IndicConjunctBreakClass == IndicConjunctBreakClass.Consonant)
        {
            return true;
        }

        // GB9b permits any printable scalar after a prepend. The previous base
        // remains the first scalar when suffixes are appended to its cell.
        return _lastGraphicCodepoint > 0 &&
            new Codepoint((uint)_lastGraphicCodepoint).GraphemeBreakClass == GraphemeBreakClass.Prepend;
    }

    private bool TryAppendToPreviousCellGrapheme(int codepoint, bool useBoundaries)
    {
        if (!Rune.IsValid(codepoint))
        {
            return false;
        }

        int targetRowIndex = _cursorRow;
        int targetColIndex = _autoWrap && _delayedWrap ? _cursorCol : _cursorCol - 1;
        if (useBoundaries && !_autoWrap && _cursorCol == CursorRightLimit &&
            _screen.GetViewportRow(_cursorRow)[_cursorCol].HasContent)
        {
            targetColIndex = _cursorCol;
        }

        if (targetRowIndex < 0 || targetRowIndex >= _screen.ViewportRows)
        {
            return false;
        }

        TerminalRow targetRow = _screen.GetViewportRow(targetRowIndex);
        while (targetColIndex >= 0 && targetRow[targetColIndex].Width == 0 &&
            !targetRow[targetColIndex].IsWideSpacerHead)
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

        Span<char> singleCodepoint = stackalloc char[2];
        scoped ReadOnlySpan<char> currentText;
        if (string.IsNullOrEmpty(targetCell.Grapheme))
        {
            if (!Rune.IsValid(targetCell.Codepoint))
            {
                return false;
            }

            int length = new Rune(targetCell.Codepoint).EncodeToUtf16(singleCodepoint);
            currentText = singleCodepoint[..length];
        }
        else
        {
            currentText = targetCell.Grapheme;
        }

        int oldWidth = targetCell.Width <= 0 ? 1 : targetCell.Width;
        int newWidth = oldWidth;
        if (useBoundaries)
        {
            if (!TerminalCellWidthCalculator.TryGetAppendedGraphemeWidth(currentText, codepoint, oldWidth, out newWidth, out bool ignored)) return false;
            if (ignored) return true;
        }
        else if (codepoint is 0xFE0E or 0xFE0F)
        {
            Codepoint first = new((uint)targetCell.Codepoint);
            if (first.GraphemeBreakClass != GraphemeBreakClass.ExtendedPictographic || first.IsEmojiModifierBase) return true;
        }

        // Ghostty retains the base plus at most 64 suffix codepoints. Keep
        // excess combining input attached (and ignored), rather than printing
        // it into another cell or allowing an unbounded per-cell allocation.
        int codepointCount = 0;
        foreach (Rune _ in currentText.EnumerateRunes())
        {
            codepointCount++;
        }
        if (codepointCount >= MaxGraphemeCodepoints)
        {
            return true;
        }

        newWidth = newWidth <= 1 ? 1 : 2;

        if (newWidth == 2 && targetColIndex >= CursorRightLimit)
        {
            // A selector can widen a base already printed in the last column.
            // Ghostty moves the entire styled grapheme to the next line.
            if (!_autoWrap || _screen.Columns < 2) return true;
            TerminalCell moved = targetCell;
            int movedCodepoint = moved.Codepoint;
            Span<char> widenedSuffix = stackalloc char[2];
            int widenedLength = new Rune(codepoint).EncodeToUtf16(widenedSuffix);
            string widenedText = string.Concat(currentText, widenedSuffix[..widenedLength]);
            ClearPreservedCellsForMutation(targetRow);
            ClearRasterGraphicsForTextMutation(targetRowIndex, targetColIndex, 1);
            TerminalCell spacerHead = targetCell;
            if (targetCell.Grapheme is null) WriteCellFromPen(ref spacerHead, 0, 0);
            spacerHead.Codepoint = 0;
            spacerHead.Grapheme = null;
            bool atScreenEdge = targetColIndex == _screen.Columns - 1;
            spacerHead.Width = atScreenEdge ? (byte)0 : (byte)1;
            spacerHead.IsWideSpacerHead = atScreenEdge;
            targetRow[targetColIndex] = spacerHead;
            targetRow.IsDirty = true;
            _delayedWrap = false;
            LineFeed(wrapForced: atScreenEdge, softWrap: true);
            _cursorCol = _scrollLeft;
            WriteCellFromPen(ref moved, movedCodepoint, 2);
            moved.Grapheme = widenedText;
            TerminalRow destination = _screen.GetViewportRow(_cursorRow);
            ClearPreservedCellsForMutation(destination);
            ClearRasterGraphicsForTextMutation(_cursorRow, _scrollLeft, 2);
            ClearCellAndWideArtifacts(destination, _scrollLeft);
            ClearCellAndWideArtifacts(destination, _scrollLeft + 1);
            destination[_scrollLeft] = moved;
            WriteCellFromPen(ref destination[_scrollLeft + 1], 0, 0);
            destination.IsDirty = true;
            AdvanceCursorAfterGraphic(2);
            return true;
        }

        ClearPreservedCellsForMutation(targetRow);
        ClearRasterGraphicsForTextMutation(targetRowIndex, targetColIndex, Math.Max(oldWidth, newWidth));

        targetCell.Codepoint = targetCell.Codepoint != 0
            ? targetCell.Codepoint
            : codepoint;
        Span<char> suffix = stackalloc char[2];
        int suffixLength = new Rune(codepoint).EncodeToUtf16(suffix);
        targetCell.Grapheme = string.Concat(currentText, suffix[..suffixLength]);
        targetCell.Width = (byte)newWidth;

        if (newWidth == 2 && oldWidth == 1)
        {
            ref TerminalCell spacer = ref targetRow[targetColIndex + 1];
            WriteCellFromPen(ref spacer, 0, 0);
        }

        if (oldWidth == 2 && newWidth == 1 && targetColIndex + 1 < targetRow.Columns)
        {
            targetRow[targetColIndex + 1].Width = 1;
            if (targetRowIndex == _cursorRow)
            {
                _delayedWrap = false;
                _cursorCol = Math.Min(targetColIndex + 1, CursorRightLimit);
            }
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

        LineFeed(wrapForced: _cursorCol == _screen.Columns - 1, softWrap: true);
        _cursorCol = _scrollLeft;
        return true;
    }

    private void AdvanceCursorAfterGraphic(int width)
    {
        int nextColumn = _cursorCol + Math.Max(1, width);
        int right = CursorRightLimit;
        if (_autoWrap && nextColumn > right)
        {
            _cursorCol = right;
            _delayedWrap = true;
            return;
        }

        _cursorCol = Math.Min(nextColumn, right);
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
                left = CreateErasedCell();
            }
        }
        else if (existing.Width == 2 && column + 1 < row.Columns)
        {
            ref TerminalCell right = ref row[column + 1];
            if (right.Width == 0)
            {
                right = CreateErasedCell();
            }
        }

        row[column] = CreateErasedCell();
    }

    private void ClearPreviousWideSpacerHead()
        => ClearWideSpacerHeadAt(_cursorRow - 1);

    private void ClearWideSpacerHeadAt(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _screen.ViewportRows) return;
        TerminalRow previous = _screen.GetViewportRow(rowIndex);
        if (!previous.ReadOnlyCells[^1].IsWideSpacerHead) return;
        ref TerminalCell head = ref previous[previous.Columns - 1];
        head.IsWideSpacerHead = false;
        head.Width = 1;
        previous.IsDirty = true;
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

    private void LineFeed(bool wrapForced, bool softWrap = false)
    {
        if (_cursorRow >= 0 && _cursorRow < _screen.ViewportRows)
        {
            if (wrapForced) _screen.GetViewportRow(_cursorRow).WrapsToNext = true;
        }

        if (_cursorRow == _scrollBottom && CursorInsideHorizontalMargins)
        {
            // At bottom of scroll region — scroll the region up
            ScrollUpInRegion();
        }
        else if (_cursorRow < _screen.ViewportRows - 1 && _cursorRow != _scrollBottom)
        {
            _cursorRow++;
        }
        AdvanceSemanticLine(softWrap || wrapForced);
        if (wrapForced)
        {
            TerminalRow row = _screen.GetViewportRow(_cursorRow);
            row.IsWrapContinuation = true;
            row.IsDirty = true;
        }
    }

    private void ReverseIndex()
    {
        if (_cursorRow == _scrollTop && CursorInsideHorizontalMargins)
        {
            // At top of scroll region — scroll region down
            ScrollDownInRegion();
        }
        else if (_cursorRow > 0)
        {
            _cursorRow = Math.Max(_cursorRow >= _scrollTop ? _scrollTop : 0, _cursorRow - 1);
        }
    }

    private void TabForward()
    {
        if (_cursorCol >= RightMargin) return;
        for (var c = _cursorCol + 1; c <= RightMargin; c++)
        {
            if (_tabStops.Contains(c))
            {
                _cursorCol = c;
                return;
            }
        }
        _cursorCol = RightMargin;
    }

    #endregion

    #region Scroll Region Operations

    private void ScrollUpInRegion(int count = 1) => ScrollRegion(count, down: false);

    private void ScrollDownInRegion(int count = 1) => ScrollRegion(count, down: true);

    private void ScrollRegion(int count, bool down)
    {
        count = Math.Clamp(count, 1, _scrollBottom - _scrollTop + 1);
        bool adjustImages = _kittyStore.PlacementCount > 0 &&
            (_scrollTop != 0 || _scrollBottom != _screen.ViewportRows - 1 || HasHorizontalMargins);
        ulong revision = _kittyStore.Revision;
        if (adjustImages)
            _kittyStore.BeginMarginScroll(_screen, _scrollTop, _scrollBottom, down ? count : -count,
                (uint)(_widthPx / _screen.Columns), (uint)(_heightPx / _screen.ViewportRows),
                windowShift: !down && _scrollTop == 0 && !_inAltScreen && !HasHorizontalMargins,
                left: _scrollLeft, right: RightMargin);
        try
        {
            if (HasHorizontalMargins)
            {
                ScrollRectangle(_scrollTop, _scrollBottom, count, down);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                if (down) ScrollDownOneRow();
                else ScrollUpOneRow();
            }
        }
        finally
        {
            if (adjustImages)
            {
                _kittyStore.EndMarginScroll(_screen);
                if (_kittyStore.Revision != revision) PublishKittyGraphics();
            }
        }
    }

    private void ScrollUpOneRow()
    {
        if (_scrollTop == 0 && !_inAltScreen)
        {
            // A top-origin region creates history even with a bottom margin.
            TerminalRow added = _scrollBottom == _screen.ViewportRows - 1
                ? _screen.AddRow() : _screen.AddRowAtActiveRow(_scrollBottom);
            if (_currentBgKind != SgrColorKind.Default)
                added.Clear(_screen.DefaultForeground, _currentBg, CurrentBackgroundIdentity);
            _screen.InvalidateViewport();
        }
        else
        {
            // Scroll within region: shift rows up, insert blank at bottom of region
            _screen.ShiftAnchorsInViewportRows(_scrollTop, _scrollBottom, rowDelta: -1);
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
                _screen.GetViewportRow(_scrollBottom).Clear(_currentFg, _currentBg, CurrentBackgroundIdentity);
            }
            _screen.InvalidateViewport();
        }
    }

    private void ScrollDownOneRow()
    {
        // Shift rows down within the scroll region, insert blank at top of region
        _screen.ShiftAnchorsInViewportRows(_scrollTop, _scrollBottom, rowDelta: 1);
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
            _screen.GetViewportRow(_scrollTop).Clear(_currentFg, _currentBg, CurrentBackgroundIdentity);
        }
        _screen.InvalidateViewport();
    }

    private void CopyRow(TerminalRow src, TerminalRow dst)
    {
        src.WrapsToNext = false;
        src.IsWrapContinuation = false;
        dst.CopyActiveFrom(src, _screen.DefaultForeground, _screen.DefaultBackground);
        dst.WrapsToNext = false;
        dst.IsWrapContinuation = false;
        if (dst.ReadOnlyCells[^1].IsWideSpacerHead)
        {
            dst[dst.Columns - 1].IsWideSpacerHead = false;
            dst[dst.Columns - 1].Width = 1;
        }
        NormalizeRowWideCells(dst);
    }

    #endregion

    #region Escape Sequences

    private void ProcessEscape(byte b)
    {
        if (b is >= 0x20 and <= 0x2F)
        {
            _intermediateChar = (char)b;
            _intermediateCount = 1;
            _state = ParserState.EscapeIntermediate;
            return;
        }
        if (b >= 0x80) return;
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

            case (byte)'X': // SOS
            case (byte)'^': // PM
            case (byte)'_': // APC
                EnterApcState();
                break;


            case (byte)'N': _charsets.Invoke(2, single: true); _state = ParserState.Ground; break;
            case (byte)'O': _charsets.Invoke(3, single: true); _state = ParserState.Ground; break;
            case (byte)'n': _charsets.Invoke(2); _state = ParserState.Ground; break;
            case (byte)'o': _charsets.Invoke(3); _state = ParserState.Ground; break;
            case (byte)'~': _charsets.Invoke(1, right: true); _state = ParserState.Ground; break;
            case (byte)'}': _charsets.Invoke(2, right: true); _state = ParserState.Ground; break;
            case (byte)'|': _charsets.Invoke(3, right: true); _state = ParserState.Ground; break;

            case (byte)'V': // SPA — Start of guarded area
                SetCharacterProtection(CharacterProtectionMode.Iso);
                _state = ParserState.Ground;
                break;

            case (byte)'W': // EPA — End of guarded area
                SetCharacterProtection(CharacterProtectionMode.Off);
                _state = ParserState.Ground;
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
                CarriageReturn();
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

            case (byte)'Z': // DECID — Same report as primary device attributes.
                ResponseCallback?.Invoke(GetPrimaryDeviceAttributesResponse());
                _state = ParserState.Ground;
                break;

            case (byte)'c': // RIS — Full reset
                ResetInternal(raiseModeChanged: false, SessionScreenResetMode.ClearViewport);
                ProgressReportCallback?.Invoke(new TerminalProgressReport(TerminalProgressState.Remove, null));
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
        if (b is >= 0x20 and <= 0x2F) { _intermediateCount = Math.Min(5, _intermediateCount + 1); return; }
        if (b >= 0x80) return;
        if (_intermediateCount > 1) { _state = ParserState.Ground; return; }
        int slot = _intermediateChar switch { '(' => 0, ')' => 1, '*' => 2, '+' => 3, _ => -1 };
        if (slot >= 0) _charsets.Designate(slot, b);

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
            AddCsiParameter(colon: false);
            _state = ParserState.CsiParam;
        }
        else if (b is >= 0x3C and <= 0x3F)
        {
            _csiPrivateMarker = (char)b;
            _state = ParserState.CsiParam;
        }
        else if (b is >= 0x20 and <= 0x2F)
        {
            _intermediateChar = (char)b;
            _intermediateCount = 1;
            _state = ParserState.CsiIntermediate;
        }
        else if (b == ':') _state = ParserState.CsiIgnore;
        else if (b is >= 0x40 and <= 0x7E)
        {
            ExecuteCsi((char)b);
        }
    }

    private void ProcessCsiParam(byte b)
    {
        if (b is >= (byte)'0' and <= (byte)'9')
        {
            _currentParam = Math.Min(ushort.MaxValue, _currentParam * 10 + (b - '0'));
            _hasParam = true;
        }
        else if (b is (byte)';' or (byte)':')
        {
            AddCsiParameter(colon: b == ':');
        }
        else if (b is >= 0x20 and <= 0x2F)
        {
            if (_hasParam)
            {
                AddCsiParameter(colon: false, final: true);
            }
            if (_state == ParserState.CsiIgnore) return;
            _intermediateChar = (char)b;
            _intermediateCount = 1;
            _state = ParserState.CsiIntermediate;
        }
        else if (b is >= 0x40 and <= 0x7E)
        {
            if (_hasParam)
                AddCsiParameter(colon: false, final: true);
            if (_state == ParserState.CsiIgnore) { _state = ParserState.Ground; return; }
            ExecuteCsi((char)b);
        }
        else if (b is >= 0x3C and <= 0x3F) _state = ParserState.CsiIgnore;
    }

    private void ProcessCsiIntermediate(byte b)
    {
        if (b is >= 0x40 and <= 0x7E)
        {
            ExecuteCsi((char)b);
        }
        else if (b is >= 0x20 and <= 0x2F)
        {
            _intermediateCount = Math.Min(5, _intermediateCount + 1);
        }
        else if (b is >= 0x30 and <= 0x3F) _state = ParserState.CsiIgnore;
    }

    private void ProcessOscString(byte b)
    {
        if (b == 0x07) // BEL terminator
        {
            HandleOscString(bellTerminator: true);
            _state = ParserState.Ground;
            return;
        }

        if (b == 0x1B)
        {
            HandleOscString(bellTerminator: false);
            EnterCsiState(); // Clear metadata for the new ESC operation.
            _state = ParserState.Escape;
            return;
        }

        // Ordinary C0 bytes are ignored inside OSC, not executed or retained.
        if (b >= 0x20) AppendOscByteOrDiscard(b);
    }

    private void HandleOscString(bool bellTerminator)
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

        ReadOnlySpan<byte> rawPayload = CollectionsMarshal.AsSpan(_oscBuffer);
        int colorSeparator = rawPayload.IndexOf((byte)';');
        ReadOnlySpan<byte> colorSelector = colorSeparator < 0 ? rawPayload : rawPayload[..colorSeparator];
        if (TryOscColorOperation(colorSelector, out int colorOperation))
        {
            string colors = colorSeparator < 0 ? string.Empty : Encoding.UTF8.GetString(rawPayload[(colorSeparator + 1)..]);
            _oscBuffer.Clear();
            if (colorOperation == 21) HandleKittyColors(colors.AsSpan(), bellTerminator);
            else HandleOscColors(colorOperation, colors.AsSpan(), bellTerminator);
            return;
        }
        if (rawPayload.StartsWith("8;"u8))
        {
            HandleOscHyperlink(rawPayload[2..]);
            _oscBuffer.Clear();
            return;
        }
        string oscPayload = Encoding.UTF8.GetString(rawPayload);
        _oscBuffer.Clear();

        int separator = oscPayload.IndexOf(';');
        ReadOnlySpan<char> selector = separator < 0 ? oscPayload.AsSpan() : oscPayload.AsSpan(0, separator);
        string value = separator >= 0 && separator + 1 < oscPayload.Length
            ? oscPayload[(separator + 1)..]
            : string.Empty;

        if (!int.TryParse(selector, out int selectorCode))
        {
            return;
        }

        if (selectorCode == 133) HandleSemanticPrompt(value.AsSpan());
        _shellIntegrationParser.TryHandleOsc(selectorCode, value);

        switch (selectorCode)
        {
            case 0:
            case 1:
            case 2:
                TitleCallback?.Invoke(value);
                break;

            case 7:
                WorkingDirectoryCallback?.Invoke(value);
                break;

            case 9:
                HandleOsc9(value);
                break;

            case 52:
                HandleOscClipboard(value);
                break;

            case 5522:
                _kittyClipboardProtocol.Handle(
                    value,
                    bellTerminator,
                    ClipboardReadCallback,
                    ClipboardWriteRequestCallback,
                    ClipboardWriteCallback,
                    ResponseCallback);
                break;

            case 777:
                HandleOsc777(value);
                break;

            case 1337:
                HandleOsc1337(value);
                break;
        }
    }

    private void HandleOsc9(string value)
    {
        if (value.StartsWith("9;", StringComparison.Ordinal))
        {
            WorkingDirectoryCallback?.Invoke(value[2..]);
            return;
        }

        if (TryParseOsc9Progress(value, out TerminalProgressReport? report) &&
            report is not null)
        {
            ProgressReportCallback?.Invoke(report);
            return;
        }

        if (IsRecognizedConEmuOsc9(value))
        {
            return;
        }

        DesktopNotificationCallback?.Invoke(new TerminalDesktopNotification(string.Empty, value));
    }

    private void HandleOsc777(string value)
    {
        const string Prefix = "notify;";
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return;
        }

        int titleSeparator = value.IndexOf(';', Prefix.Length);
        if (titleSeparator < 0)
        {
            return;
        }

        string title = value[Prefix.Length..titleSeparator];
        string body = titleSeparator + 1 < value.Length
            ? value[(titleSeparator + 1)..]
            : string.Empty;
        DesktopNotificationCallback?.Invoke(new TerminalDesktopNotification(title, body));
    }

    private void HandleOsc1337(string value)
    {
        int separator = value.IndexOf('=');
        if (separator < 0)
        {
            return;
        }

        ReadOnlySpan<char> key = value.AsSpan(0, separator);
        ReadOnlySpan<char> payload = value.AsSpan(separator + 1);
        if (AsciiEqualsIgnoreCase(key, "CurrentDir"))
        {
            if (!payload.IsEmpty)
            {
                WorkingDirectoryCallback?.Invoke(payload.ToString());
            }

            return;
        }

        if (AsciiEqualsIgnoreCase(key, "Copy") &&
            payload.Length > 1 &&
            payload[0] == ':' &&
            !(payload.Length == 2 && payload[1] == '?'))
        {
            TryWriteClipboard(
                TerminalClipboardLocation.Standard,
                payload[1..],
                allowClear: false);
        }
    }

    private void HandleOscClipboard(string value)
    {
        int separator = value.IndexOf(';');
        if (separator < 0)
        {
            return;
        }

        ReadOnlySpan<char> selector = value.AsSpan(0, separator);
        if (selector.Length > 1)
        {
            return;
        }

        TerminalClipboardLocation location;
        if (selector.IsEmpty || selector.SequenceEqual("c"))
        {
            location = TerminalClipboardLocation.Standard;
        }
        else if (selector.SequenceEqual("s"))
        {
            location = TerminalClipboardLocation.Selection;
        }
        else if (selector.SequenceEqual("p"))
        {
            location = TerminalClipboardLocation.Primary;
        }
        else
        {
            return;
        }

        ReadOnlySpan<char> payload = value.AsSpan(separator + 1);
        if (payload.SequenceEqual("?"))
        {
            TryReadClipboard(location, selector);
            return;
        }

        TryWriteClipboard(location, payload, allowClear: true);
    }

    private void TryReadClipboard(TerminalClipboardLocation location, ReadOnlySpan<char> selector)
    {
        if (ClipboardReadCallback is null || ResponseCallback is null)
        {
            return;
        }

        TerminalClipboardReadReply reply;
        try
        {
            reply = ClipboardReadCallback(
                new TerminalClipboardRead(location, ["text/plain"]));
        }
        catch
        {
            return;
        }

        if (reply.Result != TerminalClipboardReadResult.Success)
        {
            return;
        }

        TerminalClipboardContent? content = reply.Contents.FirstOrDefault(
            static value => value.MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase));
        if (content is null)
        {
            return;
        }

        string response = string.Concat(
            "\u001b]52;",
            selector.ToString(),
            ";",
            Convert.ToBase64String(content.Data),
            "\u001b\\");
        ResponseCallback(Encoding.ASCII.GetBytes(response));
    }

    private void TryWriteClipboard(
        TerminalClipboardLocation location,
        ReadOnlySpan<char> payload,
        bool allowClear)
    {
        if (payload.IsEmpty)
        {
            if (allowClear)
            {
                DispatchClipboardWrite(new TerminalClipboardWrite(location, []));
            }

            return;
        }

        if (!IsStrictBase64(payload))
        {
            return;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(payload.ToString());
        }
        catch (FormatException)
        {
            return;
        }

        DispatchClipboardWrite(
            new TerminalClipboardWrite(
                location,
                [new TerminalClipboardContent("text/plain", decoded)]));
    }

    private void DispatchClipboardWrite(TerminalClipboardWrite request)
    {
        if (ClipboardWriteRequestCallback is not null)
        {
            ClipboardWriteRequestCallback(request);
            return;
        }

        ClipboardWriteCallback?.Invoke(request);
    }

    private static bool IsRecognizedConEmuOsc9(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        return value[0] switch
        {
            '1' => IsRecognizedConEmuOsc9Command1(value),
            '2' or '3' or '6' or '7' or '8' =>
                value.Length >= 2 && value[1] == ';',
            '5' => value.Length == 1,
            _ => false,
        };
    }

    private static bool IsRecognizedConEmuOsc9Command1(string value)
    {
        if (value.Length < 2)
        {
            return false;
        }

        return value[1] switch
        {
            ';' => IsNonEmptyAsciiDecimal(value.AsSpan(2)),
            '0' => value.Length == 2 ||
                   (value.Length == 4 &&
                    value[2] == ';' &&
                    value[3] is >= '0' and <= '3'),
            '1' => value.Length >= 3 && value[2] == ';',
            '2' => value.Length == 2,
            _ => false,
        };
    }

    private static bool IsNonEmptyAsciiDecimal(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsStrictBase64(ReadOnlySpan<char> value)
    {
        if ((value.Length & 3) != 0)
        {
            return false;
        }

        int paddingStart = value.Length;
        while (paddingStart > 0 && value[paddingStart - 1] == '=')
        {
            paddingStart--;
        }

        int paddingLength = value.Length - paddingStart;
        if (paddingLength > 2)
        {
            return false;
        }

        for (int index = 0; index < paddingStart; index++)
        {
            char character = value[index];
            if (!((character is >= 'A' and <= 'Z') ||
                  (character is >= 'a' and <= 'z') ||
                  (character is >= '0' and <= '9') ||
                  character is '+' or '/'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AsciiEqualsIgnoreCase(
        ReadOnlySpan<char> value,
        ReadOnlySpan<char> expected)
    {
        if (value.Length != expected.Length)
        {
            return false;
        }

        for (int index = 0; index < value.Length; index++)
        {
            char actual = value[index];
            char target = expected[index];
            if (actual == target)
            {
                continue;
            }

            if (actual is >= 'A' and <= 'Z')
            {
                actual = (char)(actual + ('a' - 'A'));
            }

            if (target is >= 'A' and <= 'Z')
            {
                target = (char)(target + ('a' - 'A'));
            }

            if (actual != target)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseOsc9Progress(
        string value,
        out TerminalProgressReport? report)
    {
        report = null;
        if (!value.StartsWith("4;", StringComparison.Ordinal) || value.Length < 3)
        {
            return false;
        }

        TerminalProgressState state = value[2] switch
        {
            '0' => TerminalProgressState.Remove,
            '1' => TerminalProgressState.Set,
            '2' => TerminalProgressState.Error,
            '3' => TerminalProgressState.Indeterminate,
            '4' => TerminalProgressState.Pause,
            _ => (TerminalProgressState)(-1),
        };
        if ((int)state < 0)
        {
            return false;
        }

        byte? progress = state == TerminalProgressState.Set ? (byte)0 : null;
        if (value.Length > 3)
        {
            if (value[3] != ';')
            {
                return false;
            }

            ReadOnlySpan<char> percentage = value.AsSpan(4);
            if (percentage.IsEmpty)
            {
                return false;
            }

            int parsed = 0;
            foreach (char character in percentage)
            {
                if (character is < '0' or > '9')
                {
                    return false;
                }

                parsed = Math.Min(100, (parsed * 10) + (character - '0'));
            }

            if (state is TerminalProgressState.Set or
                TerminalProgressState.Error or
                TerminalProgressState.Pause)
            {
                progress = checked((byte)parsed);
            }
        }

        report = new TerminalProgressReport(state, progress);
        return true;
    }

    private void HandleOscHyperlink(ReadOnlySpan<byte> value)
    {
        int separator = value.IndexOf((byte)';');
        if (separator < 0) return;
        ReadOnlySpan<byte> uri = value[(separator + 1)..];
        ReadOnlySpan<byte> options = value[..separator];
        ReadOnlySpan<byte> id = default;
        while (!options.IsEmpty)
        {
            int colon = options.IndexOf((byte)':');
            ReadOnlySpan<byte> item = colon < 0 ? options : options[..colon];
            int equals = item.IndexOf((byte)'=');
            // Ghostty stops at a malformed option, and the last nonempty ID wins.
            if (equals < 0) break;
            if (item[..equals].SequenceEqual("id"u8) && equals + 1 < item.Length)
                id = item[(equals + 1)..];
            if (colon < 0) break;
            options = options[(colon + 1)..];
        }
        if (uri.IsEmpty)
        {
            // An ID on a close is invalid and must not close the active link.
            if (id.IsEmpty) _currentHyperlinkId = 0;
            return;
        }
        ref uint counter = ref (_inAltScreen ? ref _alternateHyperlinkImplicitCounter : ref _primaryHyperlinkImplicitCounter);
        _currentHyperlinkId = _screen.RegisterHyperlink(uri, id, counter);
        if (id.IsEmpty) counter = unchecked(counter + 1);
    }

    private void ProcessDcsString(byte b)
    {
        if (b == 0x1B)
        {
            HandleDcsString();
            EnterCsiState();
            _state = ParserState.Escape;
            return;
        }

        // All C0 bytes except CAN/SUB/ESC are payload; DEL alone is ignored.
        if (b != 0x7F) AppendDcsByteOrDiscard(b);
    }

    private void ProcessApcString(byte b)
    {
        if (b == 0x1B)
        {
            CompleteApc();
            EnterCsiState();
            _state = ParserState.Escape;
            return;
        }

        // C1 SOS/PM/APC stay in the same parser state without exit/entry actions.
        if (b is 0x98 or 0x9E or 0x9F) return;
        if (b is >= 0x80 and <= 0x9F)
        {
            CompleteApc(terminated: b == 0x9C);
            ProcessC1(b);
        }
        // A0-FF are ignored by Ghostty's APC table. All ordinary payload
        // bytes, including C0 and DEL, are handled by the bulk feed path.
    }

    private void AppendApcPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return;
        ReadOnlySpan<byte> glyphIdentifier = "25a1;"u8;
        // Identify only the small prefix byte-by-byte, then retain bulk payload
        // scanning. Unknown '2...' commands keep the existing 4 KiB capture cap.
        while (_apcBuffer.Count < glyphIdentifier.Length && !payload.IsEmpty &&
            CollectionsMarshal.AsSpan(_apcBuffer).SequenceEqual(glyphIdentifier[.._apcBuffer.Count]) &&
            payload[0] == glyphIdentifier[_apcBuffer.Count])
        {
            _apcBuffer.Add(payload[0]);
            payload = payload[1..];
            if (_apcBuffer.Count == glyphIdentifier.Length)
            {
                _apcGlyphRecognized = true;
                _apcGlyphEnabled = _glyphProtocolEnabled;
            }
        }
        if (payload.IsEmpty) return;
        byte first = _apcBuffer.Count > 0 ? _apcBuffer[0] : payload[0];
        int limit = _apcGlyphRecognized
            ? (_apcGlyphEnabled ? 1024 * 1024 + 5 : 5)
            : first == (byte)'G'
            ? _options.KittyGraphicsMaxApcBytes
            : MaxUnknownSequenceBytes;
        int count = Math.Min(payload.Length, Math.Max(0, limit - _apcBuffer.Count));
        if (count > 0)
        {
            int offset = _apcBuffer.Count;
            _apcBuffer.EnsureCapacity(offset + count);
            CollectionsMarshal.SetCount(_apcBuffer, offset + count);
            payload[..count].CopyTo(CollectionsMarshal.AsSpan(_apcBuffer)[offset..]);
        }
        if (count < payload.Length) _apcTruncated = true;
    }

    private void CompleteApc(bool terminated = true)
    {
        try
        {
            if (_apcGlyphRecognized)
            {
                if (_apcGlyphEnabled && !_apcTruncated &&
                    ManagedGlyphProtocol.Execute(CollectionsMarshal.AsSpan(_apcBuffer)[5..], _screen.GlyphGlossary, ResponseCallback))
                    _screen.NotifyGlyphGlossaryChanged();
                return;
            }
            if (_apcBuffer.Count > 0 && _apcBuffer[0] == (byte)'G')
            {
                if (!_apcTruncated)
                    ProcessKittyApc(CollectionsMarshal.AsSpan(_apcBuffer)[1..]);
                return;
            }
            if (terminated) UnknownSequenceCallback?.Invoke(
                new TerminalUnknownSequence(
                    TerminalUnknownSequenceType.Apc,
                    _apcBuffer.ToArray(),
                    _apcTruncated));
        }
        finally
        {
            _apcBuffer.Clear();
            _apcTruncated = false;
            _state = ParserState.Ground;
        }
    }

    private void HandleDcsString(bool aborted = false)
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
            // Optional Sixel follows xterm's unsuccessful-unhook policy.
            if (!aborted) HandleSixelDcsPayload(payloadBytes);
            return;
        }

        string payload = Encoding.ASCII.GetString(payloadBytes);

        // DCS $ q Pt ST — DECRQSS request.
        if (payload.StartsWith("$q", StringComparison.Ordinal))
        {
            string request = payload.Length > 2 ? payload[2..] : string.Empty;
            HandleDecRequestStatusString(request);
            return;
        }

        // DCS + q Pt ST — XTGETTCAP query. One reply is emitted per supported key.
        if (payload.StartsWith("+q", StringComparison.Ordinal))
        {
            ReadOnlySpan<char> keys = payload.AsSpan(2);
            while (!keys.IsEmpty)
            {
                int separator = keys.IndexOf(';');
                ReadOnlySpan<char> key = separator < 0 ? keys : keys[..separator];
                if (GhosttyXtgettcap.TryCreateResponse(key, _options.TerminfoName, out byte[] response))
                {
                    ResponseCallback?.Invoke(response);
                }

                if (separator < 0)
                {
                    break;
                }

                keys = keys[(separator + 1)..];
            }
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

        if (_dcsBuffer.Count >= _dcsBufferLimit)
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

        if (_oscBuffer.Count >= GetOscBufferLimit([]))
        {
            _oscBuffer.Clear();
            _isDiscardingOscPayload = true;
            return;
        }

        _oscBuffer.Add(b);
    }

    private void AppendControlStringPayload(ReadOnlySpan<byte> payload)
    {
        // Span's vectorized control-byte search and one bulk copy mirror
        // Ghostty's OSC scanner while preserving control dispatch at boundaries.
        bool osc = _state == ParserState.OscString;
        if (osc ? _isDiscardingOscPayload : _isDiscardingDcsPayload)
        {
            return;
        }
        List<byte> buffer = osc ? _oscBuffer : _dcsBuffer;
        int limit = osc
            ? GetOscBufferLimit(payload)
            : _dcsBufferLimit;
        if (payload.Length > limit - buffer.Count)
        {
            buffer.Clear();
            if (osc) _isDiscardingOscPayload = true;
            else _isDiscardingDcsPayload = true;
            return;
        }
        int offset = buffer.Count;
        CollectionsMarshal.SetCount(buffer, offset + payload.Length);
        payload.CopyTo(CollectionsMarshal.AsSpan(buffer)[offset..]);
    }

    private int GetOscBufferLimit(ReadOnlySpan<byte> incoming)
    {
        // Native color OSC captures are fixed-size (2048 payload bytes), not
        // allocating like clipboard strings. Recognize prefixes split anywhere
        // across bytewise or bulk input before retaining a large incoming span.
        ReadOnlySpan<byte> retained = CollectionsMarshal.AsSpan(_oscBuffer);
        Span<byte> prefix = stackalloc byte[4];
        int retainedLength = Math.Min(prefix.Length, retained.Length);
        retained[..retainedLength].CopyTo(prefix);
        int added = Math.Min(prefix.Length - retainedLength, incoming.Length);
        incoming[..added].CopyTo(prefix[retainedLength..]);
        ReadOnlySpan<byte> combined = prefix[..(retainedLength + added)];
        int separator = combined.IndexOf((byte)';');
        return separator >= 0 && TryOscColorOperation(combined[..separator], out _)
            ? 2048 + separator + 1 : MaxOscBufferBytes;
    }

    private bool TryHandleAnywhereCancelControl(byte b)
    {
        if (b is not 0x18 and not 0x1A)
        {
            return false;
        }

        // Ghostty dispatches a valid OSC on every exit, including CAN/SUB.
        if (_state == ParserState.OscString) HandleOscString(bellTerminator: false);
        // Unknown APCs are suppressed on abort, but parsed Kitty commands still
        // finalize, matching stream_terminal.apcEnd's protocol-specific policy.
        if (_state == ParserState.ApcString) CompleteApc(terminated: false);
        if (_state == ParserState.DcsString) HandleDcsString(aborted: true);
        AbortActiveControlString();
        return true;
    }

    private void AbortActiveControlString()
    {
        _params.Clear();
        _currentParam = 0;
        _hasParam = false;
        _csiPrivateMarker = '\0';
        _intermediateChar = '\0';
        _intermediateCount = 0;
        _csiColonSeparators = 0;
        _utf8Codepoint = 0;
        _utf8Remaining = 0;
        _oscBuffer.Clear();
        _dcsBuffer.Clear();
        _apcBuffer.Clear();
        _isDiscardingOscPayload = false;
        _isDiscardingDcsPayload = false;
        _apcTruncated = false;
        _state = ParserState.Ground;
    }

    private void HandleDecRequestStatusString(string request)
    {
        string? responsePayload = request switch
        {
            "m" => $"{BuildCurrentSgrState()}m",
            "r" => $"{_scrollTop + 1};{_scrollBottom + 1}r",
            "s" when _extendedDecModesEnabled.Contains(69) => $"{_scrollLeft + 1};{RightMargin + 1}s",
            " q" => $"{CursorStyleReport} q",
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
        // DEC DECRPSS requires an initial reset parameter. Ghostty and
        // Windows Terminal both preserve indexed colors in this response.
        List<string> parameters = ["0"];

        if ((_currentAttrs & CellAttributes.Bold) != 0) parameters.Add("1");
        if ((_currentAttrs & CellAttributes.Dim) != 0) parameters.Add("2");
        if ((_currentAttrs & CellAttributes.Italic) != 0) parameters.Add("3");
        if (_currentUnderlineStyle == TerminalUnderlineStyle.Double)
        {
            parameters.Add("4:2");
        }
        else if (_currentUnderlineStyle != TerminalUnderlineStyle.None ||
                 (_currentAttrs & CellAttributes.Underline) != 0)
        {
            parameters.Add("4");
        }
        if ((_currentAttrs & CellAttributes.Blink) != 0) parameters.Add("5");
        if ((_currentAttrs & CellAttributes.Inverse) != 0) parameters.Add("7");
        if ((_currentAttrs & CellAttributes.Hidden) != 0) parameters.Add("8");
        if ((_currentAttrs & CellAttributes.Strikethrough) != 0) parameters.Add("9");
        if ((_currentDecorations & CellDecorations.Overline) != 0) parameters.Add("53");

        AppendSgrColor(
            parameters,
            foreground: true,
            _currentFgKind,
            _currentFgPaletteIndex,
            _currentFg);
        AppendSgrColor(
            parameters,
            foreground: false,
            _currentBgKind,
            _currentBgPaletteIndex,
            _currentBg);

        return string.Join(';', parameters);
    }

    private static void AppendSgrColor(
        ICollection<string> parameters,
        bool foreground,
        SgrColorKind kind,
        int paletteIndex,
        uint rgb)
    {
        int baseIndex = foreground ? 30 : 40;
        switch (kind)
        {
            case SgrColorKind.Default:
                return;
            case SgrColorKind.Palette when paletteIndex < 8:
                parameters.Add((baseIndex + paletteIndex).ToString(CultureInfo.InvariantCulture));
                return;
            case SgrColorKind.Palette when paletteIndex < 16:
                parameters.Add((baseIndex + 60 + paletteIndex - 8).ToString(CultureInfo.InvariantCulture));
                return;
            case SgrColorKind.Palette:
                parameters.Add($"{baseIndex + 8}:5:{paletteIndex}");
                return;
            case SgrColorKind.Rgb:
                parameters.Add(
                    $"{baseIndex + 8}:2::{(rgb >> 16) & 0xFF}:{(rgb >> 8) & 0xFF}:{rgb & 0xFF}");
                return;
        }
    }

    private void ExecuteCsi(char finalByte)
    {
        _state = ParserState.Ground;
        if (_intermediateCount > 1) return;
        if (_csiColonSeparators != 0 && finalByte != 'm') return;
        if (_csiPrivateMarker != '\0' && _intermediateChar != '\0' &&
            !(_csiPrivateMarker == '?' && _intermediateChar == '$' && finalByte == 'p')) return;

        var p0 = _params.Count > 0 ? _params[0] : 0;
        var p1 = _params.Count > 1 ? _params[1] : 0;

        // DEC private mode families: CSI ? ...
        if (_csiPrivateMarker == '?')
        {
            if (finalByte is 's' or 'r')
            {
                SaveOrRestoreDecModes(restore: finalByte == 'r');
                return;
            }
            if (finalByte is 'J' or 'K' && _params.Count <= 1)
            {
                if (finalByte == 'J') EraseInDisplay(p0, selective: true);
                else EraseInLine(p0, selective: true);
                return;
            }
            if (_intermediateChar == '$' && finalByte == 'p')
            {
                HandleDecModeQuery();
                return;
            }

            if (finalByte == 'n')
            {
                HandleDecDeviceStatusQuery();
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
        if (_csiPrivateMarker == '\0' && _intermediateChar == '!' && finalByte == 'p')
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
            if (_params.Count <= 1) SetCursorStyle(p0);
            return;
        }

        // ANSI mode query family: CSI Ps $ p
        if (_csiPrivateMarker == '\0' && _intermediateChar == '$' && finalByte == 'p')
        {
            HandleAnsiModeQuery();
            return;
        }

        if (_intermediateChar == '"' && finalByte == 'q' && _params.Count <= 1)
        {
            if (p0 is 0 or 2) SetCharacterProtection(CharacterProtectionMode.Off);
            else if (p0 == 1) SetCharacterProtection(CharacterProtectionMode.Dec);
            return;
        }
        if (_intermediateChar != '\0') return;

        ResetDelayedWrapForCsi(finalByte);

        switch (finalByte)
        {
            case 'A': // CUU — Cursor Up
                _cursorRow = Math.Max(_cursorRow >= _scrollTop ? _scrollTop : 0, _cursorRow - Math.Max(1, p0));
                break;

            case 'B': // CUD — Cursor Down
                _cursorRow = Math.Min(_cursorRow <= _scrollBottom ? _scrollBottom : _screen.ViewportRows - 1, _cursorRow + Math.Max(1, p0));
                break;

            case 'C': // CUF — Cursor Forward
                _cursorCol = Math.Min(CursorRightLimit, _cursorCol + Math.Max(1, p0));
                break;

            case 'D': // CUB — Cursor Back
                _cursorCol = Math.Max(0, _cursorCol - Math.Max(1, p0));
                break;

            case 'E': // CNL — Cursor Next Line
                CarriageReturn();
                _cursorRow = Math.Min(_cursorRow <= _scrollBottom ? _scrollBottom : _screen.ViewportRows - 1, _cursorRow + Math.Max(1, p0));
                break;

            case 'F': // CPL — Cursor Previous Line
                CarriageReturn();
                _cursorRow = Math.Max(_cursorRow >= _scrollTop ? _scrollTop : 0, _cursorRow - Math.Max(1, p0));
                break;

            case 'G': // CHA — Cursor Horizontal Absolute
                _cursorCol = Math.Clamp(Math.Max(1, p0) - 1 + (_originMode ? _scrollLeft : 0), 0, _originMode ? RightMargin : _screen.Columns - 1);
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
                    col = Math.Clamp(col + _scrollLeft, _scrollLeft, RightMargin);
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
                _cursorRow = Math.Clamp(Math.Max(1, p0) - 1 + (_originMode ? _scrollTop : 0), 0, _originMode ? _scrollBottom : _screen.ViewportRows - 1);
                break;

            case 'e': // VPR — Vertical Position Relative
                _cursorRow = Math.Min(_cursorRow <= _scrollBottom ? _scrollBottom : _screen.ViewportRows - 1, _cursorRow + Math.Max(1, p0));
                break;

            case 'a': // HPR — Horizontal Position Relative
                _cursorCol = Math.Min(CursorRightLimit, _cursorCol + Math.Max(1, p0));
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
                    HomeCursor();
                }
                break;
            }

            case 's': // SCP — Save Cursor Position (ANSI.SYS)
                if (_params.Count > 2) break;
                if (_extendedDecModesEnabled.Contains(69))
                {
                    SetHorizontalMargins();
                    break;
                }
                if (_params.Count != 0) break; // Explicit parameters mean DECSLRM, disabled without mode 69.
                SaveCursor();
                break;

            case 'u': // RCP — Restore Cursor Position (ANSI.SYS)
                RestoreCursor();
                break;

            case '@': // ICH — Insert Characters
                InsertCharacters(Math.Max(1, p0));
                break;

            case 'S': // SU — Scroll Up
                ScrollUpInRegion(Math.Max(1, p0));
                break;

            case 'T': // SD — Scroll Down
                ScrollDownInRegion(Math.Max(1, p0));
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
                    int reportRow = Math.Max(0, _cursorRow - (_originMode ? _scrollTop : 0));
                    int reportColumn = Math.Max(0, _cursorCol - (_originMode ? _scrollLeft : 0));
                    var cpr = $"\x1b[{reportRow + 1};{reportColumn + 1}R";
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
                int left = _originMode ? _scrollLeft : 0;
                for (var n = 0; n < count; n++)
                {
                    if (_cursorCol <= left) break;
                    var found = false;
                    for (var c = _cursorCol - 1; c >= left; c--)
                    {
                        if (_tabStops.Contains(c))
                        {
                            _cursorCol = c;
                            found = true;
                            break;
                        }
                    }
                    if (!found) _cursorCol = left;
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
                string response = $"\x1b[4;{(long)_reportCellHeightPx * _screen.ViewportRows};{(long)_reportCellWidthPx * _screen.Columns}t";
                ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
                break;
            }

            case 16: // CSI 16 t — cell size in pixels
            {
                string response = $"\x1b[6;{_reportCellHeightPx};{_reportCellWidthPx}t";
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
                if (_options.TitleReportEnabled)
                {
                    ResponseCallback?.Invoke("\x1b]l\x1b\\"u8.ToArray());
                }
                break;
        }
    }

    private void HandleDecDeviceStatusQuery()
    {
        if (_params.Count == 0)
        {
            return;
        }

        switch (_params[0])
        {
            case 996: // Report current color scheme.
                EmitColorSchemeReport();
                break;
            case 998: // Report terminal visibility. The C embedding is potentially visible.
                EmitVisibilityReport();
                break;
        }
    }

    private void EmitColorSchemeReport()
    {
        uint background = _theme.DefaultBackground;
        int red = (int)((background >> 16) & 0xFF);
        int green = (int)((background >> 8) & 0xFF);
        int blue = (int)(background & 0xFF);
        int luminance = ((red * 299) + (green * 587) + (blue * 114)) / 1000;
        ResponseCallback?.Invoke(luminance >= 128
            ? "\x1b[?997;2n"u8.ToArray()
            : "\x1b[?997;1n"u8.ToArray());
    }

    private void EmitVisibilityReport()
    {
        ResponseCallback?.Invoke("\x1b[?999;1n"u8.ToArray());
    }

    private void EmitInBandSizeReport()
    {
        if (!_extendedDecModesEnabled.Contains(2048) ||
            _screen.Columns <= 0 ||
            _screen.ViewportRows <= 0)
        {
            return;
        }

        long reportWidth = (long)_screen.Columns * _reportCellWidthPx;
        long reportHeight = (long)_screen.ViewportRows * _reportCellHeightPx;
        string response = $"\x1b[48;{_screen.ViewportRows};{_screen.Columns};{reportHeight};{reportWidth}t";
        ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(response));
    }

    private void HandleDecModeQuery()
    {
        if (_params.Count != 1) return;
        int mode = _params[0];
        EmitDecModeQueryResponse(mode, GetDecPrivateModeReportStatus(mode));
    }

    private void HandleAnsiModeQuery()
    {
        if (_params.Count != 1) return;
        int mode = _params[0];
        EmitAnsiModeQueryResponse(mode, GetAnsiModeReportStatus(mode));
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
        if (mode == 117) // DECECM is recognized but permanently reset.
        {
            return 4;
        }

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
            47 => (_alternateScreenModeBits & 1) != 0 ? 1 : 2,
            1047 => (_alternateScreenModeBits & 2) != 0 ? 1 : 2,
            1048 => _saveCursorMode ? 1 : 2,
            1049 => (_alternateScreenModeBits & 4) != 0 ? 1 : 2,
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

    private void ResetExtendedDecModesToDefaults()
    {
        _mouseModeState = default;
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
                HomeCursor();
                break;

            case 69: // DECLRMM — disabling restores full-width margins
                SetExtendedDecMode(mode, set);
                if (!set) ResetHorizontalMargins();
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
                _alternateScreenModeBits = (byte)(set ? _alternateScreenModeBits | 1 : _alternateScreenModeBits & ~1);
                if (set && !_inAltScreen)
                    SwitchToAltScreen(clearAlt: false);
                else if (!set && _inAltScreen)
                    SwitchToMainScreen();
                break;

            case 3: // DECCOLM is ignored (including its mode value) unless DEC 40 permits it.
                SetColumnMode(set);
                break;

            case 4: // Smooth scroll
            case 5: // Reverse video
            case 8: // Auto-repeat
            case 12: // Cursor blinking
            case 40: // Allow 80/132 mode
            case 45: // Reverse wraparound
            case 1004: // Focus event reporting
            case 1007: // Alternate scroll mode
            case 1035: // Ignore keypad with numlock
            case 1036: // Meta sends escape prefix
            case 1039: // Alt sends escape
            case 1045: // Reverse wrap extended
            case 2027: // Grapheme cluster mode
            case 2031: // Report color scheme mode
                SetExtendedDecMode(mode, set);
                break;

            case 9:
            case 1000:
            case 1002:
            case 1003:
            case 1005:
            case 1006:
            case 1015:
            case 1016:
                SetExtendedDecMode(mode, set);
                _mouseModeState = TerminalMouseModeTransitions.Apply(_mouseModeState, mode, set);
                break;

            case 2026: // Synchronized output
                SetExtendedDecMode(mode, set);
                if (set) BeginRenderHold();
                else EndRenderHold();
                break;

            case 2033: // Report terminal visibility mode
                SetExtendedDecMode(mode, set);
                if (set)
                {
                    EmitVisibilityReport();
                }
                break;

            case 2048: // In-band size reports
                SetExtendedDecMode(mode, set);
                if (set)
                {
                    EmitInBandSizeReport();
                }
                break;

            case 5522: // Kitty clipboard paste events
                SetExtendedDecMode(mode, set);
                break;

            case 1047: // Use alternate screen buffer
                _alternateScreenModeBits = (byte)(set ? _alternateScreenModeBits | 2 : _alternateScreenModeBits & ~2);
                if (set && !_inAltScreen)
                    SwitchToAltScreen(clearAlt: false);
                else if (!set && _inAltScreen)
                {
                    EraseInDisplay(2);
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
                _alternateScreenModeBits = (byte)(set ? _alternateScreenModeBits | 4 : _alternateScreenModeBits & ~4);
                if (set)
                {
                    SaveCursor();
                    SwitchToAltScreen(clearAlt: true);
                }
                else
                {
                    SwitchToMainScreen(copySemanticPen: false);
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
        if (_inAltScreen)
        {
            if (clearAlt) EraseInDisplay(2);
            return;
        }

        if (_screen.ScrollOffset != 0)
        {
            _screen.ScrollOffset = 0;
        }

        _savedMainCursorCol = _cursorCol;
        _savedMainCursorRow = _cursorRow;
        _savedMainDelayedWrap = _delayedWrap;
        _alternateSemanticPen = _primarySemanticPen;
        _alternateCursorStyle = _primaryCursorStyle;
        _alternateHyperlinkImplicitCounter = _primaryHyperlinkImplicitCounter;
        _currentHyperlinkId = 0;
        _inAltScreen = true;
        _screen.SwitchToAlternateBuffer(clear: false);
        _kittyStore = _alternateKittyStore ??= new ManagedKittyGraphicsStore(_options.KittyGraphicsStorageLimitBytes);
        AdvanceKittyAnimations();
        PublishKittyGraphics();
        if (clearAlt)
        {
            ClearAlternateBeforeCursorCopy();
            // Ghostty clears the destination before copying the entering cursor.
            _delayedWrap = _savedMainDelayedWrap;
        }

        // Scrolling margins are terminal-wide and survive screen switches.
    }

    private void SwitchToMainScreen(bool restoreRestartPosition = false, bool copySemanticPen = true)
    {
        if (!_inAltScreen) return;
        _alternateEraseBackground = CurrentBackgroundIdentity;
        (_savedAlternateCursorCol, _savedAlternateCursorRow, _savedAlternateDelayedWrap) = (_cursorCol, _cursorRow, _delayedWrap);

        if (_screen.ScrollOffset != 0)
        {
            _screen.ScrollOffset = 0;
        }

        if (copySemanticPen)
        {
            _primaryCursorStyle = _alternateCursorStyle;
            _primarySemanticPen = _alternateSemanticPen;
            _primaryHyperlinkImplicitCounter = _alternateHyperlinkImplicitCounter;
        }
        _screen.SwitchToPrimaryBuffer();
        _currentHyperlinkId = 0;
        _kittyStore = _primaryKittyStore;
        AdvanceKittyAnimations();
        PublishKittyGraphics();

        if (restoreRestartPosition)
        {
            _cursorCol = _savedMainCursorCol;
            _cursorRow = _savedMainCursorRow;
            _delayedWrap = _savedMainDelayedWrap;
        }
        _inAltScreen = false;

        // Scrolling margins are terminal-wide and survive screen switches.

        _screen.InvalidateAll();
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
            if (ProcessSgrParameterGroup(ref i)) continue;

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
                    SetForegroundPalette(p - 30);
                    break;
                case 39:
                    _currentFg = _screen.DefaultForeground;
                    _currentFgKind = SgrColorKind.Default;
                    break;

                // Standard background colors
                case >= 40 and <= 47:
                    SetBackgroundPalette(p - 40);
                    break;
                case 49:
                    _currentBg = _screen.DefaultBackground;
                    _currentBgKind = SgrColorKind.Default;
                    break;

                // Bright foreground colors
                case >= 90 and <= 97:
                    SetForegroundPalette(p - 82);
                    break;

                // Bright background colors
                case >= 100 and <= 107:
                    SetBackgroundPalette(p - 92);
                    break;


                case 59:
                    _currentUnderlineColor = 0;
                    _currentUnderlineIdentity = default;
                    _currentHasUnderlineColor = false;
                    break;
            }
        }
    }

    private void ResetAttributes()
    {
        _currentFg = _screen.DefaultForeground;
        _currentBg = _screen.DefaultBackground;
        _currentFgKind = SgrColorKind.Default;
        _currentBgKind = SgrColorKind.Default;
        _currentAttrs = CellAttributes.None;
        _currentUnderlineStyle = TerminalUnderlineStyle.None;
        _currentUnderlineColor = 0;
        _currentUnderlineIdentity = default;
        _currentHasUnderlineColor = false;
        _currentDecorations = CellDecorations.None;
    }

    private void SetForegroundPalette(int paletteIndex)
    {
        paletteIndex = Math.Clamp(paletteIndex, 0, 255);
        _currentFg = PaletteColor(paletteIndex);
        _currentFgKind = SgrColorKind.Palette;
        _currentFgPaletteIndex = paletteIndex;
    }

    private static TerminalColorIdentity GetColorIdentity(SgrColorKind kind, int paletteIndex, uint rgb)
        => kind switch
        {
            SgrColorKind.Palette => TerminalColorIdentity.Palette((byte)paletteIndex),
            SgrColorKind.Rgb => TerminalColorIdentity.Rgb(rgb),
            _ => default,
        };

    private TerminalColorIdentity CurrentBackgroundIdentity
        => GetColorIdentity(_currentBgKind, _currentBgPaletteIndex, _currentBg);

    private TerminalCell CreateErasedCell()
        => TerminalCell.Empty(_currentFg, _currentBg, CurrentBackgroundIdentity);

    private void SetBackgroundPalette(int paletteIndex)
    {
        paletteIndex = Math.Clamp(paletteIndex, 0, 255);
        _currentBg = PaletteColor(paletteIndex);
        _currentBgKind = SgrColorKind.Palette;
        _currentBgPaletteIndex = paletteIndex;
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

    private void EraseInDisplay(int mode, bool selective = false)
    {
        ClampCursor();
        if ((selective || ProtectionMode == CharacterProtectionMode.Iso) && mode is >= 0 and <= 2)
        {
            EraseProtectedDisplay(mode);
            return;
        }

        switch (mode)
        {
            case 0: // From cursor to end
                EraseInLine(0);
                for (var r = _cursorRow + 1; r < _screen.ViewportRows; r++)
                    _screen.GetViewportRow(r).Clear(_currentFg, _currentBg, CurrentBackgroundIdentity);
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
                    _screen.GetViewportRow(r).Clear(_currentFg, _currentBg, CurrentBackgroundIdentity);
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
                        rowToCursor[c] = CreateErasedCell();
                    NormalizeRowWideCells(rowToCursor);
                    rowToCursor.IsDirty = true;
                }
                break;

            case 2: // Entire display
                if (!_inAltScreen && (ScrollOnEraseInDisplay ||
                    _screen.GetViewportRow(_screen.ViewportRows - 1).SemanticPrompt != TerminalSemanticPrompt.None))
                {
                    _screen.MoveViewportToScrollbackAndClear();
                    for (var r = 0; r < _screen.ViewportRows; r++)
                        _screen.GetViewportRow(r).Clear(_currentFg, _currentBg, CurrentBackgroundIdentity);
                }
                else
                {
                    for (var r = 0; r < _screen.ViewportRows; r++)
                        _screen.GetViewportRow(r).Clear(_currentFg, _currentBg, CurrentBackgroundIdentity);
                    _screen.ClearRasterGraphics();
                }
                break;

            case 3: // Scrollback only
                _screen.ClearScrollback();
                break;

            case 22: // Kitty extension: move viewport into scrollback and clear display
                _screen.MoveViewportToScrollbackAndClear();
                _cursorCol = 0;
                _cursorRow = 0;
                ResetDelayedWrap();
                break;
        }

        if (mode is 2 or 22)
        {
            _kittyStore.ClearScreen(_screen, (uint)GetEffectiveCellWidthPx(), (uint)GetEffectiveCellHeightPx());
            AdvanceKittyAnimations();
            PublishKittyGraphics();
        }
        _screen.InvalidateAll();
    }

    private void EraseInLine(int mode, bool selective = false)
    {
        ClampCursor();
        if (_cursorRow < 0 || _cursorRow >= _screen.ViewportRows) return;
        if (selective || ProtectionMode == CharacterProtectionMode.Iso)
        {
            EraseProtectedLine(mode);
            return;
        }

        var row = _screen.GetViewportRow(_cursorRow);
        ClearPreservedCellsForMutation(row);
        switch (mode)
        {
            case 0: // From cursor to end of line
                for (var c = Math.Max(0, _cursorCol); c < _screen.Columns; c++)
                    row[c] = CreateErasedCell();
                ResetRowSoftWrap(row);
                _screen.ClearRasterGraphicsInViewportRectangle(
                    _cursorRow,
                    _cursorRow,
                    _cursorCol,
                    _screen.Columns - 1);
                break;

            case 1: // From start to cursor
                for (var c = 0; c <= _cursorCol && c < _screen.Columns; c++)
                    row[c] = CreateErasedCell();
                _screen.ClearRasterGraphicsInViewportRectangle(
                    _cursorRow,
                    _cursorRow,
                    0,
                    _cursorCol);
                break;

            case 2: // Entire line
                TerminalSemanticPrompt prompt = row.SemanticPrompt;
                bool continuation = row.IsWrapContinuation;
                ResetRowSoftWrap(row);
                row.Clear(_currentFg, _currentBg, CurrentBackgroundIdentity);
                row.SemanticPrompt = prompt;
                row.IsWrapContinuation = continuation;
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

    private void InsertLines(int count) => ShiftLines(count, insert: true);

    private void DeleteLines(int count) => ShiftLines(count, insert: false);

    private void ShiftLines(int count, bool insert)
    {
        ClampCursor();
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom || !CursorInsideHorizontalMargins) return;
        bool restoreImages = _kittyStore.PlacementCount > 0;
        if (restoreImages) _kittyStore.BeginMarginScroll(_screen, _cursorRow, _scrollBottom, 0, 0, 0);
        try
        {
            if (HasHorizontalMargins)
            {
                ScrollRectangle(_cursorRow, _scrollBottom, count, insert);
                return;
            }
            if (insert) InsertLinesCore(count);
            else DeleteLinesCore(count);
        }
        finally
        {
            if (restoreImages) _kittyStore.EndMarginScroll(_screen);
            _cursorCol = _scrollLeft;
        }
    }

    private void InsertLinesCore(int count)
    {
        ClampCursor();
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;

        for (var n = 0; n < count; n++)
        {
            _screen.ShiftAnchorsInViewportRows(_cursorRow, _scrollBottom, rowDelta: 1);
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
                _screen.GetViewportRow(_cursorRow).Clear(_currentFg, _currentBg, CurrentBackgroundIdentity);
                _screen.ClearRasterGraphicsInViewportRectangle(
                    _cursorRow,
                    _cursorRow,
                    0,
                    _screen.Columns - 1);
            }
        }
        _screen.InvalidateAll();
    }

    private void DeleteLinesCore(int count)
    {
        ClampCursor();
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;

        for (var n = 0; n < count; n++)
        {
            _screen.ShiftAnchorsInViewportRows(_cursorRow, _scrollBottom, rowDelta: -1);
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
                _screen.GetViewportRow(_scrollBottom).Clear(_currentFg, _currentBg, CurrentBackgroundIdentity);
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
        if (!CursorInsideHorizontalMargins) return;
        count = Math.Min(count, RightMargin - _cursorCol + 1);

        var row = _screen.GetViewportRow(_cursorRow);
        ClearPreservedCellsForMutation(row);
        for (var c = RightMargin; c >= _cursorCol + count; c--)
            row[c] = row[c - count];
        for (var c = _cursorCol; c < _cursorCol + count && c <= RightMargin; c++)
            row[c] = CreateErasedCell();
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
        if (!CursorInsideHorizontalMargins) return;
        count = Math.Min(count, RightMargin - _cursorCol + 1);

        var row = _screen.GetViewportRow(_cursorRow);
        if (_cursorCol <= 1 && row.ReadOnlyCells[0].Width == 2) ErasePreviousWideSpacerHead();
        ClearPreservedCellsForMutation(row);
        if (row.ReadOnlyCells[^1].IsWideSpacerHead)
            row[row.Columns - 1] = CreateErasedCell();
        ResetRowSoftWrap(row);
        ResetDelayedWrap();
        for (var c = _cursorCol; c + count <= RightMargin; c++)
            row[c] = row[c + count];
        for (var c = Math.Max(_cursorCol, RightMargin + 1 - count); c <= RightMargin; c++)
            row[c] = CreateErasedCell();
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
        if (ProtectionMode == CharacterProtectionMode.Iso)
        {
            EraseProtectedCharacters(count);
            return;
        }

        var row = _screen.GetViewportRow(_cursorRow);
        if (_cursorCol <= 1 && row.ReadOnlyCells[0].Width == 2) ErasePreviousWideSpacerHead();
        ClearPreservedCellsForMutation(row);
        if (row.ReadOnlyCells[^1].IsWideSpacerHead)
            row[row.Columns - 1] = CreateErasedCell();
        ResetRowSoftWrap(row);
        ResetDelayedWrap();
        for (var c = _cursorCol; c < _cursorCol + count && c < _screen.Columns; c++)
            row[c] = CreateErasedCell();
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
                // A late width selector prints the tail using the current
                // pen. Keep its independent style, link and protection.
                trailing.Width = 0;
                trailing.IsWideSpacerHead = false;
                col++;
                continue;
            }

            if (cell.Width == 0)
            {
                if (cell.IsWideSpacerHead && col == row.Columns - 1 && row.WrapsToNext) continue;
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
        EndRenderHold();
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
        ResetSavedAndScreenModes();
        _insertMode = false;
        _lineFeedNewLineMode = false;
        ResetDelayedWrap();
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;
        ResetHorizontalMargins();
        _charsets = new();
        _lastGraphicCodepoint = 0;
        _oscBuffer.Clear();
        _isDiscardingOscPayload = false;
        _dcsBuffer.Clear();
        _isDiscardingDcsPayload = false;
        _apcBuffer.Clear();
        _apcTruncated = false;
        _currentHyperlinkId = 0;
        _kittyKeyboardMain = default;
        _kittyKeyboardAlt = default;
        ResetAttributes();
        InitTabStops();
        ApplyConfiguredModeDefaults();
        SetCursorStyle(0);
    }

    #endregion

    #region Colors

    private uint PaletteColor(int index)
    {
        index = Math.Clamp(index, 0, 255);
        return _theme.Palette[index];
    }

    #endregion

    /// <summary>
    /// Resets the processor to initial state.
    /// </summary>
    public void Reset()
    {
        ResetInternal(raiseModeChanged: true, SessionScreenResetMode.ClearViewport);
    }

    /// <inheritdoc />
    public void PrepareForNewSession(bool preserveScrollback)
    {
        ResetInternal(
            raiseModeChanged: true,
            preserveScrollback
                ? SessionScreenResetMode.PreserveScrollback
                : SessionScreenResetMode.ClearAll);
    }

    /// <inheritdoc />
    public void ClearScrollback()
    {
        _screen.ClearScrollback();
    }

    /// <inheritdoc />
    public void ClearVisibleHistory()
    {
        if (_inAltScreen)
        {
            return;
        }

        _screen.ClearVisibleHistory(_cursorRow);
        _cursorRow = 0;
        ResetDelayedWrap();
    }

    private void ResetInternal(bool raiseModeChanged, SessionScreenResetMode screenResetMode)
    {
        TerminalModeState before = ModeState;
        EndRenderHold();
        _primaryKittyStore.Clear(_screen);
        _alternateKittyStore?.Clear(_screen);
        _kittyStore = _primaryKittyStore;
        _animationNextTickDelay = null;
        _screen.ClearKittyGraphics();
        bool restorePrimaryAfterAlternateRestart =
            screenResetMode == SessionScreenResetMode.PreserveScrollback &&
            (_inAltScreen || _screen.AlternateBufferActive);
        int restoredPrimaryCursorCol = 0;
        int restoredPrimaryCursorRow = 0;

        if (restorePrimaryAfterAlternateRestart)
        {
            if (_inAltScreen)
            {
                SwitchToMainScreen(restoreRestartPosition: true);
                restoredPrimaryCursorCol = _cursorCol;
                restoredPrimaryCursorRow = _cursorRow;
            }
            else if (_screen.AlternateBufferActive)
            {
                _screen.SwitchToPrimaryBuffer();
                restoredPrimaryCursorCol = Math.Clamp(_cursorCol, 0, Math.Max(0, _screen.Columns - 1));
                restoredPrimaryCursorRow = Math.Clamp(_cursorRow, 0, Math.Max(0, _screen.ViewportRows - 1));
            }

            _screen.DiscardInactiveAlternateBuffer();
        }
        else
        {
            if (_inAltScreen || _screen.AlternateBufferActive)
            {
                _screen.SwitchToPrimaryBuffer();
            }

            _screen.DiscardInactiveAlternateBuffer();
        }

        _cursorCol = 0;
        _cursorRow = 0;
        ResetDelayedWrap();
        _state = ParserState.Ground;
        _utf8Codepoint = 0;
        _utf8Remaining = 0;
        _continuation.Reset();
        _params.Clear();
        _currentParam = 0;
        _hasParam = false;
        _csiColonSeparators = 0;
        _intermediateCount = 0;
        _csiPrivateMarker = '\0';
        _intermediateChar = '\0';
        _oscBuffer.Clear();
        _isDiscardingOscPayload = false;
        _dcsBuffer.Clear();
        _isDiscardingDcsPayload = false;
        _apcBuffer.Clear();
        _apcTruncated = false;
        _kittyClipboardProtocol.Reset();
        _screen.ClearRegisteredGlyphs();
        _currentProtected = false;
        _primaryProtectionMode = _alternateProtectionMode = CharacterProtectionMode.Off;
        _primarySemanticPen = _alternateSemanticPen = default;
        _primaryHyperlinkImplicitCounter = _alternateHyperlinkImplicitCounter = 0;
        _primaryPromptPolicy = _alternatePromptPolicy = default;
        _promptRedraw = TerminalPromptRedraw.All;
        _scrollTop = 0;
        _scrollBottom = _screen.ViewportRows - 1;
        ResetHorizontalMargins();
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
        ResetSavedAndScreenModes();
        _insertMode = false;
        _lineFeedNewLineMode = false;
        _primaryCursorStyle = _alternateCursorStyle = TerminalCursorStyle.Block;
        _charsets = new();
        _lastGraphicCodepoint = 0;
        _primarySavedCursor = _alternateSavedCursor = null;
        _alternateEraseBackground = default;
        _savedAlternateCursorCol = _savedAlternateCursorRow = 0;
        _savedAlternateDelayedWrap = false;
        _currentHyperlinkId = 0;
        _kittyKeyboardMain = default;
        _kittyKeyboardAlt = default;
        ResetAttributes();
        InitTabStops();
        ApplyConfiguredModeDefaults();

        // Ghostty fullReset selects the configured cursor after modes.reset;
        // this policy takes precedence over the restored default mode bank.
        SetCursorStyle(0);

        switch (screenResetMode)
        {
            case SessionScreenResetMode.ClearViewport:
                for (var r = 0; r < _screen.ViewportRows; r++)
                    _screen.GetViewportRow(r).Clear(_screen.DefaultForeground, _screen.DefaultBackground);
                _screen.ClearRasterGraphics();
                break;

            case SessionScreenResetMode.ClearAll:
                _screen.ClearAll();
                break;

            case SessionScreenResetMode.PreserveScrollback:
                if (restorePrimaryAfterAlternateRestart)
                {
                    _cursorCol = restoredPrimaryCursorCol;
                    _cursorRow = restoredPrimaryCursorRow;
                    ResetDelayedWrap();
                    _screen.ScrollOffset = 0;
                    _screen.InvalidateAll();
                }
                else
                {
                    _screen.MoveViewportToScrollbackAndClear();
                }
                break;
        }

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
        if (EndRenderHold())
        {
            SetExtendedDecMode(2026, false);
            _screen.Resize(columns, rows, reflowOnResize: !_inAltScreen);
        }
        ApplyResizeState(columns, rows);
        if (_kittyStore.PlacementCount > 0) PublishKittyGraphics();
    }

    /// <summary>
    /// Notify the processor that the screen has been resized with pixel dimensions.
    /// Pixel dimensions are used to answer CSI 14t/16t size reports.
    /// </summary>
    public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
    {
        UpdateReportCellSize(columns, rows, widthPx, heightPx);
        _widthPx = Math.Max(0, widthPx);
        _heightPx = Math.Max(0, heightPx);
        NotifyResize(columns, rows);
        EmitInBandSizeReport();
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
        => ResizeScreenCore(columns, rows, widthPx, heightPx, reflowOnResize,
            trackedAbsolutePositions, preserveViewportTopOnRowsIncrease, reportSize: true);

    private void ResizeScreenCore(int columns, int rows, int widthPx, int heightPx,
        bool reflowOnResize, Span<TerminalGridPosition> trackedAbsolutePositions,
        bool preserveViewportTopOnRowsIncrease, bool reportSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        if (reportSize) UpdateReportCellSize(columns, rows, widthPx, heightPx);
        EndRenderHold();
        SetExtendedDecMode(2026, false);
        _widthPx = Math.Max(0, widthPx);
        _heightPx = Math.Max(0, heightPx);

        int oldColumns = _screen.Columns;
        int oldRows = _screen.ViewportRows;
        if (columns != oldColumns || rows != oldRows)
        {
            // Ghostty resizes primary first, even when the alternate is visible.
            if (_inAltScreen)
                ResizeInactiveScreen(oldColumns, oldRows, columns, rows, reflowOnResize, preserveViewportTopOnRowsIncrease);
            ResizeActiveScreenBuffer(columns, rows, reflowOnResize, trackedAbsolutePositions, preserveViewportTopOnRowsIncrease);
            if (!_inAltScreen)
                ResizeInactiveScreen(oldColumns, oldRows, columns, rows, reflowOnResize, preserveViewportTopOnRowsIncrease);
            ApplyResizeState(columns, rows);
        }
        PublishKittyGraphics();
        if (reportSize) EmitInBandSizeReport();
    }

    private void ResizeActiveScreenBuffer(int columns, int rows, bool reflowOnResize,
        Span<TerminalGridPosition> trackedAbsolutePositions, bool preserveViewportTopOnRowsIncrease)
    {
        bool alternateScreen = _inAltScreen;
        bool gridSizeChanged = columns != _screen.Columns || rows != _screen.ViewportRows;
        bool discardHiddenCells = columns < _screen.Columns &&
            (alternateScreen || (!reflowOnResize || !_autoWrap) && !preserveViewportTopOnRowsIncrease);
        bool previousDelayedWrap = _delayedWrap;
        // Ghostty pins the actual cell, including a pending-wrap cursor.
        // Pending wrap survives independently; it is not an end-column pin.
        int resizeCursorCol = _cursorCol;
        int restoreScrollOffset = alternateScreen ? 0 : _screen.ScrollOffset;
        if (_screen.ScrollOffset != 0)
        {
            _screen.ScrollOffset = 0;
        }

        TerminalScreenAnchor? savedCursorAnchor = null;
        TerminalScreenAnchor? activeTopAnchor = null;
        TerminalGridPosition mappedCursor;
        try
        {
            savedCursorAnchor = TrackSavedCursorForResize();
            if (!alternateScreen && !_options.ResizePullScrollback && !preserveViewportTopOnRowsIncrease)
                activeTopAnchor = _screen.CreateAnchor(_screen.TotalRows - _screen.ViewportRows, 0);
            mappedCursor = _screen.Resize(
                columns,
                rows,
                reflowOnResize && _autoWrap && !alternateScreen,
                new TerminalGridPosition(resizeCursorCol, _cursorRow),
                trackedAbsolutePositions,
                preserveViewportTopOnRowsIncrease && !alternateScreen);

            if (discardHiddenCells) _screen.DiscardHiddenCells();
            if (activeTopAnchor is not null && _screen.TryResolveAnchor(activeTopAnchor, out TerminalGridPosition activeTop))
            {
                int previousTop = _screen.TotalRows - _screen.ViewportRows;
                _screen.PadBottomViewportToPreserveTop(activeTop.Row);
                int mappedRow = mappedCursor.Row - (_screen.TotalRows - _screen.ViewportRows - previousTop);
                mappedCursor = mappedRow < 0 ? new(0, 0) : mappedCursor with { Row = mappedRow };
            }

            if (savedCursorAnchor is not null)
            {
                RemapSavedCursorAfterResize(savedCursorAnchor);
            }
        }
        finally
        {
            if (savedCursorAnchor is not null) _screen.ReleaseAnchor(savedCursorAnchor);
            if (activeTopAnchor is not null) _screen.ReleaseAnchor(activeTopAnchor);
            if (!alternateScreen && restoreScrollOffset != 0)
            {
                _screen.ScrollOffset = restoreScrollOffset;
            }
        }

        SetCursorFromMappedResize(columns, mappedCursor, previousDelayedWrap);
        _cursorRow = mappedCursor.Row;

        _cursorCol = Math.Clamp(_cursorCol, 0, columns - 1);
        _cursorRow = Math.Clamp(_cursorRow, 0, rows - 1);
        if (gridSizeChanged)
        {
            // Redraw concerns the live cursor, not the user's scrolled viewport.
            int scrollOffset = _screen.ScrollOffset;
            _screen.ScrollOffset = 0;
            try { ClearPromptForRedraw(); }
            finally { _screen.ScrollOffset = scrollOffset; }
        }
    }

    private void ApplyResizeState(int columns, int rows)
    {
        int safeColumns = Math.Max(1, columns);
        int safeRows = Math.Max(1, rows);
        ResetHorizontalMargins();

        _scrollBottom = safeRows - 1;
        _scrollTop = 0;

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

        if (_tabStopColumns != safeColumns) InitTabStops();
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
        _delayedWrap = restoreDelayedWrapAtEnd;
    }

    /// <inheritdoc />
    public void ApplyTheme(TerminalTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        _colors.Configure(theme);
        ApplyEffectiveTheme(_colors.GetEffectiveTheme());
    }

    private void ApplyEffectiveTheme(TerminalTheme theme)
    {

        _theme = theme;
        _screen.ApplyTheme(theme, invalidateRows: true);

        _currentFg = ResolveColorIdentity(GetColorIdentity(_currentFgKind, _currentFgPaletteIndex, _currentFg), theme.DefaultForeground);
        _currentBg = ResolveColorIdentity(GetColorIdentity(_currentBgKind, _currentBgPaletteIndex, _currentBg), theme.DefaultBackground);
        if (_currentHasUnderlineColor) _currentUnderlineColor = ResolveColorIdentity(_currentUnderlineIdentity, _currentFg);
    }

    private uint ResolveColorIdentity(TerminalColorIdentity identity, uint defaultColor)
        => identity.Kind switch
        {
            TerminalColorKind.Palette => PaletteColor((int)identity.Value),
            TerminalColorKind.Rgb => 0xFF000000u | identity.Value,
            _ => defaultColor,
        };

    /// <inheritdoc />
    public void Dispose()
    {
        EndRenderHold();
    }

    /// <inheritdoc />
    public TimeSpan? NextTimedRefreshDelay
    {
        get
        {
            if (_renderHold is { } hold)
                return ClampRefreshDelay(TimeSpan.FromSeconds(1) -
                    _options.TimeProvider.GetElapsedTime(hold.StartedTimestamp));
            return _animationNextTickDelay is TimeSpan next
                ? ClampRefreshDelay(next - _options.TimeProvider.GetElapsedTime(_animationTickTimestamp))
                : null;
        }
    }

    /// <inheritdoc />
    public bool RefreshTimedState()
    {
        bool changed = false;
        if (_renderHold is { } hold &&
            _options.TimeProvider.GetElapsedTime(hold.StartedTimestamp) >= TimeSpan.FromSeconds(1))
        {
            TerminalModeState before = ModeState;
            SetExtendedDecMode(2026, false);
            EndRenderHold();
            RaiseModeChangedIfNeeded(before);
            changed = true;
        }
        if (_animationNextTickDelay is TimeSpan delay &&
            _options.TimeProvider.GetElapsedTime(_animationTickTimestamp) >= delay)
        {
            if (AdvanceKittyAnimations())
            {
                PublishKittyGraphics();
                changed = true;
            }
        }
        return changed;
    }

    private static TimeSpan ClampRefreshDelay(TimeSpan delay)
        => delay > TimeSpan.Zero ? delay : TimeSpan.Zero;

    private void BeginRenderHold()
    {
        if (_renderHold is not null) return;
        // Publish the completed prefix of the current input chunk immediately,
        // then continue parsing into an isolated state. Queries and host effects
        // remain responsive while readers/renderers retain the completed frame.
        _screen = _publishedScreen.CreateStateCopy();
        _renderHold = new(
            _options.TimeProvider.GetTimestamp(),
            _cursorCol, _cursorRow, _cursorVisible, ActiveCursorStyle, _extendedDecModesEnabled.Contains(12), _inAltScreen);
    }

    private bool EndRenderHold()
    {
        if (_renderHold is null) return false;
        _publishedScreen.AdoptStateFrom(_screen);
        _screen = _publishedScreen;
        _renderHold = null;
        if (AdvanceKittyAnimations()) PublishKittyGraphics();
        return true;
    }

    private readonly record struct RenderHoldState(
        long StartedTimestamp,
        int CursorColumn,
        int CursorRow,
        bool CursorVisible,
        TerminalCursorStyle CursorStyle,
        bool CursorBlinking,
        bool AlternateScreen);

    private void RaiseModeChangedIfNeeded(TerminalModeState before)
    {
        TerminalModeState current = ModeState;
        if (before != current)
        {
            ModeChanged?.Invoke(this, current);
        }
    }
}
