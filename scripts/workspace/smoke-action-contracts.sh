#!/usr/bin/env bash
# Real interpreter, real authenticated HTTP, nested calls, and an offline CLI with no bearer.
set -euo pipefail
cd "$(dirname "$0")/../.."; umask 077
root=$(mktemp -d); pid=''
cleanup(){ if [[ -n $pid ]]; then kill "$pid" 2>/dev/null || :; wait "$pid" || :; fi; rm -rf "$root"; };trap cleanup EXIT
cli=(dotnet samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll)
server=samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll
cat > "$root/init.lua" <<'LUA'
rex.action{name='mark',title='Mark workspace',description='Change a workspace label',category='Workspaces',keywords='rename annotate',repeats=true,
  args={label='string',n='integer?',anything='any?'},run=function(ctx,a)return assert(rex.session.set_label{label=a.label}) end}
rex.action{name='nested',run=function(ctx,a)local x,e=rex.invoke('mark',{label=12});assert(x==nil and e);return true end}
rex.action{name='bounded',args={type='object',additionalProperties=false,required={'values'},properties={values={type='array',minItems=1,maxItems=2,items={type='integer',minimum=1,maximum=3}}}},run=function(ctx,a)return a.values[1] end}
rex.bind('ctrl+m','mark',{lable='misspelled'})
LUA
export ROYALTERMINAL_MUX_LUA_INIT="$root/init.lua" ROYALTERMINAL_MUX_SOCKET="$root/host.sock" ROYALTERMINAL_MUX_TOKEN="$(openssl rand -hex 32)"
dotnet "$server" "--Kestrel:Endpoints:Local:Url=http://unix:$root/host.sock" >"$root/server.log" 2>&1 & pid=$!
for i in {1..150};do [[ -S $root/host.sock ]] && break;kill -0 "$pid" || { cat "$root/server.log" >&2;exit 1; };sleep .05;done
rpc(){ curl -sS --max-time 8 --unix-socket "$root/host.sock" -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN" -H 'Content-Type: application/json' --data "$(jq -cn --arg m "$1" --argjson a "$2" '{method:$m,arguments:$a}')" -w '%{http_code}' -o "$root/body" http://localhost/api/call; }
[[ $(rpc lua.actions '{}') == 200 ]]; jq -e '.bindingWarnings|length==1' "$root/body" >/dev/null
gen=$(jq -er .generation "$root/body")
"${cli[@]}" actions --describe mark | jq -e '.title=="Mark workspace" and .repeats and .inputSchema.required==["label"] and (.inputSchema.additionalProperties|not)' >/dev/null
[[ $(rpc session.create '{"label":"unchanged"}') == 200 ]];s=$(jq -er .id "$root/body")
invoke(){ rpc lua.invoke "$(jq -cn --arg n "$1" --arg s "$s" --argjson a "$2" '{sessionId:$s,name:$n,arguments:$a}')"; }
for bad in '{}' '{"label":true}' '{"label":"x","other":1}' '{"label":"x","n":0.5}'; do [[ $(invoke mark "$bad") == 400 ]];jq -e '.code=="invalid_action_arguments"' "$root/body" >/dev/null; done
[[ $(invoke nested '{}') == 200 ]];[[ $(rpc session.view "{\"sessionId\":\"$s\"}") == 200 ]];jq -e '.label=="unchanged"' "$root/body" >/dev/null
[[ $(invoke bounded '{"values":[2]}') == 200 && $(cat "$root/body") == 2 ]];[[ $(invoke bounded '{"values":[4]}') == 400 ]];[[ $(invoke mark '{"label":"changed","n":2,"anything":false}') == 200 ]];jq -e '.label=="changed"' "$root/body" >/dev/null
printf '%s\n' 'rex.action{name="broken",args={type="object",foo=true},run=function()end}' >> "$root/init.lua"
[[ $(rpc lua.reload '{}') == 422 ]];[[ $(rpc lua.actions '{}') == 200 ]];[[ $(jq -r .generation "$root/body") == "$gen" ]]
unset ROYALTERMINAL_MUX_TOKEN ROYALTERMINAL_MUX_SOCKET
cat > "$root/local.lua" <<'LUA'
local count=0
rex.action{name='local_typed',args={value='integer',flag='boolean?'},run=function(_,a) count=count+1;return a.value end}
local r,e=rex.invoke('local_typed',{value='1'});assert(r==nil and e and count==0)
local v=assert(rex.invoke('local_typed',{value=0,flag=false}));assert(v==0 and count==1)
LUA
"${cli[@]}" do "$root/local.lua" --action local_typed value=5 | jq -e '.==5' >/dev/null
if "${cli[@]}" do "$root/local.lua" --action local_typed value=wrong;then exit 1;fi
echo 'Verified local/server shorthand, nested schema, rejected side effects, binding diagnostics and immutable reload.'
