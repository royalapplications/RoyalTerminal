const std = @import("std");
const builtin = @import("builtin");
const renderer = @import("renderer_capi");

const objc_get_class = @extern(
    *const fn ([*:0]const u8) callconv(.c) ?*anyopaque,
    .{ .name = "objc_getClass" },
);

const sel_register_name = @extern(
    *const fn ([*:0]const u8) callconv(.c) ?*anyopaque,
    .{ .name = "sel_registerName" },
);

const objc_msg_send_texture_descriptor = @extern(
    *const fn (?*anyopaque, ?*anyopaque, usize, usize, usize, u8) callconv(.c) ?*anyopaque,
    .{ .name = "objc_msgSend" },
);

const objc_msg_send_void_uint = @extern(
    *const fn (?*anyopaque, ?*anyopaque, usize) callconv(.c) void,
    .{ .name = "objc_msgSend" },
);

const objc_msg_send_new_texture = @extern(
    *const fn (?*anyopaque, ?*anyopaque, ?*anyopaque) callconv(.c) ?*anyopaque,
    .{ .name = "objc_msgSend" },
);

const objc_msg_send_release = @extern(
    *const fn (?*anyopaque, ?*anyopaque) callconv(.c) void,
    .{ .name = "objc_msgSend" },
);

const objc_msg_send_get_bytes = @extern(
    *const fn (?*anyopaque, ?*anyopaque, [*]u8, usize, MTLRegion, usize) callconv(.c) void,
    .{ .name = "objc_msgSend" },
);

const mtl_create_system_default_device = @extern(
    *const fn () callconv(.c) ?*anyopaque,
    .{ .name = "MTLCreateSystemDefaultDevice" },
);

const MTLOrigin = extern struct {
    x: usize,
    y: usize,
    z: usize,
};

const MTLSize = extern struct {
    width: usize,
    height: usize,
    depth: usize,
};

const MTLRegion = extern struct {
    origin: MTLOrigin,
    size: MTLSize,
};

pub fn main() !void {
    if (builtin.os.tag != .macos) {
        std.debug.print("Skipping Metal sample: current OS is not macOS.\n", .{});
        return;
    }

    const device = mtl_create_system_default_device() orelse {
        std.debug.print("No Metal device available.\n", .{});
        return error.NoMetalDevice;
    };

    const texture_descriptor_class = objc_get_class("MTLTextureDescriptor") orelse return error.MissingTextureDescriptorClass;

    const texture_2d_selector = sel_register_name("texture2DDescriptorWithPixelFormat:width:height:mipmapped:") orelse return error.MissingTextureDescriptorSelector;
    const set_storage_mode_selector = sel_register_name("setStorageMode:") orelse return error.MissingSetStorageModeSelector;
    const new_texture_selector = sel_register_name("newTextureWithDescriptor:") orelse return error.MissingNewTextureSelector;
    const get_bytes_selector = sel_register_name("getBytes:bytesPerRow:fromRegion:mipmapLevel:") orelse return error.MissingGetBytesSelector;
    const release_selector = sel_register_name("release") orelse return error.MissingReleaseSelector;

    const width: usize = 128;
    const height: usize = 96;

    // MTLPixelFormatBGRA8Unorm = 80
    const texture_descriptor = objc_msg_send_texture_descriptor(
        texture_descriptor_class,
        texture_2d_selector,
        80,
        width,
        height,
        0,
    ) orelse return error.CreateTextureDescriptorFailed;

    // MTLStorageModeShared = 0 (prototype expects CPU-readable texture for smoke validation).
    objc_msg_send_void_uint(texture_descriptor, set_storage_mode_selector, 0);

    const texture = objc_msg_send_new_texture(device, new_texture_selector, texture_descriptor) orelse {
        objc_msg_send_release(texture_descriptor, release_selector);
        return error.CreateTextureFailed;
    };

    // Descriptor object is no longer needed after texture creation.
    objc_msg_send_release(texture_descriptor, release_selector);
    defer objc_msg_send_release(texture, release_selector);

    const context = renderer.ghostty_render_context_new() orelse return error.CreateContextFailed;
    defer renderer.ghostty_render_context_free(context);

    const surface = renderer.ghostty_render_surface_new(context, .metal) orelse return error.CreateSurfaceFailed;
    defer renderer.ghostty_render_surface_free(surface);

    const ok = @intFromEnum(renderer.GhosttyRenderResult.ok);
    if (renderer.ghostty_render_surface_set_size(surface, @intCast(width), @intCast(height)) != ok) {
        return error.SetSizeFailed;
    }

    if (renderer.ghostty_render_surface_set_scale(surface, 1.0, 1.0) != ok) {
        return error.SetScaleFailed;
    }

    var target: renderer.GhosttyRenderTargetDesc = .{
        .backend = .metal,
        .target_kind = .texture_2d,
        .pixel_format = .bgra8_unorm,
        .width = @intCast(width),
        .height = @intCast(height),
        .sample_count = 1,
        .device_handle = device,
        .context_handle = null,
        .command_queue_handle = null,
        .command_buffer_handle = null,
        .target_handle = texture,
        .target_view_handle = null,
        .frame_id = 1,
        .debug_name_utf8 = "smoke-target",
    };

    var validation_error: ?[*:0]const u8 = null;
    const validate_rc = renderer.ghostty_render_surface_validate_target(surface, &target, &validation_error);
    if (validate_rc != ok) {
        const text = validation_error orelse renderer.ghostty_render_result_message(validate_rc) orelse "unknown";
        std.debug.print("Validation failed: {s}\n", .{text});
        return error.ValidationFailed;
    }

    var frame_token: u64 = 0;
    const begin_rc = renderer.ghostty_render_surface_begin_frame(surface, &frame_token);
    if (begin_rc != ok) {
        const text = renderer.ghostty_render_result_message(begin_rc) orelse "unknown";
        std.debug.print("Begin frame failed: {s}\n", .{text});
        return error.BeginFrameFailed;
    }

    var sync_token: u64 = 0;
    const render_rc = renderer.ghostty_render_surface_render_to_target(surface, &target, &sync_token);
    if (render_rc != ok) {
        const text = renderer.ghostty_render_result_message(render_rc) orelse "unknown";
        std.debug.print("Render failed: {s}\n", .{text});
        return error.RenderFailed;
    }

    const end_rc = renderer.ghostty_render_surface_end_frame(surface, frame_token);
    if (end_rc != ok) {
        const text = renderer.ghostty_render_result_message(end_rc) orelse "unknown";
        std.debug.print("End frame failed: {s}\n", .{text});
        return error.EndFrameFailed;
    }

    const bytes_per_row = width * 4;
    const read_len = bytes_per_row * height;

    var allocator = std.heap.page_allocator;
    const pixels = try allocator.alloc(u8, read_len);
    defer allocator.free(pixels);

    const region = MTLRegion{
        .origin = .{ .x = 0, .y = 0, .z = 0 },
        .size = .{ .width = width, .height = height, .depth = 1 },
    };

    objc_msg_send_get_bytes(texture, get_bytes_selector, pixels.ptr, bytes_per_row, region, 0);

    var any_non_zero = false;
    for (pixels) |byte| {
        if (byte != 0) {
            any_non_zero = true;
            break;
        }
    }

    if (!any_non_zero) {
        return error.ReadbackWasEmpty;
    }

    std.debug.print(
        "Metal smoke OK: sync_token={d}, first_pixel=({d},{d},{d},{d})\n",
        .{ sync_token, pixels[0], pixels[1], pixels[2], pixels[3] },
    );
}
