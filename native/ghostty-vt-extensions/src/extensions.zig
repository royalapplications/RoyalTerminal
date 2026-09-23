// Appended to upstream src/lib_vt.zig by build.zig. Names are deliberately
// prefixed to keep these extensions separate from Ghostty's public API.
export fn ghostty_royal_kitty_graphics_animation_tick(
    graphics: ?*anyopaque,
    now_ms: u64,
    out_delay_ms: ?*u64,
) callconv(.c) c_int {
    const output = out_delay_ms orelse return -2;
    output.* = 0;
    const handle = graphics orelse return -2;
    if (comptime terminal.options.kitty_graphics) {
        const storage: *kitty.graphics.ImageStorage = @ptrCast(@alignCast(handle));
        const delay = storage.animationTick((TinyIo.init).io(), now_ms) orelse return -4;
        output.* = delay;
        return 0;
    }
    return -4;
}
