#!/usr/bin/env bash
# Install the packaged APK and verify that the resumed activity belongs to it.
set -euo pipefail
sdk=${ANDROID_SDK_ROOT:-${ANDROID_HOME:?}}
export PATH="$sdk/platform-tools:$sdk/emulator:$sdk/cmdline-tools/latest/bin:$PATH" ANDROID_SERIAL=emulator-5554
# avdmanager and emulator can otherwise resolve their .ini indexes under different homes.
export ANDROID_USER_HOME="$RUNNER_TEMP/rt-android" ANDROID_EMULATOR_HOME="$RUNNER_TEMP/rt-android" ANDROID_AVD_HOME="$RUNNER_TEMP/rt-android/avd"
image='system-images;android-35;default;x86_64'; pkg=com.royalapplications.experimental.workspace
mkdir -p "$ANDROID_AVD_HOME" out
sdkmanager "$image" emulator >"$RUNNER_TEMP/sdkmanager.log" || { tail -60 "$RUNNER_TEMP/sdkmanager.log"; exit 1; }
avdmanager create avd -n workspace-ci -k "$image" --path "$ANDROID_AVD_HOME/workspace-ci.avd" --device pixel_2 --force <<< no
test -s "$ANDROID_AVD_HOME/workspace-ci.avd/config.ini"
if [[ ! -e $ANDROID_AVD_HOME/workspace-ci.ini ]];then
  printf 'avd.ini.encoding=UTF-8\npath=%s\ntarget=android-35\n' "$ANDROID_AVD_HOME/workspace-ci.avd" >"$ANDROID_AVD_HOME/workspace-ci.ini"
fi
emulator -list-avds | grep -Fx workspace-ci
sudo setfacl -m "u:$(id -un):rw" /dev/kvm; emulator -accel-check; adb start-server
emulator @workspace-ci -port 5554 -accel on -no-window -no-audio -no-boot-anim -no-snapshot -gpu swiftshader -feature -Vulkan -cores 2 -memory 2048 -partition-size 2048 >"$RUNNER_TEMP/workspace-emulator.log" 2>&1 & emu=$!
cleanup(){ code=$?; if ((code));then tail -100 "$RUNNER_TEMP/workspace-emulator.log" >&2;timeout 6 adb logcat -d -b crash -t 150 >&2 || :;df -h "$HOME" >&2;fi;kill "$emu" 2>/dev/null || :;wait "$emu" 2>/dev/null || :; }
trap cleanup EXIT
booted=false
for ((i=0;i<180;i++));do
  kill -0 "$emu" 2>/dev/null || { echo 'The emulator exited before boot' >&2;exit 1; }
  if [[ $(timeout 3 adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r' || :) == 1 ]];then booted=true;break;fi;sleep 2
done
[[ $booted == true ]] || { echo 'Android boot was not acknowledged' >&2;exit 1; }
adb shell wm dismiss-keyguard;adb install -r out/workspace-debug.apk;adb shell am start -W -n "$pkg/.MainActivity"
for i in {1..30};do activity=$(adb shell dumpsys activity activities);if grep -E 'topResumedActivity|mResumedActivity' <<< "$activity" | grep -F "$pkg" >/dev/null;then break;fi;sleep 1;done
sleep 8;adb shell pidof "$pkg" | grep -Eq '[0-9]'
activity=$(adb shell dumpsys activity activities);grep -E 'topResumedActivity|mResumedActivity' <<< "$activity" | grep -F "$pkg"
adb exec-out screencap -p >out/android-launch.png;test -s out/android-launch.png
