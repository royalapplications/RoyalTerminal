// RoyalTerminal.GhosttySharp Terminal C API Library
//
// A standalone shared library that wraps Ghostty's VT terminal processing
// into a simple C API. Built by referencing libghostty-vt's Zig modules from
// the Ghostty submodule.
//
// This library provides:
//   - Terminal create/destroy
//   - VT data processing with query response support via C callbacks
//   - Screen state reading (cells with codepoints, colors, attributes)
//   - Cursor position/style queries
//   - Terminal mode queries (DECCKM, bracketed paste, alt screen, etc.)
//   - Terminal resize
//
// Build: zig build -Doptimize=ReleaseFast
// Output: zig-out/lib/libghostty-terminal.{dylib,so}

const std = @import("std");
const builtin = @import("builtin");

// Import the ghostty-vt module (provided by build.zig)
const ghostty = @import("ghostty_vt");
const Terminal = ghostty.Terminal;
const page = ghostty.page;
const Style = ghostty.Style;
const color = ghostty.color;
const Stream = ghostty.Stream;
const Action = ghostty.StreamAction;
const Screen = ghostty.Screen;
const modes = ghostty.modes;
const device_status = ghostty.device_status;
const SizeReportStyle = ghostty.SizeReportStyle;

// ═══════════════════════════════════════════════════════════════════════════
// C-compatible structs
// ═══════════════════════════════════════════════════════════════════════════

/// Cell information for a single terminal cell.
pub const GhosttyTerminalCellInfo = extern struct {
    /// UTF-32 codepoint (0 = empty cell).
    codepoint: u32 = 0,
    /// Foreground color as 0xAARRGGBB (alpha is always 0xFF).
    fg_color: u32 = 0xFFD4D4D4,
    /// Background color as 0xAARRGGBB (alpha is always 0xFF).
    bg_color: u32 = 0xFF1E1E1E,
    /// Packed attributes:
    ///   bit  0: bold
    ///   bit  1: italic
    ///   bit  2: dim/faint
    ///   bit  3: inverse/reverse
    ///   bit  4: hidden/invisible
    ///   bit  5: strikethrough
    ///   bit  6: overline
    ///   bits 8-10: underline style (0=none,1=single,2=double,3=curly,4=dotted,5=dashed)
    ///   bit 16: wide char (occupies 2 cells)
    ///   bit 17: wide spacer (second cell of a wide char)
    attrs: u32 = 0,
};

/// Grapheme span for a single cell.
/// The span indexes into a flattened UTF-32 codepoint buffer emitted by
/// `ghostty_terminal_get_row_cells_with_graphemes`.
///
/// The flattened buffer contains only trailing codepoints that come after
/// `GhosttyTerminalCellInfo.codepoint` for each grapheme cluster.
/// Therefore:
///   - length == 0 means no trailing grapheme codepoints for this cell.
///   - full grapheme = cell.codepoint + grapheme_codepoints[offset..offset+length].
pub const GhosttyTerminalGraphemeSpan = extern struct {
    offset: u32 = 0,
    length: u32 = 0,
};

/// Cursor position and style information.
pub const GhosttyTerminalCursorInfo = extern struct {
    /// Cursor column (0-based).
    col: u32 = 0,
    /// Cursor row (0-based).
    row: u32 = 0,
    /// 1 = visible, 0 = hidden.
    visible: u8 = 1,
    /// Cursor style (maps to Ghostty CursorStyle enum).
    cursor_style: u8 = 0,
};

/// C callback function pointer type for terminal responses.
/// Called when the terminal needs to send data back to the input source
/// (e.g., DSR cursor position report, DA device attributes response).
///
/// Parameters:
///   data     — pointer to the response bytes
///   len      — number of response bytes
///   userdata — opaque pointer passed when the callback was set
const ResponseCallback = *const fn (data: [*]const u8, len: usize, userdata: ?*anyopaque) callconv(.c) void;

/// C callback for terminal notifications (bell, title changes, etc.).
/// Parameters:
///   event_type — 1 = bell, 2 = title change
///   data       — event-specific data (null for bell, title string for title change)
///   len        — length of data
///   userdata   — opaque pointer passed when the callback was set
const NotificationCallback = *const fn (event_type: u8, data: ?[*]const u8, len: usize, userdata: ?*anyopaque) callconv(.c) void;

// ═══════════════════════════════════════════════════════════════════════════
// Interactive stream handler
// ═══════════════════════════════════════════════════════════════════════════

/// A stream handler that extends the readonly handler with query responses.
/// Terminal-modifying actions are handled identically to the readonly stream.
/// Query actions (DSR, DA, ENQ, DECRQM, kitty keyboard query, XTVERSION)
/// format a response and deliver it via a C callback function.
const InteractiveHandler = struct {
    /// The readonly handler for all terminal-modifying actions.
    readonly: ReadonlyHandler,

    /// Response callback — called when a query needs a response written back.
    response_callback: ?ResponseCallback = null,
    response_userdata: ?*anyopaque = null,

    /// Notification callback — called for bell, title changes, etc.
    notification_callback: ?NotificationCallback = null,
    notification_userdata: ?*anyopaque = null,

    /// Pixel dimensions for size reports (CSI 14t, CSI 16t).
    width_px: u32 = 0,
    height_px: u32 = 0,

    pub fn init(term: *Terminal) InteractiveHandler {
        return .{
            .readonly = ReadonlyHandler.init(term),
        };
    }

    pub fn deinit(self: *InteractiveHandler) void {
        self.readonly.deinit();
    }

    pub fn vt(
        self: *InteractiveHandler,
        comptime action: Action.Tag,
        value: Action.Value(action),
    ) void {
        switch (action) {
            // ── Query actions that need responses ──────────────────
            .enquiry => {}, // Ghostty default: no enquiry response
            .device_attributes => self.deviceAttributes(value),
            .device_status => self.deviceStatusReport(value.request),
            .kitty_keyboard_query => self.queryKittyKeyboard(),
            .request_mode => self.requestMode(value.mode),
            .request_mode_unknown => self.requestModeUnknown(value.mode, value.ansi),
            .xtversion => self.reportXtversion(),
            .size_report => self.sizeReport(value),

            // ── Notification actions ──────────────────────────────
            .bell => self.notifyBell(),
            .window_title => {
                self.notifyTitle(value.title);
                self.readonly.vt(action, value);
            },

            // ── Terminal-modifying actions — delegate to readonly ──
            else => self.readonly.vt(action, value),
        }
    }

    fn terminal(self: *InteractiveHandler) *Terminal {
        return self.readonly.terminal;
    }

    fn deviceAttributes(self: *InteractiveHandler, req: anytype) void {
        switch (req) {
            .primary => self.sendResponse("\x1B[?62;22c"),
            .secondary => self.sendResponse("\x1B[>1;10;0c"),
            .tertiary => self.sendResponse("\x1BP!|464F4F\x1B\\"), // "FOO" hex
            //else => {}, // Unknown DA request — ignore
        }
    }

    fn deviceStatusReport(self: *InteractiveHandler, req: device_status.Request) void {
        switch (req) {
            .operating_status => self.sendResponse("\x1B[0n"),
            .cursor_position => {
                const t = self.terminal();
                const pos_x = t.screens.active.cursor.x;
                const pos_y = t.screens.active.cursor.y;

                // Format: ESC [ row ; col R (1-based)
                var buf: [32]u8 = undefined;
                const resp = std.fmt.bufPrint(&buf, "\x1B[{};{}R", .{
                    pos_y + 1,
                    pos_x + 1,
                }) catch return;
                self.sendResponseSlice(resp);
            },
            .color_scheme => {
                // Report dark color scheme by default
                self.sendResponse("\x1B[?997;1n");
            },
        }
    }

    fn queryKittyKeyboard(self: *InteractiveHandler) void {
        const t = self.terminal();
        var buf: [16]u8 = undefined;
        const resp = std.fmt.bufPrint(&buf, "\x1b[?{}u", .{
            t.screens.active.kitty_keyboard.current().int(),
        }) catch return;
        self.sendResponseSlice(resp);
    }

    fn requestMode(self: *InteractiveHandler, mode: modes.Mode) void {
        const tag: modes.ModeTag = @bitCast(@intFromEnum(mode));
        const t = self.terminal();
        const code: u8 = if (t.modes.get(mode)) 1 else 2;

        var buf: [32]u8 = undefined;
        const resp = std.fmt.bufPrint(&buf, "\x1B[{s}{};{}$y", .{
            if (tag.ansi) "" else "?",
            tag.value,
            code,
        }) catch return;
        self.sendResponseSlice(resp);
    }

    fn requestModeUnknown(self: *InteractiveHandler, mode_raw: u16, ansi: bool) void {
        var buf: [32]u8 = undefined;
        const resp = std.fmt.bufPrint(&buf, "\x1B[{s}{};0$y", .{
            if (ansi) "" else "?",
            mode_raw,
        }) catch return;
        self.sendResponseSlice(resp);
    }

    fn reportXtversion(self: *InteractiveHandler) void {
        self.sendResponse("\x1BP>|ghostty-terminal 0.1.0\x1B\\");
    }

    fn sizeReport(self: *InteractiveHandler, style: SizeReportStyle) void {
        const t = self.terminal();
        var buf: [64]u8 = undefined;

        switch (style) {
            // CSI 14 t — report text area size in pixels
            .csi_14_t => {
                const resp = std.fmt.bufPrint(&buf, "\x1B[4;{};{}t", .{
                    self.height_px,
                    self.width_px,
                }) catch return;
                self.sendResponseSlice(resp);
            },
            // CSI 16 t — report character cell size in pixels
            .csi_16_t => {
                const cell_w: u32 = if (t.cols > 0 and self.width_px > 0) self.width_px / t.cols else 8;
                const cell_h: u32 = if (t.rows > 0 and self.height_px > 0) self.height_px / t.rows else 16;
                const resp = std.fmt.bufPrint(&buf, "\x1B[6;{};{}t", .{ cell_h, cell_w }) catch return;
                self.sendResponseSlice(resp);
            },
            // CSI 18 t — report text area size in characters
            .csi_18_t => {
                const resp = std.fmt.bufPrint(&buf, "\x1B[8;{};{}t", .{ t.rows, t.cols }) catch return;
                self.sendResponseSlice(resp);
            },
            // CSI 21 t — report window title (report empty title)
            .csi_21_t => {
                self.sendResponse("\x1B]l\x1B\\");
            },
        }
    }

    fn notifyBell(self: *InteractiveHandler) void {
        if (self.notification_callback) |cb| {
            cb(1, null, 0, self.notification_userdata);
        }
    }

    fn notifyTitle(self: *InteractiveHandler, title: []const u8) void {
        if (self.notification_callback) |cb| {
            cb(2, title.ptr, title.len, self.notification_userdata);
        }
    }

    /// Sends a compile-time known response string.
    fn sendResponse(self: *InteractiveHandler, comptime resp: []const u8) void {
        if (self.response_callback) |cb| {
            cb(resp.ptr, resp.len, self.response_userdata);
        }
    }

    /// Sends a dynamically formatted response slice.
    fn sendResponseSlice(self: *InteractiveHandler, resp: []const u8) void {
        if (self.response_callback) |cb| {
            cb(resp.ptr, resp.len, self.response_userdata);
        }
    }
};

// Get the ReadonlyHandler type from Terminal's vtHandler return type.
const ReadonlyHandler = @typeInfo(@TypeOf(Terminal.vtHandler)).@"fn".return_type.?;

// Our interactive stream type.
const InteractiveStream = Stream(InteractiveHandler);

// ═══════════════════════════════════════════════════════════════════════════
// Opaque handle
// ═══════════════════════════════════════════════════════════════════════════

const TerminalHandle = struct {
    terminal: Terminal,
    stream: InteractiveStream,
    alloc: std.mem.Allocator,
    default_fg: u32 = 0xFFD4D4D4,
    default_bg: u32 = 0xFF1E1E1E,
    width_px: u32 = 0,
    height_px: u32 = 0,
};

// ═══════════════════════════════════════════════════════════════════════════
// Exported C functions
// ═══════════════════════════════════════════════════════════════════════════

/// Creates a new terminal with the given dimensions.
/// Returns an opaque handle, or null on failure.
export fn ghostty_terminal_new(cols: u32, rows: u32, max_scrollback: u32) ?*TerminalHandle {
    // Guard: zero-dimension terminals are not allowed by Ghostty internals.
    const safe_cols: u32 = if (cols == 0) 1 else cols;
    const safe_rows: u32 = if (rows == 0) 1 else rows;

    const alloc = defaultAllocator();

    const handle = alloc.create(TerminalHandle) catch return null;
    handle.* = .{
        .terminal = Terminal.init(alloc, .{
            .cols = @intCast(safe_cols),
            .rows = @intCast(safe_rows),
            .max_scrollback = max_scrollback,
            // Enable terminal Unicode grapheme clustering (mode 2027)
            // by default so regional-indicator flags and other emoji
            // sequences are represented as a single grapheme cluster.
            .default_modes = .{ .grapheme_cluster = true },
        }) catch {
            alloc.destroy(handle);
            return null;
        },
        .stream = undefined,
        .alloc = alloc,
    };

    // Initialize the interactive stream with our custom handler.
    // The handler wraps the readonly handler for state changes and adds query responses.
    const handler = InteractiveHandler.init(&handle.terminal);
    handle.stream = InteractiveStream.initAlloc(alloc, handler);

    return handle;
}

/// Sets a callback that receives terminal query responses (DSR, DA, ENQ, etc.).
/// When the terminal processes a query escape sequence, the response bytes are
/// delivered via this callback instead of being silently ignored.
///
/// Parameters:
///   handle   — terminal handle
///   callback — function to call with response data, or null to disable
///   userdata — opaque pointer passed through to callback
export fn ghostty_terminal_set_response_callback(
    handle: ?*TerminalHandle,
    callback: ?ResponseCallback,
    userdata: ?*anyopaque,
) void {
    const h = handle orelse return;
    h.stream.handler.response_callback = callback;
    h.stream.handler.response_userdata = userdata;
}

/// Destroys a terminal and frees all associated resources.
export fn ghostty_terminal_free(handle: ?*TerminalHandle) void {
    const h = handle orelse return;
    h.stream.deinit();
    h.terminal.deinit(h.alloc);
    h.alloc.destroy(h);
}

/// Processes raw VT data through the terminal's persistent readonly stream.
/// Parser state is preserved across calls so split escape sequences work correctly.
export fn ghostty_terminal_process(handle: ?*TerminalHandle, data: ?[*]const u8, len: usize) void {
    const h = handle orelse return;
    const d = data orelse return;
    if (len == 0) return;

    h.stream.nextSlice(d[0..len]);
}

/// Returns the number of columns.
export fn ghostty_terminal_get_cols(handle: ?*const TerminalHandle) u32 {
    const h = handle orelse return 0;
    return h.terminal.cols;
}

/// Returns the number of rows.
export fn ghostty_terminal_get_rows(handle: ?*const TerminalHandle) u32 {
    const h = handle orelse return 0;
    return h.terminal.rows;
}

/// Fills cursor information into the provided struct.
export fn ghostty_terminal_get_cursor(handle: ?*const TerminalHandle, out: ?*GhosttyTerminalCursorInfo) void {
    const o = out orelse return;
    const h = handle orelse {
        o.* = .{};
        return;
    };

    const screen = h.terminal.screens.active;
    o.* = .{
        .col = screen.cursor.x,
        .row = screen.cursor.y,
        .visible = if (h.terminal.modes.get(.cursor_visible)) 1 else 0,
        .cursor_style = @intFromEnum(screen.cursor.cursor_style),
    };
}

/// Fills cell info for a viewport row. Returns the number of cells filled.
export fn ghostty_terminal_get_row_cells(
    handle: ?*TerminalHandle,
    row_idx: u32,
    cells_out: ?[*]GhosttyTerminalCellInfo,
    max_cells: u32,
) u32 {
    return fillRowCells(
        handle,
        row_idx,
        cells_out,
        max_cells,
        null,
        0,
        null,
        0,
        null,
    );
}

/// Fills cell info for a viewport row and optionally exports grapheme spans
/// plus flattened trailing grapheme codepoints.
///
/// Trailing grapheme codepoints are those after the first codepoint in the
/// cell's grapheme cluster. The first codepoint is always in
/// `GhosttyTerminalCellInfo.codepoint`.
export fn ghostty_terminal_get_row_cells_with_graphemes(
    handle: ?*TerminalHandle,
    row_idx: u32,
    cells_out: ?[*]GhosttyTerminalCellInfo,
    max_cells: u32,
    grapheme_spans_out: ?[*]GhosttyTerminalGraphemeSpan,
    max_spans: u32,
    grapheme_codepoints_out: ?[*]u32,
    max_grapheme_codepoints: u32,
    grapheme_codepoints_written: ?*u32,
) u32 {
    return fillRowCells(
        handle,
        row_idx,
        cells_out,
        max_cells,
        grapheme_spans_out,
        max_spans,
        grapheme_codepoints_out,
        max_grapheme_codepoints,
        grapheme_codepoints_written,
    );
}

fn fillRowCells(
    handle: ?*TerminalHandle,
    row_idx: u32,
    cells_out: ?[*]GhosttyTerminalCellInfo,
    max_cells: u32,
    grapheme_spans_out: ?[*]GhosttyTerminalGraphemeSpan,
    max_spans: u32,
    grapheme_codepoints_out: ?[*]u32,
    max_grapheme_codepoints: u32,
    grapheme_codepoints_written: ?*u32,
) u32 {
    const h = handle orelse return 0;
    const out = cells_out orelse return 0;
    if (max_cells == 0) return 0;

    if (grapheme_codepoints_written) |written| {
        written.* = 0;
    }

    const screen = h.terminal.screens.active;
    const cols: u32 = h.terminal.cols;
    const fill_count = @min(max_cells, cols);
    const span_count = @min(max_spans, fill_count);

    // Get the pin for this row in the viewport
    const pin = screen.pages.pin(.{ .viewport = .{
        .x = 0,
        .y = @intCast(row_idx),
    } }) orelse {
        // Row out of range — zero-fill
        for (0..fill_count) |i| {
            out[i] = .{};
        }
        if (grapheme_spans_out) |span_out| {
            for (0..span_count) |i| {
                span_out[i] = .{};
            }
        }
        return fill_count;
    };

    const page_data = &pin.node.data;
    const row = pin.rowAndCell().row;
    const cells = page_data.getCells(row);
    const palette = &h.terminal.colors.palette.current;
    var grapheme_write_idx: u32 = 0;

    for (0..fill_count) |col| {
        const cell = &cells[col];
        var info: GhosttyTerminalCellInfo = .{};
        var span: GhosttyTerminalGraphemeSpan = .{
            .offset = grapheme_write_idx,
            .length = 0,
        };

        // Extract codepoint
        info.codepoint = cell.codepoint();
        // Default colors for unstyled cells.
        info.fg_color = h.default_fg;
        info.bg_color = h.default_bg;

        // Extract style (colors + attributes)
        // style_id 0 is the default style (no custom colors/attributes)
        if (cell.style_id != 0) {
            const s = page_data.styles.get(page_data.memory, cell.style_id);
            info.attrs = packAttributes(s.*);
            info.fg_color = packColor(s.fg_color, true, palette, s.flags.bold, h.default_fg, h.default_bg);
            info.bg_color = packColor(s.bg_color, false, palette, false, h.default_fg, h.default_bg);
        }

        // Optional grapheme export: write trailing grapheme codepoints
        // (excluding the first codepoint in `info.codepoint`).
        if (cell.hasGrapheme() and grapheme_codepoints_out != null) {
            if (page_data.lookupGrapheme(cell)) |trailing| {
                const trailing_len_u32: u32 = @intCast(trailing.len);
                if (trailing_len_u32 > 0 and
                    trailing_len_u32 <= max_grapheme_codepoints -| grapheme_write_idx)
                {
                    const dst = grapheme_codepoints_out.?[grapheme_write_idx .. grapheme_write_idx + trailing_len_u32];
                    for (trailing, 0..) |cp, i| {
                        dst[i] = @intCast(cp);
                    }
                    span.length = trailing_len_u32;
                    grapheme_write_idx += trailing_len_u32;
                }
            }
        }

        // Wide char flags
        if (cell.wide == .wide) {
            info.attrs |= (1 << 16);
        } else if (cell.wide == .spacer_tail) {
            info.attrs |= (1 << 17);
        }

        out[col] = info;
        if (grapheme_spans_out != null and col < span_count) {
            grapheme_spans_out.?[col] = span;
        }
    }

    if (grapheme_codepoints_written) |written| {
        written.* = grapheme_write_idx;
    }

    return fill_count;
}

/// Sets the default foreground and background colors used when a cell
/// has no explicit style.  Colors are 0xAARRGGBB.
export fn ghostty_terminal_set_default_colors(handle: ?*TerminalHandle, fg: u32, bg: u32) void {
    const h = handle orelse return;
    h.default_fg = fg;
    h.default_bg = bg;
}

/// Overrides a single entry in the 256-color palette.
export fn ghostty_terminal_set_palette_color(handle: ?*TerminalHandle, idx: u8, r: u8, g: u8, b: u8) void {
    const h = handle orelse return;
    h.terminal.colors.palette.set(idx, .{ .r = r, .g = g, .b = b });
}

/// Resizes the terminal grid.
export fn ghostty_terminal_resize(handle: ?*TerminalHandle, cols: u32, rows: u32) void {
    const h = handle orelse return;
    if (cols == 0 or rows == 0) return;
    h.terminal.resize(h.alloc, @intCast(cols), @intCast(rows)) catch {};
}

/// Resizes the terminal grid with pixel dimensions for accurate size reports.
/// The pixel dimensions are used to respond to CSI 14t (text area pixels) and
/// CSI 16t (character cell pixels) queries.
export fn ghostty_terminal_resize_with_pixels(
    handle: ?*TerminalHandle,
    cols: u32,
    rows: u32,
    width_px: u32,
    height_px: u32,
) void {
    const h = handle orelse return;
    if (cols == 0 or rows == 0) return;
    h.width_px = width_px;
    h.height_px = height_px;
    h.stream.handler.width_px = width_px;
    h.stream.handler.height_px = height_px;
    h.terminal.resize(h.alloc, @intCast(cols), @intCast(rows)) catch {};
}

/// Sets a callback for terminal notifications (bell, title change, etc.).
/// Event types:
///   1 = bell (data is null, len is 0)
///   2 = title change (data is UTF-8 title string, len is its length)
export fn ghostty_terminal_set_notification_callback(
    handle: ?*TerminalHandle,
    callback: ?NotificationCallback,
    userdata: ?*anyopaque,
) void {
    const h = handle orelse return;
    h.stream.handler.notification_callback = callback;
    h.stream.handler.notification_userdata = userdata;
}

/// Returns 1 if application cursor key mode (DECCKM) is active.
export fn ghostty_terminal_get_mode_app_cursor(handle: ?*const TerminalHandle) u8 {
    const h = handle orelse return 0;
    return if (h.terminal.modes.get(.cursor_keys)) 1 else 0;
}

/// Returns 1 if application keypad mode is active.
export fn ghostty_terminal_get_mode_app_keypad(handle: ?*const TerminalHandle) u8 {
    const h = handle orelse return 0;
    return if (h.terminal.modes.get(.keypad_keys)) 1 else 0;
}

/// Returns 1 if bracketed paste mode is active.
export fn ghostty_terminal_get_mode_bracketed_paste(handle: ?*const TerminalHandle) u8 {
    const h = handle orelse return 0;
    return if (h.terminal.modes.get(.bracketed_paste)) 1 else 0;
}

/// Returns 1 if the alternate screen buffer is active.
export fn ghostty_terminal_get_mode_alt_screen(handle: ?*const TerminalHandle) u8 {
    const h = handle orelse return 0;
    return if (h.terminal.screens.active_key == .alternate) 1 else 0;
}

/// Self-test: creates a terminal, processes VT data with SGR escape codes,
/// and verifies cells contain only the expected text (no escape code leaks).
/// Returns 0 on success, or a non-zero error code indicating the failure:
///   1 = terminal creation failed
///   2 = cell 0 wrong (expected 'H', got something else)
///   3 = cell 4 wrong (expected 'o', got something else)
///   4 = escape leak detected ('3' or '7' or 'm' in unexpected position)
///   5 = split-sequence test failed
export fn ghostty_terminal_self_test() u32 {
    const alloc = defaultAllocator();

    // Create terminal
    const handle = alloc.create(TerminalHandle) catch return 1;
    handle.* = .{
        .terminal = Terminal.init(alloc, .{
            .cols = 80,
            .rows = 24,
            .max_scrollback = 0,
            .default_modes = .{ .grapheme_cluster = true },
        }) catch {
            alloc.destroy(handle);
            return 1;
        },
        .stream = undefined,
        .alloc = alloc,
    };
    handle.stream = InteractiveStream.initAlloc(alloc, InteractiveHandler.init(&handle.terminal));

    // Test 1: Process complete SGR sequence + text in one chunk
    const test1 = "\x1B[37mHello";
    handle.stream.nextSlice(test1);

    // Read cell contents from row 0
    const screen = handle.terminal.screens.active;
    const pin = screen.pages.pin(.{ .viewport = .{ .x = 0, .y = 0 } }) orelse {
        handle.stream.deinit();
        handle.terminal.deinit(alloc);
        alloc.destroy(handle);
        return 2;
    };
    const row = pin.rowAndCell().row;
    const cells = pin.node.data.getCells(row);

    // Cells should contain H,e,l,l,o — NOT 3,7,m,H,e
    if (cells[0].codepoint() != 'H') {
        handle.stream.deinit();
        handle.terminal.deinit(alloc);
        alloc.destroy(handle);
        return 2;
    }
    if (cells[4].codepoint() != 'o') {
        handle.stream.deinit();
        handle.terminal.deinit(alloc);
        alloc.destroy(handle);
        return 3;
    }
    // Check for escape leak: cells should NOT start with '3' or contain '7','m' before 'H'
    if (cells[0].codepoint() == '3' or cells[0].codepoint() == '[') {
        handle.stream.deinit();
        handle.terminal.deinit(alloc);
        alloc.destroy(handle);
        return 4;
    }

    // Test 2: Split sequence across two chunks
    // Reset cursor to next row
    handle.stream.nextSlice("\r\n");

    // Send ESC [ in first chunk
    handle.stream.nextSlice("\x1B[");
    // Send 31m + text in second chunk
    handle.stream.nextSlice("31mRed");

    // Read row 1
    const pin2 = screen.pages.pin(.{ .viewport = .{ .x = 0, .y = 1 } }) orelse {
        handle.stream.deinit();
        handle.terminal.deinit(alloc);
        alloc.destroy(handle);
        return 5;
    };
    const row2 = pin2.rowAndCell().row;
    const cells2 = pin2.node.data.getCells(row2);

    if (cells2[0].codepoint() != 'R') {
        handle.stream.deinit();
        handle.terminal.deinit(alloc);
        alloc.destroy(handle);
        return 5;
    }

    // Cleanup
    handle.stream.deinit();
    handle.terminal.deinit(alloc);
    alloc.destroy(handle);
    return 0; // Success
}

// ═══════════════════════════════════════════════════════════════════════════
// Internal helpers
// ═══════════════════════════════════════════════════════════════════════════

fn defaultAllocator() std.mem.Allocator {
    if (comptime builtin.link_libc) return std.heap.c_allocator;
    return std.heap.smp_allocator;
}

fn packAttributes(s: Style) u32 {
    var attrs: u32 = 0;
    if (s.flags.bold) attrs |= (1 << 0);
    if (s.flags.italic) attrs |= (1 << 1);
    if (s.flags.faint) attrs |= (1 << 2);
    if (s.flags.inverse) attrs |= (1 << 3);
    if (s.flags.invisible) attrs |= (1 << 4);
    if (s.flags.strikethrough) attrs |= (1 << 5);
    if (s.flags.overline) attrs |= (1 << 6);
    if (s.flags.blink) attrs |= (1 << 7);
    const ul: u32 = @intFromEnum(s.flags.underline);
    attrs |= (ul << 8);
    return attrs;
}

fn packColor(c: Style.Color, is_fg: bool, palette: *const color.Palette, bold_bright: bool, default_fg: u32, default_bg: u32) u32 {
    return switch (c) {
        .none => if (is_fg) default_fg else default_bg,
        .palette => |idx| blk: {
            // Bold foreground: promote standard colors 0-7 to bright 8-15
            const resolved_idx = if (bold_bright and is_fg and idx < 8) idx + 8 else idx;
            const rgb = palette[resolved_idx];
            break :blk 0xFF000000 | (@as(u32, rgb.r) << 16) | (@as(u32, rgb.g) << 8) | @as(u32, rgb.b);
        },
        .rgb => |rgb| 0xFF000000 | (@as(u32, rgb.r) << 16) | (@as(u32, rgb.g) << 8) | @as(u32, rgb.b),
    };
}
