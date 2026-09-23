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
    // verifies the full pinned file hash and exactly one replacement site.
    // Upstream's C exports are root-only, so importing its module from another
    // root would silently omit them.
    const sources = b.addWriteFiles();
    _ = sources.addCopyDirectory(ghostty.path("src"), "src", .{
        .exclude_extensions = &.{ "terminal/Terminal.zig", "terminal/kitty/graphics_storage.zig" },
    });
    try addOverlay(b, sources, ghostty, "terminal/Terminal.zig", "3305a832a49891b2e84d0efa4ace415d0d5e709d9c3c8f7e061228a3e1f18035", "        try self.screens.active.appendGrapheme(prev, c);\n        return;", "        self.screens.active.cursorMarkDirty();\n        try self.screens.active.appendGrapheme(prev, c);\n        return;");
    try addOverlay(b, sources, ghostty, "terminal/kitty/graphics_storage.zig", "a2c29c02531f00b939485a9e45eeb8198d55648f116282c31e37bed84677328d", "        const removed_idx: u32 = if (number == 1) 0 else number - 2;", "        const removed_idx: u32 = number - 1;");
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

fn addOverlay(
    b: *std.Build,
    sources: *std.Build.Step.WriteFile,
    ghostty: *std.Build.Dependency,
    path: []const u8,
    expected_hash: []const u8,
    before: []const u8,
    after: []const u8,
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
    if (!std.mem.eql(u8, &actual_hash, expected_hash) or std.mem.count(u8, original, before) != 1) {
        std.log.err("Ghostty overlay requires review after upstream change: {s}", .{source_path});
        return error.UpstreamOverlayMismatch;
    }
    _ = sources.add(source_path, try std.mem.replaceOwned(u8, b.allocator, original, before, after));
}
