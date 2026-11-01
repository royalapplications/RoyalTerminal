#!/usr/bin/env bash
set -euo pipefail
# The net10 workload ships a backwards-compatible iOS 26.0 pack. Pair it with its supported compiler, not the runner default.
xcode=/Applications/Xcode_26.0.app/Contents/Developer
test -x "$xcode/usr/bin/xcodebuild"
DEVELOPER_DIR="$xcode" dotnet build samples/RoyalTerminal.Multiplexing.Mobile.iOS -c Debug -r iossimulator-arm64 -p:MtouchSdkRoot="$xcode"
app=$(find samples/RoyalTerminal.Multiplexing.Mobile.iOS/bin/Debug -type d -name '*.app' -print -quit);test -n "$app"
# Boot using the runner's default Xcode and installed simulator runtime. The deployment target is iOS 15+, not iOS 27.
device=$(xcrun simctl list devices available -j | python3 -c 'import json,sys;print(next(d["udid"] for rt,ds in sorted(json.load(sys.stdin)["devices"].items(),reverse=True) if "iOS" in rt for d in ds if d["name"].startswith("iPhone")))')
xcrun simctl boot "$device" || xcrun simctl bootstatus "$device" -b
xcrun simctl bootstatus "$device" -b; xcrun simctl install "$device" "$app"
pid=$(xcrun simctl launch "$device" com.royalapplications.experimental.workspace | awk '{print $NF}');sleep 8;kill -0 "$pid"
mkdir -p out;xcrun simctl io "$device" screenshot out/ios-launch.png;ditto -c -k --keepParent "$app" out/workspace-ios-simulator.zip
