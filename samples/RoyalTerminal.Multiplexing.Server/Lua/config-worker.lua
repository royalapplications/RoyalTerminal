-- A private worker for one trusted startup configuration generation. No HTTP/bearer access.
-- Load system dkjson before installing the environment for the operator's chunk.
assert(_VERSION == 'Lua 5.1', 'Lua 5.1 is required')
local json = require 'dkjson'
local input, output = io.stdin, io.stdout
local function receive()
  local s = assert(input:read('*l'), 'parent closed the channel')
  local v, pos, err = json.decode(s, 1, json.null)
  assert(v and not err and not s:sub(pos):match('%S'), 'invalid parent frame')
  return v
end
local function send(v)
  local frame = assert(json.encode(v)); assert(#frame <= 262144, 'result exceeds frame quota')
  assert(output:write(frame, '\n')); assert(output:flush())
end
local function object(t) return setmetatable(t or {}, {__jsontype='object'}) end
local function array(t) return setmetatable(t or {}, {__jsontype='array'}) end
local function run()
  local spec = receive(); local defs, names, loading = {}, array{}, true
  local rex = {session={}, json={null=json.null,object=object,array=array}}
  function rex.action(d)
    assert(loading and type(d)=='table' and type(d.name)=='string' and #d.name<=80 and d.name:match('^[A-Za-z_][A-Za-z0-9_.%-]*$') and type(d.run)=='function', 'invalid action declaration')
    assert(not defs[d.name] and #names < 256, 'duplicate action or action limit')
    defs[d.name]=d.run; names[#names+1]=d.name
  end
  function rex.call(method,args)
    assert(not loading, 'no API calls during configuration loading'); assert(type(method)=='string' and type(args or {})=='table')
    send{op='call',method=method,arguments=object(args)}
    local reply=receive(); if not reply.ok then return nil, reply.error end
    if reply.value==json.null then return nil end
    return reply.value
  end
  local ctx={origin='action', action=spec.name, session_id=spec.context.sessionId,window_id=spec.context.windowId,block_id=spec.context.blockId}
  for k,v in pairs(ctx) do if v==json.null then ctx[k]=nil end end
  for _,m in ipairs{'view','set_label','new_window','set_block_label','set_window_label','move_window','close_window','attention'} do
    local method=m
    rex.session[m]=function(args)
      local a=object{}; for k,v in pairs(args or {}) do a[(k:gsub('_(%l)',string.upper))]=v end
      a.sessionId=a.sessionId or ctx.session_id
      return rex.call('session.'..method,a)
    end
  end
  -- Not an OS sandbox. This deliberately small environment prevents accidental raw I/O and phase confusion.
  local env={rex=rex,assert=assert,error=error,ipairs=ipairs,pairs=pairs,next=next,select=select,tonumber=tonumber,tostring=tostring,type=type,unpack=unpack,
    math=math,string=string,table=table,pcall=pcall,xpcall=xpcall,_VERSION=_VERSION}
  assert(spec.source:byte(1)~=27,'bytecode is not allowed')
  local chunk,why=loadstring(spec.source,'@init.lua'); if not chunk then error(why,0) end
  setfenv(chunk,env);chunk();loading=false
  if spec.mode=='validate' then table.sort(names);send{op='done',value=names};return end
  local fn=assert(defs[spec.name],'unknown action');local result,err=fn(ctx,spec.arguments)
  if err~=nil then error(tostring(err),0) end
  if result==nil then result=json.null end
  send{op='done',value=result}
end
local ok,why=pcall(run)
if not ok then
  -- Report a source location only. An exception may contain caller arguments or secrets.
  local line=tostring(why):match('init%.lua:(%d+):'); send{op='failed',line=tonumber(line)}
end
