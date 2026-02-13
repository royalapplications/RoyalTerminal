#!/usr/bin/env bash
# run-integration-tests.sh — Build native lib and run integration tests
#
# Usage:
#   ./scripts/run-integration-tests.sh              # Build + test
#   ./scripts/run-integration-tests.sh --skip-build  # Skip native build, run tests only
#   ./scripts/run-integration-tests.sh --verbose     # Verbose test output

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
GHOSTTY_DIR="$ROOT_DIR/external/ghostty"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

info()  { echo -e "${GREEN}[INFO]${NC} $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
error() { echo -e "${RED}[ERROR]${NC} $*" >&2; }
step()  { echo -e "${CYAN}[STEP]${NC} $*"; }

SKIP_BUILD=false
VERBOSE=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-build) SKIP_BUILD=true; shift ;;
        --verbose)    VERBOSE="--verbosity normal"; shift ;;
        --help)
            echo "Usage: $0 [--skip-build] [--verbose] [--help]"
            echo ""
            echo "Build the native libghostty-vt library and run integration tests."
            echo ""
            echo "Options:"
            echo "  --skip-build  Skip native Zig build, use existing library"
            echo "  --verbose     Show detailed test output"
            echo "  --help        Show this help"
            exit 0
            ;;
        *) error "Unknown option: $1"; exit 1 ;;
    esac
done

# Detect platform
OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
    Darwin)
        if [ "$ARCH" = "arm64" ]; then
            RID="osx-arm64"
        else
            RID="osx-x64"
        fi
        LIB_NAME="libghostty-vt.dylib"
        ;;
    Linux)
        RID="linux-x64"
        LIB_NAME="libghostty-vt.so"
        ;;
    *)
        error "Unsupported platform: $OS"
        exit 1
        ;;
esac

NATIVE_LIB="$ROOT_DIR/native/$RID/$LIB_NAME"

# ─────────────────── Step 1: Build Native Library ────────────────────

if [ "$SKIP_BUILD" = false ]; then
    step "1/4 — Building native libghostty-vt..."

    if ! command -v zig &>/dev/null; then
        error "Zig not found in PATH. Install with: brew install zig"
        exit 1
    fi

    info "Zig version: $(zig version)"

    if [ ! -d "$GHOSTTY_DIR" ] || [ ! -f "$GHOSTTY_DIR/build.zig" ]; then
        error "Ghostty submodule not found. Run: git submodule update --init"
        exit 1
    fi

    cd "$GHOSTTY_DIR"
    info "Building libghostty-vt with ReleaseFast..."
    zig build lib-vt -Doptimize=ReleaseFast 2>&1

    # Find the built library
    BUILT_LIB=$(find zig-out -name "libghostty-vt*" -type f \( -name "*.dylib" -o -name "*.so" \) | head -1)
    if [ -z "$BUILT_LIB" ]; then
        error "libghostty-vt not found after build"
        exit 1
    fi

    info "Built: $BUILT_LIB"
    mkdir -p "$ROOT_DIR/native/$RID"
    cp "$BUILT_LIB" "$NATIVE_LIB"
    info "Copied to: $NATIVE_LIB"

    # Also copy headers
    if [ -d "zig-out/include/ghostty" ]; then
        mkdir -p "$ROOT_DIR/native/include"
        cp -r zig-out/include/ghostty "$ROOT_DIR/native/include/"
        info "Headers copied to: $ROOT_DIR/native/include/"
    fi

    cd "$ROOT_DIR"
else
    step "1/4 — Skipping native build (--skip-build)"
fi

# ─────────────────── Step 2: Verify Native Library ───────────────────

step "2/4 — Verifying native library..."

if [ ! -f "$NATIVE_LIB" ]; then
    error "Native library not found: $NATIVE_LIB"
    error "Run without --skip-build to build it first."
    exit 1
fi

info "Library: $(file "$NATIVE_LIB")"

# Verify expected symbols
EXPECTED_SYMBOLS=("ghostty_paste_is_safe" "ghostty_osc_new" "ghostty_sgr_new" "ghostty_key_encoder_new" "ghostty_key_event_new")
MISSING=0
for sym in "${EXPECTED_SYMBOLS[@]}"; do
    if ! nm -gU "$NATIVE_LIB" 2>/dev/null | grep -q "_$sym"; then
        error "Missing symbol: $sym"
        MISSING=$((MISSING + 1))
    fi
done

if [ $MISSING -gt 0 ]; then
    error "$MISSING expected symbols missing from native library"
    exit 1
fi

TOTAL_SYMBOLS=$(nm -gU "$NATIVE_LIB" 2>/dev/null | grep ' T _ghostty' | wc -l | tr -d ' ')
info "All expected symbols present ($TOTAL_SYMBOLS total exports)"

# ─────────────────── Step 3: Build Test Project ──────────────────────

step "3/4 — Building integration test project..."

cd "$ROOT_DIR"
dotnet build tests/GhosttySharp.IntegrationTests/GhosttySharp.IntegrationTests.csproj -c Debug 2>&1

# Verify native lib was copied to output
OUTPUT_DIR="tests/GhosttySharp.IntegrationTests/bin/Debug/net10.0"
if [ -f "$OUTPUT_DIR/$LIB_NAME" ]; then
    info "Native library present in test output: $OUTPUT_DIR/$LIB_NAME"
else
    warn "Native library not in test output, copying manually..."
    cp "$NATIVE_LIB" "$OUTPUT_DIR/"
    info "Copied native library to test output"
fi

# ─────────────────── Step 4: Run Integration Tests ───────────────────

step "4/4 — Running native integration tests..."

dotnet test tests/GhosttySharp.IntegrationTests/GhosttySharp.IntegrationTests.csproj \
    $VERBOSE \
    --no-build \
    2>&1

echo ""
info "=== All integration tests passed! ==="
info "Native library: $NATIVE_LIB"
info "Platform: $OS/$ARCH ($RID)"
