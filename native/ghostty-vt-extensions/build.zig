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

    // Keep the submodule immutable. The generated root is the exact upstream
    // root plus our export, alongside reviewed source overlays. Each overlay
    // verifies the full pinned file hash and the exact replacement-site count.
    // Upstream's C exports are root-only, so importing its module from another
    // root would silently omit them.
    const sources = b.addWriteFiles();
    _ = sources.addCopyDirectory(ghostty.path("src"), "src", .{
        .exclude_extensions = &.{ "terminal/Terminal.zig", "terminal/kitty/graphics_storage.zig", "terminal/stream_continuation.zig" },
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
    });
    try addOverlay(b, sources, ghostty, "terminal/kitty/graphics_storage.zig", "a2c29c02531f00b939485a9e45eeb8198d55648f116282c31e37bed84677328d", &.{.{
        .before = "        const removed_idx: u32 = if (number == 1) 0 else number - 2;",
        .after = "        const removed_idx: u32 = number - 1;",
    }});
    try addOverlay(b, sources, ghostty, "terminal/stream_continuation.zig", "a86feef9e53dc62349e64ddb6d25f1d6d971b9813578a24e915e39daeb39a9a2", &.{.{
        .before = "        var scanner: BoundaryScanner = .init();\n        for (self.bytes.items) |c| {\n            if (scanner.next(c) == .omittable) continue;\n            try writer.writeByte(c);\n        }\n",
        .after = @embedFile("src/continuation_write.zig.inc"),
    }});
    const upstream = try std.Io.Dir.cwd().readFileAlloc(
        b.graph.io,
        ghostty.path("src/lib_vt.zig").getPath(b),
        b.allocator,
        .unlimited,
    );
    module.root_source_file = sources.add(
        "src/lib_vt_royal.zig",
        try std.mem.concat(b.allocator, u8, &.{ upstream, "\n", @embedFile("src/extensions.zig") }),
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
