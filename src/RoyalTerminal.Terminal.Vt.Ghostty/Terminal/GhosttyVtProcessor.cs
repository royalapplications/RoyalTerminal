// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal — VT processor using Ghostty's official libghostty-vt C API.

using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Terminal;

/// <summary>
/// VT processor that wraps Ghostty's official <c>GhosttyTerminal</c> and
/// <c>GhosttyRenderState</c> libghostty-vt APIs.
/// </summary>
public sealed class GhosttyVtProcessor : IVtProcessor,
    ITerminalThemeSink,
    IKittyKeyboardStateSource,
    ITerminalCursorStyleSource,
    ITerminalFocusEventModeSource,
    ITerminalKeySequenceEncoderSource,
    ITerminalPasteSequenceEncoderSource,
    ITerminalPointerSequenceEncoderSource,
    ITerminalMouseReportingStateSource,
    ITerminalViewportScrollSource,
    ITerminalSelectionExportSource,
    ITerminalSnapshotExportSource,
    ITerminalSearchSource,
    ITerminalSixelOptionsSink
{
    private readonly TerminalScreen _screen;
    private GhosttyTerminal _terminal;
    private GhosttyRenderState _renderState;
    private readonly GhosttyKeyEncoder _keyEncoder;
    private readonly GhosttyKeyEvent _keyEvent;
    private readonly GhosttyMouseEncoder _mouseEncoder;
    private readonly GhosttyMouseEvent _mouseEvent;
    private bool _disposed;
    private TerminalTheme _theme;

    private int _cursorCol;
    private int _cursorRow;
    private bool _cursorVisible = true;
    private bool _cursorInViewport = true;
    private TerminalCursorStyle _cursorStyle = TerminalCursorStyle.Block;
    private bool _cursorBlinking = true;
    private bool _applicationCursorKeys;
    private bool _applicationKeypad;
    private bool _backarrowKeyMode;
    private bool _alternateScreen;
    private bool _bracketedPaste;
    private bool _mouseReportingEnabled;
    private bool _win32InputMode;
    private bool _focusEventMode;
    private int _kittyKeyboardFlags;
    private int _cellWidthPx;
    private int _cellHeightPx;
    private byte _pressedMouseButtons;
    private GhosttyVtNative.GhosttyTerminalScrollbar _scrollbar;
    private readonly bool _kittyGraphicsSupported;
    private readonly GhosttyKittyGraphicsPlacementIterator? _kittyPlacementIterator;
    private readonly List<int> _searchRowColumnMapScratch = [];
    private bool _sixelGraphicsEnabled;
    private TerminalScreen? _sixelOverlayScreen;
    private BasicVtProcessor? _sixelOverlayProcessor;

    private readonly TerminalWin32InputModeTracker _win32InputModeTracker = new();
    private readonly TerminalUnsupportedWindowsSequenceSanitizer _unsupportedWindowsSequenceSanitizer = new();

    // Keep delegates alive for the duration of the native terminal.
    private GhosttyVtNative.GhosttyTerminalWritePtyCallback? _writePtyDelegate;
    private GhosttyVtNative.GhosttyTerminalBellCallback? _bellDelegate;
    private GhosttyVtNative.GhosttyTerminalTitleChangedCallback? _titleChangedDelegate;
    private GhosttyVtNative.GhosttyTerminalEnquiryCallback? _enquiryDelegate;
    private GhosttyVtNative.GhosttyTerminalXtversionCallback? _xtversionDelegate;
    private GhosttyVtNative.GhosttyTerminalSizeCallback? _sizeDelegate;
    private GhosttyVtNative.GhosttyTerminalColorSchemeCallback? _colorSchemeDelegate;
    private GhosttyVtNative.GhosttyTerminalDeviceAttributesCallback? _deviceAttributesDelegate;
    private static readonly byte[] s_answerbackBytes = "RoyalTerminal"u8.ToArray();
    private static readonly GCHandle s_answerbackHandle = GCHandle.Alloc(s_answerbackBytes, GCHandleType.Pinned);

    /// <inheritdoc />
    public TerminalViewportScrollState ViewportScrollState =>
        new(_scrollbar.Total, _scrollbar.Offset, _scrollbar.Length);

    /// <inheritdoc />
    public int CursorCol => _cursorCol;

    /// <inheritdoc />
    public int CursorRow => _cursorRow;

    /// <inheritdoc />
    public bool CursorVisible => _cursorVisible && _cursorInViewport;

    /// <inheritdoc />
    public TerminalCursorStyle CursorStyle => _cursorStyle;

    /// <inheritdoc />
    public bool CursorBlinking => _cursorBlinking;

    /// <inheritdoc />
    public bool ApplicationCursorKeys => _applicationCursorKeys;

    /// <inheritdoc />
    public bool ApplicationKeypad => _applicationKeypad;

    /// <inheritdoc />
    public bool AlternateScreen => _alternateScreen;

    /// <inheritdoc />
    public bool BracketedPaste => _bracketedPaste;

    /// <inheritdoc />
    public bool Win32InputMode => _win32InputMode;

    /// <inheritdoc />
    public bool FocusEventsEnabled => _focusEventMode;

    /// <inheritdoc />
    public int KittyKeyboardFlags => _kittyKeyboardFlags;

    /// <inheritdoc />
    public bool SixelGraphicsEnabled
    {
        get => _sixelGraphicsEnabled;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sixelGraphicsEnabled == value)
            {
                return;
            }

            _sixelGraphicsEnabled = value;
            if (value)
            {
                EnsureSixelOverlayProcessor();
                return;
            }

            DisposeSixelOverlay();
            _screen.ClearRasterGraphics();
        }
    }

    /// <inheritdoc />
    public bool MouseReportingEnabled => _mouseReportingEnabled;

    /// <inheritdoc />
    public TerminalModeState ModeState => new(
        CursorVisible,
        ApplicationCursorKeys,
        ApplicationKeypad,
        AlternateScreen,
        BracketedPaste,
        Win32InputMode,
        _backarrowKeyMode);

    /// <inheritdoc />
    public event EventHandler<TerminalModeState>? ModeChanged;

    /// <inheritdoc />
    public Action<byte[]>? ResponseCallback { get; set; }

    /// <inheritdoc />
    public Action? BellCallback { get; set; }

    /// <inheritdoc />
    public Action<string>? TitleCallback { get; set; }

    /// <summary>
    /// Creates a new Ghostty VT processor backed by the official libghostty-vt C API.
    /// </summary>
    public GhosttyVtProcessor(TerminalScreen screen)
    {
        _screen = screen;
        _theme = screen.Theme;
        _terminal = new GhosttyTerminal((ushort)screen.Columns, (ushort)screen.ViewportRows, 10_000);
        _renderState = new GhosttyRenderState();
        _keyEncoder = new GhosttyKeyEncoder();
        _keyEvent = new GhosttyKeyEvent();
        _mouseEncoder = new GhosttyMouseEncoder();
        _mouseEvent = new GhosttyMouseEvent();
        GhosttyVtHelpers.GhosttyBuildFeatures buildFeatures = GhosttyVtHelpers.GetBuildFeatures();
        _kittyGraphicsSupported = buildFeatures.KittyGraphics;
        _kittyPlacementIterator = _kittyGraphicsSupported ? new GhosttyKittyGraphicsPlacementIterator() : null;

        ConfigureOptionalNativeFeatures();
        ApplyThemeToNative(_theme);
        SetupTerminalEffects();
        RefreshStateAndScreenFromNative();
    }

    /// <inheritdoc />
    public unsafe void Process(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data.IsEmpty)
        {
            return;
        }

        TerminalModeState before = ModeState;
        _win32InputModeTracker.Process(data);
        _win32InputMode = _win32InputModeTracker.Win32InputMode;

        byte[]? sanitizedBuffer = null;
        int sanitizedLength = 0;
        ReadOnlySpan<byte> input = data;

        // On Windows, strip the small set of sequences that Ghostty still
        // rejects in this integration path to keep the processor resilient
        // against typical conpty output.
        if (OperatingSystem.IsWindows() &&
            _unsupportedWindowsSequenceSanitizer.TrySanitize(data, out sanitizedBuffer, out sanitizedLength))
        {
            input = sanitizedBuffer.AsSpan(0, sanitizedLength);
            if (input.IsEmpty)
            {
                if (sanitizedBuffer is not null)
                {
                    ArrayPool<byte>.Shared.Return(sanitizedBuffer);
                }

                RaiseModeChangedIfNeeded(before);
                return;
            }
        }

        try
        {
            _terminal.Write(input);
            if (_sixelGraphicsEnabled)
            {
                ProcessSixelOverlay(input);
            }
        }
        finally
        {
            if (sanitizedBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(sanitizedBuffer);
            }
        }

        RefreshStateAndScreenFromNative();
        SyncSixelOverlayRasterGraphics();
        RaiseModeChangedIfNeeded(before);
    }

    /// <inheritdoc />
    public void NotifyResize(int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _terminal.Resize(
            checked((ushort)columns),
            checked((ushort)rows),
            checked((uint)Math.Max(_cellWidthPx, 0)),
            checked((uint)Math.Max(_cellHeightPx, 0)));

        ResizeSixelOverlay(columns, rows);
        _mouseEncoder.Reset();
        RefreshStateAndScreenFromNative();
        SyncSixelOverlayRasterGraphics();
    }

    /// <inheritdoc />
    public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _cellWidthPx = columns > 0 ? Math.Max(0, widthPx / columns) : 0;
        _cellHeightPx = rows > 0 ? Math.Max(0, heightPx / rows) : 0;

        _terminal.Resize(
            checked((ushort)columns),
            checked((ushort)rows),
            checked((uint)_cellWidthPx),
            checked((uint)_cellHeightPx));

        ResizeSixelOverlay(columns, rows);
        _mouseEncoder.Reset();
        RefreshStateAndScreenFromNative();
        SyncSixelOverlayRasterGraphics();
    }

    /// <inheritdoc />
    public void ApplyTheme(TerminalTheme theme)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(theme);

        _theme = theme;
        _screen.ApplyTheme(theme, invalidateRows: true);
        ApplyThemeToNative(theme);
        _sixelOverlayProcessor?.ApplyTheme(theme);
        RefreshStateAndScreenFromNative();
        SyncSixelOverlayRasterGraphics();
    }

    /// <inheritdoc />
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        TerminalModeState before = ModeState;
        _win32InputModeTracker.Reset();
        _unsupportedWindowsSequenceSanitizer.Reset();
        _win32InputMode = false;
        _pressedMouseButtons = 0;

        _terminal.Reset();
        ConfigureOptionalNativeFeatures();
        ApplyThemeToNative(_theme);
        SetupTerminalEffects();
        _mouseEncoder.Reset();
        _sixelOverlayProcessor?.Reset();
        _screen.ClearRasterGraphics();
        RefreshStateAndScreenFromNative();
        RaiseModeChangedIfNeeded(before);
    }

    /// <summary>
    /// Checks whether the official libghostty-vt API is available.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            return GhosttyVtNative.IsAvailable();
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _kittyPlacementIterator?.Dispose();
        DisposeSixelOverlay();
        _mouseEvent.Dispose();
        _mouseEncoder.Dispose();
        _keyEvent.Dispose();
        _keyEncoder.Dispose();
        _renderState.Dispose();
        _terminal.Dispose();
        _unsupportedWindowsSequenceSanitizer.Reset();
        ResetManagedState();
    }

    /// <inheritdoc />
    public void ScrollViewportByRows(int deltaRows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (deltaRows == 0)
        {
            return;
        }

        _terminal.ScrollViewport(GhosttyVtNative.GhosttyTerminalScrollViewport.DeltaRows(deltaRows));
        RefreshStateAndScreenFromNative();
        SyncSixelOverlayRasterGraphics();
    }

    /// <inheritdoc />
    public void ScrollViewportToTop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _terminal.ScrollViewport(GhosttyVtNative.GhosttyTerminalScrollViewport.Top());
        RefreshStateAndScreenFromNative();
        SyncSixelOverlayRasterGraphics();
    }

    /// <inheritdoc />
    public void ScrollViewportToBottom()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _terminal.ScrollViewport(GhosttyVtNative.GhosttyTerminalScrollViewport.Bottom());
        RefreshStateAndScreenFromNative();
        SyncSixelOverlayRasterGraphics();
    }

    /// <inheritdoc />
    public void SetViewportOffsetRows(ulong offsetRows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ulong clampedOffsetRows = Math.Min(offsetRows, ViewportScrollState.MaxOffsetRows);
        long delta = checked((long)clampedOffsetRows) - checked((long)_scrollbar.Offset);
        if (delta == 0)
        {
            return;
        }

        int clampedDelta = delta > int.MaxValue
            ? int.MaxValue
            : delta < int.MinValue
                ? int.MinValue
                : (int)delta;
        _terminal.ScrollViewport(GhosttyVtNative.GhosttyTerminalScrollViewport.DeltaRows(clampedDelta));
        RefreshStateAndScreenFromNative();
        SyncSixelOverlayRasterGraphics();
    }

    /// <inheritdoc />
    public void PopulateSearchMatches(string needle, List<TerminalSearchMatch> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(destination);

        destination.Clear();
        if (string.IsNullOrEmpty(needle))
        {
            return;
        }

        using GhosttyFormatter formatter = new(
            _terminal,
            GhosttyVtNative.GhosttyFormatterFormat.Plain,
            unwrap: false,
            trim: false);
        string fullBuffer = formatter.FormatToString();

        int rowStart = 0;
        int absoluteRow = 0;
        while (rowStart <= fullBuffer.Length)
        {
            int lineBreak = fullBuffer.IndexOf('\n', rowStart);
            ReadOnlySpan<char> rowSpan = lineBreak >= 0
                ? fullBuffer.AsSpan(rowStart, lineBreak - rowStart)
                : fullBuffer.AsSpan(rowStart);
            if (!rowSpan.IsEmpty && rowSpan[^1] == '\r')
            {
                rowSpan = rowSpan[..^1];
            }

            PopulateSearchMatchesFromFormattedRow(rowSpan, absoluteRow, needle, destination);

            if (lineBreak < 0)
            {
                break;
            }

            absoluteRow++;
            rowStart = lineBreak + 1;
        }
    }

    /// <inheritdoc />
    public string? ReadSelection(in TerminalSelectionRange selection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!TryCreateNativeSelection(selection, out RoyalTerminal.GhosttySharp.GhosttySelection nativeSelection))
        {
            return null;
        }

        using GhosttyFormatter formatter = new(
            _terminal,
            GhosttyVtNative.GhosttyFormatterFormat.Plain,
            unwrap: false,
            trim: false,
            selection: nativeSelection);
        return formatter.FormatToString();
    }

    /// <inheritdoc />
    public bool IsPasteSafe(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        return GhosttyPaste.IsSafe(text);
    }

    /// <inheritdoc />
    public bool TryEncodePaste(string text, bool bracketedPaste, out byte[] sequence)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            sequence = [];
            return false;
        }

        sequence = GhosttyPaste.Encode(text, bracketedPaste);
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!SupportsSnapshotFormat(format))
        {
            snapshot = string.Empty;
            return false;
        }

        RoyalTerminal.GhosttySharp.GhosttySelection? selection = null;
        RoyalTerminal.GhosttySharp.GhosttySelection nativeSelection = default;
        if (options.Selection is TerminalSelectionRange selectionRange)
        {
            if (!TryCreateNativeSelection(selectionRange, out nativeSelection))
            {
                snapshot = string.Empty;
                return false;
            }

            selection = nativeSelection;
        }

        GhosttyFormatterOptions formatterOptions = new(
            Format: format switch
            {
                TerminalSnapshotExportFormat.PlainText => GhosttyVtNative.GhosttyFormatterFormat.Plain,
                TerminalSnapshotExportFormat.StyledVt => GhosttyVtNative.GhosttyFormatterFormat.Vt,
                TerminalSnapshotExportFormat.Html => GhosttyVtNative.GhosttyFormatterFormat.Html,
                _ => GhosttyVtNative.GhosttyFormatterFormat.Plain,
            },
            Unwrap: options.Unwrap,
            Trim: options.TrimTrailingWhitespace,
            Extra: new GhosttyFormatterExtraOptions(
                IncludePalette: options.Extras.IncludePalette,
                IncludeModes: options.Extras.IncludeModes,
                IncludeScrollingRegion: options.Extras.IncludeScrollingRegion,
                IncludeTabstops: options.Extras.IncludeTabstops,
                IncludeWorkingDirectory: options.Extras.IncludeWorkingDirectory,
                IncludeKeyboardModes: options.Extras.IncludeKeyboardModes,
                Screen: new GhosttyFormatterScreenOptions(
                    IncludeCursor: options.Extras.IncludeCursor,
                    IncludeStyle: options.Extras.IncludeStyle,
                    IncludeHyperlinks: options.Extras.IncludeHyperlinks,
                    IncludeProtection: options.Extras.IncludeProtection,
                    IncludeKittyKeyboard: options.Extras.IncludeKittyKeyboard,
                    IncludeCharsets: options.Extras.IncludeCharsets)),
            Selection: selection);

        using GhosttyFormatter formatter = new(_terminal, formatterOptions);
        snapshot = formatter.FormatToString();
        return true;
    }

    private unsafe void SetupTerminalEffects()
    {
        _writePtyDelegate ??= OnNativeWritePty;
        _bellDelegate ??= OnNativeBell;
        _titleChangedDelegate ??= OnNativeTitleChanged;
        _enquiryDelegate ??= OnNativeEnquiry;
        _xtversionDelegate ??= OnNativeXtversion;
        _sizeDelegate ??= OnNativeSize;
        _colorSchemeDelegate ??= OnNativeColorScheme;
        _deviceAttributesDelegate ??= OnNativeDeviceAttributes;

        _terminal.SetWritePtyCallback(Marshal.GetFunctionPointerForDelegate(_writePtyDelegate));
        _terminal.SetBellCallback(Marshal.GetFunctionPointerForDelegate(_bellDelegate));
        _terminal.SetTitleChangedCallback(Marshal.GetFunctionPointerForDelegate(_titleChangedDelegate));
        _terminal.SetEnquiryCallback(Marshal.GetFunctionPointerForDelegate(_enquiryDelegate));
        _terminal.SetXtversionCallback(Marshal.GetFunctionPointerForDelegate(_xtversionDelegate));
        _terminal.SetSizeCallback(Marshal.GetFunctionPointerForDelegate(_sizeDelegate));
        _terminal.SetColorSchemeCallback(Marshal.GetFunctionPointerForDelegate(_colorSchemeDelegate));
        _terminal.SetDeviceAttributesCallback(Marshal.GetFunctionPointerForDelegate(_deviceAttributesDelegate));
    }

    private void ConfigureOptionalNativeFeatures()
    {
        if (!_kittyGraphicsSupported)
        {
            return;
        }

        GhosttySys.EnsureSkiaPngDecoderInstalled();
        _terminal.SetKittyImageStorageLimit(32UL * 1024UL * 1024UL);
        _terminal.SetApcMaxBytesKitty(64UL * 1024UL * 1024UL);
        _terminal.SetKittyImageMediumFile(enabled: true);
        _terminal.SetKittyImageMediumTempFile(enabled: true);
        _terminal.SetKittyImageMediumSharedMemory(enabled: true);
    }

    private void ApplyThemeToNative(TerminalTheme theme)
    {
        _terminal.SetForegroundColor(GhosttyTerminal.ToNativeColor(theme.DefaultForeground));
        _terminal.SetBackgroundColor(GhosttyTerminal.ToNativeColor(theme.DefaultBackground));
        _terminal.SetCursorColor(GhosttyTerminal.ToNativeColor(theme.CursorColor));

        Span<GhosttyVtNative.GhosttyColorRgb> palette = stackalloc GhosttyVtNative.GhosttyColorRgb[256];
        for (int i = 0; i < palette.Length; i++)
        {
            palette[i] = GhosttyTerminal.ToNativeColor(theme.Palette[i]);
        }

        _terminal.SetPalette(palette);
    }

    private void RefreshStateAndScreenFromNative()
    {
        _renderState.Update(_terminal);
        RefreshStateFromNative();
        SyncScreenFromNative();
    }

    private void ProcessSixelOverlay(ReadOnlySpan<byte> input)
    {
        BasicVtProcessor overlayProcessor = EnsureSixelOverlayProcessor();
        overlayProcessor.Process(input);
    }

    private BasicVtProcessor EnsureSixelOverlayProcessor()
    {
        if (_sixelOverlayProcessor is not null)
        {
            return _sixelOverlayProcessor;
        }

        TerminalScreen overlayScreen = new(
            Math.Max(1, _screen.Columns),
            Math.Max(1, _screen.ViewportRows),
            scrollbackLimit: 0);
        overlayScreen.ApplyTheme(_theme, invalidateRows: true);

        BasicVtProcessor overlayProcessor = new(
            overlayScreen,
            new BasicVtProcessorOptions
            {
                SixelGraphicsEnabled = true,
            });
        int widthPx = _cellWidthPx > 0 ? checked(_cellWidthPx * Math.Max(1, _screen.Columns)) : 0;
        int heightPx = _cellHeightPx > 0 ? checked(_cellHeightPx * Math.Max(1, _screen.ViewportRows)) : 0;
        overlayProcessor.ResizeScreen(
            Math.Max(1, _screen.Columns),
            Math.Max(1, _screen.ViewportRows),
            widthPx,
            heightPx,
            reflowOnResize: false);

        _sixelOverlayScreen = overlayScreen;
        _sixelOverlayProcessor = overlayProcessor;
        return overlayProcessor;
    }

    private void ResizeSixelOverlay(int columns, int rows)
    {
        if (!_sixelGraphicsEnabled || _sixelOverlayProcessor is null)
        {
            return;
        }

        int safeColumns = Math.Max(1, columns);
        int safeRows = Math.Max(1, rows);
        int widthPx = _cellWidthPx > 0 ? checked(_cellWidthPx * safeColumns) : 0;
        int heightPx = _cellHeightPx > 0 ? checked(_cellHeightPx * safeRows) : 0;
        _sixelOverlayProcessor.ResizeScreen(
            safeColumns,
            safeRows,
            widthPx,
            heightPx,
            reflowOnResize: false);
    }

    private void SyncSixelOverlayRasterGraphics()
    {
        if (!_sixelGraphicsEnabled || _sixelOverlayScreen is null)
        {
            return;
        }

        TerminalViewportScrollState scrollState = ViewportScrollState;
        if (scrollState.OffsetRows != scrollState.MaxOffsetRows)
        {
            _screen.ClearRasterGraphics();
            return;
        }

        if (!_sixelOverlayScreen.HasRasterGraphics && !_screen.HasRasterGraphics)
        {
            return;
        }

        _screen.ReplaceRasterGraphicsFrom(_sixelOverlayScreen);
    }

    private void DisposeSixelOverlay()
    {
        _sixelOverlayProcessor?.Dispose();
        _sixelOverlayProcessor = null;
        _sixelOverlayScreen = null;
    }

    private void RefreshStateFromNative()
    {
        _cursorVisible = _renderState.GetCursorVisible();
        _cursorInViewport = _renderState.TryGetCursorViewport(out ushort cursorX, out ushort cursorY, out _);
        _cursorCol = _cursorInViewport ? cursorX : _terminal.GetCursorX();
        _cursorRow = _cursorInViewport ? cursorY : _terminal.GetCursorY();
        _cursorStyle = ConvertCursorStyle(_renderState.GetCursorVisualStyle());
        _cursorBlinking = _renderState.GetCursorBlinking();
        _applicationCursorKeys = _terminal.GetMode(GhosttyVtNative.ModeDecckm);
        _applicationKeypad = _terminal.GetMode(GhosttyVtNative.ModeKeypadKeys);
        _backarrowKeyMode = _terminal.GetMode(GhosttyVtNative.ModeBackarrowKeyMode);
        _alternateScreen =
            _terminal.GetActiveScreen() == GhosttyVtNative.GhosttyTerminalScreen.Alternate ||
            _terminal.GetMode(GhosttyVtNative.ModeAltScreen) ||
            _terminal.GetMode(GhosttyVtNative.ModeAltScreenSave);
        _bracketedPaste = _terminal.GetMode(GhosttyVtNative.ModeBracketedPaste);
        _mouseReportingEnabled = _terminal.GetMouseTracking();
        _focusEventMode = _terminal.GetMode(GhosttyVtNative.ModeFocusEvent);
        _kittyKeyboardFlags = (int)_terminal.GetKittyKeyboardFlags();
        _scrollbar = _terminal.GetScrollbar();
    }

    private void ResetManagedState()
    {
        _cursorCol = 0;
        _cursorRow = 0;
        _cursorVisible = true;
        _cursorInViewport = true;
        _cursorStyle = TerminalCursorStyle.Block;
        _cursorBlinking = true;
        _applicationCursorKeys = false;
        _applicationKeypad = false;
        _backarrowKeyMode = false;
        _alternateScreen = false;
        _bracketedPaste = false;
        _mouseReportingEnabled = false;
        _win32InputMode = false;
        _focusEventMode = false;
        _kittyKeyboardFlags = 0;
        _pressedMouseButtons = 0;
        _scrollbar = default;
    }

    private unsafe void SyncScreenFromNative()
    {
        GhosttyVtNative.GhosttyRenderStateDirty dirty = _renderState.GetDirty();
        bool fullRefresh = dirty == GhosttyVtNative.GhosttyRenderStateDirty.Full ||
            _renderState.GetColumns() != _screen.Columns ||
            _renderState.GetRows() != _screen.ViewportRows;
        bool syncKittyGraphics = _kittyGraphicsSupported;
        if (!fullRefresh && dirty == GhosttyVtNative.GhosttyRenderStateDirty.False && !syncKittyGraphics)
        {
            return;
        }

        GhosttyVtNative.GhosttyRenderStateColors colors = _renderState.GetColors();
        Span<GhosttyVtNative.GhosttyColorRgb> palette = stackalloc GhosttyVtNative.GhosttyColorRgb[256];
        colors.CopyPaletteTo(palette);

        _screen.DefaultForeground = GhosttyTerminal.ToArgb(colors.Foreground);
        _screen.DefaultBackground = GhosttyTerminal.ToArgb(colors.Background);

        int rows = _renderState.GetRows();
        _renderState.BeginRows();

        int rowIndex = 0;
        while (rowIndex < rows && rowIndex < _screen.ViewportRows && _renderState.MoveNextRow())
        {
            TerminalRow row = _screen.GetViewportRow(rowIndex);
            bool rowDirty = fullRefresh || _renderState.GetCurrentRowDirty();
            if (rowDirty)
            {
                row.WrapsToNext = _renderState.GetCurrentRowWrap();
                _renderState.BeginCurrentRowCells();

                int colIndex = 0;
                while (colIndex < row.Columns && _renderState.MoveNextCell())
                {
                    PopulateCell(ref row[colIndex], rowIndex, colIndex, colors, palette);
                    colIndex++;
                }

                for (; colIndex < row.Columns; colIndex++)
                {
                    ClearCell(ref row[colIndex]);
                }

                row.IsDirty = true;
                _renderState.SetCurrentRowDirty(false);
            }
            rowIndex++;
        }

        for (; fullRefresh && rowIndex < _screen.ViewportRows; rowIndex++)
        {
            _screen.GetViewportRow(rowIndex).Clear(_screen.DefaultForeground, _screen.DefaultBackground);
        }

        SyncKittyGraphicsFromNative();
        _renderState.SetDirty(GhosttyVtNative.GhosttyRenderStateDirty.False);
    }

    private bool TryCreateNativeSelection(
        TerminalSelectionRange selection,
        out RoyalTerminal.GhosttySharp.GhosttySelection nativeSelection)
    {
        TerminalSelectionRange normalized = selection.Normalize();
        int startColumn = Math.Clamp(normalized.StartColumn, 0, Math.Max(0, _screen.Columns - 1));
        int endColumn = Math.Clamp(normalized.EndColumn, 0, Math.Max(0, _screen.Columns - 1));
        int startRow = Math.Clamp(normalized.StartRow, 0, Math.Max(0, _screen.ViewportRows - 1));
        int endRow = Math.Clamp(normalized.EndRow, 0, Math.Max(0, _screen.ViewportRows - 1));
        ulong viewportOffset = ViewportScrollState.OffsetRows;
        uint startAbsoluteRow = checked((uint)Math.Min(uint.MaxValue, viewportOffset + (ulong)startRow));
        uint endAbsoluteRow = checked((uint)Math.Min(uint.MaxValue, viewportOffset + (ulong)endRow));

        if (!_terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Screen((ushort)startColumn, startAbsoluteRow),
                out GhosttyVtNative.GhosttyGridRef startRef) ||
            !_terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Screen((ushort)endColumn, endAbsoluteRow),
                out GhosttyVtNative.GhosttyGridRef endRef))
        {
            nativeSelection = default;
            return false;
        }

        nativeSelection = new RoyalTerminal.GhosttySharp.GhosttySelection(startRef, endRef, normalized.Rectangle);
        return true;
    }

    private unsafe void PopulateCell(
        ref TerminalCell target,
        int rowIndex,
        int columnIndex,
        in GhosttyVtNative.GhosttyRenderStateColors colors,
        ReadOnlySpan<GhosttyVtNative.GhosttyColorRgb> palette)
    {
        ulong rawCell = _renderState.GetCurrentCellRaw();
        GhosttyVtNative.GhosttyStyle style = _renderState.GetCurrentCellStyle();

        uint codepoint = 0;
        GhosttyVtNative.CellGet(rawCell, GhosttyVtNative.GhosttyCellData.Codepoint, &codepoint);
        target.Codepoint = checked((int)codepoint);

        uint graphemeLength = _renderState.GetCurrentCellGraphemeLength();
        target.Grapheme = graphemeLength > 1 ? BuildCellGrapheme(graphemeLength) : null;

        GhosttyVtNative.GhosttyCellWide wide = default;
        GhosttyVtNative.CellGet(rawCell, GhosttyVtNative.GhosttyCellData.Wide, &wide);
        target.Width = wide switch
        {
            GhosttyVtNative.GhosttyCellWide.Wide => 2,
            GhosttyVtNative.GhosttyCellWide.SpacerHead or GhosttyVtNative.GhosttyCellWide.SpacerTail => 0,
            _ => 1,
        };

        if (_renderState.TryGetCurrentCellForegroundColor(out GhosttyVtNative.GhosttyColorRgb foreground))
        {
            target.Foreground = GhosttyTerminal.ToArgb(foreground);
        }
        else
        {
            target.Foreground = GhosttyTerminal.ToArgb(colors.Foreground);
        }

        if (_renderState.TryGetCurrentCellBackgroundColor(out GhosttyVtNative.GhosttyColorRgb background))
        {
            target.Background = GhosttyTerminal.ToArgb(background);
            target.HasBackground = true;
        }
        else
        {
            target.Background = GhosttyTerminal.ToArgb(colors.Background);
            target.HasBackground = false;
        }

        target.Attributes = MapAttributesFromStyle(style);
        target.UnderlineStyle = MapUnderlineStyleFromStyle(style);
        target.HasUnderlineColor = TryResolveUnderlineColor(style.UnderlineColor, palette, out uint underlineColor);
        target.UnderlineColor = underlineColor;
        target.Decorations = MapDecorationsFromStyle(style);
        target.HyperlinkId = TryResolveHyperlinkId(rowIndex, columnIndex, rawCell, target.Width);
    }

    private void ClearCell(ref TerminalCell target)
    {
        target.Codepoint = 0;
        target.Grapheme = null;
        target.Foreground = _screen.DefaultForeground;
        target.Background = _screen.DefaultBackground;
        target.Attributes = CellAttributes.None;
        target.UnderlineStyle = TerminalUnderlineStyle.None;
        target.UnderlineColor = 0;
        target.HasUnderlineColor = false;
        target.Decorations = CellDecorations.None;
        target.HasBackground = true;
        target.HyperlinkId = 0;
        target.Width = 1;
    }

    private unsafe int TryResolveHyperlinkId(int rowIndex, int columnIndex, ulong rawCell, int width)
    {
        if (width <= 0)
        {
            return 0;
        }

        bool hasHyperlink = false;
        GhosttyVtNative.CellGet(rawCell, GhosttyVtNative.GhosttyCellData.HasHyperlink, &hasHyperlink);
        if (!hasHyperlink)
        {
            return 0;
        }

        if (!_terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Viewport((ushort)columnIndex, checked((uint)rowIndex)),
                out GhosttyVtNative.GhosttyGridRef reference))
        {
            return 0;
        }

        string? uri = _terminal.GetHyperlinkUri(in reference);
        if (string.IsNullOrWhiteSpace(uri))
        {
            return 0;
        }

        return _screen.RegisterHyperlink(uri);
    }

    private void SyncKittyGraphicsFromNative()
    {
        if (!_kittyGraphicsSupported || _kittyPlacementIterator is null)
        {
            _screen.ClearKittyGraphics();
            return;
        }

        if (!_terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics) || graphics is null)
        {
            _screen.ClearKittyGraphics();
            return;
        }

        Dictionary<int, TerminalKittyImageSource> images = new();
        List<TerminalKittyImagePlacement> placements = new();

        _kittyPlacementIterator.SetLayer(GhosttyVtNative.GhosttyKittyPlacementLayer.All);
        graphics.Populate(_kittyPlacementIterator);

        while (_kittyPlacementIterator.MoveNext())
        {
            int imageId = checked((int)_kittyPlacementIterator.GetImageId());
            if (!graphics.TryGetImage((uint)imageId, out GhosttyKittyGraphicsImage image) || !image.IsValid)
            {
                continue;
            }

            if (!images.TryGetValue(imageId, out TerminalKittyImageSource? imageSource))
            {
                if (!TryCreateKittyImageSource(imageId, image, out imageSource) || imageSource is null)
                {
                    continue;
                }

                images[imageId] = imageSource;
            }

            if (!_kittyPlacementIterator.TryGetRenderInfo(
                    image,
                    _terminal,
                    out GhosttyVtNative.GhosttyKittyGraphicsPlacementRenderInfo renderInfo) ||
                !renderInfo.ViewportVisible)
            {
                continue;
            }

            TerminalKittyImageLayer layer = ClassifyKittyLayer(_kittyPlacementIterator.GetZIndex());
            placements.Add(new TerminalKittyImagePlacement(
                imageId,
                layer,
                renderInfo.ViewportColumn,
                renderInfo.ViewportRow,
                checked((int)_kittyPlacementIterator.GetXOffset()),
                checked((int)_kittyPlacementIterator.GetYOffset()),
                checked((int)renderInfo.PixelWidth),
                checked((int)renderInfo.PixelHeight),
                checked((int)renderInfo.SourceX),
                checked((int)renderInfo.SourceY),
                checked((int)renderInfo.SourceWidth),
                checked((int)renderInfo.SourceHeight)));
        }

        if (placements.Count == 0)
        {
            _screen.ClearKittyGraphics();
            return;
        }

        _screen.ReplaceKittyGraphics(images.Values.ToArray(), placements);
    }

    private static TerminalKittyImageLayer ClassifyKittyLayer(int zIndex)
    {
        if (zIndex < (int.MinValue / 2))
        {
            return TerminalKittyImageLayer.BelowBackground;
        }

        return zIndex < 0
            ? TerminalKittyImageLayer.BelowText
            : TerminalKittyImageLayer.AboveText;
    }

    private static bool TryCreateKittyImageSource(
        int imageId,
        GhosttyKittyGraphicsImage image,
        out TerminalKittyImageSource? source)
    {
        byte[] raw = image.CopyData();
        if (raw.Length == 0)
        {
            source = null;
            return false;
        }

        int width = checked((int)image.GetWidth());
        int height = checked((int)image.GetHeight());
        byte[] rgba = image.GetFormat() switch
        {
            GhosttyVtNative.GhosttyKittyImageFormat.Rgba => raw,
            GhosttyVtNative.GhosttyKittyImageFormat.Rgb => ExpandRgbToRgba(raw, width, height),
            GhosttyVtNative.GhosttyKittyImageFormat.Gray => ExpandGrayToRgba(raw, width, height),
            GhosttyVtNative.GhosttyKittyImageFormat.GrayAlpha => ExpandGrayAlphaToRgba(raw, width, height),
            _ => Array.Empty<byte>(),
        };

        if (rgba.Length == 0)
        {
            source = null;
            return false;
        }

        source = new TerminalKittyImageSource(imageId, width, height, rgba);
        return true;
    }

    private static byte[] ExpandRgbToRgba(byte[] rgb, int width, int height)
    {
        int pixelCount = checked(width * height);
        if (rgb.Length < pixelCount * 3)
        {
            return [];
        }

        byte[] rgba = new byte[pixelCount * 4];
        int sourceIndex = 0;
        int destinationIndex = 0;
        for (int i = 0; i < pixelCount; i++)
        {
            rgba[destinationIndex++] = rgb[sourceIndex++];
            rgba[destinationIndex++] = rgb[sourceIndex++];
            rgba[destinationIndex++] = rgb[sourceIndex++];
            rgba[destinationIndex++] = 0xFF;
        }

        return rgba;
    }

    private static byte[] ExpandGrayToRgba(byte[] grayscale, int width, int height)
    {
        int pixelCount = checked(width * height);
        if (grayscale.Length < pixelCount)
        {
            return [];
        }

        byte[] rgba = new byte[pixelCount * 4];
        int sourceIndex = 0;
        int destinationIndex = 0;
        for (int i = 0; i < pixelCount; i++)
        {
            byte luminance = grayscale[sourceIndex++];
            rgba[destinationIndex++] = luminance;
            rgba[destinationIndex++] = luminance;
            rgba[destinationIndex++] = luminance;
            rgba[destinationIndex++] = 0xFF;
        }

        return rgba;
    }

    private static byte[] ExpandGrayAlphaToRgba(byte[] grayscaleAlpha, int width, int height)
    {
        int pixelCount = checked(width * height);
        if (grayscaleAlpha.Length < pixelCount * 2)
        {
            return [];
        }

        byte[] rgba = new byte[pixelCount * 4];
        int sourceIndex = 0;
        int destinationIndex = 0;
        for (int i = 0; i < pixelCount; i++)
        {
            byte luminance = grayscaleAlpha[sourceIndex++];
            byte alpha = grayscaleAlpha[sourceIndex++];
            rgba[destinationIndex++] = luminance;
            rgba[destinationIndex++] = luminance;
            rgba[destinationIndex++] = luminance;
            rgba[destinationIndex++] = alpha;
        }

        return rgba;
    }

    private void PopulateSearchMatchesFromFormattedRow(
        ReadOnlySpan<char> rowSpan,
        int absoluteRow,
        string needle,
        List<TerminalSearchMatch> destination)
    {
        _searchRowColumnMapScratch.Clear();
        if (rowSpan.IsEmpty)
        {
            return;
        }

        string rowText = rowSpan.ToString();
        int[] textElementStarts = StringInfo.ParseCombiningCharacters(rowText);
        if (textElementStarts.Length == 0)
        {
            return;
        }

        for (int column = 0; column < textElementStarts.Length; column++)
        {
            int start = textElementStarts[column];
            int endExclusive = column + 1 < textElementStarts.Length
                ? textElementStarts[column + 1]
                : rowText.Length;
            for (int i = start; i < endExclusive; i++)
            {
                _searchRowColumnMapScratch.Add(column);
            }
        }

        int searchFrom = 0;
        while (searchFrom <= rowText.Length - needle.Length)
        {
            int found = rowText.IndexOf(needle, searchFrom, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            int mapEnd = found + needle.Length - 1;
            if ((uint)found < (uint)_searchRowColumnMapScratch.Count &&
                (uint)mapEnd < (uint)_searchRowColumnMapScratch.Count)
            {
                destination.Add(
                    new TerminalSearchMatch(
                        absoluteRow,
                        _searchRowColumnMapScratch[found],
                        _searchRowColumnMapScratch[mapEnd]));
            }

            searchFrom = found + Math.Max(needle.Length, 1);
        }
    }

    private string? BuildCellGrapheme(uint graphemeLength)
    {
        if (graphemeLength <= 1 || graphemeLength > int.MaxValue)
        {
            return null;
        }

        int length = checked((int)graphemeLength);
        uint[]? rented = null;
        Span<uint> buffer = length <= 16
            ? stackalloc uint[length]
            : (rented = ArrayPool<uint>.Shared.Rent(length)).AsSpan(0, length);

        try
        {
            _renderState.GetCurrentCellGraphemes(buffer);

            StringBuilder builder = new(length * 2);
            for (int i = 0; i < buffer.Length; i++)
            {
                uint codepoint = buffer[i];
                if (!Rune.IsValid((int)codepoint))
                {
                    return null;
                }

                builder.Append(char.ConvertFromUtf32((int)codepoint));
            }

            return builder.ToString();
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<uint>.Shared.Return(rented);
            }
        }
    }

    private void RaiseModeChangedIfNeeded(TerminalModeState before)
    {
        TerminalModeState current = ModeState;
        if (before != current)
        {
            ModeChanged?.Invoke(this, current);
        }
    }

    /// <inheritdoc />
    public bool TryEncodeKey(in TerminalKeyEncodingRequest request, out byte[] sequence)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(request.KeyId) ||
            !TryMapKeyId(request.KeyId, out GhosttyVtNative.GhosttyVtKey key))
        {
            sequence = [];
            return false;
        }

        _keyEncoder.SetFromTerminal(_terminal);
        _keyEvent.SetAction(MapKeyAction(request.Action));
        _keyEvent.SetKey(key);
        _keyEvent.SetModifiers(MapModifiers(request.Modifiers));
        _keyEvent.SetConsumedModifiers(GhosttyVtNative.GhosttyVtMods.None);
        _keyEvent.SetComposing(request.IsComposing);
        _keyEvent.SetText(request.Text);
        _keyEvent.SetUnshiftedCodepoint(TryGetUnshiftedCodepoint(request.KeyId, out uint codepoint) ? codepoint : 0);

        sequence = _keyEncoder.Encode(_keyEvent);
        return sequence.Length > 0;
    }

    /// <inheritdoc />
    public bool TryEncodePointer(
        in TerminalPointerEvent pointerEvent,
        in TerminalPointerEncodingContext context,
        out byte[] sequence)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (context.ScreenWidthPx <= 0 ||
            context.ScreenHeightPx <= 0 ||
            context.CellWidthPx <= 0 ||
            context.CellHeightPx <= 0)
        {
            sequence = [];
            return false;
        }

        _mouseEncoder.SetFromTerminal(_terminal);
        _mouseEncoder.SetSize(new GhosttyVtNative.GhosttyMouseEncoderSize
        {
            Size = (nuint)Marshal.SizeOf<GhosttyVtNative.GhosttyMouseEncoderSize>(),
            ScreenWidth = checked((uint)context.ScreenWidthPx),
            ScreenHeight = checked((uint)context.ScreenHeightPx),
            CellWidth = checked((uint)context.CellWidthPx),
            CellHeight = checked((uint)context.CellHeightPx),
            PaddingTop = checked((uint)Math.Max(0, context.PaddingTopPx)),
            PaddingBottom = checked((uint)Math.Max(0, context.PaddingBottomPx)),
            PaddingRight = checked((uint)Math.Max(0, context.PaddingRightPx)),
            PaddingLeft = checked((uint)Math.Max(0, context.PaddingLeftPx)),
        });
        _mouseEncoder.SetAnyButtonPressed(IsAnyMouseButtonPressed(pointerEvent));
        _mouseEncoder.SetTrackLastCell(true);

        _mouseEvent.SetModifiers(MapModifiers(pointerEvent.Modifiers));
        _mouseEvent.SetPosition((float)pointerEvent.X, (float)pointerEvent.Y);

        switch (pointerEvent.Kind)
        {
            case TerminalPointerEventKind.Button:
                _mouseEvent.SetAction(pointerEvent.Action == TerminalInputAction.Release
                    ? GhosttyVtNative.GhosttyMouseAction.Release
                    : GhosttyVtNative.GhosttyMouseAction.Press);

                if (!TryMapMouseButton(pointerEvent.Button, out GhosttyVtNative.GhosttyMouseButtonId button))
                {
                    sequence = [];
                    return false;
                }

                _mouseEvent.SetButton(button);
                break;

            case TerminalPointerEventKind.Move:
                _mouseEvent.SetAction(GhosttyVtNative.GhosttyMouseAction.Motion);
                if (TryMapMouseButton(pointerEvent.Button, out GhosttyVtNative.GhosttyMouseButtonId moveButton))
                {
                    _mouseEvent.SetButton(moveButton);
                }
                else
                {
                    _mouseEvent.ClearButton();
                }

                break;

            case TerminalPointerEventKind.Scroll:
                _mouseEvent.SetAction(GhosttyVtNative.GhosttyMouseAction.Press);
                if (!TryMapScrollButton(pointerEvent, out GhosttyVtNative.GhosttyMouseButtonId scrollButton))
                {
                    sequence = [];
                    return false;
                }

                _mouseEvent.SetButton(scrollButton);
                break;

            default:
                sequence = [];
                return false;
        }

        sequence = _mouseEncoder.Encode(_mouseEvent);
        UpdatePressedMouseButtons(pointerEvent);
        return sequence.Length > 0;
    }

    private void OnNativeWritePty(nint terminal, nint userdata, nint data, nuint len)
    {
        if (ResponseCallback is null || len == 0)
        {
            return;
        }

        byte[] response = new byte[(int)len];
        Marshal.Copy(data, response, 0, response.Length);
        ResponseCallback(response);
    }

    private void OnNativeBell(nint terminal, nint userdata)
    {
        BellCallback?.Invoke();
    }

    private void OnNativeTitleChanged(nint terminal, nint userdata)
    {
        if (TitleCallback is null)
        {
            return;
        }

        TitleCallback(_terminal.GetTitle());
    }

    private static GhosttyVtNative.GhosttyString OnNativeEnquiry(nint terminal, nint userdata)
    {
        return CreateAnswerbackString();
    }

    private static GhosttyVtNative.GhosttyString OnNativeXtversion(nint terminal, nint userdata)
    {
        return CreateAnswerbackString();
    }

    private unsafe byte OnNativeSize(nint terminal, nint userdata, GhosttyVtNative.GhosttySizeReportSize* size)
    {
        *size = new GhosttyVtNative.GhosttySizeReportSize
        {
            Rows = checked((ushort)_screen.ViewportRows),
            Columns = checked((ushort)_screen.Columns),
            CellWidth = checked((uint)Math.Max(_cellWidthPx, 0)),
            CellHeight = checked((uint)Math.Max(_cellHeightPx, 0)),
        };

        return 1;
    }

    private unsafe byte OnNativeColorScheme(nint terminal, nint userdata, GhosttyColorScheme* scheme)
    {
        *scheme = IsPerceivedLightColor(_theme.DefaultBackground)
            ? GhosttyColorScheme.Light
            : GhosttyColorScheme.Dark;
        return 1;
    }

    private unsafe byte OnNativeDeviceAttributes(
        nint terminal,
        nint userdata,
        GhosttyVtNative.GhosttyDeviceAttributes* attributes)
    {
        *attributes = GhosttyVtNative.GhosttyDeviceAttributes.Create();
        attributes->Primary.ConformanceLevel = 62;
        int featureCount = 0;
        attributes->Primary.SetFeature(featureCount++, 1);
        if (_sixelGraphicsEnabled)
        {
            attributes->Primary.SetFeature(featureCount++, 4);
        }

        attributes->Primary.SetFeature(featureCount++, 6);
        attributes->Primary.SetFeature(featureCount++, 22);
        attributes->Primary.NumFeatures = (nuint)featureCount;
        attributes->Secondary.DeviceType = 1;
        attributes->Secondary.FirmwareVersion = 10;
        attributes->Secondary.RomCartridge = 0;
        attributes->Tertiary.UnitId = 0x00464F4F;
        return 1;
    }

    private static bool IsPerceivedLightColor(uint argb)
    {
        int red = (int)((argb >> 16) & 0xFF);
        int green = (int)((argb >> 8) & 0xFF);
        int blue = (int)(argb & 0xFF);
        int luminance = ((red * 299) + (green * 587) + (blue * 114)) / 1000;
        return luminance >= 128;
    }

    private static GhosttyVtNative.GhosttyString CreateAnswerbackString()
    {
        return new GhosttyVtNative.GhosttyString(
            s_answerbackHandle.AddrOfPinnedObject(),
            checked((nuint)s_answerbackBytes.Length));
    }

    private static TerminalCursorStyle ConvertCursorStyle(GhosttyVtNative.GhosttyRenderStateCursorVisualStyle style)
    {
        return style switch
        {
            GhosttyVtNative.GhosttyRenderStateCursorVisualStyle.Bar => TerminalCursorStyle.Bar,
            GhosttyVtNative.GhosttyRenderStateCursorVisualStyle.Underline => TerminalCursorStyle.Underline,
            GhosttyVtNative.GhosttyRenderStateCursorVisualStyle.BlockHollow => TerminalCursorStyle.BlockHollow,
            _ => TerminalCursorStyle.Block,
        };
    }

    private static CellAttributes MapAttributesFromStyle(GhosttyVtNative.GhosttyStyle style)
    {
        CellAttributes result = CellAttributes.None;
        if (style.Bold) result |= CellAttributes.Bold;
        if (style.Italic) result |= CellAttributes.Italic;
        if (style.Faint) result |= CellAttributes.Dim;
        if (style.Blink) result |= CellAttributes.Blink;
        if (style.Inverse) result |= CellAttributes.Inverse;
        if (style.Invisible) result |= CellAttributes.Hidden;
        if (style.Strikethrough) result |= CellAttributes.Strikethrough;
        if (MapUnderlineStyleFromStyle(style) != TerminalUnderlineStyle.None) result |= CellAttributes.Underline;
        return result;
    }

    private static TerminalUnderlineStyle MapUnderlineStyleFromStyle(GhosttyVtNative.GhosttyStyle style)
    {
        return MapUnderlineStyleBits(style.Underline);
    }

    private static TerminalUnderlineStyle MapUnderlineStyleBits(int underline)
    {
        return underline switch
        {
            1 => TerminalUnderlineStyle.Single,
            2 => TerminalUnderlineStyle.Double,
            3 => TerminalUnderlineStyle.Curly,
            4 => TerminalUnderlineStyle.Dotted,
            5 => TerminalUnderlineStyle.Dashed,
            _ => TerminalUnderlineStyle.None,
        };
    }

    private static CellDecorations MapDecorationsFromStyle(GhosttyVtNative.GhosttyStyle style)
    {
        CellDecorations decorations = CellDecorations.None;
        if (style.Overline)
        {
            decorations |= CellDecorations.Overline;
        }

        return decorations;
    }

    private static bool TryResolveUnderlineColor(
        GhosttyVtNative.GhosttyStyleColor color,
        ReadOnlySpan<GhosttyVtNative.GhosttyColorRgb> palette,
        out uint value)
    {
        switch (color.Tag)
        {
            case GhosttyVtNative.GhosttyStyleColorTag.Rgb:
                value = GhosttyTerminal.ToArgb(color.Value.Rgb);
                return true;

            case GhosttyVtNative.GhosttyStyleColorTag.Palette:
                byte index = color.Value.Palette;
                if (index < palette.Length)
                {
                    value = GhosttyTerminal.ToArgb(palette[index]);
                    return true;
                }
                break;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Maps the packed legacy native attribute bits to <see cref="CellAttributes"/>.
    /// Retained for compatibility with existing tests while the old custom C API
    /// is phased out.
    /// </summary>
    private static CellAttributes MapAttributes(uint attrs)
    {
        CellAttributes result = CellAttributes.None;

        if ((attrs & (1 << 0)) != 0) result |= CellAttributes.Bold;
        if ((attrs & (1 << 1)) != 0) result |= CellAttributes.Italic;
        if ((attrs & (1 << 2)) != 0) result |= CellAttributes.Dim;
        if ((attrs & (1 << 3)) != 0) result |= CellAttributes.Inverse;
        if ((attrs & (1 << 4)) != 0) result |= CellAttributes.Hidden;
        if ((attrs & (1 << 5)) != 0) result |= CellAttributes.Strikethrough;
        if (MapUnderlineStyle(attrs) != TerminalUnderlineStyle.None) result |= CellAttributes.Underline;
        if ((attrs & (1 << 7)) != 0) result |= CellAttributes.Blink;

        return result;
    }

    private static TerminalUnderlineStyle MapUnderlineStyle(uint attrs)
    {
        return MapUnderlineStyleBits((int)((attrs >> 8) & 0x7));
    }

    private static CellDecorations MapDecorations(uint attrs)
    {
        CellDecorations decorations = CellDecorations.None;
        if ((attrs & (1 << 6)) != 0)
        {
            decorations |= CellDecorations.Overline;
        }

        return decorations;
    }

    private void UpdatePressedMouseButtons(in TerminalPointerEvent pointerEvent)
    {
        if (pointerEvent.Kind != TerminalPointerEventKind.Button)
        {
            return;
        }

        byte mask = GetMouseButtonMask(pointerEvent.Button);
        if (mask == 0)
        {
            return;
        }

        if (pointerEvent.Action == TerminalInputAction.Release)
        {
            _pressedMouseButtons = (byte)(_pressedMouseButtons & ~mask);
            return;
        }

        _pressedMouseButtons = (byte)(_pressedMouseButtons | mask);
    }

    private bool IsAnyMouseButtonPressed(in TerminalPointerEvent pointerEvent)
    {
        if (pointerEvent.Kind != TerminalPointerEventKind.Button)
        {
            return _pressedMouseButtons != 0;
        }

        byte mask = GetMouseButtonMask(pointerEvent.Button);
        if (mask == 0)
        {
            return _pressedMouseButtons != 0;
        }

        return pointerEvent.Action == TerminalInputAction.Release
            ? (_pressedMouseButtons & (byte)~mask) != 0
            : (_pressedMouseButtons | mask) != 0;
    }

    private static byte GetMouseButtonMask(TerminalMouseButton button)
    {
        return button switch
        {
            TerminalMouseButton.Left => 1 << 0,
            TerminalMouseButton.Middle => 1 << 1,
            TerminalMouseButton.Right => 1 << 2,
            _ => 0,
        };
    }

    private static GhosttyVtNative.GhosttyVtKeyAction MapKeyAction(TerminalInputAction action)
    {
        return action == TerminalInputAction.Release
            ? GhosttyVtNative.GhosttyVtKeyAction.Release
            : GhosttyVtNative.GhosttyVtKeyAction.Press;
    }

    private static GhosttyVtNative.GhosttyVtMods MapModifiers(TerminalModifiers modifiers)
    {
        GhosttyVtNative.GhosttyVtMods result = GhosttyVtNative.GhosttyVtMods.None;
        if ((modifiers & TerminalModifiers.Shift) != 0) result |= GhosttyVtNative.GhosttyVtMods.Shift;
        if ((modifiers & TerminalModifiers.Control) != 0) result |= GhosttyVtNative.GhosttyVtMods.Ctrl;
        if ((modifiers & TerminalModifiers.Alt) != 0) result |= GhosttyVtNative.GhosttyVtMods.Alt;
        if ((modifiers & TerminalModifiers.Meta) != 0) result |= GhosttyVtNative.GhosttyVtMods.Super;
        return result;
    }

    private static bool TryMapMouseButton(
        TerminalMouseButton button,
        out GhosttyVtNative.GhosttyMouseButtonId value)
    {
        switch (button)
        {
            case TerminalMouseButton.Left:
                value = GhosttyVtNative.GhosttyMouseButtonId.Left;
                return true;
            case TerminalMouseButton.Middle:
                value = GhosttyVtNative.GhosttyMouseButtonId.Middle;
                return true;
            case TerminalMouseButton.Right:
                value = GhosttyVtNative.GhosttyMouseButtonId.Right;
                return true;
            default:
                value = GhosttyVtNative.GhosttyMouseButtonId.Unknown;
                return false;
        }
    }

    private static bool TryMapScrollButton(
        in TerminalPointerEvent pointerEvent,
        out GhosttyVtNative.GhosttyMouseButtonId button)
    {
        if (pointerEvent.DeltaY > 0)
        {
            button = GhosttyVtNative.GhosttyMouseButtonId.Four;
            return true;
        }

        if (pointerEvent.DeltaY < 0)
        {
            button = GhosttyVtNative.GhosttyMouseButtonId.Five;
            return true;
        }

        if (pointerEvent.DeltaX < 0)
        {
            button = GhosttyVtNative.GhosttyMouseButtonId.Six;
            return true;
        }

        if (pointerEvent.DeltaX > 0)
        {
            button = GhosttyVtNative.GhosttyMouseButtonId.Seven;
            return true;
        }

        button = GhosttyVtNative.GhosttyMouseButtonId.Unknown;
        return false;
    }

    private static bool TryMapKeyId(string keyId, out GhosttyVtNative.GhosttyVtKey key)
    {
        key = keyId switch
        {
            nameof(GhosttyVtNative.GhosttyVtKey.A) => GhosttyVtNative.GhosttyVtKey.A,
            nameof(GhosttyVtNative.GhosttyVtKey.B) => GhosttyVtNative.GhosttyVtKey.B,
            nameof(GhosttyVtNative.GhosttyVtKey.C) => GhosttyVtNative.GhosttyVtKey.C,
            nameof(GhosttyVtNative.GhosttyVtKey.D) => GhosttyVtNative.GhosttyVtKey.D,
            nameof(GhosttyVtNative.GhosttyVtKey.E) => GhosttyVtNative.GhosttyVtKey.E,
            nameof(GhosttyVtNative.GhosttyVtKey.F) => GhosttyVtNative.GhosttyVtKey.F,
            nameof(GhosttyVtNative.GhosttyVtKey.G) => GhosttyVtNative.GhosttyVtKey.G,
            nameof(GhosttyVtNative.GhosttyVtKey.H) => GhosttyVtNative.GhosttyVtKey.H,
            nameof(GhosttyVtNative.GhosttyVtKey.I) => GhosttyVtNative.GhosttyVtKey.I,
            nameof(GhosttyVtNative.GhosttyVtKey.J) => GhosttyVtNative.GhosttyVtKey.J,
            nameof(GhosttyVtNative.GhosttyVtKey.K) => GhosttyVtNative.GhosttyVtKey.K,
            nameof(GhosttyVtNative.GhosttyVtKey.L) => GhosttyVtNative.GhosttyVtKey.L,
            nameof(GhosttyVtNative.GhosttyVtKey.M) => GhosttyVtNative.GhosttyVtKey.M,
            nameof(GhosttyVtNative.GhosttyVtKey.N) => GhosttyVtNative.GhosttyVtKey.N,
            nameof(GhosttyVtNative.GhosttyVtKey.O) => GhosttyVtNative.GhosttyVtKey.O,
            nameof(GhosttyVtNative.GhosttyVtKey.P) => GhosttyVtNative.GhosttyVtKey.P,
            nameof(GhosttyVtNative.GhosttyVtKey.Q) => GhosttyVtNative.GhosttyVtKey.Q,
            nameof(GhosttyVtNative.GhosttyVtKey.R) => GhosttyVtNative.GhosttyVtKey.R,
            nameof(GhosttyVtNative.GhosttyVtKey.S) => GhosttyVtNative.GhosttyVtKey.S,
            nameof(GhosttyVtNative.GhosttyVtKey.T) => GhosttyVtNative.GhosttyVtKey.T,
            nameof(GhosttyVtNative.GhosttyVtKey.U) => GhosttyVtNative.GhosttyVtKey.U,
            nameof(GhosttyVtNative.GhosttyVtKey.V) => GhosttyVtNative.GhosttyVtKey.V,
            nameof(GhosttyVtNative.GhosttyVtKey.W) => GhosttyVtNative.GhosttyVtKey.W,
            nameof(GhosttyVtNative.GhosttyVtKey.X) => GhosttyVtNative.GhosttyVtKey.X,
            nameof(GhosttyVtNative.GhosttyVtKey.Y) => GhosttyVtNative.GhosttyVtKey.Y,
            nameof(GhosttyVtNative.GhosttyVtKey.Z) => GhosttyVtNative.GhosttyVtKey.Z,
            "D0" => GhosttyVtNative.GhosttyVtKey.Digit0,
            "D1" => GhosttyVtNative.GhosttyVtKey.Digit1,
            "D2" => GhosttyVtNative.GhosttyVtKey.Digit2,
            "D3" => GhosttyVtNative.GhosttyVtKey.Digit3,
            "D4" => GhosttyVtNative.GhosttyVtKey.Digit4,
            "D5" => GhosttyVtNative.GhosttyVtKey.Digit5,
            "D6" => GhosttyVtNative.GhosttyVtKey.Digit6,
            "D7" => GhosttyVtNative.GhosttyVtKey.Digit7,
            "D8" => GhosttyVtNative.GhosttyVtKey.Digit8,
            "D9" => GhosttyVtNative.GhosttyVtKey.Digit9,
            "Return" => GhosttyVtNative.GhosttyVtKey.Enter,
            "Escape" => GhosttyVtNative.GhosttyVtKey.Escape,
            "Back" => GhosttyVtNative.GhosttyVtKey.Backspace,
            "Tab" => GhosttyVtNative.GhosttyVtKey.Tab,
            "Space" => GhosttyVtNative.GhosttyVtKey.Space,
            "OemMinus" => GhosttyVtNative.GhosttyVtKey.Minus,
            "OemPlus" => GhosttyVtNative.GhosttyVtKey.Equal,
            "OemOpenBrackets" => GhosttyVtNative.GhosttyVtKey.BracketLeft,
            "OemCloseBrackets" => GhosttyVtNative.GhosttyVtKey.BracketRight,
            "OemBackslash" => GhosttyVtNative.GhosttyVtKey.Backslash,
            "OemPipe" => GhosttyVtNative.GhosttyVtKey.Backslash,
            "OemSemicolon" => GhosttyVtNative.GhosttyVtKey.Semicolon,
            "OemQuotes" => GhosttyVtNative.GhosttyVtKey.Quote,
            "OemTilde" => GhosttyVtNative.GhosttyVtKey.Backquote,
            "OemComma" => GhosttyVtNative.GhosttyVtKey.Comma,
            "OemPeriod" => GhosttyVtNative.GhosttyVtKey.Period,
            "Oem2" => GhosttyVtNative.GhosttyVtKey.Slash,
            "Insert" => GhosttyVtNative.GhosttyVtKey.Insert,
            "Home" => GhosttyVtNative.GhosttyVtKey.Home,
            "PageUp" => GhosttyVtNative.GhosttyVtKey.PageUp,
            "Delete" => GhosttyVtNative.GhosttyVtKey.Delete,
            "End" => GhosttyVtNative.GhosttyVtKey.End,
            "PageDown" => GhosttyVtNative.GhosttyVtKey.PageDown,
            "Right" => GhosttyVtNative.GhosttyVtKey.ArrowRight,
            "Left" => GhosttyVtNative.GhosttyVtKey.ArrowLeft,
            "Down" => GhosttyVtNative.GhosttyVtKey.ArrowDown,
            "Up" => GhosttyVtNative.GhosttyVtKey.ArrowUp,
            "NumPad0" => GhosttyVtNative.GhosttyVtKey.Numpad0,
            "NumPad1" => GhosttyVtNative.GhosttyVtKey.Numpad1,
            "NumPad2" => GhosttyVtNative.GhosttyVtKey.Numpad2,
            "NumPad3" => GhosttyVtNative.GhosttyVtKey.Numpad3,
            "NumPad4" => GhosttyVtNative.GhosttyVtKey.Numpad4,
            "NumPad5" => GhosttyVtNative.GhosttyVtKey.Numpad5,
            "NumPad6" => GhosttyVtNative.GhosttyVtKey.Numpad6,
            "NumPad7" => GhosttyVtNative.GhosttyVtKey.Numpad7,
            "NumPad8" => GhosttyVtNative.GhosttyVtKey.Numpad8,
            "NumPad9" => GhosttyVtNative.GhosttyVtKey.Numpad9,
            "Decimal" => GhosttyVtNative.GhosttyVtKey.NumpadDecimal,
            "Divide" => GhosttyVtNative.GhosttyVtKey.NumpadDivide,
            "Multiply" => GhosttyVtNative.GhosttyVtKey.NumpadMultiply,
            "Subtract" => GhosttyVtNative.GhosttyVtKey.NumpadSubtract,
            "Add" => GhosttyVtNative.GhosttyVtKey.NumpadAdd,
            "LeftShift" => GhosttyVtNative.GhosttyVtKey.ShiftLeft,
            "RightShift" => GhosttyVtNative.GhosttyVtKey.ShiftRight,
            "LeftCtrl" => GhosttyVtNative.GhosttyVtKey.ControlLeft,
            "RightCtrl" => GhosttyVtNative.GhosttyVtKey.ControlRight,
            "LeftAlt" => GhosttyVtNative.GhosttyVtKey.AltLeft,
            "RightAlt" => GhosttyVtNative.GhosttyVtKey.AltRight,
            "LWin" => GhosttyVtNative.GhosttyVtKey.MetaLeft,
            "RWin" => GhosttyVtNative.GhosttyVtKey.MetaRight,
            "CapsLock" => GhosttyVtNative.GhosttyVtKey.CapsLock,
            "NumLock" => GhosttyVtNative.GhosttyVtKey.NumLock,
            "Apps" => GhosttyVtNative.GhosttyVtKey.ContextMenu,
            "F1" => GhosttyVtNative.GhosttyVtKey.F1,
            "F2" => GhosttyVtNative.GhosttyVtKey.F2,
            "F3" => GhosttyVtNative.GhosttyVtKey.F3,
            "F4" => GhosttyVtNative.GhosttyVtKey.F4,
            "F5" => GhosttyVtNative.GhosttyVtKey.F5,
            "F6" => GhosttyVtNative.GhosttyVtKey.F6,
            "F7" => GhosttyVtNative.GhosttyVtKey.F7,
            "F8" => GhosttyVtNative.GhosttyVtKey.F8,
            "F9" => GhosttyVtNative.GhosttyVtKey.F9,
            "F10" => GhosttyVtNative.GhosttyVtKey.F10,
            "F11" => GhosttyVtNative.GhosttyVtKey.F11,
            "F12" => GhosttyVtNative.GhosttyVtKey.F12,
            "F13" => GhosttyVtNative.GhosttyVtKey.F13,
            "F14" => GhosttyVtNative.GhosttyVtKey.F14,
            "F15" => GhosttyVtNative.GhosttyVtKey.F15,
            "F16" => GhosttyVtNative.GhosttyVtKey.F16,
            "F17" => GhosttyVtNative.GhosttyVtKey.F17,
            "F18" => GhosttyVtNative.GhosttyVtKey.F18,
            "F19" => GhosttyVtNative.GhosttyVtKey.F19,
            "F20" => GhosttyVtNative.GhosttyVtKey.F20,
            _ => GhosttyVtNative.GhosttyVtKey.Unidentified,
        };

        return key != GhosttyVtNative.GhosttyVtKey.Unidentified;
    }

    private static bool TryGetUnshiftedCodepoint(string keyId, out uint codepoint)
    {
        codepoint = keyId switch
        {
            nameof(GhosttyVtNative.GhosttyVtKey.A) => 'a',
            nameof(GhosttyVtNative.GhosttyVtKey.B) => 'b',
            nameof(GhosttyVtNative.GhosttyVtKey.C) => 'c',
            nameof(GhosttyVtNative.GhosttyVtKey.D) => 'd',
            nameof(GhosttyVtNative.GhosttyVtKey.E) => 'e',
            nameof(GhosttyVtNative.GhosttyVtKey.F) => 'f',
            nameof(GhosttyVtNative.GhosttyVtKey.G) => 'g',
            nameof(GhosttyVtNative.GhosttyVtKey.H) => 'h',
            nameof(GhosttyVtNative.GhosttyVtKey.I) => 'i',
            nameof(GhosttyVtNative.GhosttyVtKey.J) => 'j',
            nameof(GhosttyVtNative.GhosttyVtKey.K) => 'k',
            nameof(GhosttyVtNative.GhosttyVtKey.L) => 'l',
            nameof(GhosttyVtNative.GhosttyVtKey.M) => 'm',
            nameof(GhosttyVtNative.GhosttyVtKey.N) => 'n',
            nameof(GhosttyVtNative.GhosttyVtKey.O) => 'o',
            nameof(GhosttyVtNative.GhosttyVtKey.P) => 'p',
            nameof(GhosttyVtNative.GhosttyVtKey.Q) => 'q',
            nameof(GhosttyVtNative.GhosttyVtKey.R) => 'r',
            nameof(GhosttyVtNative.GhosttyVtKey.S) => 's',
            nameof(GhosttyVtNative.GhosttyVtKey.T) => 't',
            nameof(GhosttyVtNative.GhosttyVtKey.U) => 'u',
            nameof(GhosttyVtNative.GhosttyVtKey.V) => 'v',
            nameof(GhosttyVtNative.GhosttyVtKey.W) => 'w',
            nameof(GhosttyVtNative.GhosttyVtKey.X) => 'x',
            nameof(GhosttyVtNative.GhosttyVtKey.Y) => 'y',
            nameof(GhosttyVtNative.GhosttyVtKey.Z) => 'z',
            "D0" => '0',
            "D1" => '1',
            "D2" => '2',
            "D3" => '3',
            "D4" => '4',
            "D5" => '5',
            "D6" => '6',
            "D7" => '7',
            "D8" => '8',
            "D9" => '9',
            "OemMinus" => '-',
            "OemPlus" => '=',
            "OemOpenBrackets" => '[',
            "OemCloseBrackets" => ']',
            "OemBackslash" => '\\',
            "OemPipe" => '\\',
            "OemSemicolon" => ';',
            "OemQuotes" => '\'',
            "OemTilde" => '`',
            "OemComma" => ',',
            "OemPeriod" => '.',
            "Oem2" => '/',
            "Space" => ' ',
            "Decimal" => '.',
            "Divide" => '/',
            "Multiply" => '*',
            "Subtract" => '-',
            "Add" => '+',
            "NumPad0" => '0',
            "NumPad1" => '1',
            "NumPad2" => '2',
            "NumPad3" => '3',
            "NumPad4" => '4',
            "NumPad5" => '5',
            "NumPad6" => '6',
            "NumPad7" => '7',
            "NumPad8" => '8',
            "NumPad9" => '9',
            _ => 0,
        };

        return codepoint != 0;
    }
}
