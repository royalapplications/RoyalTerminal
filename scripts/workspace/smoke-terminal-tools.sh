#!/usr/bin/env bash
# Exercise the real CLI, permissions, DEC application cursor mode and raw stdin. No virtual keyboard or OS layout assumptions.
set -euo pipefail
cd "$(dirname "$0")/../.."; umask 077
tmp=$(mktemp -d); export ROYALTERMINAL_MUX_SOCKET="$tmp/host.sock"
ROYALTERMINAL_MUX_TOKEN=$(openssl rand -hex 32); export ROYALTERMINAL_MUX_TOKEN
cli=(dotnet samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll)
dotnet samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll "--Kestrel:Endpoints:Only:Url=http://unix:$ROYALTERMINAL_MUX_SOCKET" >"$tmp/server" 2>&1 & pid=$!
trap 'kill "$pid" 2>/dev/null || :; wait "$pid" 2>/dev/null || :; rm -rf "$tmp"' EXIT
for i in {1..100};do [[ -S $ROYALTERMINAL_MUX_SOCKET ]] && curl -fsS --unix-socket "$ROYALTERMINAL_MUX_SOCKET" http://localhost/healthz >/dev/null && break;kill -0 "$pid";sleep .1;done
s=$("${cli[@]}" new keyboard | jq -er .id)
b=$("${cli[@]}" run "$s" --pipe -- /usr/bin/python3 -u -c 'import sys;print("\033[?1h\a\033]2;build\aREADY",flush=True);a=sys.stdin.buffer.read(4);print("HEX="+a.hex(),flush=True);sys.exit(37)' | jq -er .id)
for i in {1..80}; do "${cli[@]}" capture "$s" "$b" | jq -e '.content|contains("READY")' >/dev/null && break; sleep .1; done
call(){ curl -fsS --unix-socket "$ROYALTERMINAL_MUX_SOCKET" -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN" -H 'Content-Type: application/json' --data "$1" http://localhost/api/call; }
share=$(call "$(jq -cn --arg s "$s" '{method:"share.create",arguments:{sessionId:$s,role:"observer",lifetimeMinutes:5}}')" | jq -er .token)
if ROYALTERMINAL_MUX_TOKEN="$share" "${cli[@]}" send-key "$s" "$b" enter >/dev/null 2>&1;then echo 'Observer acquired input!' >&2;exit 1;fi
if "${cli[@]}" send-key "$s" "$b" enter no-such-key >/dev/null 2>&1;then echo 'Malformed batch accepted!' >&2;exit 1;fi
"${cli[@]}" send-key "$s" "$b" up ctrl+c >/dev/null
code=0; timeout 10 "${cli[@]}" wait "$s" "$b" >/dev/null || code=$?;test "$code" -eq 37
"${cli[@]}" capture "$s" "$b" | jq -e '.content|contains("HEX=1b4f4103")' >/dev/null
ROYALTERMINAL_MUX_TOKEN="$share" timeout 10 "${cli[@]}" events "$s" "$b" --after 0 >"$tmp/events"
jq -se 'map(.name) == ["terminal.bell","terminal.title_changed","terminal.child_exited"] and .[2].exitCode==37 and (map(.sequence)==[1,2,3])' "$tmp/events" >/dev/null
"${cli[@]}" events "$s" "$b" --after 1 --count 1 | jq -se 'length==1 and .[0].sequence==2' >/dev/null
echo 'Scoped observer could read ordered NDJSON, not send keys. Real child received exactly ESC O A ETX and returned 37.'
