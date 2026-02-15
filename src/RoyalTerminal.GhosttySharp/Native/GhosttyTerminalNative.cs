// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.GhosttySharp — P/Invoke bindings for libghostty-terminal.
//
// This is a standalone shared library built from Ghostty's VT terminal modules.
// It provides full terminal emulation (VT parsing, screen state, cursor, modes)
// without requiring the macOS-only embedding surface.
//
// Build the native library: native/ghostty-terminal/build.sh release

using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

/// <summary>
/// P/Invoke declarations for libghostty-terminal — a standalone shared library
/// that wraps Ghostty's VT terminal processing engine into a simple C API.
/// </summary>
public static partial class GhosttyTerminalNative
{
    private const string LibName = "ghostty-terminal";
    private const string TerminalGetRowCellsWithGraphemesSymbol = "ghostty_terminal_get_row_cells_with_graphemes";

    private static readonly nint s_nativeLibraryHandle = LoadNativeLibraryHandle();
    private static readonly nint s_terminalGetRowCellsWithGraphemesExport =
        ResolveOptionalExport(TerminalGetRowCellsWithGraphemesSymbol);

    /// <summary>
    /// Returns true when the loaded libghostty-terminal exposes grapheme-aware row-cell reading.
    /// </summary>
    public static bool SupportsRowCellGraphemes => s_terminalGetRowCellsWithGraphemesExport != nint.Zero;

    // ──────────────────────── Structs ────────────────────────────────

    /// <summary>Cell information for a single terminal cell.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CellInfo
    {
        /// <summary>UTF-32 codepoint (0 for empty cells).</summary>
        public uint Codepoint;

        /// <summary>Foreground color as 0xAARRGGBB (alpha is always 0xFF).</summary>
        public uint FgColor;

        /// <summary>Background color as 0xAARRGGBB (alpha is always 0xFF).</summary>
        public uint BgColor;

        /// <summary>
        /// Packed attribute flags:
        /// bit 0: bold, bit 1: italic, bit 2: dim/faint, bit 3: inverse,
        /// bit 4: hidden/invisible, bit 5: strikethrough, bit 6: overline,
        /// bits 8-10: underline style (0=none, 1=single, 2=double, 3=curly, 4=dotted, 5=dashed),
        /// bit 16: wide char, bit 17: wide spacer.
        /// </summary>
        public uint Attrs;
    }

    /// <summary>
    /// Grapheme span for a cell. The span indexes into a flattened UTF-32
    /// trailing-codepoint buffer returned by
    /// <see cref="TerminalGetRowCellsWithGraphemes"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GraphemeSpan
    {
        /// <summary>Start index into the flattened trailing-codepoint buffer.</summary>
        public uint Offset;

        /// <summary>Number of trailing codepoints for this cell.</summary>
        public uint Length;
    }

    /// <summary>Cursor position and style information.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CursorInfo
    {
        /// <summary>Cursor column (0-based).</summary>
        public uint Col;

        /// <summary>Cursor row (0-based).</summary>
        public uint Row;

        /// <summary>Whether the cursor is visible (1 = visible, 0 = hidden).</summary>
        public byte Visible;

        /// <summary>
        /// Cursor style: 0=block_steady, 1=block_blink, 2=underline_steady,
        /// 3=underline_blink, 4=bar_steady, 5=bar_blink.
        /// </summary>
        public byte CursorStyle;
    }

    // ──────────────────────── Lifecycle ──────────────────────────────

    /// <summary>
    /// Creates a new terminal with the specified grid dimensions.
    /// </summary>
    /// <param name="cols">Number of columns.</param>
    /// <param name="rows">Number of rows.</param>
    /// <param name="maxScrollback">Maximum scrollback lines.</param>
    /// <returns>Opaque terminal handle, or <see cref="nint.Zero"/> on failure.</returns>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_new")]
    public static partial nint TerminalNew(uint cols, uint rows, uint maxScrollback);

    /// <summary>
    /// Destroys a terminal and releases all resources.
    /// </summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_free")]
    public static partial void TerminalFree(nint terminal);

    // ──────────────────────── Processing ─────────────────────────────

    /// <summary>
    /// Feeds raw VT data through the terminal's processing pipeline.
    /// Updates internal screen state (cells, cursor, modes). If a response
    /// callback is set, query sequences (DSR, DA, etc.) will trigger the
    /// callback with the appropriate response bytes.
    /// </summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_process")]
    public static unsafe partial void TerminalProcess(nint terminal, byte* data, nuint len);

    // ──────────────────────── Response Callback ─────────────────────

    /// <summary>
    /// Native callback function pointer type for terminal query responses.
    /// Called when the terminal needs to send data back (DSR, DA, DECRQM, etc.).
    /// </summary>
    /// <param name="data">Pointer to response bytes.</param>
    /// <param name="len">Number of response bytes.</param>
    /// <param name="userdata">Opaque pointer passed when the callback was set.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ResponseCallback(nint data, nuint len, nint userdata);

    /// <summary>
    /// Sets or clears the response callback for terminal query responses.
    /// When the terminal processes a query escape sequence (DSR, DA, DECRQM, etc.),
    /// the response bytes are delivered via this callback.
    /// </summary>
    /// <param name="terminal">Terminal handle.</param>
    /// <param name="callback">Response callback function pointer, or null to disable.</param>
    /// <param name="userdata">Opaque pointer passed through to callback.</param>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_set_response_callback")]
    public static partial void TerminalSetResponseCallback(nint terminal, nint callback, nint userdata);

    // ──────────────────────── Notification Callback ──────────────────

    /// <summary>
    /// Native callback function pointer type for terminal notifications (bell, title change).
    /// </summary>
    /// <param name="eventType">Event type: 1 = bell, 2 = window title changed.</param>
    /// <param name="data">Pointer to event data (UTF-8 title for type 2, null for type 1).</param>
    /// <param name="len">Length of event data in bytes.</param>
    /// <param name="userdata">Opaque pointer passed when the callback was set.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void NotificationCallback(byte eventType, nint data, nuint len, nint userdata);

    /// <summary>
    /// Sets or clears the notification callback for terminal events (bell, title change).
    /// </summary>
    /// <param name="terminal">Terminal handle.</param>
    /// <param name="callback">Notification callback function pointer, or null to disable.</param>
    /// <param name="userdata">Opaque pointer passed through to callback.</param>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_set_notification_callback")]
    public static partial void TerminalSetNotificationCallback(nint terminal, nint callback, nint userdata);

    // ──────────────────────── Screen State ───────────────────────────

    /// <summary>Returns the number of columns.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_get_cols")]
    public static partial uint TerminalGetCols(nint terminal);

    /// <summary>Returns the number of rows.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_get_rows")]
    public static partial uint TerminalGetRows(nint terminal);

    /// <summary>Gets cursor position and style.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_get_cursor")]
    public static partial void TerminalGetCursor(nint terminal, out CursorInfo cursor);

    /// <summary>
    /// Fills cell info for a viewport row.
    /// </summary>
    /// <param name="terminal">Terminal handle.</param>
    /// <param name="rowIdx">Viewport row (0-based from top).</param>
    /// <param name="cells">Array of cell info structs to fill.</param>
    /// <param name="maxCells">Maximum cells to fill.</param>
    /// <returns>Number of cells filled.</returns>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_get_row_cells")]
    public static unsafe partial uint TerminalGetRowCells(nint terminal, uint rowIdx, CellInfo* cells, uint maxCells);

    /// <summary>
    /// Reads row cells with optional grapheme payload.
    /// Falls back to <see cref="TerminalGetRowCells"/> when the native symbol
    /// is unavailable in the loaded library.
    /// </summary>
    public static unsafe uint TerminalGetRowCellsWithGraphemes(
        nint terminal,
        uint rowIdx,
        CellInfo* cells,
        uint maxCells,
        GraphemeSpan* graphemeSpans,
        uint maxSpans,
        uint* graphemeCodepoints,
        uint maxGraphemeCodepoints,
        uint* graphemeCodepointsWritten)
    {
        if (s_terminalGetRowCellsWithGraphemesExport == nint.Zero)
        {
            uint filled = TerminalGetRowCells(terminal, rowIdx, cells, maxCells);

            if (graphemeSpans != null)
            {
                uint spanCount = filled < maxSpans ? filled : maxSpans;
                for (uint i = 0; i < spanCount; i++)
                {
                    graphemeSpans[i] = default;
                }
            }

            if (graphemeCodepointsWritten != null)
            {
                *graphemeCodepointsWritten = 0;
            }

            return filled;
        }

        var fn = (delegate* unmanaged[Cdecl]<
            nint,
            uint,
            CellInfo*,
            uint,
            GraphemeSpan*,
            uint,
            uint*,
            uint,
            uint*,
            uint>)s_terminalGetRowCellsWithGraphemesExport;

        return fn(
            terminal,
            rowIdx,
            cells,
            maxCells,
            graphemeSpans,
            maxSpans,
            graphemeCodepoints,
            maxGraphemeCodepoints,
            graphemeCodepointsWritten);
    }

    // ──────────────────────── Resize ─────────────────────────────────

    /// <summary>Resizes the terminal grid.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_resize")]
    public static partial void TerminalResize(nint terminal, uint cols, uint rows);

    /// <summary>
    /// Resizes the terminal grid with pixel dimensions.
    /// Pixel dimensions are used for CSI 14t/16t size reports.
    /// </summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_resize_with_pixels")]
    public static partial void TerminalResizeWithPixels(nint terminal, uint cols, uint rows, uint widthPx, uint heightPx);

    // ──────────────────────── Colors ─────────────────────────────────

    /// <summary>
    /// Sets the default foreground and background colors used when a cell
    /// has no explicit style. Colors are packed as 0xAARRGGBB.
    /// </summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_set_default_colors")]
    public static partial void TerminalSetDefaultColors(nint terminal, uint fg, uint bg);

    /// <summary>
    /// Overrides a single entry in the 256-color palette.
    /// </summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_set_palette_color")]
    public static partial void TerminalSetPaletteColor(nint terminal, byte idx, byte r, byte g, byte b);

    // ──────────────────────── Mode Queries ───────────────────────────

    /// <summary>Returns 1 if application cursor key mode (DECCKM) is active.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_get_mode_app_cursor")]
    public static partial byte TerminalGetModeAppCursor(nint terminal);

    /// <summary>Returns 1 if application keypad mode is active.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_get_mode_app_keypad")]
    public static partial byte TerminalGetModeAppKeypad(nint terminal);

    /// <summary>Returns 1 if bracketed paste mode is active.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_get_mode_bracketed_paste")]
    public static partial byte TerminalGetModeBracketedPaste(nint terminal);

    /// <summary>Returns 1 if the alternate screen buffer is active.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_get_mode_alt_screen")]
    public static partial byte TerminalGetModeAltScreen(nint terminal);

    // ──────────────────────── Self Test ──────────────────────────────

    /// <summary>
    /// Runs a self-test of VT parsing inside the native library.
    /// Returns 0 on success, non-zero error code on failure.
    /// </summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_self_test")]
    public static partial uint SelfTest();

    // ──────────────────────── Availability ───────────────────────────

    /// <summary>
    /// Checks whether the libghostty-terminal library is available at runtime.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            var handle = TerminalNew(1, 1, 0);
            if (handle != nint.Zero)
                TerminalFree(handle);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch
        {
            // Symbol exists but call may have failed — library is available.
            return true;
        }
    }

    private static nint LoadNativeLibraryHandle()
    {
        return NativeLibrary.TryLoad(
            LibName,
            typeof(GhosttyTerminalNative).Assembly,
            null,
            out nint handle)
            ? handle
            : nint.Zero;
    }

    private static nint ResolveOptionalExport(string symbol)
    {
        if (s_nativeLibraryHandle == nint.Zero)
        {
            return nint.Zero;
        }

        return NativeLibrary.TryGetExport(s_nativeLibraryHandle, symbol, out nint export)
            ? export
            : nint.Zero;
    }

}
