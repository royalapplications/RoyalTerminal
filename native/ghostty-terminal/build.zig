// build.zig — Builds libghostty-terminal shared library.
//
// This library wraps Ghostty's VT terminal processing into a simple C API.
// It depends on the Ghostty source tree (via the submodule at external/ghostty)
// and reuses its fully-wired VT module including terminal emulation, VT parsing,
// Unicode tables, and SIMD acceleration.
//
// Usage:
//   zig build                         # Debug build
//   zig build -Doptimize=ReleaseFast  # Optimized build
//
// Output:
//   zig-out/lib/libghostty-terminal.{dylib,so,dll}
//   zig-out/include/ghostty_terminal.h

const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});

    // Get the Ghostty dependency (from build.zig.zon path dependency).
    // This runs Ghostty's build.zig which sets up the "ghostty-vt" module
    // with all its dependencies (unicode tables, uucode, SIMD, etc.).
    const ghostty = b.dependency("ghostty", .{
        .target = target,
        .optimize = optimize,
        // This project only consumes the VT Zig module; avoid building
        // Ghostty macOS artifacts (xcframework/app) in dependency builds.
        .@"emit-xcframework" = false,
        .@"emit-macos-app" = false,
    });

    // Get the Zig-only VT module. We use "ghostty-vt" (not "ghostty-vt-c")
    // because we define our own C exports — we don't need the internal code
    // to use C calling conventions.
    const vt_module = ghostty.module("ghostty-vt");

    // ── Shared library ──────────────────────────────────────────────

    const lib = b.addLibrary(.{
        .name = "ghostty-terminal",
        .linkage = .dynamic,
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/main.zig"),
            .target = target,
            .optimize = optimize,
            .imports = &.{
                .{ .name = "ghostty_vt", .module = vt_module },
            },
        }),
        .version = .{ .major = 0, .minor = 1, .patch = 0 },
    });

    // Install the library and header
    b.installArtifact(lib);
    b.installFile("include/ghostty_terminal.h", "include/ghostty_terminal.h");

    // ── Static library (for embedding) ──────────────────────────────

    const static_lib = b.addLibrary(.{
        .name = "ghostty-terminal",
        .linkage = .static,
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/main.zig"),
            .target = target,
            .optimize = optimize,
            .imports = &.{
                .{ .name = "ghostty_vt", .module = vt_module },
            },
        }),
    });

    const static_step = b.step("static", "Build static library");
    static_step.dependOn(&b.addInstallArtifact(static_lib, .{}).step);

    // ── Tests ───────────────────────────────────────────────────────

    const tests = b.addTest(.{
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/main.zig"),
            .target = target,
            .optimize = optimize,
            .imports = &.{
                .{ .name = "ghostty_vt", .module = vt_module },
            },
        }),
    });

    const test_step = b.step("test", "Run unit tests");
    test_step.dependOn(&b.addRunArtifact(tests).step);
}
