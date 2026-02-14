// GhosttySharp Renderer C API Library
//
// Phase 2 prototype:
//   - API surface for external render targets
//   - Descriptor validation
//   - macOS Metal texture write prototype (external MTLTexture)
//   - CPU RGBA fallback path

const std = @import("std");
const builtin = @import("builtin");

pub const GhosttyGpuBackend = enum(c_int) {
    unknown = 0,
    software = 1,
    metal = 2,
    vulkan = 3,
    d3d11 = 4,
    d3d12 = 5,
    opengl = 6,
};

pub const GhosttyRenderTargetKind = enum(c_int) {
    unknown = 0,
    texture_2d = 1,
    framebuffer = 2,
};

pub const GhosttyRenderPixelFormat = enum(c_int) {
    unknown = 0,
    bgra8_unorm = 1,
    bgra8_srgb = 2,
    rgba8_unorm = 3,
    rgba8_srgb = 4,
    rgba16_float = 5,
};

pub const GhosttyRenderResult = enum(c_int) {
    ok = 0,
    invalid_argument = 1,
    unsupported_backend = 2,
    unsupported_platform = 3,
    invalid_target = 4,
    render_failed = 5,
    out_of_memory = 6,
};

pub const GhosttyRenderTargetDesc = extern struct {
    backend: GhosttyGpuBackend,
    target_kind: GhosttyRenderTargetKind,
    pixel_format: GhosttyRenderPixelFormat,

    width: i32,
    height: i32,
    sample_count: u32,

    device_handle: ?*anyopaque,
    context_handle: ?*anyopaque,
    command_queue_handle: ?*anyopaque,
    command_buffer_handle: ?*anyopaque,
    target_handle: ?*anyopaque,
    target_view_handle: ?*anyopaque,

    frame_id: u64,
    debug_name_utf8: ?[*:0]const u8,
};

const RenderContext = struct {
    allocator: std.mem.Allocator,
};

const RenderSurface = struct {
    allocator: std.mem.Allocator,
    backend: GhosttyGpuBackend,
    width: i32 = 1,
    height: i32 = 1,
    scale_x: f64 = 1.0,
    scale_y: f64 = 1.0,
    focused: bool = true,
    color_scheme: u32 = 0,
    frame_counter: u64 = 0,
    frame_in_progress: bool = false,
    frame_token: u64 = 0,
    scratch: []u8 = &.{},

    fn deinit(self: *RenderSurface) void {
        if (self.scratch.len > 0) {
            self.allocator.free(self.scratch);
            self.scratch = &.{};
        }
    }
};

const msg_ok: [*:0]const u8 = "ok";
const msg_invalid_argument: [*:0]const u8 = "invalid argument";
const msg_unsupported_backend: [*:0]const u8 = "unsupported backend";
const msg_unsupported_platform: [*:0]const u8 = "unsupported platform";
const msg_invalid_target: [*:0]const u8 = "invalid target";
const msg_render_failed: [*:0]const u8 = "render failed";
const msg_out_of_memory: [*:0]const u8 = "out of memory";

fn toCode(result: GhosttyRenderResult) c_int {
    return @intFromEnum(result);
}

fn setError(out_error_utf8: ?*?[*:0]const u8, message: ?[*:0]const u8) void {
    if (out_error_utf8) |out| {
        out.* = message;
    }
}

fn checkedMul(a: usize, b: usize) ?usize {
    return std.math.mul(usize, a, b) catch null;
}

fn ensureScratch(surface: *RenderSurface, required_len: usize) GhosttyRenderResult {
    if (surface.scratch.len >= required_len) {
        return .ok;
    }

    if (surface.scratch.len > 0) {
        surface.allocator.free(surface.scratch);
        surface.scratch = &.{};
    }

    surface.scratch = surface.allocator.alloc(u8, required_len) catch return .out_of_memory;
    return .ok;
}

fn fillSolidTarget(
    bytes: []u8,
    width: usize,
    height: usize,
    format: GhosttyRenderPixelFormat,
    color_scheme: u32,
) void {
    const is_light = color_scheme == 0;
    const r: u8 = if (is_light) 0xF5 else 0x1E;
    const g: u8 = if (is_light) 0xF5 else 0x1E;
    const b: u8 = if (is_light) 0xF5 else 0x1E;

    var idx: usize = 0;
    for (0..height) |_| {
        for (0..width) |_| {
            switch (format) {
                .rgba8_unorm, .rgba8_srgb => {
                    bytes[idx + 0] = r;
                    bytes[idx + 1] = g;
                    bytes[idx + 2] = b;
                    bytes[idx + 3] = 0xFF;
                },
                else => {
                    bytes[idx + 0] = b;
                    bytes[idx + 1] = g;
                    bytes[idx + 2] = r;
                    bytes[idx + 3] = 0xFF;
                },
            }

            idx += 4;
        }
    }
}

fn isMetalPrototypePixelFormatSupported(pixel_format: GhosttyRenderPixelFormat) bool {
    return switch (pixel_format) {
        .bgra8_unorm, .bgra8_srgb, .rgba8_unorm, .rgba8_srgb => true,
        else => false,
    };
}

fn validateTarget(
    surface: *const RenderSurface,
    target: *const GhosttyRenderTargetDesc,
    out_error_utf8: ?*?[*:0]const u8,
) GhosttyRenderResult {
    setError(out_error_utf8, null);

    if (target.backend == .unknown) {
        setError(out_error_utf8, "backend must be specified");
        return .invalid_target;
    }

    if (target.backend != surface.backend) {
        setError(out_error_utf8, "target backend must match surface backend");
        return .invalid_target;
    }

    if (target.target_kind == .unknown) {
        setError(out_error_utf8, "target kind must be specified");
        return .invalid_target;
    }

    if (target.width <= 0 or target.height <= 0) {
        setError(out_error_utf8, "target dimensions must be positive");
        return .invalid_target;
    }

    if (target.sample_count == 0) {
        setError(out_error_utf8, "sample count must be greater than zero");
        return .invalid_target;
    }

    const has_target_handle = if (target.backend == .opengl and target.target_kind == .framebuffer)
        true // OpenGL default framebuffer (0) is valid.
    else
        target.target_handle != null;

    if (!has_target_handle) {
        setError(out_error_utf8, "target handle must be provided");
        return .invalid_target;
    }

    if (target.target_kind == .texture_2d and target.pixel_format == .unknown) {
        setError(out_error_utf8, "texture target requires known pixel format");
        return .invalid_target;
    }

    switch (target.backend) {
        .metal => {
            if (target.device_handle == null) {
                setError(out_error_utf8, "backend requires a valid device handle");
                return .invalid_target;
            }
        },
        .vulkan => {
            if (target.device_handle == null) {
                setError(out_error_utf8, "Vulkan backend requires a valid device handle");
                return .invalid_target;
            }

            if (target.command_queue_handle == null) {
                setError(out_error_utf8, "Vulkan backend requires a valid command queue handle");
                return .invalid_target;
            }

            if (target.target_kind == .texture_2d and target.target_view_handle == null) {
                setError(out_error_utf8, "Vulkan texture targets require a valid image-view handle");
                return .invalid_target;
            }
        },
        .d3d11 => {
            if (target.device_handle == null) {
                setError(out_error_utf8, "D3D11 backend requires a valid device handle");
                return .invalid_target;
            }

            if (target.target_kind == .texture_2d and target.target_view_handle == null) {
                setError(out_error_utf8, "D3D11 texture targets require a valid render-target view handle");
                return .invalid_target;
            }
        },
        .d3d12 => {
            if (target.device_handle == null) {
                setError(out_error_utf8, "D3D12 backend requires a valid device handle");
                return .invalid_target;
            }

            if (target.command_queue_handle == null) {
                setError(out_error_utf8, "D3D12 backend requires a valid command queue handle");
                return .invalid_target;
            }

            if (target.command_buffer_handle == null) {
                setError(out_error_utf8, "D3D12 backend requires a valid command-list handle");
                return .invalid_target;
            }

            if (target.target_kind == .texture_2d and target.target_view_handle == null) {
                setError(out_error_utf8, "D3D12 texture targets require a valid render-target view handle");
                return .invalid_target;
            }
        },
        .opengl => {
            if (target.context_handle == null) {
                setError(out_error_utf8, "OpenGL backend requires a valid context handle");
                return .invalid_target;
            }
        },
        .software => {},
        else => {},
    }

    switch (target.backend) {
        .metal => {
            if (target.target_kind != .texture_2d) {
                setError(out_error_utf8, "metal prototype supports texture_2d targets only");
                return .invalid_target;
            }

            if (!isMetalPrototypePixelFormatSupported(target.pixel_format)) {
                setError(out_error_utf8, "metal prototype supports only 8-bit RGBA/BGRA formats");
                return .invalid_target;
            }

            if (builtin.os.tag != .macos) {
                setError(out_error_utf8, "metal prototype is only available on macOS");
                return .unsupported_platform;
            }
        },
        .software => {},
        else => {
            setError(out_error_utf8, "backend is not implemented in this prototype");
            return .unsupported_backend;
        },
    }

    return .ok;
}

const mac = if (builtin.os.tag == .macos) struct {
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

    const sel_register_name = @extern(
        *const fn ([*:0]const u8) callconv(.c) ?*anyopaque,
        .{ .name = "sel_registerName" },
    );

    const objc_msg_send_replace_region = @extern(
        *const fn (
            ?*anyopaque,
            ?*anyopaque,
            MTLRegion,
            usize,
            [*]const u8,
            usize,
        ) callconv(.c) void,
        .{ .name = "objc_msgSend" },
    );

    fn replaceTexture(
        texture: ?*anyopaque,
        bytes: []const u8,
        width: usize,
        height: usize,
        bytes_per_row: usize,
    ) bool {
        if (texture == null) {
            return false;
        }

        const selector = sel_register_name("replaceRegion:mipmapLevel:withBytes:bytesPerRow:") orelse return false;

        const region = MTLRegion{
            .origin = .{ .x = 0, .y = 0, .z = 0 },
            .size = .{ .width = width, .height = height, .depth = 1 },
        };

        objc_msg_send_replace_region(texture, selector, region, 0, bytes.ptr, bytes_per_row);
        return true;
    }
} else struct {};

fn renderMetalToTarget(
    surface: *RenderSurface,
    target: *const GhosttyRenderTargetDesc,
    out_sync_token: ?*u64,
) GhosttyRenderResult {
    if (builtin.os.tag != .macos) {
        return .unsupported_platform;
    }

    const width: usize = @intCast(target.width);
    const height: usize = @intCast(target.height);

    const bytes_per_row = checkedMul(width, 4) orelse return .invalid_target;
    const bytes_len = checkedMul(bytes_per_row, height) orelse return .invalid_target;

    const scratch_result = ensureScratch(surface, bytes_len);
    if (scratch_result != .ok) {
        return scratch_result;
    }

    const buffer = surface.scratch[0..bytes_len];
    fillSolidTarget(buffer, width, height, target.pixel_format, surface.color_scheme);

    const wrote = mac.replaceTexture(target.target_handle, buffer, width, height, bytes_per_row);
    if (!wrote) {
        return .render_failed;
    }

    if (out_sync_token) |sync| {
        const token = if (surface.frame_in_progress) surface.frame_token else surface.frame_counter;
        sync.* = if (target.frame_id != 0) target.frame_id else token;
    }

    return .ok;
}

fn fillRgbaFallback(
    dst_rgba: []u8,
    width: usize,
    height: usize,
    stride: usize,
    color_scheme: u32,
) void {
    const is_light = color_scheme == 0;
    const r: u8 = if (is_light) 0xF5 else 0x1E;
    const g: u8 = if (is_light) 0xF5 else 0x1E;
    const b: u8 = if (is_light) 0xF5 else 0x1E;

    for (0..height) |y| {
        const row = dst_rgba[y * stride .. y * stride + stride];
        for (0..width) |x| {
            const base = x * 4;
            row[base + 0] = r;
            row[base + 1] = g;
            row[base + 2] = b;
            row[base + 3] = 0xFF;
        }
    }
}

pub export fn ghostty_render_context_new() ?*RenderContext {
    const allocator = std.heap.page_allocator;
    const context = allocator.create(RenderContext) catch return null;
    context.* = .{ .allocator = allocator };
    return context;
}

pub export fn ghostty_render_context_free(context: ?*RenderContext) void {
    const ctx = context orelse return;
    ctx.allocator.destroy(ctx);
}

pub export fn ghostty_render_surface_new(
    context: ?*RenderContext,
    backend: GhosttyGpuBackend,
) ?*RenderSurface {
    const ctx = context orelse return null;
    if (backend == .unknown) {
        return null;
    }

    const surface = ctx.allocator.create(RenderSurface) catch return null;
    surface.* = .{
        .allocator = ctx.allocator,
        .backend = backend,
    };

    return surface;
}

pub export fn ghostty_render_surface_free(surface: ?*RenderSurface) void {
    const s = surface orelse return;
    s.deinit();
    s.allocator.destroy(s);
}

pub export fn ghostty_render_surface_set_size(
    surface: ?*RenderSurface,
    width: i32,
    height: i32,
) c_int {
    const s = surface orelse return toCode(.invalid_argument);
    if (width <= 0 or height <= 0) {
        return toCode(.invalid_argument);
    }

    s.width = width;
    s.height = height;
    return toCode(.ok);
}

pub export fn ghostty_render_surface_set_scale(
    surface: ?*RenderSurface,
    scale_x: f64,
    scale_y: f64,
) c_int {
    const s = surface orelse return toCode(.invalid_argument);
    if (scale_x <= 0 or scale_y <= 0) {
        return toCode(.invalid_argument);
    }

    if (!std.math.isFinite(scale_x) or !std.math.isFinite(scale_y)) {
        return toCode(.invalid_argument);
    }

    s.scale_x = scale_x;
    s.scale_y = scale_y;
    return toCode(.ok);
}

pub export fn ghostty_render_surface_set_focus(
    surface: ?*RenderSurface,
    focused: u8,
) c_int {
    const s = surface orelse return toCode(.invalid_argument);
    s.focused = focused != 0;
    return toCode(.ok);
}

pub export fn ghostty_render_surface_set_color_scheme(
    surface: ?*RenderSurface,
    color_scheme: u32,
) c_int {
    const s = surface orelse return toCode(.invalid_argument);
    s.color_scheme = color_scheme;
    return toCode(.ok);
}

pub export fn ghostty_render_surface_begin_frame(
    surface: ?*RenderSurface,
    out_frame_token: ?*u64,
) c_int {
    const s = surface orelse return toCode(.invalid_argument);
    if (s.frame_in_progress) {
        return toCode(.invalid_argument);
    }

    s.frame_counter +%= 1;
    s.frame_token = s.frame_counter;
    s.frame_in_progress = true;

    if (out_frame_token) |token| {
        token.* = s.frame_token;
    }

    return toCode(.ok);
}

pub export fn ghostty_render_surface_end_frame(
    surface: ?*RenderSurface,
    frame_token: u64,
) c_int {
    const s = surface orelse return toCode(.invalid_argument);
    if (!s.frame_in_progress) {
        return toCode(.invalid_argument);
    }

    if (frame_token != 0 and frame_token != s.frame_token) {
        return toCode(.invalid_argument);
    }

    s.frame_in_progress = false;
    s.frame_token = 0;
    return toCode(.ok);
}

pub export fn ghostty_render_surface_validate_target(
    surface: ?*RenderSurface,
    target: ?*const GhosttyRenderTargetDesc,
    out_error_utf8: ?*?[*:0]const u8,
) c_int {
    const s = surface orelse {
        setError(out_error_utf8, msg_invalid_argument);
        return toCode(.invalid_argument);
    };

    const t = target orelse {
        setError(out_error_utf8, msg_invalid_argument);
        return toCode(.invalid_argument);
    };

    const result = validateTarget(s, t, out_error_utf8);
    return toCode(result);
}

pub export fn ghostty_render_surface_render_to_target(
    surface: ?*RenderSurface,
    target: ?*const GhosttyRenderTargetDesc,
    out_sync_token: ?*u64,
) c_int {
    const s = surface orelse return toCode(.invalid_argument);
    const t = target orelse return toCode(.invalid_argument);

    var error_message: ?[*:0]const u8 = null;
    const valid_result = validateTarget(s, t, &error_message);
    if (valid_result != .ok) {
        return toCode(valid_result);
    }

    var implicit_frame = false;
    if (!s.frame_in_progress) {
        s.frame_counter +%= 1;
        s.frame_token = s.frame_counter;
        s.frame_in_progress = true;
        implicit_frame = true;
    }
    defer {
        if (implicit_frame) {
            s.frame_in_progress = false;
            s.frame_token = 0;
        }
    }

    const result = switch (s.backend) {
        .metal => renderMetalToTarget(s, t, out_sync_token),
        else => .unsupported_backend,
    };

    return toCode(result);
}

pub export fn ghostty_render_surface_render_to_rgba(
    surface: ?*RenderSurface,
    dst_rgba: ?[*]u8,
    dst_len: u32,
    width: i32,
    height: i32,
    stride: i32,
) c_int {
    const s = surface orelse return toCode(.invalid_argument);
    const dst_ptr = dst_rgba orelse return toCode(.invalid_argument);

    if (width <= 0 or height <= 0 or stride <= 0) {
        return toCode(.invalid_argument);
    }

    const w: usize = @intCast(width);
    const h: usize = @intCast(height);
    const row_stride: usize = @intCast(stride);

    const min_stride = checkedMul(w, 4) orelse return toCode(.invalid_argument);
    if (row_stride < min_stride) {
        return toCode(.invalid_argument);
    }

    const required = checkedMul(row_stride, h) orelse return toCode(.invalid_argument);
    if (required > dst_len) {
        return toCode(.invalid_argument);
    }

    const dst = dst_ptr[0..required];
    if (!s.frame_in_progress) {
        s.frame_counter +%= 1;
    }
    fillRgbaFallback(dst, w, h, row_stride, s.color_scheme);

    return toCode(.ok);
}

pub export fn ghostty_render_result_message(result_code: c_int) ?[*:0]const u8 {
    const code: GhosttyRenderResult = std.meta.intToEnum(GhosttyRenderResult, result_code) catch return "unknown result";

    return switch (code) {
        .ok => msg_ok,
        .invalid_argument => msg_invalid_argument,
        .unsupported_backend => msg_unsupported_backend,
        .unsupported_platform => msg_unsupported_platform,
        .invalid_target => msg_invalid_target,
        .render_failed => msg_render_failed,
        .out_of_memory => msg_out_of_memory,
    };
}

test "validate target rejects unknown backend" {
    const context = ghostty_render_context_new() orelse return error.OutOfMemory;
    defer ghostty_render_context_free(context);

    const surface = ghostty_render_surface_new(context, .software) orelse return error.OutOfMemory;
    defer ghostty_render_surface_free(surface);

    var target: GhosttyRenderTargetDesc = .{
        .backend = .unknown,
        .target_kind = .framebuffer,
        .pixel_format = .unknown,
        .width = 10,
        .height = 10,
        .sample_count = 1,
        .device_handle = null,
        .context_handle = null,
        .command_queue_handle = null,
        .command_buffer_handle = null,
        .target_handle = @ptrFromInt(0x1),
        .target_view_handle = null,
        .frame_id = 0,
        .debug_name_utf8 = null,
    };

    var error_text: ?[*:0]const u8 = null;
    const rc = ghostty_render_surface_validate_target(surface, &target, &error_text);

    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_target), rc);
    try std.testing.expect(error_text != null);
}

test "rgba fallback writes non-empty output" {
    const context = ghostty_render_context_new() orelse return error.OutOfMemory;
    defer ghostty_render_context_free(context);

    const surface = ghostty_render_surface_new(context, .software) orelse return error.OutOfMemory;
    defer ghostty_render_surface_free(surface);

    var buffer: [4 * 4 * 4]u8 = [_]u8{0} ** (4 * 4 * 4);

    const rc = ghostty_render_surface_render_to_rgba(
        surface,
        &buffer,
        buffer.len,
        4,
        4,
        16,
    );

    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.ok), rc);

    var any_non_zero = false;
    for (buffer) |byte| {
        if (byte != 0) {
            any_non_zero = true;
            break;
        }
    }

    try std.testing.expect(any_non_zero);
}

test "validate target enforces device/context handle invariants before backend support check" {
    const context = ghostty_render_context_new() orelse return error.OutOfMemory;
    defer ghostty_render_context_free(context);

    const vulkan_surface = ghostty_render_surface_new(context, .vulkan) orelse return error.OutOfMemory;
    defer ghostty_render_surface_free(vulkan_surface);

    var vulkan_target: GhosttyRenderTargetDesc = .{
        .backend = .vulkan,
        .target_kind = .framebuffer,
        .pixel_format = .unknown,
        .width = 64,
        .height = 64,
        .sample_count = 1,
        .device_handle = null,
        .context_handle = null,
        .command_queue_handle = null,
        .command_buffer_handle = null,
        .target_handle = @ptrFromInt(0x1),
        .target_view_handle = null,
        .frame_id = 0,
        .debug_name_utf8 = null,
    };

    var error_text: ?[*:0]const u8 = null;
    const vulkan_rc = ghostty_render_surface_validate_target(vulkan_surface, &vulkan_target, &error_text);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_target), vulkan_rc);
    try std.testing.expect(error_text != null);

    const opengl_surface = ghostty_render_surface_new(context, .opengl) orelse return error.OutOfMemory;
    defer ghostty_render_surface_free(opengl_surface);

    var opengl_target: GhosttyRenderTargetDesc = .{
        .backend = .opengl,
        .target_kind = .framebuffer,
        .pixel_format = .unknown,
        .width = 64,
        .height = 64,
        .sample_count = 1,
        .device_handle = @ptrFromInt(0x1),
        .context_handle = null,
        .command_queue_handle = null,
        .command_buffer_handle = null,
        .target_handle = null,
        .target_view_handle = null,
        .frame_id = 0,
        .debug_name_utf8 = null,
    };

    error_text = null;
    const opengl_rc = ghostty_render_surface_validate_target(opengl_surface, &opengl_target, &error_text);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_target), opengl_rc);
    try std.testing.expect(error_text != null);
}

test "validate target enforces backend-specific Vulkan and D3D handle invariants" {
    const context = ghostty_render_context_new() orelse return error.OutOfMemory;
    defer ghostty_render_context_free(context);

    const vulkan_surface = ghostty_render_surface_new(context, .vulkan) orelse return error.OutOfMemory;
    defer ghostty_render_surface_free(vulkan_surface);

    var vulkan_target: GhosttyRenderTargetDesc = .{
        .backend = .vulkan,
        .target_kind = .texture_2d,
        .pixel_format = .bgra8_unorm,
        .width = 32,
        .height = 32,
        .sample_count = 1,
        .device_handle = @ptrFromInt(0x1),
        .context_handle = null,
        .command_queue_handle = null,
        .command_buffer_handle = null,
        .target_handle = @ptrFromInt(0x2),
        .target_view_handle = @ptrFromInt(0x3),
        .frame_id = 0,
        .debug_name_utf8 = null,
    };

    var error_text: ?[*:0]const u8 = null;
    const vulkan_queue_rc = ghostty_render_surface_validate_target(vulkan_surface, &vulkan_target, &error_text);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_target), vulkan_queue_rc);
    try std.testing.expect(error_text != null);

    vulkan_target.command_queue_handle = @ptrFromInt(0x4);
    vulkan_target.target_view_handle = null;
    error_text = null;
    const vulkan_view_rc = ghostty_render_surface_validate_target(vulkan_surface, &vulkan_target, &error_text);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_target), vulkan_view_rc);
    try std.testing.expect(error_text != null);

    const d3d11_surface = ghostty_render_surface_new(context, .d3d11) orelse return error.OutOfMemory;
    defer ghostty_render_surface_free(d3d11_surface);

    var d3d11_target: GhosttyRenderTargetDesc = .{
        .backend = .d3d11,
        .target_kind = .texture_2d,
        .pixel_format = .bgra8_unorm,
        .width = 32,
        .height = 32,
        .sample_count = 1,
        .device_handle = @ptrFromInt(0x1),
        .context_handle = null,
        .command_queue_handle = null,
        .command_buffer_handle = null,
        .target_handle = @ptrFromInt(0x2),
        .target_view_handle = null,
        .frame_id = 0,
        .debug_name_utf8 = null,
    };

    error_text = null;
    const d3d11_rc = ghostty_render_surface_validate_target(d3d11_surface, &d3d11_target, &error_text);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_target), d3d11_rc);
    try std.testing.expect(error_text != null);

    const d3d12_surface = ghostty_render_surface_new(context, .d3d12) orelse return error.OutOfMemory;
    defer ghostty_render_surface_free(d3d12_surface);

    var d3d12_target: GhosttyRenderTargetDesc = .{
        .backend = .d3d12,
        .target_kind = .texture_2d,
        .pixel_format = .bgra8_unorm,
        .width = 32,
        .height = 32,
        .sample_count = 1,
        .device_handle = @ptrFromInt(0x1),
        .context_handle = null,
        .command_queue_handle = @ptrFromInt(0x2),
        .command_buffer_handle = null,
        .target_handle = @ptrFromInt(0x3),
        .target_view_handle = @ptrFromInt(0x4),
        .frame_id = 0,
        .debug_name_utf8 = null,
    };

    error_text = null;
    const d3d12_rc = ghostty_render_surface_validate_target(d3d12_surface, &d3d12_target, &error_text);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_target), d3d12_rc);
    try std.testing.expect(error_text != null);
}

test "validate target rejects unsupported metal target kind and pixel format for prototype" {
    const context = ghostty_render_context_new() orelse return error.OutOfMemory;
    defer ghostty_render_context_free(context);

    const surface = ghostty_render_surface_new(context, .metal) orelse return error.OutOfMemory;
    defer ghostty_render_surface_free(surface);

    var framebuffer_target: GhosttyRenderTargetDesc = .{
        .backend = .metal,
        .target_kind = .framebuffer,
        .pixel_format = .unknown,
        .width = 32,
        .height = 32,
        .sample_count = 1,
        .device_handle = @ptrFromInt(0x1),
        .context_handle = null,
        .command_queue_handle = null,
        .command_buffer_handle = null,
        .target_handle = @ptrFromInt(0x2),
        .target_view_handle = null,
        .frame_id = 0,
        .debug_name_utf8 = null,
    };

    var error_text: ?[*:0]const u8 = null;
    const framebuffer_rc = ghostty_render_surface_validate_target(surface, &framebuffer_target, &error_text);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_target), framebuffer_rc);
    try std.testing.expect(error_text != null);

    var rgba16_target: GhosttyRenderTargetDesc = .{
        .backend = .metal,
        .target_kind = .texture_2d,
        .pixel_format = .rgba16_float,
        .width = 32,
        .height = 32,
        .sample_count = 1,
        .device_handle = @ptrFromInt(0x1),
        .context_handle = null,
        .command_queue_handle = null,
        .command_buffer_handle = null,
        .target_handle = @ptrFromInt(0x2),
        .target_view_handle = null,
        .frame_id = 0,
        .debug_name_utf8 = null,
    };

    error_text = null;
    const rgba16_rc = ghostty_render_surface_validate_target(surface, &rgba16_target, &error_text);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_target), rgba16_rc);
    try std.testing.expect(error_text != null);
}

test "frame lifecycle APIs enforce begin/end ordering" {
    const context = ghostty_render_context_new() orelse return error.OutOfMemory;
    defer ghostty_render_context_free(context);

    const surface = ghostty_render_surface_new(context, .software) orelse return error.OutOfMemory;
    defer ghostty_render_surface_free(surface);

    var token: u64 = 0;
    const begin_rc = ghostty_render_surface_begin_frame(surface, &token);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.ok), begin_rc);
    try std.testing.expect(token != 0);

    const second_begin_rc = ghostty_render_surface_begin_frame(surface, null);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_argument), second_begin_rc);

    const bad_end_rc = ghostty_render_surface_end_frame(surface, token + 1);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_argument), bad_end_rc);

    const end_rc = ghostty_render_surface_end_frame(surface, token);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.ok), end_rc);

    const second_end_rc = ghostty_render_surface_end_frame(surface, token);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_argument), second_end_rc);
}

test "set_scale rejects non-finite values" {
    const context = ghostty_render_context_new() orelse return error.OutOfMemory;
    defer ghostty_render_context_free(context);

    const surface = ghostty_render_surface_new(context, .software) orelse return error.OutOfMemory;
    defer ghostty_render_surface_free(surface);

    const nan_rc = ghostty_render_surface_set_scale(surface, std.math.nan(f64), 1.0);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_argument), nan_rc);

    const inf_rc = ghostty_render_surface_set_scale(surface, std.math.inf(f64), 1.0);
    try std.testing.expectEqual(@intFromEnum(GhosttyRenderResult.invalid_argument), inf_rc);
}
