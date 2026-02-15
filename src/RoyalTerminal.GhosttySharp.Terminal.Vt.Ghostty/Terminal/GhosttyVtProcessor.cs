// Licensed under the MIT License.
// RoyalTerminal.Avalonia — VT processor using Ghostty's native terminal via libghostty-terminal.

using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Avalonia.Terminal;

/// <summary>
/// VT processor that wraps Ghostty's native terminal via the libghostty-terminal C API.
/// This provides battle-tested, complete VT processing using the same engine that
/// powers the full Ghostty terminal — without requiring the macOS embedding surface.
///
/// Terminal query responses (DSR, DA, DECRQM, etc.) are handled natively by the Zig
/// library and delivered to C# via a callback function pointer, which then forwards
/// them through <see cref="ResponseCallback"/> to the PTY.
///
/// Falls back to <see cref="BasicVtProcessor"/> when <c>libghostty-terminal</c> is not
/// available.
/// </summary>
public sealed class GhosttyVtProcessor : IVtProcessor
{
    private readonly TerminalScreen _screen;
    private nint _terminal;
    private bool _disposed;

    private int _cursorCol;
    private int _cursorRow;
    private bool _cursorVisible = true;
    private bool _applicationCursorKeys;

    /// <summary>
    /// Prevent the native callback delegates from being garbage collected.
    /// </summary>
    private GhosttyTerminalNative.ResponseCallback? _nativeCallbackDelegate;
    private GhosttyTerminalNative.NotificationCallback? _nativeNotificationDelegate;

    /// <inheritdoc />
    public int CursorCol => _cursorCol;

    /// <inheritdoc />
    public int CursorRow => _cursorRow;

    /// <inheritdoc />
    public bool CursorVisible => _cursorVisible;

    /// <inheritdoc />
    public bool ApplicationCursorKeys => _applicationCursorKeys;

    /// <inheritdoc />
    public bool AlternateScreen =>
        _terminal != nint.Zero && GhosttyTerminalNative.TerminalGetModeAltScreen(_terminal) != 0;

    /// <inheritdoc />
    public bool BracketedPaste =>
        _terminal != nint.Zero && GhosttyTerminalNative.TerminalGetModeBracketedPaste(_terminal) != 0;

    /// <inheritdoc />
    public Action<byte[]>? ResponseCallback { get; set; }

    /// <inheritdoc />
    public Action? BellCallback { get; set; }

    /// <inheritdoc />
    public Action<string>? TitleCallback { get; set; }

    /// <summary>
    /// Creates a new Ghostty VT processor backed by the native terminal.
    /// </summary>
    /// <param name="screen">The terminal screen to update with processed VT data.</param>
    /// <exception cref="InvalidOperationException">Thrown if the native terminal cannot be created.</exception>
    public GhosttyVtProcessor(TerminalScreen screen)
    {
        _screen = screen;

        _terminal = GhosttyTerminalNative.TerminalNew(
            (uint)screen.Columns,
            (uint)screen.ViewportRows,
            10_000);

        if (_terminal == nint.Zero)
        {
            throw new InvalidOperationException(
                "Failed to create Ghostty native terminal. " +
                "Ensure libghostty-terminal is available.");
        }

        // Sync default colors from the screen so unstyled cells match the
        // configured theme rather than the Ghostty built-in colors.
        GhosttyTerminalNative.TerminalSetDefaultColors(
            _terminal, screen.DefaultForeground, screen.DefaultBackground);

        // Override the first 16 palette entries with standard xterm colors so
        // ANSI-color applications (mc, htop, etc.) render with the expected
        // saturated palette instead of Ghostty's muted "Tomorrow Night" theme.
        SetXtermPalette(_terminal);

        // Set up the native response callback — the Zig library will call this
        // when it encounters a query escape sequence (DSR, DA, DECRQM, etc.)
        SetupNativeResponseCallback();

        // Set up the native notification callback — bell and title change events
        SetupNativeNotificationCallback();
    }

    /// <summary>
    /// Configures the standard xterm 16-color palette on the native terminal.
    /// </summary>
    private static void SetXtermPalette(nint terminal)
    {
        // Standard xterm 16-color palette (matching xterm-256color)
        ReadOnlySpan<byte> palette =
        [
            // 0  black          1  red            2  green          3  yellow
            0x00, 0x00, 0x00,    0xCD, 0x00, 0x00, 0x00, 0xCD, 0x00, 0xCD, 0xCD, 0x00,
            // 4  blue           5  magenta        6  cyan           7  white
            0x00, 0x00, 0xEE,    0xCD, 0x00, 0xCD, 0x00, 0xCD, 0xCD, 0xE5, 0xE5, 0xE5,
            // 8  bright black   9  bright red    10  bright green  11  bright yellow
            0x7F, 0x7F, 0x7F,    0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0x00,
            //12  bright blue   13  bright mag.   14  bright cyan   15  bright white
            0x5C, 0x5C, 0xFF,    0xFF, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        ];

        for (byte i = 0; i < 16; i++)
        {
            var offset = i * 3;
            GhosttyTerminalNative.TerminalSetPaletteColor(
                terminal, i, palette[offset], palette[offset + 1], palette[offset + 2]);
        }
    }

    /// <inheritdoc />
    public unsafe void Process(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data.IsEmpty || _terminal == nint.Zero)
            return;

        // Feed raw VT data into the native terminal.
        // The native interactive stream handler will:
        //   1. Process state-modifying sequences (SGR, cursor moves, etc.)
        //   2. Detect query sequences (DSR, DA, DECRQM, etc.) and call
        //      our native callback with the formatted response bytes.
        fixed (byte* ptr = data)
        {
            GhosttyTerminalNative.TerminalProcess(_terminal, ptr, (nuint)data.Length);
        }

        // Update cursor state from native terminal
        GhosttyTerminalNative.TerminalGetCursor(_terminal, out var cursor);
        _cursorCol = (int)cursor.Col;
        _cursorRow = (int)cursor.Row;
        _cursorVisible = cursor.Visible != 0;

        // Update mode flags
        _applicationCursorKeys = GhosttyTerminalNative.TerminalGetModeAppCursor(_terminal) != 0;

        // Sync native terminal state back into the TerminalScreen for rendering
        SyncScreenFromNative();
    }

    /// <summary>
    /// Sets up the native response callback so the Zig library can deliver
    /// query responses directly to C# without the C#-side query scanner.
    /// </summary>
    private void SetupNativeResponseCallback()
    {
        // Create the delegate and prevent it from being GC'd by keeping a reference.
        _nativeCallbackDelegate = OnNativeResponse;
        var funcPtr = Marshal.GetFunctionPointerForDelegate(_nativeCallbackDelegate);
        GhosttyTerminalNative.TerminalSetResponseCallback(_terminal, funcPtr, nint.Zero);
    }

    /// <summary>
    /// Called from native code when a terminal query response is generated.
    /// Marshals the response bytes and forwards them to <see cref="ResponseCallback"/>.
    /// </summary>
    private void OnNativeResponse(nint data, nuint len, nint userdata)
    {
        if (ResponseCallback is null || len == 0)
            return;

        // Copy the response bytes — the pointer is only valid during this call.
        var response = new byte[(int)len];
        Marshal.Copy(data, response, 0, (int)len);
        ResponseCallback(response);
    }

    /// <summary>
    /// Sets up the native notification callback for bell and title change events.
    /// </summary>
    private void SetupNativeNotificationCallback()
    {
        _nativeNotificationDelegate = OnNativeNotification;
        var funcPtr = Marshal.GetFunctionPointerForDelegate(_nativeNotificationDelegate);
        GhosttyTerminalNative.TerminalSetNotificationCallback(_terminal, funcPtr, nint.Zero);
    }

    /// <summary>
    /// Called from native code when a terminal notification event occurs (bell, title change).
    /// </summary>
    private void OnNativeNotification(byte eventType, nint data, nuint len, nint userdata)
    {
        switch (eventType)
        {
            case 1: // Bell
                BellCallback?.Invoke();
                break;
            case 2: // Title change
                if (len > 0 && TitleCallback is not null)
                {
                    var title = Marshal.PtrToStringUTF8(data, (int)len) ?? string.Empty;
                    TitleCallback(title);
                }
                break;
        }
    }

    /// <inheritdoc />
    public void NotifyResize(int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_terminal == nint.Zero)
            return;

        GhosttyTerminalNative.TerminalResize(_terminal, (uint)columns, (uint)rows);

        // Sync the reflowed native state into the managed screen immediately
        // so the renderer always sees consistent data after resize.
        SyncScreenFromNative();
    }

    /// <inheritdoc />
    public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_terminal == nint.Zero)
            return;

        GhosttyTerminalNative.TerminalResizeWithPixels(
            _terminal, (uint)columns, (uint)rows, (uint)widthPx, (uint)heightPx);

        // Sync the reflowed native state into the managed screen immediately.
        SyncScreenFromNative();
    }

    /// <inheritdoc />
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Recreate the native terminal (no reset API exposed)
        if (_terminal != nint.Zero)
        {
            GhosttyTerminalNative.TerminalFree(_terminal);
        }

        _terminal = GhosttyTerminalNative.TerminalNew(
            (uint)_screen.Columns,
            (uint)_screen.ViewportRows,
            10_000);

        // Re-establish the native callbacks on the new terminal
        if (_terminal != nint.Zero)
        {
            SetupNativeResponseCallback();
            SetupNativeNotificationCallback();
        }

        _cursorCol = 0;
        _cursorRow = 0;
        _cursorVisible = true;
        _applicationCursorKeys = false;

        // Clear the screen
        for (var r = 0; r < _screen.ViewportRows; r++)
            _screen.GetViewportRow(r).Clear(_screen.DefaultForeground, _screen.DefaultBackground);
    }

    /// <summary>
    /// Reads all cell data from the native terminal and updates the managed
    /// <see cref="TerminalScreen"/> so that the SkiaSharp renderer can draw it.
    /// Clears any stale rows/cells that are beyond the native terminal's current
    /// grid dimensions to prevent rendering artifacts after resize.
    /// </summary>
    private unsafe void SyncScreenFromNative()
    {
        if (_terminal == nint.Zero)
            return;

        var cols = (int)GhosttyTerminalNative.TerminalGetCols(_terminal);
        var rows = (int)GhosttyTerminalNative.TerminalGetRows(_terminal);

        // Allocate a buffer for one row of cells
        var cellBuffer = stackalloc GhosttyTerminalNative.CellInfo[cols];
        bool supportsRowCellGraphemes = GhosttyTerminalNative.SupportsRowCellGraphemes;

        GhosttyTerminalNative.GraphemeSpan[]? graphemeSpanBuffer = null;
        uint[]? graphemeCodepointBuffer = null;
        if (supportsRowCellGraphemes)
        {
            graphemeSpanBuffer = ArrayPool<GhosttyTerminalNative.GraphemeSpan>.Shared.Rent(Math.Max(cols, 1));
            graphemeCodepointBuffer = ArrayPool<uint>.Shared.Rent(Math.Max(cols * 8, 1));
        }

        GhosttyTerminalNative.GraphemeSpan[] graphemeSpanArray =
            graphemeSpanBuffer ?? Array.Empty<GhosttyTerminalNative.GraphemeSpan>();
        uint[] graphemeCodepointArray = graphemeCodepointBuffer ?? Array.Empty<uint>();

        try
        {
            fixed (GhosttyTerminalNative.GraphemeSpan* graphemeSpanPtr = graphemeSpanArray)
            fixed (uint* graphemeCodepointPtr = graphemeCodepointArray)
            {
                for (var row = 0; row < rows && row < _screen.ViewportRows; row++)
                {
                    uint filled;
                    ReadOnlySpan<uint> graphemeCodepoints = ReadOnlySpan<uint>.Empty;
                    if (supportsRowCellGraphemes)
                    {
                        uint graphemeCodepointsWritten = 0;
                        filled = GhosttyTerminalNative.TerminalGetRowCellsWithGraphemes(
                            _terminal,
                            (uint)row,
                            cellBuffer,
                            (uint)cols,
                            graphemeSpanPtr,
                            (uint)cols,
                            graphemeCodepointPtr,
                            (uint)graphemeCodepointArray.Length,
                            &graphemeCodepointsWritten);

                        int graphemeLength = (int)Math.Min(graphemeCodepointsWritten, (uint)graphemeCodepointArray.Length);
                        graphemeCodepoints = graphemeCodepointArray.AsSpan(0, graphemeLength);
                    }
                    else
                    {
                        filled = GhosttyTerminalNative.TerminalGetRowCells(
                            _terminal,
                            (uint)row,
                            cellBuffer,
                            (uint)cols);
                    }

                    var screenRow = _screen.GetViewportRow(row);

                    for (var col = 0; col < (int)filled && col < screenRow.Columns; col++)
                    {
                        ref var cell = ref screenRow[col];
                        var native = cellBuffer[col];

                        cell.Codepoint = (int)native.Codepoint;
                        if (supportsRowCellGraphemes)
                        {
                            cell.Grapheme = TryBuildCellGrapheme(
                                native.Codepoint,
                                graphemeSpanArray[col],
                                graphemeCodepoints);
                        }
                        else
                        {
                            cell.Grapheme = null;
                        }

                        // Native default-style cells may not carry explicit colors.
                        // Fall back to terminal defaults instead of rendering transparent text.
                        cell.Foreground = native.FgColor != 0 ? native.FgColor : _screen.DefaultForeground;
                        cell.Background = native.BgColor != 0 ? native.BgColor : _screen.DefaultBackground;
                        cell.Attributes = MapAttributes(native.Attrs);

                        // Wide char handling
                        if ((native.Attrs & (1 << 16)) != 0)
                            cell.Width = 2; // wide char
                        else if ((native.Attrs & (1 << 17)) != 0)
                            cell.Width = 0; // spacer (second cell of wide char)
                        else
                            cell.Width = 1;
                    }

                    // Clear any stale cells beyond the native column count
                    for (var col = (int)filled; col < screenRow.Columns; col++)
                    {
                        ref var cell = ref screenRow[col];
                        cell.Codepoint = 0;
                        cell.Grapheme = null;
                        cell.Foreground = _screen.DefaultForeground;
                        cell.Background = _screen.DefaultBackground;
                        cell.Attributes = CellAttributes.None;
                        cell.Width = 1;
                    }

                    screenRow.IsDirty = true;
                }
            }
        }
        finally
        {
            if (graphemeSpanBuffer is not null)
            {
                ArrayPool<GhosttyTerminalNative.GraphemeSpan>.Shared.Return(graphemeSpanBuffer);
            }

            if (graphemeCodepointBuffer is not null)
            {
                ArrayPool<uint>.Shared.Return(graphemeCodepointBuffer);
            }
        }

        // Clear any stale rows beyond the native row count
        for (var row = rows; row < _screen.ViewportRows; row++)
        {
            _screen.GetViewportRow(row).Clear(_screen.DefaultForeground, _screen.DefaultBackground);
        }
    }

    private static string? TryBuildCellGrapheme(
        uint primaryCodepoint,
        GhosttyTerminalNative.GraphemeSpan span,
        ReadOnlySpan<uint> graphemeCodepoints)
    {
        if (span.Length == 0 || span.Offset > int.MaxValue || span.Length > int.MaxValue)
        {
            return null;
        }

        int offset = (int)span.Offset;
        int length = (int)span.Length;
        if (offset < 0 || length < 0 || offset > graphemeCodepoints.Length - length)
        {
            return null;
        }

        if (!Rune.IsValid((int)primaryCodepoint))
        {
            return null;
        }

        StringBuilder sb = new();
        sb.Append(char.ConvertFromUtf32((int)primaryCodepoint));
        ReadOnlySpan<uint> trailing = graphemeCodepoints.Slice(offset, length);
        for (int i = 0; i < trailing.Length; i++)
        {
            uint cp = trailing[i];
            if (!Rune.IsValid((int)cp))
            {
                return null;
            }

            sb.Append(char.ConvertFromUtf32((int)cp));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Maps the packed native attribute bits to <see cref="CellAttributes"/>.
    /// </summary>
    private static CellAttributes MapAttributes(uint attrs)
    {
        var result = CellAttributes.None;

        if ((attrs & (1 << 0)) != 0) result |= CellAttributes.Bold;
        if ((attrs & (1 << 1)) != 0) result |= CellAttributes.Italic;
        if ((attrs & (1 << 2)) != 0) result |= CellAttributes.Dim;
        if ((attrs & (1 << 3)) != 0) result |= CellAttributes.Inverse;
        if ((attrs & (1 << 4)) != 0) result |= CellAttributes.Hidden;
        if ((attrs & (1 << 5)) != 0) result |= CellAttributes.Strikethrough;
        // bit 6 = overline (not in CellAttributes — skip)
        // bits 8-10 = underline style
        if (((attrs >> 8) & 0x7) != 0) result |= CellAttributes.Underline;
        // bit 7 = blink (from Ghostty flags — map to Blink)
        if ((attrs & (1 << 7)) != 0) result |= CellAttributes.Blink;

        return result;
    }

    /// <summary>
    /// Checks whether the Ghostty native terminal API is available.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            return GhosttyTerminalNative.IsAvailable();
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
            return;
        _disposed = true;

        if (_terminal != nint.Zero)
        {
            GhosttyTerminalNative.TerminalFree(_terminal);
            _terminal = nint.Zero;
        }
    }
}
