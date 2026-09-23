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

const RoyalKittyPlacementMetadata = extern struct {
    size: usize = @sizeOf(RoyalKittyPlacementMetadata),
    image_id: u32 = 0,
    placement_id: u32 = 0,
    flags: u32 = 0,
    root_image_id: u32 = 0,
    root_placement_id: u32 = 0,
    root_internal: u32 = 0,
    horizontal_offset: i32 = 0,
    vertical_offset: i32 = 0,
};

// Preserve the exact key namespace and resolve relative chains upstream. No
// allocation or mutation; the iterator and storage must belong to one terminal.
export fn ghostty_royal_kitty_graphics_placement_metadata(
    graphics: ?*anyopaque,
    iterator: @import("terminal/c/kitty_graphics.zig").PlacementIterator,
    output: ?*RoyalKittyPlacementMetadata,
) callconv(.c) c_int {
    const result = output orelse return -2;
    if (result.size < @sizeOf(RoyalKittyPlacementMetadata)) return -2;
    const handle = graphics orelse return -2;
    if (comptime terminal.options.kitty_graphics) {
        const it = iterator orelse return -2;
        const entry = it.entry orelse return -2;
        const storage: *kitty.graphics.ImageStorage = @ptrCast(@alignCast(handle));
        result.* = .{
            .image_id = entry.key_ptr.image_id,
            .placement_id = entry.key_ptr.placement_id.id,
            .flags = if (entry.key_ptr.placement_id.tag == .internal) @as(u32, 1) else 0,
        };
        switch (entry.value_ptr.location) {
            .pin => {},
            .virtual => result.flags |= 2,
            .relative => |rel| {
                if (storage.resolveChain(rel)) |chain| {
                    if (chain.root.location == .virtual) {
                        result.flags |= 4;
                        result.root_image_id = chain.root_key.image_id;
                        result.root_placement_id = chain.root_key.placement_id.id;
                        result.root_internal = if (chain.root_key.placement_id.tag == .internal) 1 else 0;
                        result.horizontal_offset = chain.horizontal_offset;
                        result.vertical_offset = chain.vertical_offset;
                    }
                }
            },
        }
        return 0;
    }
    return -4;
}
