#!/usr/bin/env bash
# ensure-macos-libghostty.sh — Materialize libghostty.dylib from Ghostty build cache.
#
# Upstream Ghostty currently builds the macOS embed dylib for app-runtime=none
# but does not install it into zig-out/lib. This helper copies the newest
# built dylib from .zig-cache into zig-out/lib so downstream packaging can
# consume a stable path. Keep this as a small, reversible downstream patch.

set -euo pipefail

if [ $# -ne 1 ]; then
    echo "Usage: $0 <ghostty-dir>" >&2
    exit 1
fi

GHOSTTY_DIR="$1"
OUT_DIR="$GHOSTTY_DIR/zig-out/lib"
TARGET_LIB="$OUT_DIR/libghostty.dylib"

if [ -f "$TARGET_LIB" ]; then
    exit 0
fi

mkdir -p "$OUT_DIR"

LATEST_CANDIDATE=$(
    find "$GHOSTTY_DIR/.zig-cache" \
        -type f \
        -name 'libghostty.dylib' \
        -size +1048576c \
        -print 2>/dev/null \
        | xargs ls -t 2>/dev/null \
        | head -n 1
)

if [ -z "${LATEST_CANDIDATE:-}" ]; then
    echo "Could not locate a built libghostty.dylib in $GHOSTTY_DIR/.zig-cache" >&2
    exit 1
fi

cp -L "$LATEST_CANDIDATE" "$TARGET_LIB"
echo "Materialized $TARGET_LIB from $LATEST_CANDIDATE"
