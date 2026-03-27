#!/usr/bin/env bash
# build-native.sh — Build Ghostty native libraries from submodule
#
# Usage:
#   ./scripts/build-native.sh              # Build for current platform
#   ./scripts/build-native.sh --clean      # Clean build
#   ./scripts/build-native.sh --help       # Show usage
#
# Prerequisites:
#   - Zig 0.15.2+ (https://ziglang.org/download/)
#   - Git submodule initialized: git submodule update --init
#
# The script builds libghostty plus the official libghostty-vt API library,
# and also builds the transitional libghostty-terminal and
# libghostty-renderer-capi libraries used by compatibility and renderer interop
# paths. Artifacts are copied to the native NuGet runtime package location.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
GHOSTTY_DIR="$ROOT_DIR/external/ghostty"
NATIVE_OUT_DIR="$ROOT_DIR/native"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

info()  { echo -e "${GREEN}[INFO]${NC} $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
error() { echo -e "${RED}[ERROR]${NC} $*" >&2; }

usage() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Build Ghostty native shared libraries from source."
    echo ""
    echo "Options:"
    echo "  --clean       Clean before building"
    echo "  --release     Build with ReleaseFast optimization (default)"
    echo "  --debug       Build with Debug optimization"
    echo "  --static      Also build static library"
    echo "  --help        Show this help message"
    echo ""
    echo "Prerequisites:"
    echo "  - Zig 0.15.2+ must be in PATH"
    echo "  - Git submodule must be initialized:"
    echo "    git submodule update --init"
}

# Parse arguments
CLEAN=false
OPTIMIZE="-Doptimize=ReleaseFast"
BUILD_STATIC=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --clean)   CLEAN=true; shift ;;
        --release) OPTIMIZE="-Doptimize=ReleaseFast"; shift ;;
        --debug)   OPTIMIZE=""; shift ;;
        --static)  BUILD_STATIC=true; shift ;;
        --help)    usage; exit 0 ;;
        *)         error "Unknown option: $1"; usage; exit 1 ;;
    esac
done

# Verify prerequisites
if ! command -v zig &>/dev/null; then
    error "Zig not found in PATH."
    echo ""
    echo "Install Zig 0.15.2+:"
    echo "  macOS:  brew install zig"
    echo "  Linux:  snap install zig --classic"
    echo "  Manual: https://ziglang.org/download/"
    exit 1
fi

ZIG_VERSION=$(zig version)
info "Zig version: $ZIG_VERSION"

if [ ! -d "$GHOSTTY_DIR" ] || [ ! -f "$GHOSTTY_DIR/build.zig" ]; then
    error "Ghostty submodule not found at $GHOSTTY_DIR"
    echo "Initialize with: git submodule update --init"
    exit 1
fi

# Detect platform
OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
    Darwin)
        PLATFORM="osx"
        LIB_NAME="libghostty.dylib"
        if [ "$ARCH" = "arm64" ]; then
            RID="osx-arm64"
        else
            RID="osx-x64"
        fi
        ;;
    Linux)
        PLATFORM="linux"
        LIB_NAME="libghostty.so"
        RID="linux-x64"
        ;;
    *)
        error "Unsupported platform: $OS"
        echo "Ghostty native builds are supported on macOS and Linux."
        exit 1
        ;;
esac

info "Platform: $PLATFORM ($RID)"
info "Library: $LIB_NAME"

# Enter Ghostty source directory
cd "$GHOSTTY_DIR"

# Clean if requested
if [ "$CLEAN" = true ]; then
    info "Cleaning previous build..."
    rm -rf zig-out .zig-cache
fi

# Build shared library
info "Building libghostty shared library..."
info "Command: zig build $OPTIMIZE -Dtarget=native -Dapp-runtime=none"

zig build $OPTIMIZE -Dtarget=native -Dapp-runtime=none 2>&1 || {
    error "Zig build failed."
    warn "This may be expected if platform-specific dependencies are missing."
    warn "On macOS, ensure Xcode command line tools are installed: xcode-select --install"
    warn "On Linux, ensure GTK4/libadwaita dev packages are installed."
    exit 1
}

# Locate the built library
BUILT_LIB=""
if [ -f "zig-out/lib/$LIB_NAME" ]; then
    BUILT_LIB="zig-out/lib/$LIB_NAME"
elif [ -f "zig-out/lib/libghostty-fat.a" ] && [ "$BUILD_STATIC" = true ]; then
    info "Found fat static library (macOS universal)"
    BUILT_LIB="zig-out/lib/libghostty-fat.a"
fi

# Also check for shared lib in default output
if [ -z "$BUILT_LIB" ]; then
    # Try finding it
    FOUND=$(find zig-out -name "$LIB_NAME" 2>/dev/null | head -1)
    if [ -n "$FOUND" ]; then
        BUILT_LIB="$FOUND"
    fi
fi

if [ -z "$BUILT_LIB" ]; then
    warn "Shared library not found in zig-out/. Listing what was built:"
    find zig-out -type f -name "*.so" -o -name "*.dylib" -o -name "*.a" 2>/dev/null || true
    error "Could not find $LIB_NAME. Build may have produced different artifacts."
    echo ""
    echo "For macOS, Ghostty may only produce an xcframework."
    echo "You may need to extract the library manually."
    exit 1
fi

info "Built library: $BUILT_LIB"

# Locate the official libghostty-vt shared library emitted by the upstream build.
VT_LIB_NAME=""
case "$PLATFORM" in
    osx) VT_LIB_NAME="libghostty-vt.dylib" ;;
    linux) VT_LIB_NAME="libghostty-vt.so" ;;
esac

VT_LIB=""
if [ -n "$VT_LIB_NAME" ]; then
    if [ -f "zig-out/lib/$VT_LIB_NAME" ]; then
        VT_LIB="zig-out/lib/$VT_LIB_NAME"
    else
        VT_FOUND=$(find zig-out -name "$VT_LIB_NAME" 2>/dev/null | head -1)
        if [ -n "$VT_FOUND" ]; then
            VT_LIB="$VT_FOUND"
        fi
    fi
fi

if [ -z "$VT_LIB" ]; then
    warn "Official libghostty-vt artifact not found in zig-out/."
    warn "Managed wrappers for GhosttyTerminal/GhosttyRenderState will require manual native library setup."
else
    info "Built official VT library: $VT_LIB"
fi

# Copy to NuGet native package directory
case "$PLATFORM" in
    osx) NATIVE_RUNTIME_DIR="$ROOT_DIR/src/RoyalTerminal.GhosttySharp.Native.OSX/runtimes/$RID/native" ;;
    linux) NATIVE_RUNTIME_DIR="$ROOT_DIR/src/RoyalTerminal.GhosttySharp.Native.Linux64/runtimes/$RID/native" ;;
esac

mkdir -p "$NATIVE_RUNTIME_DIR"
cp "$BUILT_LIB" "$NATIVE_RUNTIME_DIR/"
info "Copied to: $NATIVE_RUNTIME_DIR/$LIB_NAME"

# Also copy to a central output dir for easy access
mkdir -p "$NATIVE_OUT_DIR/$RID"
cp "$BUILT_LIB" "$NATIVE_OUT_DIR/$RID/"
info "Copied to: $NATIVE_OUT_DIR/$RID/$LIB_NAME"

if [ -n "$VT_LIB" ]; then
    cp -L "$VT_LIB" "$NATIVE_RUNTIME_DIR/$VT_LIB_NAME"
    info "Copied to: $NATIVE_RUNTIME_DIR/$VT_LIB_NAME"

    cp -L "$VT_LIB" "$NATIVE_OUT_DIR/$RID/$VT_LIB_NAME"
    info "Copied to: $NATIVE_OUT_DIR/$RID/$VT_LIB_NAME"
fi

# Copy header
HEADER_DEST="$NATIVE_OUT_DIR/include"
mkdir -p "$HEADER_DEST"
if [ -f "include/ghostty.h" ]; then
    cp "include/ghostty.h" "$HEADER_DEST/"
    info "Copied header: $HEADER_DEST/ghostty.h"
fi

if [ -d "include/ghostty/vt" ]; then
    mkdir -p "$HEADER_DEST/ghostty"
    rm -rf "$HEADER_DEST/ghostty/vt"
    cp -R "include/ghostty/vt" "$HEADER_DEST/ghostty/"
    info "Copied official VT headers: $HEADER_DEST/ghostty/vt"
fi

# Build static library too if requested
if [ "$BUILD_STATIC" = true ]; then
    STATIC_LIB=""
    case "$PLATFORM" in
        osx) STATIC_LIB="zig-out/lib/libghostty-fat.a" ;;
        linux) STATIC_LIB="zig-out/lib/libghostty.a" ;;
    esac

    if [ -n "$STATIC_LIB" ] && [ -f "$STATIC_LIB" ]; then
        cp "$STATIC_LIB" "$NATIVE_OUT_DIR/$RID/"
        info "Static library: $NATIVE_OUT_DIR/$RID/$(basename $STATIC_LIB)"
    fi
fi

# ═══════════════════════════════════════════════════════════════════════
# Build libghostty-terminal (standalone terminal C API library)
# ═══════════════════════════════════════════════════════════════════════

TERMINAL_DIR="$ROOT_DIR/native/ghostty-terminal"

if [ -f "$TERMINAL_DIR/build.zig" ]; then
    info ""
    info "Building libghostty-terminal..."

    cd "$TERMINAL_DIR"

    if [ "$CLEAN" = true ]; then
        rm -rf zig-out .zig-cache
    fi

    case "$PLATFORM" in
        osx) TERMINAL_LIB_NAME="libghostty-terminal.dylib" ;;
        linux) TERMINAL_LIB_NAME="libghostty-terminal.so" ;;
    esac

    zig build $OPTIMIZE 2>&1 || {
        warn "libghostty-terminal build failed — skipping."
        warn "Native VT mode will not be available."
        TERMINAL_LIB_NAME=""
    }

    if [ -n "$TERMINAL_LIB_NAME" ]; then
        # Find the built library (follow symlinks)
        TERMINAL_LIB=""
        if [ -e "zig-out/lib/$TERMINAL_LIB_NAME" ]; then
            TERMINAL_LIB="zig-out/lib/$TERMINAL_LIB_NAME"
        fi

        if [ -n "$TERMINAL_LIB" ]; then
            info "Built: $TERMINAL_LIB"

            # Copy to NuGet native package directory (same runtimes dir as libghostty)
            cp -L "$TERMINAL_LIB" "$NATIVE_RUNTIME_DIR/"
            info "Copied to: $NATIVE_RUNTIME_DIR/$TERMINAL_LIB_NAME"

            # Copy to central output dir
            cp -L "$TERMINAL_LIB" "$NATIVE_OUT_DIR/$RID/"
            info "Copied to: $NATIVE_OUT_DIR/$RID/$TERMINAL_LIB_NAME"

            # Copy header
            if [ -f "include/ghostty_terminal.h" ]; then
                cp "include/ghostty_terminal.h" "$HEADER_DEST/"
                info "Copied header: $HEADER_DEST/ghostty_terminal.h"
            fi
        else
            warn "libghostty-terminal not found in zig-out/lib/"
        fi
    fi

    cd "$GHOSTTY_DIR"
else
    warn "native/ghostty-terminal not found — skipping libghostty-terminal build."
fi

# ═══════════════════════════════════════════════════════════════════════
# Build libghostty-renderer-capi (renderer interop C API)
# ═══════════════════════════════════════════════════════════════════════

RENDERER_DIR="$ROOT_DIR/native/ghostty-renderer-capi"

if [ -f "$RENDERER_DIR/build.zig" ]; then
    info ""
    info "Building libghostty-renderer-capi..."

    cd "$RENDERER_DIR"

    if [ "$CLEAN" = true ]; then
        rm -rf zig-out .zig-cache
    fi

    case "$PLATFORM" in
        osx) RENDERER_LIB_NAME="libghostty-renderer-capi.dylib" ;;
        linux) RENDERER_LIB_NAME="libghostty-renderer-capi.so" ;;
    esac

    zig build $OPTIMIZE 2>&1 || {
        warn "libghostty-renderer-capi build failed — skipping."
        warn "Texture interop managed APIs will require manual native library setup."
        RENDERER_LIB_NAME=""
    }

    if [ -n "$RENDERER_LIB_NAME" ]; then
        RENDERER_LIB=""
        if [ -e "zig-out/lib/$RENDERER_LIB_NAME" ]; then
            RENDERER_LIB="zig-out/lib/$RENDERER_LIB_NAME"
        fi

        if [ -n "$RENDERER_LIB" ]; then
            info "Built: $RENDERER_LIB"

            cp -L "$RENDERER_LIB" "$NATIVE_RUNTIME_DIR/"
            info "Copied to: $NATIVE_RUNTIME_DIR/$RENDERER_LIB_NAME"

            cp -L "$RENDERER_LIB" "$NATIVE_OUT_DIR/$RID/"
            info "Copied to: $NATIVE_OUT_DIR/$RID/$RENDERER_LIB_NAME"

            if [ -f "include/ghostty_renderer.h" ]; then
                cp "include/ghostty_renderer.h" "$HEADER_DEST/"
                info "Copied header: $HEADER_DEST/ghostty_renderer.h"
            fi
        else
            warn "libghostty-renderer-capi not found in zig-out/lib/"
        fi
    fi

    cd "$GHOSTTY_DIR"
else
    warn "native/ghostty-renderer-capi not found — skipping renderer-capi build."
fi

# Print library info
info ""
info "=== Build Complete ==="
info "Library:  $BUILT_LIB"
file "$BUILT_LIB" 2>/dev/null || true
if command -v otool &>/dev/null; then
    info "Dependencies:"
    otool -L "$BUILT_LIB" 2>/dev/null | head -10 || true
elif command -v ldd &>/dev/null; then
    info "Dependencies:"
    ldd "$BUILT_LIB" 2>/dev/null | head -10 || true
fi

info ""
info "Next steps:"
info "  1. Build the .NET solution: dotnet build"
info "  2. Run the demo: dotnet run --project samples/RoyalTerminal.Demo"
info "  3. Pack NuGet packages: dotnet pack -c Release"
