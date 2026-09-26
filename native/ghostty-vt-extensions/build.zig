//! Build upstream libghostty-vt with RoyalTerminal's narrowly scoped extensions.
//! All code shares one Zig module, allocator and process-wide generation source.
const std = @import("std");

pub fn build(b: *std.Build) !void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});
    const simd = b.option(bool, "simd", "Enable upstream SIMD implementation") orelse true;
    const ghostty = b.dependency("ghostty", .{
        .target = target,
        .optimize = optimize,
        .simd = simd,
        .@"app-runtime" = @as([]const u8, "none"),
        .@"emit-lib-vt" = true,
        .@"emit-xcframework" = false,
    });
    const module = ghostty.module("ghostty-vt-c");

    // Wuffs uses per-function target attributes to emit AVX2 pixel routines
    // even for a baseline module with Ghostty's SIMD bundle disabled. Keep
    // the Windows no-AVX compatibility artifact genuinely instruction-clean;
    // accelerated builds and other platforms retain upstream dispatch.
    if (target.result.os.tag == .windows and target.result.cpu.arch == .x86_64 and
        !std.Target.x86.featureSetHas(target.result.cpu.features, .avx))
    {
        const wuffs = module.import_table.get("wuffs") orelse return error.MissingWuffsModule;
        const wuffs_c = wuffs.import_table.get("wuffs_c") orelse return error.MissingWuffsCModule;
        wuffs_c.addCMacro("WUFFS_CONFIG__AVOID_CPU_ARCH", "1");
    }

    // Keep the submodule immutable. The generated root is the exact upstream
    // root plus our export, alongside reviewed source overlays. Each overlay
    // verifies the full pinned file hash and the exact replacement-site count.
    // Upstream's C exports are root-only, so importing its module from another
    // root would silently omit them.
    const sources = b.addWriteFiles();
    _ = sources.addCopyDirectory(ghostty.path("src"), "src", .{
        // WriteFile compares suffixes against host-native walker paths and
        // copies directories after generated files. Literal '/' suffixes on
        // Windows would miss these files and overwrite the reviewed overlays.
        .exclude_extensions = &.{
            b.pathJoin(&.{ "terminal", "Terminal.zig" }),
            b.pathJoin(&.{ "terminal", "Screen.zig" }),
            b.pathJoin(&.{ "terminal", "PageList.zig" }),
            b.pathJoin(&.{ "terminal", "bitmap_allocator.zig" }),
            b.pathJoin(&.{ "terminal", "kitty", "graphics_storage.zig" }),
            b.pathJoin(&.{ "terminal", "stream_continuation.zig" }),
            b.pathJoin(&.{ "terminal", "stream.zig" }),
            b.pathJoin(&.{ "terminal", "stream_terminal.zig" }),
            b.pathJoin(&.{ "terminal", "c", "terminal.zig" }),
        },
    });
    try addOverlay(b, sources, ghostty, "terminal/Terminal.zig", "3305a832a49891b2e84d0efa4ace415d0d5e709d9c3c8f7e061228a3e1f18035", &.{
        .{
            .before = "        try self.screens.active.appendGrapheme(prev, c);\n        return;",
            .after = "        self.screens.active.cursorMarkDirty();\n        try self.screens.active.appendGrapheme(prev, c);\n        return;",
        },
        .{
            .before = "                    if (head_cell.wide == .spacer_head) head_cell.wide = .narrow;",
            .after = "                    if (head_cell.wide == .spacer_head) {\n                        head_cell.wide = .narrow;\n                        if (self.screens.active.cursor.page_pin.up(1)) |previous| previous.markDirty();\n                    }",
            .count = 2,
        },
        .{
            .before = "    cmd: osc.Command.SemanticPrompt,\n) !void {\n    switch (cmd.action)",
            .after = "    cmd: osc.Command.SemanticPrompt,\n) !void {\n    defer self.screens.active.cursorMarkDirty();\n    switch (cmd.action)",
        },
        .{
            .before = "            screen.cursor.page_row.semantic_prompt = .prompt_continuation;",
            .after = "            screen.cursor.page_row.semantic_prompt = .prompt_continuation;\n            screen.cursorMarkDirty();",
        },
    });
    try addOverlay(b, sources, ghostty, "terminal/Screen.zig", "3a74c3603c57db299f266c9df7f5650ae0d621c98fb57f3867b654ed9841a76b", &.{
        .{
            .before = "        next_row.rowAndCell().row.wrap_continuation = false;",
            .after = "        next_row.rowAndCell().row.wrap_continuation = false;\n        next_row.markDirty();",
        },
        .{
            // Style installation may have rebuilt the destination and restored
            // the incoming hyperlink there. Release that temporary reference
            // before detaching its pointer; startHyperlinkOnce calls endHyperlink
            // and must never see a live ID paired with a null pointer.
            .before = "    // On the new page, we need to migrate our hyperlink\n    if (self.cursor.hyperlink) |link| {\n",
            .after = "    // On the new page, we need to migrate our hyperlink\n    if (self.cursor.hyperlink) |link| {\n        if (self.cursor.hyperlink_id != 0) {\n            const page = self.cursor.page_pin.node.page();\n            page.hyperlink_set.release(page.memory, self.cursor.hyperlink_id);\n            self.cursor.hyperlink_id = 0;\n        }\n",
        },
    });
    try addOverlay(b, sources, ghostty, "terminal/PageList.zig", "ce371ed17ba9eb00f69b776cc78034e17bc588594a54706ae275814eea72d425", &.{
        .{
            // A clone can fail after retaining a styled/grapheme/link prefix.
            // Reset it while the row is still in size.rows, then remap pins for
            // earlier successful copies before destroying their source page.
            .before = "                prev_page.size.rows -= 1;\n                copied -= 1;\n                break :prev;",
            .after = "                prev_page.resetRow(dst_row);\n                prev_page.size.rows -= 1;\n                copied -= 1;\n                break;",
        },
        .{
            .before = "        assert(copied == len);\n",
            .after = "        assert(copied <= len);\n",
        },
        .{
            .before = "            if (p.node != chunk.node or p.y >= len) continue;\n            p.node = prev_node;\n            p.y += prev_page.size.rows - len;",
            .after = "            if (p.node != chunk.node or p.y >= copied) continue;\n            p.node = prev_node;\n            p.y += prev_page.size.rows - copied;",
        },
        .{
            .before = "                new_page.size.rows -= 1;\n                break;",
            .after = "                new_page.resetRow(dst_row);\n                new_page.size.rows -= 1;\n                break;",
        },
    });
    try addOverlay(b, sources, ghostty, "terminal/bitmap_allocator.zig", "bac61a65b5a3141ccfad2d9d0a6a452be7106a647182470fcf38e1289b5f86e1", &.{.{
        // The full-word loop checks its bounds, but it can finish (or be
        // skipped) with a partial-word remainder and i == bitmaps.len.
        // Report allocation pressure before inspecting that nonexistent word.
        .before = "            // If the number of available chunks at the start of this bitmap\n",
        .after = "            if (i >= bitmaps.len) return null;\n\n            // If the number of available chunks at the start of this bitmap\n",
    }});
    try addOverlay(b, sources, ghostty, "terminal/kitty/graphics_storage.zig", "a2c29c02531f00b939485a9e45eeb8198d55648f116282c31e37bed84677328d", &.{
        .{
            .before = "        const removed_idx: u32 = if (number == 1) 0 else number - 2;",
            .after = "        const removed_idx: u32 = number - 1;",
        },
        .{
            // Deleting an earlier frame renumbers the displayed frame but does
            // not replace its pixels. Do this before clamping the old last index;
            // otherwise the same frame spuriously changes generation/restarts its gap.
            .before = "        const remaining: u32 = @intCast(anim.frames.items.len);\n        if (anim.current_index > remaining) {",
            .after = "        const remaining: u32 = @intCast(anim.frames.items.len);\n        if (removed_idx < anim.current_index) {\n            anim.current_index -= 1;\n            self.markMutated(io);\n            return;\n        }\n        if (anim.current_index > remaining) {",
        },
        .{
            // RGB-to-RGBA promotion is quota-exempt. A subsequent image admission
            // can require reclaiming more than the limit, but never more than the
            // actual retained bytes. The eviction loop already handles that case.
            .before = "        assert(req <= self.total_limit);",
            .after = "        assert(req <= self.total_bytes);",
        },
    });
    try addOverlay(b, sources, ghostty, "terminal/stream_continuation.zig", "a86feef9e53dc62349e64ddb6d25f1d6d971b9813578a24e915e39daeb39a9a2", &.{.{
        .before = "        var scanner: BoundaryScanner = .init();\n        for (self.bytes.items) |c| {\n            if (scanner.next(c) == .omittable) continue;\n            try writer.writeByte(c);\n        }\n",
        .after = @embedFile("src/continuation_write.zig.inc"),
    }});
    // Route the already-validated upstream OSC 99 command to our shared host.
    // Deliberately do not add an Action enum member: that would change upstream's C ABI.
    try addOverlay(b, sources, ghostty, "terminal/stream.zig", "fd45f42dfb66ee49e359671f7d254726273ce7dfee9078d30d51e6d376ae4e0c", &.{ .{
        .before = "                .kitty_desktop_notification,\n",
        .after = "",
    }, .{
        .before = "                .conemu_sleep,\n",
        .after = "                .kitty_desktop_notification => |v| {\n                    if (comptime @hasDecl(T, \"royalDesktopNotification\")) self.handler.royalDesktopNotification(v);\n                },\n\n                .conemu_sleep,\n",
    } });
    try addOverlay(b, sources, ghostty, "terminal/stream_terminal.zig", "cdfcf97647ae756fd314e25d7a3bff9df57125ae2e111168b764a07bf2593288", &.{
        .{
            .before = "    pub const Effects = struct {\n",
            .after = "    pub const Effects = struct {\n        royal_notification: ?*const fn (*Handler, ?osc.Command.KittyDesktopNotification) void = null,\n",
        },
        .{
            // Anchor to the Handler indentation. Test handlers declare the same
            // function at a deeper indentation and must remain unmodified.
            .before = "\n    fn desktopNotification(\n",
            .after = "\n    pub fn royalDesktopNotification(self: *Handler, notification: ?osc.Command.KittyDesktopNotification) void {\n        const callback = self.effects.royal_notification orelse return;\n        callback(self, notification);\n    }\n\n    fn desktopNotification(\n",
        },
        .{
            .before = "                self.terminal.fullReset();\n",
            .after = "                self.terminal.fullReset();\n                self.royalDesktopNotification(null);\n",
        },
    });
    try addOverlay(b, sources, ghostty, "terminal/c/terminal.zig", "9b06653cb34f7407b510f25111cb45c83014a3adfc9c031c48ecc537c6f67136", &.{ .{
        .before = "const Effects = struct {\n",
        .after = "const Effects = struct {\n    royal_notification: ?*const fn (Terminal, ?*anyopaque, ?[*]const u8, usize, ?[*]const u8, usize, u8) callconv(.c) void = null,\n    royal_notification_userdata: ?*anyopaque = null,\n",
    }, .{
        .before = "    fn desktopNotificationTrampoline(\n",
        .after = @embedFile("src/notification_effect.zig.inc") ++ "    fn desktopNotificationTrampoline(\n",
    }, .{
        .before = "        .desktop_notification = &Effects.desktopNotificationTrampoline,\n",
        .after = "        .desktop_notification = &Effects.desktopNotificationTrampoline,\n        .royal_notification = &Effects.royalNotificationTrampoline,\n",
    } });
    const upstream = try std.Io.Dir.cwd().readFileAlloc(
        b.graph.io,
        ghostty.path("src/lib_vt.zig").getPath(b),
        b.allocator,
        .unlimited,
    );
    module.root_source_file = sources.add(
        "src/lib_vt_royal.zig",
        try std.mem.concat(b.allocator, u8, &.{ upstream, "\n", @embedFile("src/extensions.zig"), "\n", @embedFile("src/drag_drop.zig"), "\n", @embedFile("src/notifications.zig") }),
    );

    // Retain Ghostty's platform linking, symbol visibility, static archive
    // bundling, deployment targets and libSystem compatibility workarounds.
    // Every already-created upstream artifact references this same module.
    const install = b.addInstallDirectory(.{
        .source_dir = .{ .cwd_relative = ghostty.builder.install_path },
        .install_dir = .prefix,
        .install_subdir = "",
    });
    install.step.dependOn(ghostty.builder.getInstallStep());
    b.getInstallStep().dependOn(&install.step);
    // InstallDirectory skips symlinks. Materialize the canonical shared-library
    // name explicitly so fresh builds never depend on a stale local artifact.
    const shared_dir: std.Build.InstallDir = if (target.result.os.tag == .windows) .bin else .lib;
    const shared_name: []const u8 = switch (target.result.os.tag) {
        .windows => "ghostty-vt.dll",
        .macos => "libghostty-vt.dylib",
        else => "libghostty-vt.so",
    };
    const canonical = b.addInstallFileWithDir(
        .{ .cwd_relative = ghostty.builder.getInstallPath(shared_dir, shared_name) },
        shared_dir,
        shared_name,
    );
    canonical.step.dependOn(&install.step);
    b.getInstallStep().dependOn(&canonical.step);
    b.installFile("include/royalterminal_ghostty_vt.h", "include/royalterminal_ghostty_vt.h");

    const static_step = b.step("static", "Build the static library including extensions");
    static_step.dependOn(b.getInstallStep());
}

const Overlay = struct {
    before: []const u8,
    after: []const u8,
    count: usize = 1,
};

fn addOverlay(
    b: *std.Build,
    sources: *std.Build.Step.WriteFile,
    ghostty: *std.Build.Dependency,
    path: []const u8,
    expected_hash: []const u8,
    overlays: []const Overlay,
) !void {
    const source_path = b.pathJoin(&.{ "src", path });
    const original = try std.Io.Dir.cwd().readFileAlloc(
        b.graph.io,
        ghostty.path(source_path).getPath(b),
        b.allocator,
        .unlimited,
    );
    var digest: [32]u8 = undefined;
    std.crypto.hash.sha2.Sha256.hash(original, &digest, .{});
    const actual_hash = std.fmt.bytesToHex(digest, .lower);
    if (!std.mem.eql(u8, &actual_hash, expected_hash)) {
        std.log.err("Ghostty overlay requires review after upstream change: {s}", .{source_path});
        return error.UpstreamOverlayMismatch;
    }
    var result = original;
    for (overlays) |overlay| {
        if (std.mem.count(u8, result, overlay.before) != overlay.count) {
            std.log.err("Ghostty overlay site count requires review: {s}", .{source_path});
            return error.UpstreamOverlayMismatch;
        }
        result = try std.mem.replaceOwned(u8, b.allocator, result, overlay.before, overlay.after);
    }
    _ = sources.add(source_path, result);
}
