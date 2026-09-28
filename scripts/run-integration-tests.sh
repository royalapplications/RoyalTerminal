#!/usr/bin/env bash
# Build the pinned RoyalTerminal native integration and run required-native tests.
# --skip-build reuses the installed native artifact; it never permits missing APIs.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SKIP_BUILD=false
VERBOSITY=minimal
CONFIGURATION="${ROYALTERMINAL_TEST_CONFIGURATION:-Release}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-build) SKIP_BUILD=true; shift ;;
        --verbose) VERBOSITY=normal; shift ;;
        --help)
            echo "Usage: $0 [--skip-build] [--verbose]"
            echo "Build RoyalTerminal's pinned Ghostty extensions and run required-native integration tests."
            exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done

case "$(uname -s)/$(uname -m)" in
    Darwin/arm64) RID=osx-arm64; LIB_NAME=libghostty-vt.dylib ;;
    Darwin/x86_64) RID=osx-x64; LIB_NAME=libghostty-vt.dylib ;;
    Linux/x86_64|Linux/amd64) RID=linux-x64; LIB_NAME=libghostty-vt.so ;;
    Linux/aarch64|Linux/arm64) RID=linux-arm64; LIB_NAME=libghostty-vt.so ;;
    *) echo "Unsupported platform; use the Windows CI/native build path." >&2; exit 1 ;;
esac

cd "$ROOT_DIR"
if [[ "$SKIP_BUILD" == false ]]; then
    # The plain upstream build omits RoyalTerminal exports/overlays. Always use
    # the same entry point as shipping packages and the CI native integration.
    "$SCRIPT_DIR/build-native.sh" --release
fi
NATIVE_LIB="$ROOT_DIR/native/$RID/$LIB_NAME"
if [[ ! -f "$NATIVE_LIB" ]]; then
    echo "Missing native library: $NATIVE_LIB. Run without --skip-build." >&2
    exit 1
fi

# Match complete export names, including our extension and current upstream ABI.
# Capture nm first: piping nm into grep -q can fail with SIGPIPE under pipefail.
if [[ "$RID" == osx-* ]]; then
    symbols="$(nm -gU "$NATIVE_LIB" | awk '{print $NF}' | sed 's/^_//')"
else
    symbols="$(nm -D --defined-only "$NATIVE_LIB" | awk '{print $NF}')"
fi
for symbol in ghostty_terminal_new ghostty_render_state_set ghostty_snapshot_encode_buf \
    ghostty_royal_notification_callback ghostty_royal_window_resize_callback; do
    if ! grep -Fx "$symbol" <<< "$symbols" >/dev/null; then
        echo "Missing required native export: $symbol ($NATIVE_LIB)" >&2
        exit 1
    fi
done

project=tests/RoyalTerminal.IntegrationTests/RoyalTerminal.IntegrationTests.csproj
dotnet build "$project" -c "$CONFIGURATION"
# Do not reuse a stale output library merely because it exists.
output="tests/RoyalTerminal.IntegrationTests/bin/$CONFIGURATION/net10.0"
cp "$NATIVE_LIB" "$output/$LIB_NAME"
ROYALTERMINAL_REQUIRE_NATIVE_TESTS=1 dotnet test "$project" \
    -c "$CONFIGURATION" --no-build --verbosity "$VERBOSITY"
