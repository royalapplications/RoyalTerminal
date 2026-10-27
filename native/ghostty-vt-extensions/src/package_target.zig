//! GNU package compatibility without changing explicit caller target choices.
const std = @import("std");

/// Apply the package GNU baseline, or return null when no change is required.
pub fn normalize(query: std.Target.Query, os: std.Target.Os.Tag, abi: std.Target.Abi) ?std.Target.Query {
    if (os != .linux or !abi.isGnu() or query.glibc_version != null) return null;
    var result = query;
    // Native queries leave ABI inferred. zigTriple needs a concrete ABI when
    // serializing a glibc version for a dependency package's target option.
    result.abi = abi;
    result.glibc_version = .{ .major = 2, .minor = 28, .patch = 0 };
    return result;
}

test "inferred native GNU ABI serializes with the package glibc baseline" {
    const query = normalize(.{}, .linux, .gnu).?;
    const triple = try query.zigTriple(std.testing.allocator);
    defer std.testing.allocator.free(triple);
    const parsed = try std.Target.Query.parse(.{ .arch_os_abi = triple });
    try std.testing.expectEqual(std.Target.Abi.gnu, parsed.abi.?);
    try std.testing.expectEqual(std.SemanticVersion{ .major = 2, .minor = 28, .patch = 0 }, parsed.glibc_version.?);
    try std.testing.expectEqual(query.cpu_arch, parsed.cpu_arch);
    try std.testing.expectEqual(query.os_tag, parsed.os_tag);
}

test "explicit GNU versions and other platforms are unchanged" {
    const explicit = try std.Target.Query.parse(.{ .arch_os_abi = "x86_64-linux-gnu.2.34" });
    try std.testing.expect(normalize(explicit, .linux, .gnu) == null);
    try std.testing.expect(normalize(.{}, .linux, .musl) == null);
    try std.testing.expect(normalize(.{}, .macos, .none) == null);
    try std.testing.expect(normalize(.{}, .windows, .gnu) == null);
}

test "explicit unversioned GNU cross-target retains architecture and ABI" {
    const input = try std.Target.Query.parse(.{ .arch_os_abi = "aarch64-linux-gnu" });
    const query = normalize(input, .linux, .gnu).?;
    const triple = try query.zigTriple(std.testing.allocator);
    defer std.testing.allocator.free(triple);
    try std.testing.expectEqualStrings("aarch64-linux-gnu.2.28", triple);
}
