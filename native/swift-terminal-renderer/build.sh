#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

echo "Building Swift Terminal Renderer..."
swift build -c release

OUT_DIR="../../src/RoyalTerminal.Rendering.Interop.Swift.Native.OSX/runtimes/osx-arm64/native"
mkdir -p "$OUT_DIR"

echo "Copying library to $OUT_DIR..."
cp .build/release/libSwiftTerminalRenderer.dylib "$OUT_DIR/libswift_terminal_renderer.dylib"

echo "Build complete."
ls -lh "$OUT_DIR/libswift_terminal_renderer.dylib"
