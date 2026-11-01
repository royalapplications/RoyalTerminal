#!/bin/bash
# GUI and CLI share a launchd-owned broker, not the lifetime of an application window.
set -euo pipefail
umask 077
export PATH=/usr/bin:/bin:/usr/sbin:/sbin
base=$(cd -P "$(dirname "$0")" && pwd); label=com.royalapplications.experimental.workspace.broker
uid=$(id -u); [[ $uid != 0 ]] || { echo 'Run the personal workspace as a login user, not root.' >&2; exit 1; }
state="$HOME/Library/Application Support/RoyalTerminal/Personal"; runtime="$(getconf DARWIN_USER_TEMP_DIR)royalterminal.workspace"; sock="$runtime/host.sock"; target="gui/$uid/$label"
private_dir(){ [[ -d $1 ]] || mkdir -p "$1"; [[ ! -L $1 && -O $1 && $(stat -f '%Lp' "$1") == 700 ]] || { echo 'Unsafe private workspace directory.' >&2; return 1; }; }
private_dir "$runtime"; private_dir "$state"
credential="$state/admin-token"; plist="$state/broker.plist";
if [[ ! -e $credential ]]; then
  temp=$(mktemp "$state/.secret.XXXXXX"); trap 'rm -f "$temp"' EXIT
  openssl rand -hex 32 >"$temp"; ln "$temp" "$credential" 2>/dev/null || [[ -f $credential ]]; rm "$temp";trap - EXIT
fi
[[ -f $credential && ! -L $credential && -O $credential && $(stat -f '%Lp' "$credential") == 600 ]] || { echo 'Unsafe workspace credential.' >&2; exit 1; }
IFS= read -r ROYALTERMINAL_MUX_TOKEN <"$credential"; [[ $ROYALTERMINAL_MUX_TOKEN =~ ^[0-9a-f]{64}$ ]]; export ROYALTERMINAL_MUX_TOKEN ROYALTERMINAL_MUX_SOCKET="$sock" ROYALTERMINAL_MUX_URL=http://localhost/
case "${1:-open}" in
  serve) export ROYALTERMINAL_MUX_CONFIG="$state/automation.json"; exec "$base/server/RoyalTerminal.Multiplexing.Server" "--Kestrel:Endpoints:Only:Url=http://unix:$sock";;
  stop) launchctl bootout "$target";exit;;
  status) launchctl print "$target" >/dev/null; curl --noproxy '*' --unix-socket "$sock" -fsS --max-time 3 http://localhost/healthz;exit;;
  cli|start|open|-psn_*) ;;
  *) echo 'Usage: Workspace [open|start|status|cli ARGS...|stop]. Stop ends local terminal processes.' >&2;exit 2;;
esac
if ! launchctl print "$target" >/dev/null 2>&1; then
  tmp=$(mktemp "$state/.agent.XXXXXX");trap 'rm -f "$tmp"' EXIT
  plutil -create xml1 -o "$tmp" -
  plutil -insert Label -string "$label" "$tmp"; plutil -insert ProgramArguments -xml '<array/>' "$tmp"
  plutil -insert ProgramArguments.0 -string /bin/bash "$tmp";plutil -insert ProgramArguments.1 -string "$base/personal.sh" "$tmp";plutil -insert ProgramArguments.2 -string serve "$tmp"
  plutil -insert RunAtLoad -bool YES "$tmp";plutil -insert StandardErrorPath -string "$state/broker.log" "$tmp";plutil -insert StandardOutPath -string "$state/broker.log" "$tmp"
  mv -f "$tmp" "$plist";trap - EXIT
  launchctl bootstrap "gui/$uid" "$plist" 2>/dev/null || launchctl print "$target" >/dev/null
fi
for ((i=0;i<100;i++));do [[ -S $sock ]] && curl --noproxy '*' --unix-socket "$sock" -fsS --max-time 1 http://localhost/healthz >/dev/null 2>&1 && break;sleep .1;done
curl --noproxy '*' --unix-socket "$sock" -fsS --max-time 2 http://localhost/healthz >/dev/null || { echo 'Private broker did not start. See the personal broker log.' >&2;exit 1; }
case ${1:-open} in start) exit 0;; cli) shift;exec "$base/cli/royalterminal-mux" "$@";; *) exec "$base/client/RoyalTerminal.Multiplexing.Desktop";;esac
