// Appended to the upstream root. Host hooks for its existing OSC 72 state.
const RoyalDndItem = extern struct {
    mime: ?[*]const u8,
    mime_len: usize,
    data: ?[*]const u8,
    data_len: usize,
};

export fn ghostty_royal_dnd_state(
    handle: @import("terminal/c/terminal.zig").Terminal,
    registered: ?*u8,
    accepted: ?*i32,
) callconv(.c) c_int {
    const r = registered orelse return -2;
    const a = accepted orelse return -2;
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    r.* = @intFromBool(t.kitty_dnd != null);
    a.* = if (t.kitty_dnd) |d| if (d.clientAccepted()) |op| @intFromEnum(op) else -1 else -1;
    return 0;
}

export fn ghostty_royal_dnd_mimes(
    handle: @import("terminal/c/terminal.zig").Terminal,
    output: ?[*]u8,
    capacity: usize,
    length: ?*usize,
) callconv(.c) c_int {
    const len = length orelse return -2;
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    const data = if (t.kitty_dnd) |d| d.drop.registered_mimes.items else &.{};
    len.* = data.len;
    if (capacity < data.len) return -3;
    if (data.len > 0) @memcpy((output orelse return -2)[0..data.len], data);
    return 0;
}

// 1=move, 2=drop, 3=leave, 4=cancel held data, 5=new-session unregister.
// No input pointers escape. A drop is bounded at the embedding boundary; the
// upstream core owns its copies and retains normal registration/reset behavior.
export fn ghostty_royal_dnd_event(
    handle: @import("terminal/c/terminal.zig").Terminal,
    kind: u32,
    column: u32,
    row: u32,
    pixel_x: i32,
    pixel_y: i32,
    operations: u32,
    input: ?[*]const RoyalDndItem,
    count: usize,
    destination: @import("terminal/c/io.zig").Writer,
) callconv(.c) c_int {
    const io = @import("terminal/c/io.zig");
    const dnd = @import("terminal/kitty/dnd.zig");
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    if (kind < 1 or kind > 5 or operations > 3 or count > 16 or !destination.valid()) return -2;
    if (count > 0 and input == null) return -2;
    var items: [16]dnd.Item = undefined;
    var mimes: [16][]const u8 = undefined;
    var bytes: usize = 0;
    for (0..count) |index| {
        const item = input.?[index];
        if (item.mime_len == 0 or item.mime_len > 1024 or item.mime == null) return -2;
        const mime = item.mime.?[0..item.mime_len];
        for (mime) |c| if (c < 33 or c > 126) return -2;
        if (item.data_len > 64 * 1024 * 1024 - bytes) return -2;
        bytes += item.data_len;
        if (item.data_len > 0 and item.data == null) return -2;
        items[index] = .{ .mime = mime, .data = if (item.data_len == 0) &.{} else item.data.?[0..item.data_len] };
        mimes[index] = mime;
    }
    const state = t.kitty_dnd orelse return 0;
    var buffer: [io.WriterAdapter.recommended_buffer_len]u8 = undefined;
    var writer = io.WriterAdapter.initBuffered(destination, &buffer);
    const position: dnd.MoveEvent = .{
        .cell_x = column,
        .cell_y = row,
        .pixel_x = pixel_x,
        .pixel_y = pixel_y,
        .operations = @bitCast(@as(u2, @intCast(operations))),
    };
    switch (kind) {
        1 => state.dragMove(t.gpa(), writer.writer(), position, mimes[0..count]) catch |err| return if (err == error.OutOfMemory) -1 else -5,
        2 => state.dragDrop(t.gpa(), writer.writer(), position, items[0..count]) catch |err| return if (err == error.OutOfMemory) -1 else -5,
        3 => state.dragLeave(t.gpa(), writer.writer()) catch return -5,
        4, 5 => {
            // Host lifecycle events must not be reinterpreted as a continuation
            // of a partially received registration/acceptance from the client.
            const chunking = state.chunking;
            state.chunking = .{};
            defer {
                if (kind == 4) state.chunking = chunking;
            }
            _ = dnd.handleCommand(&t.kitty_dnd, t.gpa(), writer.writer(), .{
                .metadata = if (kind == 4) "t=r" else "t=A",
                .payload = null,
                .terminator = .st,
            }) catch |err| return if (err == error.OutOfMemory) -1 else -5;
        },
        else => unreachable,
    }
    writer.writer().flush() catch return -5;
    return 0;
}
