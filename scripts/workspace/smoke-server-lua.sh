#!/usr/bin/env bash
# Real interpreter, real API, no user configuration mutation. Broker listens only on a private socket.
set -euo pipefail
cd "$(dirname "$0")/../.."; umask 077
scratch=$(mktemp -d); token=$(openssl rand -hex 32); server=samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll
export ROYALTERMINAL_MUX_TOKEN=$token ROYALTERMINAL_MUX_LUA_INIT="$scratch/init.lua" ROYALTERMINAL_MUX_LUA_EXECUTABLE="$(command -v lua5.1)"
cat >"$scratch/init.lua" <<'LUA'
assert(io==nil and os==nil and require==nil and debug==nil)
rex.action{name='work.rename',run=function(ctx,args)
  local old=assert(rex.session.view()); assert(old.id==ctx.session_id)
  local renamed=assert(rex.session.set_label{label=args.label,expected_revision=old.revision})
  return {before=old.label,after=renamed.label,flag=args.flag,n=args.n}
end}
LUA
dotnet "$server" "--Kestrel:Endpoints:Local:Url=http://unix:$scratch/b.sock" >"$scratch/log" 2>&1 & pid=$!
trap 'kill "$pid" 2>/dev/null || true;wait "$pid" 2>/dev/null || true;rm -rf "$scratch"' EXIT
curlargs=(--unix-socket "$scratch/b.sock" --max-time 8 -sS -H 'Content-Type: application/json')
for i in {1..120};do if ! kill -0 "$pid" 2>/dev/null;then sed -E 's/[a-f0-9]{64}/[redacted]/g' "$scratch/log" >&2;exit 1;fi;if [[ -S $scratch/b.sock ]] && curl "${curlargs[@]}" -f http://localhost/healthz >/dev/null;then break;fi;sleep .1;done
call(){ curl "${curlargs[@]}" -H "Authorization: Bearer $token" -f --data "$(jq -cn --arg m "$1" --argjson a "$2" '{method:$m,arguments:$a}')" http://localhost/api/call; }
reject(){ curl "${curlargs[@]}" -H "Authorization: Bearer $1" -o "$scratch/res" -w '%{http_code}' --data "$2" http://localhost/api/call; }
first=$(call lua.actions '{}' | jq -er 'select(.actions==["work.rename"]).generation')
s=$(call session.create '{"label":"initial"}' | jq -er .id)
args=$(jq -cn --arg s "$s" '{name:"work.rename",sessionId:$s,arguments:{label:"updated",flag:false,n:37}}')
call lua.invoke "$args" | jq -e '.before=="initial" and .after=="updated" and .flag==false and .n==37' >/dev/null
share=$(call share.create "$(jq -cn --arg s "$s" '{sessionId:$s,role:"observer",lifetimeMinutes:2}')" | jq -er .token)
[[ $(reject "$share" '{"method":"lua.reload","arguments":{}}') == 403 ]]
[[ $(reject "$share" "$(jq -cn --argjson a "$args" '{method:"lua.invoke",arguments:$a}')") == 403 ]]
[[ $(reject "$token" '{"method":"lua.reload","arguments":{"source":"return 1"}}') == 400 ]]
printf '%s\n' 'rex.action{broken' >"$scratch/init.lua"
[[ $(reject "$token" '{"method":"lua.reload","arguments":{}}') == 422 ]]
[[ $(call lua.actions '{}'|jq -er .generation) == "$first" ]]
call lua.invoke "$args" | jq -e '.before=="updated"' >/dev/null
printf '%s\n' "rex.action{name='result',run=function() return false end}" >"$scratch/init.lua"
call lua.reload '{}' | jq -e --arg g "$first" '.generation!=$g and .actions==["result"]' >/dev/null
[[ $(call lua.invoke '{"name":"result"}') == false ]]
[[ $(reject "$token" "$(jq -cn --argjson a "$args" '{method:"lua.invoke",arguments:$a}')") == 404 ]]
echo 'Verified trusted Lua callback effects, typed arguments/results, scoped denial, failed reload rollback, and generation replacement.'
