#!/usr/bin/env bash
# Exercise the installed interpreter, actual management API and real OS children.
set -euo pipefail
cd "$(dirname "$0")/../.."
umask 077
scratch=$(mktemp -d)
export ROYALTERMINAL_MUX_TOKEN=$(openssl rand -hex 32)
export ROYALTERMINAL_MUX_URL=http://127.0.0.1:17331
export ROYALTERMINAL_MUX_CONFIG="$scratch/config.json"
cli=samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll
server=samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll
if curl -fsS --max-time 1 "$ROYALTERMINAL_MUX_URL/healthz" >/dev/null 2>&1; then echo 'Port 17331 already in use' >&2; exit 1; fi
dotnet "$server" >"$scratch/server.log" 2>&1 & pid=$!
cleanup() { kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; rm -rf "$scratch"; }
trap cleanup EXIT
for ((i=0;i<120;i++)); do
  if ! kill -0 "$pid" 2>/dev/null; then cat "$scratch/server.log" >&2; exit 1; fi
  if curl -fsS --max-time 1 "$ROYALTERMINAL_MUX_URL/healthz" >/dev/null 2>&1; then break; fi
  sleep .25
done
curl -fsS --max-time 2 "$ROYALTERMINAL_MUX_URL/healthz" >/dev/null
dotnet "$cli" new lua-ci >"$scratch/session.json"
ROYALTERMINAL_MUX_SESSION=$(jq -er '.id' "$scratch/session.json"); export ROYALTERMINAL_MUX_SESSION
if ! timeout 25 dotnet "$cli" lua samples/RoyalTerminal.Multiplexing.Cli/examples/smoke.lua 'space separated argument' >"$scratch/lua.out" 2>"$scratch/lua.err"; then cat "$scratch/lua.err" >&2; exit 1; fi
grep -q 'LUA_RPC_SMOKE_OK' "$scratch/lua.err"
status=0
dotnet "$cli" run "$ROYALTERMINAL_MUX_SESSION" --wait --pipe -- /bin/sh -c 'exit 37' >"$scratch/exit.json" || status=$?
[[ $status == 37 ]] || { echo "Expected process status 37, got $status" >&2; exit 1; }
jq -e '.exitCode == 37' "$scratch/exit.json" >/dev/null
s=$ROYALTERMINAL_MUX_SESSION
b=$(dotnet "$cli" run "$s" --pipe -- /bin/sh -c 'printf "READY\033["; IFS= read -r line || exit 99; printf "2;1H%s\n" "$line"; exit 37' | jq -er .id)
for ((i=0;i<40;i++)); do dotnet "$cli" capture "$s" "$b" | jq -e '.content|contains("READY")' >/dev/null && break; sleep .1; done
dotnet "$cli" mirror "$s" "$b" >"$scratch/initial.txt"; grep -q READY "$scratch/initial.txt"
timeout 30 dotnet "$cli" mirror "$s" "$b" --wait >"$scratch/mirror.txt" 2>"$scratch/mirror.err" & mirror=$!
attached=false
for ((i=0;i<60;i++)); do
  if ! kill -0 "$mirror" 2>/dev/null; then cat "$scratch/mirror.err" >&2; exit 1; fi
  if dotnet "$cli" show "$s" | jq -e --arg b "$b" '.blocks[]|select(.id==$b and .attachmentCount==1 and .controllerId==null)' >/dev/null; then attached=true; break; fi
  sleep .1
done
[[ $attached == true ]] || { cat "$scratch/mirror.err" >&2; exit 1; }
dotnet "$cli" send "$s" "$b" $'RESTORED_LIVE_SCREEN\n' >/dev/null
result=0; wait "$mirror" || result=$?; [[ $result == 37 ]] || { cat "$scratch/mirror.err" >&2; exit 1; }
grep -q READY "$scratch/mirror.txt"; grep -q RESTORED_LIVE_SCREEN "$scratch/mirror.txt"
# Bare `do` names must still reach the server catalog rather than the trusted local Lua loader.
dotnet "$cli" config load samples/RoyalTerminal.Multiplexing.Server/automation.example.json >/dev/null
dotnet "$cli" do capture_focused "$s" "$b" | jq -e '.steps[-1].result.content|contains("RESTORED_LIVE_SCREEN")' >/dev/null
# Enroll without a default block. A handler must target the block which actually emitted the event.
bellblock=$(dotnet "$cli" run "$s" --pipe -- /bin/cat | jq -er .id)
cat > "$scratch/bell.lua" <<'LUA'
assert(not rex.block_id)
local id=assert(rex.args[1]);local removed=false
rex.on('com.superlogical.terminal.program_status_removed', function(ctx,ev)
  assert(ctx.block_id==id and ev.type=='block_event' and ev.name=='program_status_removed' and ev.ids[1]=='task' and ev.payload.ids[1]=='task');removed=true
end)
rex.on('com.superlogical.terminal.bell',function(ctx,ev)
  assert(ctx.block_id==id and ev.block_id==id and ev.type=='block_event' and ev.name=='bell' and ev.creator_name=='com.superlogical.terminal');assert(removed)
  local size=assert(rex.block.call('com.superlogical.terminal','size',{}));assert(size.columns>0)
  print('METADATA_BELL_OK');rex.stop(0)
end)
-- Two OSC 7501 reports (create and clear), then a BEL. The program is cat, not a shell.
assert(rex.call('block.input',{sessionId=rex.session_id,blockId=id,data=assert(rex.args[2])}))
rex.after(8,function()rex.stop(19)end)
LUA
bytes=$(printf '\033]7501;state=working:id=task\033\\\033]7501;state=clear:id=task\033\\\007' | base64 -w0)
env -u ROYALTERMINAL_MUX_BLOCK timeout 15 dotnet "$cli" lua "$scratch/bell.lua" "$bellblock" "$bytes" 2>"$scratch/bell.result" || { cat "$scratch/bell.result" >&2; exit 1; }
grep -q METADATA_BELL_OK "$scratch/bell.result"
echo 'Verified metadata envelopes, implicit callback block scope, status removal, legacy actions and real process exits.'
