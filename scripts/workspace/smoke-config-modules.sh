#!/usr/bin/env bash
# Validate closure over module sources, not just a freshly re-read init.lua.
set -euo pipefail
cd "$(dirname "$0")/../.."; umask 077
root=$(mktemp -d); p=
cleanup(){ if [[ -n $p ]]; then kill "$p" 2>/dev/null || :; wait "$p" || :; fi;rm -rf "$root"; };trap cleanup EXIT
export ROYALTERMINAL_MUX_TOKEN=$(openssl rand -hex 32) ROYALTERMINAL_MUX_LUA_INIT="$root/init.lua" ROYALTERMINAL_MUX_LUA_MODULES="$root/modules"
mkdir -p "$root/modules/ui"
printf '%s\n' 'return {name="one"}' >"$root/modules/ui/init.lua"
printf '%s\n' 'local a=require("ui"); assert(require("ui")==a); assert(not pcall(require,"../outside")); assert(not pcall(require,"os")); rex.action{name="version",run=function()return a.name end}' >"$root/init.lua"
server=samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll
dotnet "$server" "--Kestrel:Endpoints:Local:Url=http://unix:$root/host.sock" >"$root/log" 2>&1 & p=$!
for i in {1..150};do [[ -S $root/host.sock ]] && break;kill -0 "$p";sleep .05;done
call(){ curl -fsS --max-time 8 --unix-socket "$root/host.sock" -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN" -H 'Content-Type: application/json' --data "$(jq -cn --arg m "$1" --argjson a "${2:-\{\}}" '{method:$m,arguments:$a}')" http://localhost/api/call; }
first=$(call lua.actions '{}'|jq -er .generation)
[[ $(call lua.invoke '{"name":"version"}') == '"one"' ]]
printf '%s\n' 'return {name="two"}' >"$root/modules/ui/init.lua"
next=$(call lua.check '{}'|jq -er .generation);[[ $first != "$next" ]]
[[ $(call lua.invoke '{"name":"version"}') == '"one"' ]]
[[ $(call lua.reload '{}'|jq -er .generation) == "$next" ]]
[[ $(call lua.invoke '{"name":"version"}') == '"two"' ]]
if call lua.invoke "$(jq -cn --arg g "$first" '{name:"version",generation:$g}')";then exit 1;fi
printf 'broken = {\n' >"$root/modules/unused.lua"
if call lua.reload '{}';then exit 1;fi
rm "$root/modules/unused.lua";ln -s "$root/init.lua" "$root/modules/leak.lua"
if call lua.check '{}';then exit 1;fi
rm "$root/modules/leak.lua";printf '%s\n' 'return require("ui")' >"$root/modules/ui/init.lua"
if call lua.reload '{}';then exit 1;fi
[[ $(call lua.invoke '{"name":"version"}') == '"two"' ]]
[[ $(call lua.actions '{}'|jq -er .generation) == "$next" ]]
echo 'Verified pinned source bytes, module-only generation changes, check without publish, rollback, stale rejection, package init, recursion and link rejection.'
