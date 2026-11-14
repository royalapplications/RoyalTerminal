#!/usr/bin/env bash
# Offline CLI-side acceptance: actual interpreter, argv, split/independent stdio, EOF, timeout, cleanup and typed JSON.
set -euo pipefail
cd "$(dirname "$0")/../..";umask 077
cli=(dotnet samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll do)
scratch=$(mktemp -d);trap 'rm -rf "$scratch"' EXIT
printf '%s' '{"literal":"true","n":7,"nested":{"ok":true}}' >"$scratch/args.json"
"${cli[@]}" -e 'return rex.args' --args @"$scratch/args.json" | jq -e '.literal=="true" and .n==7 and .nested.ok' >/dev/null
printf '%s\n' 'return {ok=rex.args.ok,n=rex.args.number}' | "${cli[@]}" - ok=true number=12 | jq -e '.ok and .n==12' >/dev/null
cat >"$scratch/test.lua" <<'LUA'
assert(os.getenv('ROYALTERMINAL_MUX_TOKEN')==nil)
local args={'/usr/bin/python3','-c',[[import os,sys;sys.stdout.write(sys.argv[1]);sys.stderr.write(os.environ['X']);sys.exit(37)]],'a b;$(not-a-shell)'}
local r=assert(rex.exec(args,{env={X='diagnostic'}}));assert(r.code==37 and not r.ok and r.stdout==args[4] and r.stderr=='diagnostic')
local pieces={};local h=assert(rex.exec.start({'/bin/cat'},{stdin=true,stdout=function(s)pieces[#pieces+1]=s end}));assert(h:running());local no,msg=h:wait(0);assert(no==nil and msg=='timeout')
local binary='hi'..string.char(0,255)..'x';assert(h:write(binary));assert(h:close_stdin());local cat=assert(h:wait(5));assert(cat.ok and cat.stdout==binary and table.concat(pieces)==binary and not h:running())
local missing,why=rex.exec({'/not/a/royalterminal/program'});assert(not missing and why)
local timed=assert(rex.exec({'/bin/sleep','9'},{timeout=.2}));assert(timed.timed_out and not timed.ok)
rex.action{name='owned',run=function()return assert(rex.exec.start({'/bin/sleep','300'})).pid end}
if rex.args.cleanup then return assert(rex.invoke('owned')) end
return {code=r.code,binary_bytes=#cat.stdout}
LUA
export ROYALTERMINAL_MUX_TOKEN='must-not-reach-the-child-process'
"${cli[@]}" "$scratch/test.lua" | jq -e '.code==37 and .binary_bytes==5' >/dev/null
pid=$("${cli[@]}" "$scratch/test.lua" cleanup=true | jq -er '.');if kill -0 "$pid" 2>/dev/null;then kill "$pid";echo 'Scoped subprocess escaped' >&2;exit 1;fi
printf '%s\n' 'rex.action{name="echo",run=function(ctx,args) return args.x end}' >"$scratch/action.lua"
"${cli[@]}" "$scratch/action.lua" --action echo x=42 | jq -e '.==42' >/dev/null
if "${cli[@]}" -e 'return rex.args' x=1 x=2 >/dev/null 2>&1;then echo 'Duplicate key was accepted' >&2;exit 1;fi
echo 'Verified invocation modes, typed results, local nonzero exit, streamed binary I/O, timeout and scope cleanup.'
