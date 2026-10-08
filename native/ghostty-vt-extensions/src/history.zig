// Additive sized metadata API. No terminal pointers or pins escape this call.
const RoyalHistoryInfo = extern struct {
    size: usize = @sizeOf(RoyalHistoryInfo),
    epoch: u64 = 0,
    origin: u64 = 0,
    screen_generation: u64 = 0,
    total_rows: u64 = 0,
    columns: u16 = 0,
    rows: u16 = 0,
    alternate: u8 = 0,
};

export fn ghostty_royal_history_info(
    handle: @import("terminal/c/terminal.zig").Terminal,
    output: ?*RoyalHistoryInfo,
) callconv(.c) c_int {
    const result = output orelse return -2;
    if (result.size < @sizeOf(RoyalHistoryInfo)) return -2;
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    const pages = &t.screens.active.pages;
    result.* = .{
        .epoch = pages.royal_history_epoch,
        .origin = pages.royal_history_origin,
        .screen_generation = t.screens.generation(t.screens.active_key),
        .total_rows = pages.total_rows,
        .columns = t.cols,
        .rows = t.rows,
        .alternate = @intFromBool(t.screens.active_key == .alternate),
    };
    return 0;
}

// Locate from the nearer end, so a recent-tail capture does not walk old history.
// The returned reference is borrowed only until the next terminal mutation.
export fn ghostty_royal_history_row_ref(
    handle: @import("terminal/c/terminal.zig").Terminal,
    row: u64,
    output: ?*@import("terminal/c/grid_ref.zig").CGridRef,
) callconv(.c) c_int {
    const result = output orelse return -2;
    const GridRef = @import("terminal/c/grid_ref.zig").CGridRef;
    if (result.size < @sizeOf(GridRef)) return -2;
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    const pages = &t.screens.active.pages;
    if (row >= pages.total_rows) return -2;
    var pin = (if (row < pages.total_rows / 2)
        pages.getTopLeft(.screen).down(@intCast(row))
    else
        pages.getBottomRight(.screen).?.up(@intCast(pages.total_rows - 1 - row))) orelse return -2;
    pin.x = 0;
    result.* = GridRef.fromPin(pin);
    return 0;
}

// Measure only up to the remaining UTF-16 budget before allocating scratch.
export fn ghostty_royal_history_grapheme_fits(
    reference: *const @import("terminal/c/grid_ref.zig").CGridRef,
    budget: usize,
) callconv(.c) c_int {
    const pin = reference.toPin() orelse return -2;
    const cell = pin.rowAndCell().cell;
    if (!cell.hasText()) return 0;
    var length: usize = if (cell.codepoint() > 0xFFFF) 2 else 1;
    if (length > budget) return -3;
    if (cell.hasGrapheme()) {
        if (pin.grapheme(cell)) |extra| for (extra) |scalar| {
            const width: usize = if (scalar > 0xFFFF) 2 else 1;
            if (width > budget - length) return -3;
            length += width;
        };
    }
    return 0;
}

test "Royal history sized ABI and borrowed row access" {
    const testing = std.testing;
    const api = @import("terminal/c/terminal.zig");
    var handle: api.Terminal = null;
    try testing.expectEqual(.success, api.new(&@import("terminal/lib.zig").alloc.test_allocator, &handle, 8, 2));
    defer api.free(handle);
    var info: RoyalHistoryInfo = .{};
    try testing.expectEqual(@as(c_int, -2), ghostty_royal_history_info(null, &info));
    info.size = 1;
    try testing.expectEqual(@as(c_int, -2), ghostty_royal_history_info(handle, &info));
    info.size = @sizeOf(RoyalHistoryInfo);
    try testing.expectEqual(@as(c_int, 0), ghostty_royal_history_info(handle, &info));
    try testing.expectEqual(@as(u64, 2), info.total_rows);
    const epoch = info.epoch;
    const text = "one\r\ntwo\r\nthree";
    api.vt_write(handle, text.ptr, text.len);
    try testing.expectEqual(@as(c_int, 0), ghostty_royal_history_info(handle, &info));
    try testing.expectEqual(epoch, info.epoch);
    try testing.expectEqual(@as(u64, 3), info.total_rows);
    var ref: @import("terminal/c/grid_ref.zig").CGridRef = .{ .size = @sizeOf(@import("terminal/c/grid_ref.zig").CGridRef) };
    try testing.expectEqual(@as(c_int, 0), ghostty_royal_history_row_ref(handle, 0, &ref));
    try testing.expectEqual(@as(c_int, 0), ghostty_royal_history_row_ref(handle, 2, &ref));
    try testing.expectEqual(@as(c_int, -2), ghostty_royal_history_row_ref(handle, 3, &ref));
    try testing.expectEqual(.success, api.resize(handle, 4, 2, 0, 0));
    try testing.expectEqual(@as(c_int, 0), ghostty_royal_history_info(handle, &info));
    try testing.expect(info.epoch != epoch);
}
