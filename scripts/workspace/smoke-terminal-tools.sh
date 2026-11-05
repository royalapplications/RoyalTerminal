#!/usr/bin/env bash
# Exercise two independent cursor spaces against real processes on a private authenticated Unix socket.
set -euo pipefail
cd "$(dirname "$0")/../.."; umask 077
repo=$PWD; tmp=$(mktemp -d); export ROYALTERMINAL_MUX_SOCKET="$tmp/host.sock"
ROYALTERMINAL_MUX_TOKEN=$(openssl rand -hex 32); export ROYALTERMINAL_MUX_TOKEN
cli=(dotnet "$repo/samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll")
dotnet samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll "--Kestrel:Endpoints:Only:Url=http://unix:$ROYALTERMINAL_MUX_SOCKET" >"$tmp/server" 2>&1 & pid=$!
trap 'kill "$pid" 2>/dev/null || :; wait "$pid" 2>/dev/null || :; rm -rf "$tmp"' EXIT
for i in {1..100}; do [[ -S $ROYALTERMINAL_MUX_SOCKET ]] && curl -fsS --unix-socket "$ROYALTERMINAL_MUX_SOCKET" http://localhost/healthz >/dev/null && break; kill -0 "$pid"; sleep .1; done
call(){ curl -fsS --unix-socket "$ROYALTERMINAL_MUX_SOCKET" -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN" -H 'Content-Type: application/json' --data "$1" http://localhost/api/call; }
s=$("${cli[@]}" new keyboard | jq -er .id)
share=$(call "$(jq -cn --arg s "$s" '{method:"share.create",arguments:{sessionId:$s,role:"observer",lifetimeMinutes:5}}')" | jq -er .token)
cat >"$tmp/observe.lua" <<'LUA'
assert(not rex.block_id)
local seen={}
rex.on('com.superlogical.terminal.child_exited',function(ctx,ev)
  assert(ctx.session_id==rex.session_id and ctx.block_id==ev.block_id and ev.block_sequence>=1 and not seen[ctx.block_id])
  seen[ctx.block_id]=ev.exit_code
  if ev.exit_code~=0 then assert(ev.error) end
  local n,total=0,0; for _,code in pairs(seen) do n=n+1;total=total+code end
  if n==2 then assert(total==37); print('CROSS_BLOCK_OK');rex.stop(0) end
end)
local ready=assert(io.open('enrolled','w'));ready:close();rex.after(20,function() rex.stop(91) end)
LUA
(cd "$tmp"; unset ROYALTERMINAL_MUX_BLOCK; export ROYALTERMINAL_MUX_SESSION="$s" ROYALTERMINAL_MUX_TOKEN="$share"; timeout 30 "${cli[@]}" do observe.lua) >"$tmp/watch-out" 2>"$tmp/watch-err" & watcher=$!
for i in {1..100}; do [[ -e $tmp/enrolled ]] && break; if ! kill -0 "$watcher" 2>/dev/null;then cat "$tmp/watch-err" >&2;exit 1;fi; sleep .1; done
test -e "$tmp/enrolled"
b=$("${cli[@]}" run "$s" --pipe -- /usr/bin/python3 -u -c 'import sys;print("\033[?1h\a\033]2;build\aREADY",flush=True);a=sys.stdin.buffer.read(4);print("HEX="+a.hex(),flush=True);sys.exit(37)' | jq -er .id)
for i in {1..80}; do "${cli[@]}" capture "$s" "$b" | jq -e '.content|contains("READY")' >/dev/null && break; sleep .1; done
if ROYALTERMINAL_MUX_TOKEN="$share" "${cli[@]}" send-key "$s" "$b" enter >/dev/null 2>&1;then echo 'Observer acquired input' >&2;exit 1;fi
if "${cli[@]}" send-key "$s" "$b" enter no-such-key >/dev/null 2>&1;then echo 'Malformed batch accepted' >&2;exit 1;fi
"${cli[@]}" send-key "$s" "$b" up ctrl+c >/dev/null
code=0; timeout 10 "${cli[@]}" wait "$s" "$b" >/dev/null || code=$?;test "$code" -eq 37
"${cli[@]}" capture "$s" "$b" | jq -e '.content|contains("HEX=1b4f4103")' >/dev/null
ROYALTERMINAL_MUX_TOKEN="$share" timeout 10 "${cli[@]}" events "$s" "$b" --after 0 >"$tmp/events"
jq -se 'map(.name)==["terminal.bell","terminal.title_changed","terminal.child_exited"] and .[2].exitCode==37 and map(.sequence)==[1,2,3]' "$tmp/events" >/dev/null
"${cli[@]}" events "$s" "$b" --after 1 --count 1 | jq -se 'length==1 and .[0].sequence==2' >/dev/null
other=$("${cli[@]}" new unrelated | jq -er .id); "${cli[@]}" run "$other" --pipe -- /bin/false >/dev/null
last=$("${cli[@]}" run "$s" --pipe -- /bin/true | jq -er .id)
wait "$watcher" || { cat "$tmp/watch-err" >&2; exit 1; }; grep -q CROSS_BLOCK_OK "$tmp/watch-err"
ROYALTERMINAL_MUX_TOKEN="$share" timeout 10 "${cli[@]}" events "$s" --after 0 --count 4 >"$tmp/session-events"
jq -se --arg b "$b" --arg last "$last" 'map(.sequence)==[1,2,3,4] and map(.blockSequence)==[1,2,3,1] and map(.blockId)==[$b,$b,$b,$last]' "$tmp/session-events" >/dev/null
if ROYALTERMINAL_MUX_TOKEN="$share" timeout 5 "${cli[@]}" events "$other" --after 0 --count 1 >/dev/null 2>&1; then echo 'Cross-session read!' >&2; exit 1;fi
echo 'Verified named input, per-block and session-wide cursors, scoped observation before launch and the actual exit of two children.'
