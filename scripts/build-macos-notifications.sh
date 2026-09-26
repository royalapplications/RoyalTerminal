#!/usr/bin/env bash
set -euo pipefail
notifications_root="$(cd "$(dirname "$0")/.." && pwd)"
notifications_output="$notifications_root/src/RoyalTerminal.Avalonia.App/runtimes/osx/native"
if [[ "${1:-}" == "test" ]]; then
  notifications_test_directory="$(mktemp -d "${TMPDIR:-/tmp}/royalterminal-notification-tests.XXXXXX")"
  trap 'rm -rf -- "$notifications_test_directory"' EXIT
  xcrun clang -Wall -Wextra -Werror -O0 -g -fobjc-arc -fblocks -mmacosx-version-min=13.0 \
    -framework AppKit -framework Foundation -framework UserNotifications \
    "$notifications_root/native/macos-notifications/tests.m" -o "$notifications_test_directory/tests"
  "$notifications_test_directory/tests"
  exit 0
fi
mkdir -p "$notifications_output"
xcrun clang -Wall -Wextra -Werror -O2 -fobjc-arc -fblocks -fvisibility=hidden \
  -arch arm64 -arch x86_64 -mmacosx-version-min=13.0 \
  -dynamiclib -install_name @rpath/libroyalterminal-notifications.dylib \
  -framework AppKit -framework Foundation -framework UserNotifications \
  "$notifications_root/native/macos-notifications/notifications.m" \
  -o "$notifications_output/libroyalterminal-notifications.dylib"
