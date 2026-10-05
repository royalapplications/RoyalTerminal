// Appended to upstream lib_vt; no public upstream options or ABI enums change.
export fn ghostty_royal_window_resize_callback(
    handle: @import("terminal/c/terminal.zig").Terminal,
    userdata: ?*anyopaque,
    callback: ?*const fn (@import("terminal/c/terminal.zig").Terminal, ?*anyopaque, u16, u16) callconv(.c) void,
) callconv(.c) c_int {
    const wrapper = handle orelse return -2;
    wrapper.effects.royal_window_resize = callback;
    wrapper.effects.royal_window_resize_userdata = if (callback != null) userdata else null;
    return 0;
}
