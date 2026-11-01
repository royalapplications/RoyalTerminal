#!/usr/bin/env bash
set -euo pipefail
export DEVELOPER_DIR="${WORKSPACE_XCODE_ROOT:-/Applications/Xcode_26.6.app/Contents/Developer}"
xcodebuild -version; xcrun --sdk macosx --find install_name_tool; sdk=$(xcrun --sdk iphonesimulator --show-sdk-version)
project=samples/RoyalTerminal.Multiplexing.Mobile.iOS; bundle=com.royalapplications.experimental.workspace
mkdir -p out
# Use the simulator runtime for the SDK we built against, not an unrelated newer Xcode runtime.
devices=$(xcrun simctl list devices available -j)
device=$(python3 -c 'import json,sys; rt="com.apple.CoreSimulator.SimRuntime.iOS-"+sys.argv[1].replace(".","-"); ds=json.load(sys.stdin)["devices"]; items=[d for k,vs in ds.items() if k==rt or k.startswith(rt+"-") for d in vs if d.get("isAvailable") and d["name"].startswith("iPhone")]; assert items, "Missing iPhone simulator for "+rt;print(items[0]["udid"])' "$sdk" <<< "$devices")
xcrun simctl bootstatus "$device" -b >"$RUNNER_TEMP/workspace-ios-boot.txt" 2>&1 & boot=$!
dotnet build "$project" -c Debug -r iossimulator-arm64 -p:MtouchSdkRoot="$DEVELOPER_DIR"
wait "$boot" || { tail -50 "$RUNNER_TEMP/workspace-ios-boot.txt";exit 1; }
app=$(find "$project/bin/Debug" -type d -name '*.app' -print -quit); test -n "$app"
exe=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app/Info.plist")
xcrun simctl install "$device" "$app"
stdout="$RUNNER_TEMP/workspace-ios.stdout";stderr="$RUNNER_TEMP/workspace-ios.stderr"
# Capture stderr, on which Mono reports managed exceptions, even if the process dies before the first frame.
if ! launch=$(xcrun simctl launch --stdout="$stdout" --stderr="$stderr" "$device" "$bundle"); then echo 'simctl launch rejected the bundle' >&2; exit 1; fi
printf '%s\n' "$launch"; pid=${launch##* }; [[ $pid =~ ^[0-9]+$ ]]
sleep 8
if ! kill -0 "$pid" 2>/dev/null; then
  echo 'Simulator process terminated during startup' >&2
  tail -100 "$stdout" "$stderr" 2>/dev/null >&2 || :
  xcrun simctl spawn "$device" log show --last 2m --style compact --predicate "process == \"$exe\"" 2>/dev/null | tail -100 >&2 || :
  exit 1
fi
xcrun simctl io "$device" screenshot out/ios-launch.png; test -s out/ios-launch.png
ditto -c -k --keepParent "$app" out/workspace-ios-simulator.zip
