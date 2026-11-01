#!/usr/bin/env bash
set -euo pipefail
# Test the packaged APK, not a fast-deployment installation with assemblies in a separate directory.
sdk=${ANDROID_SDK_ROOT:-${ANDROID_HOME:?}};export PATH="$sdk/platform-tools:$sdk/emulator:$sdk/cmdline-tools/latest/bin:$PATH"
image='system-images;android-35;google_apis;x86_64'; sdkmanager "$image" 'emulator'
echo no | avdmanager create avd -n workspace-ci -k "$image" --force
sudo chmod a+rw /dev/kvm
emulator -avd workspace-ci -no-window -no-audio -no-boot-anim -no-snapshot -gpu swiftshader_indirect >"$RUNNER_TEMP/emulator.log" 2>&1 & pid=$!
trap 'adb emu kill >/dev/null 2>&1 || :;kill "$pid" 2>/dev/null || :' EXIT
(timeout 300 bash -c 'adb wait-for-device; until [[ $(adb shell getprop sys.boot_completed | tr -d "\r") == 1 ]]; do sleep 2; done')
adb shell input keyevent 82;adb install -r out/workspace-debug.apk
pkg=com.royalapplications.experimental.workspace
adb shell am start -W -n "$pkg/.MainActivity"; sleep 8
if ! adb shell pidof "$pkg" | grep -Eq '[0-9]';then adb logcat -d -s AndroidRuntime mono DOTNET;exit 1;fi
adb exec-out screencap -p >out/android-launch.png
