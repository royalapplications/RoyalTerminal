#!/bin/bash
# Build on the target Mac architecture. No automatic identity discovery, key imports or external submissions.
set -euo pipefail
cd "$(dirname "$0")/../.."
out=${1:?usage: package.sh NEW_OUTPUT_DIRECTORY}; [[ ! -e $out ]] || { echo 'Output already exists.' >&2; exit 2; }; mkdir -p "$out";out=$(cd "$out" && pwd -P)
case $(uname -m) in arm64) rid=osx-arm64;; x86_64) rid=osx-x64;; *) exit 2;; esac
identity=${WORKSPACE_CODESIGN_IDENTITY:?set to - for local ad-hoc signing or a Developer ID Application identity}
if [[ $identity != - && $identity != 'Developer ID Application: '* ]]; then echo 'A Developer ID Application identity is required for distribution.' >&2;exit 2;fi
if [[ $identity == - && -n ${WORKSPACE_NOTARY_PROFILE:-} ]];then echo 'Ad-hoc builds cannot be notarized.' >&2;exit 2;fi
app="$out/RoyalTerminal Workspace.app";contents="$app/Contents";res="$contents/Resources";mkdir -p "$contents/MacOS" "$res"
for entry in 'Desktop:client' 'Server:server' 'Cli:cli';do name=RoyalTerminal.Multiplexing.${entry%%:*};dest="$res/${entry#*:}";dotnet publish "samples/$name/$name.csproj" -c Release -r "$rid" --self-contained true -p:PublishTrimmed=false -o "$dest";done
[[ -x "$res/server/royalterminal-pty-spawn" && -x "$res/server/RoyalTerminal.Multiplexing.Server" && -x "$res/cli/royalterminal-mux" && -x "$res/client/RoyalTerminal.Multiplexing.Desktop" ]]
# This local-only bundle does not serve a browser frontend over TCP. Include license and provenance.
install -m755 deploy/macos/personal.sh "$res/personal.sh";cp LICENSE "$res/LICENSE";git rev-parse HEAD >"$res/SOURCE_COMMIT"
xcrun clang -Wall -Wextra -Werror -O2 deploy/macos/workspace-launcher.c -o "$contents/MacOS/Workspace"
cat >"$contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?><plist version="1.0"><dict><key>CFBundleIdentifier</key><string>com.royalapplications.experimental.workspace</string><key>CFBundleName</key><string>RoyalTerminal Workspace</string><key>CFBundleExecutable</key><string>Workspace</string><key>CFBundlePackageType</key><string>APPL</string><key>CFBundleShortVersionString</key><string>0.1</string><key>CFBundleVersion</key><string>1</string><key>NSHighResolutionCapable</key><true/></dict></plist>
PLIST
cat >"$out/jit.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?><plist version="1.0"><dict><key>com.apple.security.cs.allow-jit</key><true/></dict></plist>
PLIST
stamp=(--timestamp);[[ $identity != - ]] || stamp=(--timestamp=none)
while IFS= read -r -d '' file;do
  case $(/usr/bin/file -b "$file") in *Mach-O*) opts=();case "$file" in */client/RoyalTerminal.Multiplexing.Desktop|*/server/RoyalTerminal.Multiplexing.Server|*/cli/royalterminal-mux) opts=(--entitlements "$out/jit.plist");; esac
  /usr/bin/codesign --force --sign "$identity" --options runtime "${stamp[@]}" ${opts[@]+"${opts[@]}"} "$file";;esac
done < <(find "$res" -type f -print0)
/usr/bin/codesign --force --sign "$identity" --options runtime "${stamp[@]}" "$app";codesign --verify --deep --strict "$app";rm "$out/jit.plist"
zip="$out/workspace-$rid.zip";ditto -c -k --keepParent "$app" "$zip"
if [[ -n ${WORKSPACE_NOTARY_PROFILE:-} ]];then
  xcrun notarytool submit "$zip" --keychain-profile "$WORKSPACE_NOTARY_PROFILE" --wait --output-format json >"$out/notary.json"
  [[ $(plutil -extract status raw -o - "$out/notary.json") == Accepted ]];xcrun stapler staple "$app";xcrun stapler validate "$app";spctl --assess --type execute "$app";rm "$zip";ditto -c -k --keepParent "$app" "$zip"
fi
(cd "$out" && shasum -a 256 "workspace-$rid.zip" > SHA256SUMS)
