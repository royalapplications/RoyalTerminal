#!/usr/bin/env bash
# Use an isolated home; do not enable a unit in the CI runner's actual user manager.
set -euo pipefail; umask 077
[[ $# == 1 ]] || { echo 'Expected a published bundle directory' >&2; exit 2; }
bundle=$(realpath "$1"); tmp=$(mktemp -d); pid=''
cleanup(){ if [[ -n $pid ]]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi; rm -rf -- "$tmp"; }; trap cleanup EXIT
export HOME="$tmp/home" XDG_CONFIG_HOME="$tmp/home/.config" XDG_RUNTIME_DIR="$tmp/run"
mkdir -p -m700 "$HOME" "$XDG_RUNTIME_DIR" "$XDG_RUNTIME_DIR/royalterminal-mux"
unset ROYALTERMINAL_MUX_TOKEN ROYALTERMINAL_MUX_SOCKET
"$bundle/install-personal.sh" install
[[ $(stat -c '%a' "$HOME/.config/royalterminal-mux/token") == 600 ]]
token_digest=$(sha256sum "$HOME/.config/royalterminal-mux/token"); first_release=$(readlink -f "$HOME/.local/share/royalterminal-mux/current")
export ROYALTERMINAL_MUX_URL=http://127.0.0.1:17332
"$first_release/launch-personal.sh" >"$tmp/server.log" 2>&1 & pid=$!
socket="$XDG_RUNTIME_DIR/royalterminal-mux/host.sock"; cli="$HOME/.local/bin/royalterminal-mux"; realcli="$first_release/cli/royalterminal-mux.dll"
for ((i=0;i<100;i++)); do
  kill -0 "$pid" 2>/dev/null || { cat "$tmp/server.log" >&2; exit 1; }
  if [[ -S $socket ]] && curl --unix-socket "$socket" -fsS http://localhost/healthz >/dev/null 2>&1; then break; fi; sleep .15
done
[[ -S $socket ]]; curl -fsS --unix-socket "$socket" http://localhost/healthz >/dev/null
! curl -fsS --connect-timeout 1 "$ROYALTERMINAL_MUX_URL/healthz" >/dev/null 2>&1 || { echo 'Unexpected TCP listener' >&2; exit 1; }
[[ $(curl -sS --unix-socket "$socket" -o /dev/null -w '%{http_code}' http://localhost/api/sessions) == 401 ]]
session=$("$cli" new socket-ci | jq -er '.id')
"$cli" run "$session" --label retained --pipe -- /bin/cat >"$tmp/block.json"
block=$(jq -er '.id' "$tmp/block.json"); "$cli" send "$session" "$block" $'SOCKET_LIVE\n' >/dev/null
for ((i=0;i<40;i++)); do if "$cli" capture "$session" "$block" | jq -e '.content|contains("SOCKET_LIVE")' >/dev/null; then break; fi; sleep .05; done
"$cli" capture "$session" "$block" | jq -e '.content|contains("SOCKET_LIVE")' >/dev/null
# Stage an upgrade. Neither the active release nor its OS processes are replaced.
"$bundle/install-personal.sh" install
[[ "$token_digest" == "$(sha256sum "$HOME/.config/royalterminal-mux/token")" ]]; kill -0 "$pid"
"$cli" show "$session" | jq -e --arg id "$block" '.blocks[]|select(.id==$id and .state=="Running")' >/dev/null
# Sending explicit endpoint requests without a credential may not silently load the local admin token.
status=0; "$cli" --endpoint http://127.0.0.1:17332 sessions >/dev/null 2>&1 || status=$?; [[ $status == 2 ]]
# Override any HTTP URL; only the Unix socket is contacted. A missing socket must fail closed.
export ROYALTERMINAL_MUX_TOKEN=$(cat "$HOME/.config/royalterminal-mux/token")
status=0; dotnet "$realcli" --socket "$tmp/missing" sessions >/dev/null 2>&1 || status=$?; [[ $status == 1 ]]
"$cli" close "$session" >/dev/null
kill "$pid"; wait "$pid"; pid=''
"$bundle/install-personal.sh" uninstall
[[ ! -e "$cli" && ! -e "$XDG_CONFIG_HOME/systemd/user/royalterminal-mux.service" && -f "$HOME/.config/royalterminal-mux/token" ]]
echo 'Verified private Unix listener, authorization, real process I/O, non-disruptive upgrade and uninstall.'
