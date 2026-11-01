#!/usr/bin/env bash
set -euo pipefail
# Keep compiler, Mac host utilities and Simulator SDK from the same validated Xcode.
export DEVELOPER_DIR="${WORKSPACE_XCODE_ROOT:-/Applications/Xcode_26.6.app/Contents/Developer}"
xcodebuild -version
xcrun --sdk macosx --find install_name_tool
xcrun --sdk iphonesimulator --show-sdk-version
project=samples/RoyalTerminal.Multiplexing.Mobile.iOS
dotnet build "$project" -c Debug -r iossimulator-arm64 -p:MtouchSdkRoot="$DEVELOPER_DIR"
app=$(find "$project/bin/Debug" -type d -name '*.app' -print -quit); test -n "$app"
device=$(xcrun simctl list devices available -j | python3 -c 'import json,sys;print(next(d["udid"] for rt,ds in sorted(json.load(sys.stdin)["devices"].items(),reverse=True) if "iOS" in rt for d in ds if d["name"].startswith("iPhone")))')
xcrun simctl bootstatus "$device" -b
xcrun simctl install "$device" "$app"
pid=$(xcrun simctl launch "$device" com.royalapplications.experimental.workspace | awk '{print $NF}')
sleep 8; kill -0 "$pid"
mkdir -p out
xcrun simctl io "$device" screenshot out/ios-launch.png
ditto -c -k --keepParent "$app" out/workspace-ios-simulator.zip
