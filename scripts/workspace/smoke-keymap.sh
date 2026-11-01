#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../..";umask 077
root=$(mktemp -d);export ROYALTERMINAL_MUX_SOCKET="$root/sock" ROYALTERMINAL_MUX_LUA_INIT="$root/init.lua" ROYALTERMINAL_MUX_LUA_EXECUTABLE="$(command -v lua5.1)";ROYALTERMINAL_MUX_TOKEN=$(openssl rand -hex 32);export ROYALTERMINAL_MUX_TOKEN
cat >"$root/init.lua" <<'LUA'
rex.mode('edit',{exclusive=true});rex.bind('cmd+j','client.mode.enter',{name='edit',once=true});rex.unbind('edit/q')
rex.action{name='stamp',run=function(ctx,args)return assert(rex.session.set_label{label=args.value}) end}
rex.bind('edit/s','stamp',{value='from binding'});rex.bind('ctrl+b>shift+5',function(ctx,ev)assert(ctx.origin=='keybinding');assert(ev.keybinding=='shift+5' and ev.prefix[1]=='ctrl+b');assert(rex.session.set_label{label='from callback'});return true end)
rex.bind('ctrl+b>shift+5',function()return false end)
rex.bind('edit/x','stamp');local found=rex.bindings{mode='edit',key='x'};assert(#found==1 and rex.unbind(found[1])==1);assert(#rex.modes()==1)
LUA
dotnet samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll "--Kestrel:Endpoints:Only:Url=http://unix:$ROYALTERMINAL_MUX_SOCKET" >"$root/log" 2>&1 & pid=$!;trap 'kill "$pid" 2>/dev/null || :;wait "$pid" 2>/dev/null || :;rm -rf "$root"' EXIT
for i in {1..100};do [[ -S $ROYALTERMINAL_MUX_SOCKET ]] && curl --unix-socket "$ROYALTERMINAL_MUX_SOCKET" -fsS http://localhost/healthz >/dev/null && break;kill -0 "$pid"||{ cat "$root/log" >&2;exit 1;};sleep .1;done
api(){ local m=$1 a=$2 auth=${3:-$ROYALTERMINAL_MUX_TOKEN};curl -fsS --unix-socket "$ROYALTERMINAL_MUX_SOCKET" -H "Authorization: Bearer $auth" -H 'Content-Type: application/json' --data "$(jq -cn --arg m "$m" --argjson a "$a" '{method:$m,arguments:$a}')" http://localhost/api/call; }
manifest=$(api lua.actions '{}');gen=$(jq -er .generation <<<"$manifest");id=$(jq -er '[.keymap.bindings[]|select(.callback)][-1].callback' <<<"$manifest");jq -e '.keymap.modes[0].exclusive and (.actions==["stamp"]) and (.keymap.bindings|length==5)' <<<"$manifest" >/dev/null
sid=$(api session.create '{"label":"initial"}'|jq -er .id);args=$(jq -cn --arg s "$sid" --arg g "$gen" --argjson id "$id" '{sessionId:$s,generation:$g,id:$id}');api lua.key "$args"|jq -e '.==true' >/dev/null
api session.view "$(jq -cn --arg s "$sid" '{sessionId:$s}')" |jq -e '.label=="from callback"' >/dev/null
share=$(api share.create "$(jq -cn --arg s "$sid" '{sessionId:$s,role:"observer"}')"|jq -er .token);if api lua.key "$args" "$share" >/dev/null 2>&1;then exit 1;fi
printf '\n-- changed generation\n' >>"$root/init.lua";api lua.reload '{}' | jq -e --arg g "$gen" '.generation!=$g' >/dev/null
status=$(curl -sS --unix-socket "$ROYALTERMINAL_MUX_SOCKET" -o "$root/error" -w '%{http_code}' -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN" -H 'Content-Type: application/json' --data "$(jq -cn --argjson a "$args" '{method:"lua.key",arguments:$a}')" http://localhost/api/call);test "$status" = 409;jq -e '.code=="stale_lua_generation"' "$root/error" >/dev/null
echo 'Verified callback fallthrough, chords, modes, binding removal, real mutation, observer denial and stale-generation rejection.'
