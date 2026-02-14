// build.zig — Builds libghostty-renderer-capi shared library.
//
// This library exposes a minimal C API for experimental GPU render-target
// interop. Phase 2 prototype supports a macOS Metal texture target path.

const std = @import("std");

fn linkAppleObjc(step: *std.Build.Step.Compile) void {
    // Add common SDK library locations so cross-target macOS builds can resolve libobjc.
    step.addLibraryPath(.{ .cwd_relative = "/usr/lib" });
    step.addLibraryPath(.{ .cwd_relative = "/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/lib" });
    step.addLibraryPath(.{ .cwd_relative = "/Applications/Xcode.app/Contents/Developer/Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk/usr/lib" });
    step.linkSystemLibrary("objc");
}

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});
    const renderer_module = b.createModule(.{
        .root_source_file = b.path("src/main.zig"),
        .target = target,
        .optimize = optimize,
    });

    const lib = b.addLibrary(.{
        .name = "ghostty-renderer-capi",
        .linkage = .dynamic,
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/main.zig"),
            .target = target,
            .optimize = optimize,
        }),
        .version = .{ .major = 0, .minor = 1, .patch = 0 },
    });
    lib.linkLibC();

    if (target.result.os.tag == .macos) {
        linkAppleObjc(lib);
    }

    b.installArtifact(lib);
    b.installFile("include/ghostty_renderer.h", "include/ghostty_renderer.h");

    const static_lib = b.addLibrary(.{
        .name = "ghostty-renderer-capi",
        .linkage = .static,
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/main.zig"),
            .target = target,
            .optimize = optimize,
        }),
    });
    static_lib.linkLibC();

    if (target.result.os.tag == .macos) {
        linkAppleObjc(static_lib);
    }

    const static_step = b.step("static", "Build static library");
    static_step.dependOn(&b.addInstallArtifact(static_lib, .{}).step);

    const tests = b.addTest(.{
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/main.zig"),
            .target = target,
            .optimize = optimize,
        }),
    });
    tests.linkLibC();

    if (target.result.os.tag == .macos) {
        linkAppleObjc(tests);
    }

    const test_step = b.step("test", "Run unit tests");
    test_step.dependOn(&b.addRunArtifact(tests).step);

    const sample = b.addExecutable(.{
        .name = "ghostty-renderer-capi-metal-smoke",
        .root_module = b.createModule(.{
            .root_source_file = b.path("samples/metal_texture_smoke.zig"),
            .target = target,
            .optimize = optimize,
            .imports = &.{
                .{ .name = "renderer_capi", .module = renderer_module },
            },
        }),
    });
    sample.linkLibC();

    if (target.result.os.tag == .macos) {
        linkAppleObjc(sample);
        sample.linkFramework("Metal");
    }

    const sample_install = b.addInstallArtifact(sample, .{});
    const sample_run = b.addRunArtifact(sample);

    const sample_step = b.step("sample-metal", "Build and run Metal texture smoke sample");
    sample_step.dependOn(&sample_install.step);
    sample_step.dependOn(&sample_run.step);
}
