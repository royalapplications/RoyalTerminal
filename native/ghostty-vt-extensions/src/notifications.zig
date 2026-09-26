// Appended to upstream lib_vt. All input slices are borrowed for callback duration.
export fn ghostty_royal_notification_callback(
    handle: @import("terminal/c/terminal.zig").Terminal,
    userdata: ?*anyopaque,
    callback: ?*const fn (@import("terminal/c/terminal.zig").Terminal, ?*anyopaque, ?[*]const u8, usize, ?[*]const u8, usize, u8) callconv(.c) void,
) callconv(.c) c_int {
    const wrapper = handle orelse return -2;
    wrapper.effects.royal_notification = callback;
    wrapper.effects.royal_notification_userdata = if (callback != null) userdata else null;
    return 0;
}
