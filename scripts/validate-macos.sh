#!/usr/bin/env bash
# validate-macos.sh — Full macOS validation: build native, run all tests, verify solution
#
# Usage:
#   ./scripts/validate-macos.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

info()  { echo -e "${GREEN}[INFO]${NC} $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
error() { echo -e "${RED}[ERROR]${NC} $*" >&2; }
step()  { echo -e "${CYAN}${BOLD}[STEP]${NC} $*"; }
pass()  { echo -e "${GREEN}  ✓${NC} $*"; }
fail()  { echo -e "${RED}  ✗${NC} $*"; }

FAILURES=0
TESTS_RUN=0

check() {
    TESTS_RUN=$((TESTS_RUN + 1))
    if eval "$2" &>/dev/null; then
        pass "$1"
    else
        fail "$1"
        FAILURES=$((FAILURES + 1))
    fi
}

echo ""
echo -e "${BOLD}═══════════════════════════════════════════════════════════${NC}"
echo -e "${BOLD}  GhosttySharp — macOS Validation Suite${NC}"
echo -e "${BOLD}═══════════════════════════════════════════════════════════${NC}"
echo ""

cd "$ROOT_DIR"

# ─────────────────── Prerequisites ───────────────────────────────────

step "1/7 — Checking prerequisites..."

check "dotnet SDK installed" "command -v dotnet"
check "dotnet SDK version" "dotnet --version"
check "Zig compiler installed" "command -v zig"
check "Zig version ≥ 0.15" "zig version | grep -qE '0\.(1[5-9]|[2-9])'"
check "Git submodule present" "test -f external/ghostty/build.zig"
check "macOS platform" "test $(uname -s) = Darwin"

echo ""

# ─────────────────── Native Build ────────────────────────────────────

step "2/7 — Building native libghostty-vt..."

ARCH="$(uname -m)"
RID="osx-$( [ "$ARCH" = "arm64" ] && echo "arm64" || echo "x64" )"
LIB_NAME="libghostty-vt.dylib"

cd external/ghostty
zig build lib-vt -Doptimize=ReleaseFast 2>&1
BUILT_LIB=$(find zig-out -name "libghostty-vt*" -type f -name "*.dylib" | head -1)
cd "$ROOT_DIR"

if [ -n "$BUILT_LIB" ]; then
    mkdir -p "native/$RID"
    cp "external/ghostty/$BUILT_LIB" "native/$RID/$LIB_NAME"
    pass "Native library built: $RID/$LIB_NAME"

    # Copy headers
    if [ -d "external/ghostty/zig-out/include/ghostty" ]; then
        mkdir -p "native/include"
        cp -r external/ghostty/zig-out/include/ghostty native/include/
        pass "Headers copied"
    fi
else
    fail "Native library build failed"
    FAILURES=$((FAILURES + 1))
fi
TESTS_RUN=$((TESTS_RUN + 1))

echo ""

# ─────────────────── Verify Native Library ───────────────────────────

step "3/7 — Verifying native library..."

NATIVE_LIB="native/$RID/$LIB_NAME"
check "Library file exists" "test -f $NATIVE_LIB"
check "Library is Mach-O arm64" "file $NATIVE_LIB | grep -q 'arm64\|x86_64'"

# Check symbols
for sym in ghostty_paste_is_safe ghostty_osc_new ghostty_osc_free ghostty_osc_next ghostty_osc_end \
    ghostty_osc_command_type ghostty_sgr_new ghostty_sgr_free ghostty_sgr_next ghostty_sgr_set_params \
    ghostty_key_event_new ghostty_key_event_free ghostty_key_encoder_new ghostty_key_encoder_free \
    ghostty_key_encoder_encode; do
    check "Symbol: $sym" "nm -gU $NATIVE_LIB | grep -q _$sym"
done

SYMBOL_COUNT=$(nm -gU "$NATIVE_LIB" | grep ' T _ghostty' | wc -l | tr -d ' ')
info "Total exported ghostty symbols: $SYMBOL_COUNT"

echo ""

# ─────────────────── Solution Build ──────────────────────────────────

step "4/7 — Building full solution..."

dotnet build GhosttySharp.sln 2>&1
if [ $? -eq 0 ]; then
    pass "Solution build succeeded"
else
    fail "Solution build failed"
    FAILURES=$((FAILURES + 1))
fi
TESTS_RUN=$((TESTS_RUN + 1))

echo ""

# ─────────────────── Unit Tests ──────────────────────────────────────

step "5/7 — Running unit tests..."

UNIT_OUTPUT=$(dotnet test tests/GhosttySharp.Tests/GhosttySharp.Tests.csproj --verbosity quiet 2>&1)
if echo "$UNIT_OUTPUT" | grep -qE 'succeeded|Passed!'; then
    UNIT_COUNT=$(echo "$UNIT_OUTPUT" | grep -oE '(succeeded: |Passed:[ ]+)[0-9]+' | grep -oE '[0-9]+' | tail -1)
    pass "Unit tests passed: $UNIT_COUNT tests"
else
    fail "Unit tests failed"
    echo "$UNIT_OUTPUT"
    FAILURES=$((FAILURES + 1))
fi
TESTS_RUN=$((TESTS_RUN + 1))

echo ""

# ─────────────────── Integration Tests ───────────────────────────────

step "6/7 — Running native integration tests..."

# Ensure native lib is in test output
dotnet build tests/GhosttySharp.IntegrationTests/GhosttySharp.IntegrationTests.csproj 2>&1
INT_OUTPUT_DIR="tests/GhosttySharp.IntegrationTests/bin/Debug/net10.0"
if [ ! -f "$INT_OUTPUT_DIR/$LIB_NAME" ]; then
    cp "$NATIVE_LIB" "$INT_OUTPUT_DIR/"
fi

INT_OUTPUT=$(dotnet test tests/GhosttySharp.IntegrationTests/GhosttySharp.IntegrationTests.csproj --no-build --verbosity quiet 2>&1)
if echo "$INT_OUTPUT" | grep -qE 'succeeded|Passed!'; then
    INT_COUNT=$(echo "$INT_OUTPUT" | grep -oE '(succeeded: |Passed:[ ]+)[0-9]+' | grep -oE '[0-9]+' | tail -1)
    pass "Integration tests passed: $INT_COUNT tests"
else
    fail "Integration tests failed"
    echo "$INT_OUTPUT"
    FAILURES=$((FAILURES + 1))
fi
TESTS_RUN=$((TESTS_RUN + 1))

echo ""

# ─────────────────── Native Library Copy Validation ──────────────────

step "7/7 — Validating native library deployment..."

check "Library in native output dir" "test -f native/$RID/$LIB_NAME"
check "Library in test output dir" "test -f $INT_OUTPUT_DIR/$LIB_NAME"

# Check the library can be loaded by .NET
check "Library load check (otool)" "otool -L $NATIVE_LIB"

echo ""

# ─────────────────── Summary ─────────────────────────────────────────

echo -e "${BOLD}═══════════════════════════════════════════════════════════${NC}"
if [ $FAILURES -eq 0 ]; then
    echo -e "${GREEN}${BOLD}  ALL $TESTS_RUN CHECKS PASSED${NC}"
else
    echo -e "${RED}${BOLD}  $FAILURES of $TESTS_RUN CHECKS FAILED${NC}"
fi
echo -e "${BOLD}═══════════════════════════════════════════════════════════${NC}"
echo ""
echo "  Platform:      macOS $(sw_vers -productVersion 2>/dev/null || echo 'unknown') ($ARCH)"
echo "  Zig:           $(zig version 2>/dev/null || echo 'N/A')"
echo "  .NET:          $(dotnet --version 2>/dev/null || echo 'N/A')"
echo "  Native lib:    $NATIVE_LIB"
echo "  Symbols:       $SYMBOL_COUNT"
echo ""

exit $FAILURES
