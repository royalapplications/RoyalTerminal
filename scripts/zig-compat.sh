#!/usr/bin/env bash
# zig-compat.sh - Run Zig with RoyalTerminal host workarounds.
#
# Ghostty currently pins Zig 0.15.2. On macOS 26 hosts with an Xcode 26.4 SDK,
# Zig 0.15.2 can select a native target of macOS 26.4.1 but link against an SDK
# max version of 26.4, which leaves libSystem symbols unresolved while linking
# build runners and native helper tools. This wrapper keeps Zig 0.15.2 while
# making those host-tool invocations explicit.

set -euo pipefail

ZIG_EXE="${ZIG_EXE:-zig}"
if [[ "$ZIG_EXE" != */* ]]; then
    ZIG_EXE="$(command -v "$ZIG_EXE")"
fi

if [ "$#" -eq 0 ]; then
    exec "$ZIG_EXE"
fi

needs_macos_compat() {
    [ "$(uname -s)" = "Darwin" ] || return 1
    [ "$("$ZIG_EXE" version)" = "0.15.2" ] || return 1

    local smoke_dir
    smoke_dir="$(mktemp -d)"
    trap 'rm -rf "$smoke_dir"' RETURN

    cat > "$smoke_dir/main.zig" <<'EOF'
const std = @import("std");
pub fn main() void {
    std.debug.print("ok\n", .{});
}
EOF

    if "$ZIG_EXE" build-exe "$smoke_dir/main.zig" \
        --cache-dir "$smoke_dir/cache" \
        --global-cache-dir "$smoke_dir/global-cache" \
        >/dev/null 2>&1; then
        return 1
    fi

    return 0
}

if [ "${1:-}" != "build" ] || ! needs_macos_compat; then
    exec "$ZIG_EXE" "$@"
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

REAL_DEVELOPER_DIR="$(xcode-select -p)"
REAL_SDK_DIR="$(xcrun --show-sdk-path)"
ZIG_LIB_DIR="$("$ZIG_EXE" env | awk -F'"' '/\.lib_dir = / { print $2; exit }')"

HOST_ARCH="$(uname -m)"
case "$HOST_ARCH" in
    arm64) HOST_ARCH="aarch64" ;;
    x86_64) HOST_ARCH="x86_64" ;;
    *) echo "Unsupported macOS Zig host architecture: $HOST_ARCH" >&2; exit 1 ;;
esac

HOST_TARGET="${ROYALTERMINAL_ZIG_HOST_TARGET:-$HOST_ARCH-macos.$(sw_vers -productVersion)}"

FAKE_DEVELOPER_DIR="$WORK_DIR/FakeXcode.app/Contents/Developer"
mkdir -p "$FAKE_DEVELOPER_DIR/Platforms/MacOSX.platform/Developer/SDKs"
ln -s "$REAL_SDK_DIR" "$FAKE_DEVELOPER_DIR/Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"

ZIG_WRAPPER="$WORK_DIR/zig-host-target-wrapper"
cat > "$ZIG_WRAPPER" <<EOF
#!/usr/bin/env bash
set -euo pipefail

REAL_ZIG="$ZIG_EXE"
HOST_TARGET="$HOST_TARGET"

if [ "\$#" -eq 0 ]; then
    exec "\$REAL_ZIG"
fi

cmd="\$1"
shift

filtered=()
has_target=false
while [ "\$#" -gt 0 ]; do
    case "\$1" in
        -target)
            has_target=true
            filtered+=("\$1")
            shift
            if [ "\$#" -gt 0 ]; then
                filtered+=("\$1")
                shift
            fi
            ;;
        --libc)
            shift
            if [ "\$#" -gt 0 ]; then
                shift
            fi
            ;;
        *)
            filtered+=("\$1")
            shift
            ;;
    esac
done

case "\$cmd" in
    build-exe|build-lib|build-obj|test|test-obj|run)
        if [ "\$has_target" = false ]; then
            exec "\$REAL_ZIG" "\$cmd" -target "\$HOST_TARGET" "\${filtered[@]}"
        fi
        exec "\$REAL_ZIG" "\$cmd" "\${filtered[@]}"
        ;;
    *)
        exec "\$REAL_ZIG" "\$cmd" "\${filtered[@]}"
        ;;
esac
EOF
chmod +x "$ZIG_WRAPPER"

BUILD_RUNNER="$WORK_DIR/build_runner.zig"
awk '
    {
        print
        if ($0 == "const runner = @This();") {
            print "extern \"c\" fn setenv(name: [*:0]const u8, value: [*:0]const u8, overwrite: c_int) c_int;"
        }
        if ($0 == "pub fn main() !void {") {
            print "    if (std.process.getEnvVarOwned(std.heap.page_allocator, \"ROYALTERMINAL_REAL_DEVELOPER_DIR\")) |developer_dir| {"
            print "        defer std.heap.page_allocator.free(developer_dir);"
            print "        const developer_dir_z = try std.heap.page_allocator.dupeZ(u8, developer_dir);"
            print "        defer std.heap.page_allocator.free(developer_dir_z);"
            print "        _ = setenv(\"DEVELOPER_DIR\", developer_dir_z, 1);"
            print "    } else |_| {}"
        }
        if ($0 == "    graph.cache.addPrefix(.{ .path = null, .handle = std.fs.cwd() });") {
            print "    if (std.process.getEnvVarOwned(arena, \"ROYALTERMINAL_ZIG_WRAPPER\")) |zig_wrapper| {"
            print "        graph.zig_exe = try arena.dupeZ(u8, zig_wrapper);"
            print "    } else |_| {}"
            print ""
        }
    }
' "$ZIG_LIB_DIR/compiler/build_runner.zig" > "$BUILD_RUNNER"

echo "[INFO] Applying Zig 0.15.2 macOS SDK compatibility wrapper for host target $HOST_TARGET" >&2

exec env \
    DEVELOPER_DIR="$FAKE_DEVELOPER_DIR" \
    ROYALTERMINAL_REAL_DEVELOPER_DIR="$REAL_DEVELOPER_DIR" \
    ROYALTERMINAL_ZIG_WRAPPER="$ZIG_WRAPPER" \
    ROYALTERMINAL_ZIG_HOST_TARGET="$HOST_TARGET" \
    ZIG_BUILD_RUNNER="$BUILD_RUNNER" \
    "$ZIG_EXE" "$@"
