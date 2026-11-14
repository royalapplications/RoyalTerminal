#!/usr/bin/env bash
# Compare the script and trusted server adapters against actual PTYs over a private socket.
set -euo pipefail
cd "$(dirname "$0")/../.."; umask 077
root=$(mktemp -d); export ROYALTERMINAL_MUX_SOCKET="$root/sock" ROYALTERMINAL_MUX_LUA_INIT="$root/init.lua" ROYALTERMINAL_MUX_LUA_EXECUTABLE="$(command -v lua5.1)"
ROYALTERMINAL_MUX_TOKEN=$(openssl rand -hex 32); export ROYALTERMINAL_MUX_TOKEN
cli=(dotnet samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll)
cat >"$root/init.lua" <<'LUA'
local leaf=rex.layout.block{flavor='com.superlogical.terminal.shell',options={command={'/bin/cat'},shell='none',exit={on_completion=false}}}
local pair=rex.layout.vertical(.3,leaf,leaf)
rex.action{name='create',run=function(ctx,args)return assert(rex.session.new_window{label='server pair',layout=pair}) end}
rex.action{name='nested',run=function(ctx,args)return assert(rex.invoke('create',args)) end}
rex.action{name='cycle',run=function()return rex.invoke('cycle') end}
LUA
dotnet samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll "--Kestrel:Endpoints:Local:Url=http://unix:$ROYALTERMINAL_MUX_SOCKET" >"$root/host.log" 2>&1 & pid=$!
trap 'kill "$pid" 2>/dev/null || :;wait "$pid" 2>/dev/null || :;rm -rf "$root"' EXIT
for i in {1..100}; do if [[ -S $ROYALTERMINAL_MUX_SOCKET ]] && curl -fsS --unix-socket "$ROYALTERMINAL_MUX_SOCKET" http://localhost/healthz >/dev/null; then break; fi;kill -0 "$pid";sleep .1;done
ROYALTERMINAL_MUX_SESSION=$("${cli[@]}" new templates|jq -er .id);export ROYALTERMINAL_MUX_SESSION
"${cli[@]}" do -e "return assert(rex.invoke('nested'))" >"$root/result"; a=$(jq -er '.block_ids[0]' "$root/result"); export ROYALTERMINAL_MUX_BLOCK=$a
cat >"$root/split.lua" <<'LUA'
local shell='com.superlogical.terminal.shell'
local function cmd(value)return rex.layout.block{flavor=shell,options={command={'/usr/bin/printf','%s',value},shell='none',exit={on_completion=false}}} end
local r=assert(rex.session.new_split{direction='horizontal',side='before',ratio=.4,focus=false,layout=rex.layout.horizontal(.6,cmd('first literal ; $HOME'),cmd('second'))}); assert(#r.block_ids==2)
local s=assert(rex.session.view{});local w;for _,v in ipairs(s.windows) do if v.id==r.window_id then w=v end end
assert(w.layout.ratio==.3 and w.layout.first.ratio==.4 and w.layout.first.first.ratio==.6 and w.layout.first.second.blockId==rex.block_id)
assert(w.layout.first.first.first.blockId==r.block_ids[1] and w.layout.first.first.second.blockId==r.block_ids[2]);return r
LUA
"${cli[@]}" do "$root/split.lua" >"$root/new"; b=$(jq -er '.block_ids[0]' "$root/new");"${cli[@]}" wait "$ROYALTERMINAL_MUX_SESSION" "$b" >/dev/null
"${cli[@]}" capture "$ROYALTERMINAL_MUX_SESSION" "$b" | jq -e '.content|contains("first literal ; $HOME")' >/dev/null
cat >"$root/reject.lua" <<'LUA'
local before=assert(rex.session.view{});local function run(template)return rex.session.new_window{layout=template}end
local shell='com.superlogical.terminal.shell';local good=rex.layout.block{flavor=shell,options={command={'/bin/cat'}}}
local v,e=run(rex.layout.vertical(.5,good,rex.layout.block{flavor='unknown'}));assert(not v and e)
local unchanged=assert(rex.session.view{});assert(unchanged.revision==before.revision and #unchanged.blocks==#before.blocks)
-- An actual process-start failure after first success must remove the invocation's window and its own blocks.
local bad=rex.layout.block{flavor=shell,options={cwd='/definitely/not/a/real/path'}}; v,e=run(rex.layout.horizontal(.5,good,bad));assert(not v and e)
local after=assert(rex.session.view{});assert(#after.windows==#before.windows and #after.blocks==#before.blocks)
local stale,why=rex.call('block.create',{sessionId=rex.session_id,block={windowId=before.windows[1].id,label='stale',expectedRevision=0}});assert(not stale and why)
local recursive,err=rex.invoke('cycle');assert(not recursive and err);return true
LUA
"${cli[@]}" do "$root/reject.lua" | jq -e '. == true' >/dev/null
echo 'Nested config and script layouts, literal argv, source ordering, stale append, preflight and recursive-action rejection OK.'
