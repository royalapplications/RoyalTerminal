#!/usr/bin/env bash
# Exercise the installed interpreter, actual loopback management API and an OS child.
set -euo pipefail
cd "$(dirname "$0")/../.."
umask 077
scratch=$(mktemp -d)
export ROYALTERMINAL_MUX_TOKEN=$(openssl rand -hex 32)
export ROYALTERMINAL_MUX_URL=http://127.0.0.1:17331
export ROYALTERMINAL_MUX_CONFIG="$scratch/config.json"
cli=samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll
server=samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll
# Don't accidentally exercise another listener on the CI port.
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
echo 'Verified Lua callbacks, timer cancellation, structural observation and child exit 37.'
