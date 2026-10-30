//! Preserve upstream package descriptions while correcting Linux static links.
const std = @import("std");

pub fn main(init: std.process.Init) !void {
    const alloc = init.arena.allocator();
    var args = try init.minimal.args.iterateAllocator(alloc);
    std.debug.assert(args.skip());
    const path = args.next() orelse return error.MissingInput;
    const kind = args.next() orelse return error.MissingKind;
    const source = try std.Io.Dir.cwd().readFileAlloc(init.io, path, alloc, .limited(1024 * 1024));
    var output = std.Io.File.stdout().writer(init.io, &.{});
    try rewrite(source, std.mem.eql(u8, kind, "static"), &output.interface);
    try output.interface.flush();
}

fn rewrite(source: []const u8, is_static: bool, output: *std.Io.Writer) !void {
    var lines = std.mem.splitScalar(u8, source, '\n');
    var libs_found = false;
    var private_found = false;
    while (lines.next()) |line| {
        if (line.len == 0 and lines.index == null) break;
        if (is_static and std.mem.startsWith(u8, line, "Libs:")) {
            const old = "${libdir}/libghostty-vt.a";
            const offset = std.mem.indexOf(u8, line, old) orelse return error.UnexpectedStaticArchive;
            try output.writeAll(line[0..offset]);
            try output.writeAll("${libdir}/libghostty-vt-static.a");
            try output.writeAll(line[offset + old.len ..]);
            libs_found = true;
        } else if (std.mem.startsWith(u8, line, "Libs.private:")) {
            try output.writeAll(line);
            for ([_][]const u8{ "-lrt", "-lm", "-lc" }) |library| {
                var flags = std.mem.tokenizeAny(u8, line["Libs.private:".len..], " \t\r");
                var present = false;
                while (flags.next()) |flag| {
                    if (std.mem.eql(u8, flag, library)) present = true;
                }
                if (!present) try output.print(" {s}", .{library});
            }
            private_found = true;
        } else {
            try output.writeAll(line);
        }
        try output.writeByte('\n');
    }
    if (is_static and !libs_found) return error.MissingStaticArchive;
    if (!private_found) return error.MissingPrivateLibraries;
}

test "static package retains upstream fields and adds complete link dependencies" {
    var output: std.Io.Writer.Allocating = .init(std.testing.allocator);
    defer output.deinit();
    try rewrite("Name: upstream\nLibs: ${libdir}/libghostty-vt.a\nLibs.private: -lc++\nRequires.private: simdutf", true, &output.writer);
    try std.testing.expectEqualStrings(
        "Name: upstream\nLibs: ${libdir}/libghostty-vt-static.a\nLibs.private: -lc++ -lrt -lm -lc\nRequires.private: simdutf\n",
        output.written(),
    );
}

test "shared package retains dynamic flags and does not duplicate dependencies" {
    var output: std.Io.Writer.Allocating = .init(std.testing.allocator);
    defer output.deinit();
    try rewrite("Libs: -L${libdir} -lghostty-vt\nLibs.private: -lrt -lm -lc\n", false, &output.writer);
    try std.testing.expectEqualStrings("Libs: -L${libdir} -lghostty-vt\nLibs.private: -lrt -lm -lc\n", output.written());
}

test "changed upstream metadata fails instead of advertising an invalid package" {
    var output: std.Io.Writer.Allocating = .init(std.testing.allocator);
    defer output.deinit();
    try std.testing.expectError(error.UnexpectedStaticArchive, rewrite("Libs: changed.a\nLibs.private: ", true, &output.writer));
    try std.testing.expectError(error.MissingPrivateLibraries, rewrite("Libs: -lghostty-vt", false, &output.writer));
}
