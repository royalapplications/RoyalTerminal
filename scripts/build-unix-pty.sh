#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
output="$root/src/RoyalTerminal.Terminal.Pty.Unix/runtimes/osx/native"
mkdir -p "$output"
xcrun clang -Wall -Wextra -Werror -O2 -arch arm64 -arch x86_64 \
  -mmacosx-version-min=13.0 "$root/native/unix-pty/spawn-helper.c" \
  -o "$output/royalterminal-pty-spawn"
