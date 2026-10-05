#!/usr/bin/env bash
# Full macOS Release validation using the shipping native extension build.
# --skip-native-build reuses the current native artifact, still requiring its APIs.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SKIP_NATIVE=false
while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-native-build) SKIP_NATIVE=true; shift ;;
        --help)
            echo "Usage: $0 [--skip-native-build]"
            echo "Validate Release solution, isolated unit batches, startup smoke and required-native integration."
            exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done
if [[ "$(uname -s)" != Darwin ]]; then
    echo "This validation entry point requires macOS." >&2
    exit 1
fi
cd "$ROOT_DIR"
if [[ "$SKIP_NATIVE" == false ]]; then
    "$SCRIPT_DIR/build-native.sh" --release
fi

dotnet build RoyalTerminal.slnx -c Release
# Match CI isolation; test-host/dispatcher lifetime must not change the result.
ROYALTERMINAL_TEST_CONFIGURATION=Release "$SCRIPT_DIR/run-unit-test-batches.sh"
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --no-build \
    --filter '(FullyQualifiedName~TerminalModeResolverTests)|(FullyQualifiedName~Headless_FallbackParity_AutoAndManaged_PreserveKeyboardMousePasteAndResize_WhenHostedInScrollViewer)' --verbosity minimal
ROYALTERMINAL_TEST_CONFIGURATION=Release "$SCRIPT_DIR/run-integration-tests.sh" --skip-build
echo "All macOS Release validation commands passed."
