#!/bin/bash
# build.sh — Build libghostty-terminal for the current platform.
#
# Usage:
#   ./build.sh                  # Debug build
#   ./build.sh release          # Release (optimized) build
#   ./build.sh clean            # Clean build artifacts
#
# Prerequisites:
#   - Zig 0.15.2 or later (https://ziglang.org/download/)
#   - Ghostty submodule initialized: git submodule update --init external/ghostty
#
# Output:
#   zig-out/lib/libghostty-terminal.{dylib,so}
#   zig-out/include/ghostty_terminal.h

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

# Check Zig is available
if ! command -v zig &> /dev/null; then
    echo "Error: Zig is not installed or not in PATH."
    echo "Install Zig 0.15.2+: https://ziglang.org/download/"
    exit 1
fi

# Check Ghostty submodule
GHOSTTY_DIR="$SCRIPT_DIR/../../external/ghostty"
if [ ! -f "$GHOSTTY_DIR/build.zig" ]; then
    echo "Error: Ghostty submodule not found at $GHOSTTY_DIR"
    echo "Run: git submodule update --init external/ghostty"
    exit 1
fi

case "${1:-debug}" in
    release|Release|ReleaseFast)
        echo "Building libghostty-terminal (ReleaseFast)..."
        zig build -Doptimize=ReleaseFast
        ;;
    release-safe|ReleaseSafe)
        echo "Building libghostty-terminal (ReleaseSafe)..."
        zig build -Doptimize=ReleaseSafe
        ;;
    clean)
        echo "Cleaning build artifacts..."
        rm -rf zig-out .zig-cache
        echo "Done."
        exit 0
        ;;
    debug|Debug)
        echo "Building libghostty-terminal (Debug)..."
        zig build
        ;;
    test)
        echo "Running tests..."
        zig build test
        echo "Tests passed."
        exit 0
        ;;
    *)
        echo "Usage: $0 [debug|release|release-safe|clean|test]"
        exit 1
        ;;
esac

# Show output
echo ""
echo "Build complete. Output:"
if [ -d "zig-out/lib" ]; then
    ls -la zig-out/lib/libghostty-terminal*
fi
if [ -d "zig-out/include" ]; then
    ls -la zig-out/include/ghostty_terminal.h
fi

# Print install hint
ROOT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
echo ""
echo "To install into the NuGet native package:"
case "$(uname -s)" in
    Darwin)
        ARCH="$(uname -m)"
        if [ "$ARCH" = "arm64" ]; then RID="osx-arm64"; else RID="osx-x64"; fi
        DEST="$ROOT_DIR/src/GhosttySharp.Native.OSX/runtimes/$RID/native"
        echo "  cp zig-out/lib/libghostty-terminal.dylib $DEST/"
        ;;
    Linux)
        DEST="$ROOT_DIR/src/GhosttySharp.Native.Linux64/runtimes/linux-x64/native"
        echo "  cp zig-out/lib/libghostty-terminal.so $DEST/"
        ;;
esac
echo ""
echo "Or run: ./scripts/build-native.sh  (builds everything including this library)"
case "$(uname)" in
    Darwin)
        echo "  cp zig-out/lib/libghostty-terminal.dylib ../../native/osx-arm64/"
        ;;
    Linux)
        echo "  cp zig-out/lib/libghostty-terminal.so ../../native/linux-x64/"
        ;;
esac
