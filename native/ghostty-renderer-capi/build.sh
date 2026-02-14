#!/bin/bash
# build.sh — Build libghostty-renderer-capi for the current platform.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

if ! command -v zig &> /dev/null; then
    echo "Error: Zig is not installed or not in PATH."
    exit 1
fi

case "${1:-debug}" in
    release|Release|ReleaseFast)
        echo "Building libghostty-renderer-capi (ReleaseFast)..."
        zig build -Doptimize=ReleaseFast
        ;;
    release-safe|ReleaseSafe)
        echo "Building libghostty-renderer-capi (ReleaseSafe)..."
        zig build -Doptimize=ReleaseSafe
        ;;
    clean)
        rm -rf zig-out .zig-cache
        echo "Cleaned."
        exit 0
        ;;
    debug|Debug)
        echo "Building libghostty-renderer-capi (Debug)..."
        zig build
        ;;
    test)
        echo "Running renderer-capi tests..."
        zig build test
        exit 0
        ;;
    sample)
        echo "Running Metal texture smoke sample..."
        zig build sample-metal
        exit 0
        ;;
    *)
        echo "Usage: $0 [debug|release|release-safe|clean|test|sample]"
        exit 1
        ;;
esac

if [ -d "zig-out/lib" ]; then
    ls -la zig-out/lib/libghostty-renderer-capi*
fi
if [ -d "zig-out/include" ]; then
    ls -la zig-out/include/ghostty_renderer.h
fi
