#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN_PATH="$(swift build --package-path "$SCRIPT_DIR" --show-bin-path)/RoyalTerminalMacNativeTabbed"

open "$BIN_PATH"
