// Appended to upstream src/lib_vt.zig by build.zig. Names are deliberately
// prefixed to keep these extensions separate from Ghostty's public API.
const RoyalMouseState = extern struct {
    size: usize = @sizeOf(RoyalMouseState),
    tracking: u32 = 0,
    format: u32 = 0,
    shift_capture: u32 = 0,
};

export fn ghostty_royal_modify_other_keys_2(
    handle: @import("terminal/c/terminal.zig").Terminal,
    output: ?*u8,
) callconv(.c) c_int {
    const result = output orelse return -2;
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    result.* = @intFromBool(t.flags.modify_other_keys_2);
    return 0;
}

// These are the same effective flags consumed by mouse_encode.setopt_from_terminal,
// not the independent DEC mode bits (which can disagree after mixed resets).
export fn ghostty_royal_mouse_state(
    handle: @import("terminal/c/terminal.zig").Terminal,
    output: ?*RoyalMouseState,
) callconv(.c) c_int {
    const result = output orelse return -2;
    if (result.size < @sizeOf(RoyalMouseState)) return -2;
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    result.* = .{
        .tracking = @intCast(@intFromEnum(t.flags.mouse_event)),
        .format = @intCast(@intFromEnum(t.flags.mouse_format)),
        .shift_capture = @intCast(@intFromEnum(t.flags.mouse_shift_capture)),
    };
    return 0;
}

export fn ghostty_royal_mouse_shift_capture_set(
    handle: @import("terminal/c/terminal.zig").Terminal,
    value: u32,
) callconv(.c) c_int {
    if (value > 2) return -2;
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    t.flags.mouse_shift_capture = @enumFromInt(value);
    return 0;
}

const RoyalPromptState = extern struct {
    size: usize = @sizeOf(RoyalPromptState),
    seen: u32 = 0,
    content: u32 = 0,
    clear_eol: u32 = 0,
    click: u32 = 0,
    redraw: u32 = 0,
    implicit_id: u32 = 0,
};

export fn ghostty_royal_prompt_state(
    handle: @import("terminal/c/terminal.zig").Terminal,
    output: ?*RoyalPromptState,
) callconv(.c) c_int {
    const result = output orelse return -2;
    if (result.size < @sizeOf(RoyalPromptState)) return -2;
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    const s = t.screens.active;
    result.* = .{
        .seen = @intFromBool(s.semantic_prompt.seen),
        .content = @intFromEnum(s.cursor.semantic_content),
        .clear_eol = @intFromBool(s.cursor.semantic_content_clear_eol),
        .click = switch (s.semantic_prompt.click) {
            .none => 0,
            .click_events => |v| 1 + @as(u32, @intCast(@intFromEnum(v))),
            .cl => |v| 3 + @as(u32, @intCast(@intFromEnum(v))),
        },
        .redraw = @intFromEnum(t.flags.shell_redraws_prompt),
        .implicit_id = s.cursor.hyperlink_implicit_id,
    };
    return 0;
}

const RoyalHyperlinkMetadata = extern struct {
    size: usize = @sizeOf(RoyalHyperlinkMetadata),
    uri_len: usize = 0,
    id_len: usize = 0,
    implicit_id: u32 = 0,
};

// A borrowed grid reference is consumed within this call; only copied bytes
// escape. Probe and copy must be serialized with mutations of its terminal.
export fn ghostty_royal_grid_ref_hyperlink(
    reference: ?*const @import("terminal/c/grid_ref.zig").CGridRef,
    output: ?*RoyalHyperlinkMetadata,
    uri_buffer: ?[*]u8,
    uri_capacity: usize,
    id_buffer: ?[*]u8,
    id_capacity: usize,
) callconv(.c) c_int {
    const ref = reference orelse return -2;
    if (ref.size < @sizeOf(@import("terminal/c/grid_ref.zig").CGridRef)) return -2;
    const result = output orelse return -2;
    if (result.size < @sizeOf(RoyalHyperlinkMetadata)) return -2;
    const pin = ref.toPin() orelse return -2;
    const p = pin.node.page();
    if (pin.x >= p.size.cols or pin.y >= p.size.rows) return -2;
    const cell = pin.rowAndCell().cell;
    result.* = .{};
    if (!cell.hyperlink) return 0;
    const link_id = p.lookupHyperlink(cell) orelse return 0;
    const link = p.hyperlink_set.get(p.memory, link_id);
    const uri = link.uri.slice(p.memory);
    const id: []const u8 = switch (link.id) {
        .explicit => |v| v.slice(p.memory),
        .implicit => |v| value: {
            result.implicit_id = v;
            break :value &.{};
        },
    };
    result.uri_len = uri.len;
    result.id_len = id.len;
    if (uri_capacity < uri.len or id_capacity < id.len) return -3;
    if ((uri.len > 0 and uri_buffer == null) or (id.len > 0 and id_buffer == null)) return -2;
    if (uri.len > 0) @memcpy(uri_buffer.?[0..uri.len], uri);
    if (id.len > 0) @memcpy(id_buffer.?[0..id.len], id);
    return 0;
}

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

const RoyalGlyphMetadata = extern struct {
    size: usize = @sizeOf(RoyalGlyphMetadata),
    codepoint: u32 = 0,
    units_per_em: u32 = 0,
    advance_width: u32 = 0,
    line_height: u32 = 0,
    width: u32 = 0,
    sizing: u32 = 0,
    horizontal: u32 = 0,
    vertical: u32 = 0,
    contour_count: u32 = 0,
    point_count: u32 = 0,
    pad_top: f64 = 0,
    pad_right: f64 = 0,
    pad_bottom: f64 = 0,
    pad_left: f64 = 0,
};

const RoyalGlyphPoint = extern struct {
    x: i32,
    y: i32,
    on_curve: u32,
};

// Read-only peek: callers must inspect dirty before render-state update clears
// terminal dirty flags. Reset/disable are also detected through the entry count.
export fn ghostty_royal_glyph_glossary_info(
    handle: @import("terminal/c/terminal.zig").Terminal,
    out_count: ?*u32,
    out_dirty: ?*u8,
) callconv(.c) c_int {
    const count = out_count orelse return -2;
    const dirty = out_dirty orelse return -2;
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    count.* = @intCast(t.glyph_glossary.entries.count());
    dirty.* = @intFromBool(t.flags.dirty.glyph_glossary);
    return 0;
}

// Entries are addressed in FIFO order. No borrowed pointer crosses the ABI.
export fn ghostty_royal_glyph_metadata(
    handle: @import("terminal/c/terminal.zig").Terminal,
    index: u32,
    output: ?*RoyalGlyphMetadata,
) callconv(.c) c_int {
    const result = output orelse return -2;
    if (result.size < @sizeOf(RoyalGlyphMetadata)) return -2;
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    const entries = &t.glyph_glossary.entries;
    if (index >= entries.count()) return -4;
    const entry = &entries.values()[index];
    const outline = entry.glyph.glyf;
    result.* = .{
        .codepoint = entries.keys()[index],
        .units_per_em = entry.design.units_per_em,
        .advance_width = entry.design.advance_width,
        .line_height = entry.design.line_height,
        .width = @intFromEnum(entry.width),
        // The glossary stores normalized renderer constraints, not the original
        // request names (height/advance and contain/cover are aliases upstream).
        .sizing = switch (entry.constraint.size) {
            .none => 0,
            .cover => 1,
            .stretch => 2,
            else => return -2,
        },
        .horizontal = switch (entry.constraint.align_horizontal) {
            .start => 0,
            .center => 1,
            .end => 2,
            else => return -2,
        },
        .vertical = switch (entry.constraint.align_vertical) {
            .start => 0,
            .center => 1,
            .end => 2,
            else => return -2,
        },
        .contour_count = @intCast(outline.contours.len),
        .point_count = @intCast(outline.points.len),
        .pad_top = entry.constraint.pad_top,
        .pad_right = entry.constraint.pad_right,
        .pad_bottom = entry.constraint.pad_bottom,
        .pad_left = entry.constraint.pad_left,
    };
    return 0;
}

export fn ghostty_royal_glyph_outline(
    handle: @import("terminal/c/terminal.zig").Terminal,
    index: u32,
    points: ?[*]RoyalGlyphPoint,
    point_capacity: usize,
    contours: ?[*]u16,
    contour_capacity: usize,
) callconv(.c) c_int {
    const t = @import("terminal/c/terminal.zig").zigTerminal(handle) orelse return -2;
    const entries = &t.glyph_glossary.entries;
    if (index >= entries.count()) return -4;
    const outline = entries.values()[index].glyph.glyf;
    if (point_capacity < outline.points.len or contour_capacity < outline.contours.len) return -3;
    if (outline.points.len > 0 and points == null) return -2;
    if (outline.contours.len > 0 and contours == null) return -2;
    // All validation precedes writes, including validation of the second buffer.
    for (outline.points, 0..) |glyph_point, i| points.?[i] = .{
        .x = glyph_point.x,
        .y = glyph_point.y,
        .on_curve = @intFromBool(glyph_point.on_curve),
    };
    if (outline.contours.len > 0) @memcpy(contours.?[0..outline.contours.len], outline.contours);
    return 0;
}
