-- The operator selects executable source on disk. No API accepts source code.
assert(_VERSION=='Lua 5.1','Lua 5.1 required')
local json=require 'dkjson';local input,output=io.stdin,io.stdout;local dir=assert(arg[0]:match('^(.*[/\\])'))
local function module(name)return assert(loadfile(dir..name..'.lua'))()end
local install_layout,install_keys,install_hosts,install_ui=module('layout'),module('config-keys'),module('hosts'),module('client-actions')
local function receive()local s=assert(input:read('*l'),'parent closed');local v,p,e=json.decode(s,1,json.null);assert(v and not e and not s:sub(p):match('%S'),'bad parent frame');return v end
local function send(v)local s=assert(json.encode(v));assert(#s<=262144,'result exceeds quota');assert(output:write(s,'\n'));assert(output:flush()) end
local function object(t)return setmetatable(t or {},{__jsontype='object'}) end
local function array(t)return setmetatable(t or {},{__jsontype='array'}) end
local function run()
  local spec=receive();local defs,names,loading={},array{},true
  local rex={session={},json={null=json.null,object=object,array=array,encode=json.encode}}
  rex.json.decode=function(s)local v,p,e=json.decode(s,1,json.null);if e or s:sub(p):match('%S') then return nil,e or 'trailing JSON' end;return v end
  function rex.action(d)
    assert(loading and type(d)=='table' and type(d.name)=='string' and #d.name<=80 and d.name:match('^[A-Za-z_][A-Za-z0-9_.%-]*$') and type(d.run)=='function','invalid action declaration');assert(not defs[d.name] and #names<256,'duplicate action or quota');defs[d.name]=d.run;names[#names+1]=d.name
  end
  function rex.call(method,args)
    assert(not loading,'no API calls at load time');assert(type(method)=='string' and type(args or {})=='table');send{op='call',method=method,arguments=object(args)};local r=receive();if not r.ok then return nil,r.error end;if r.value~=json.null then return r.value end
  end
  local ctx={origin='action',action=spec.name,session_id=spec.context.sessionId,window_id=spec.context.windowId,block_id=spec.context.blockId}
  for k,v in pairs(ctx)do if v==json.null then ctx[k]=nil end end
  for _,name in ipairs{'view','set_label','new_window','set_block_label','set_window_label','move_window','close_window','attention'} do
    local method=name;rex.session[method]=function(args)local a=object{};for k,v in pairs(args or {})do a[(k:gsub('_(%l)',string.upper))]=v end;a.sessionId=a.sessionId or ctx.session_id;return rex.call('session.'..method,a)end
  end
  install_layout(rex,function()return ctx end);local export,call_binding=install_keys(rex,array,object,function()return loading end)
  local hosts=install_hosts(rex,function()return loading end,true)
  local uiScope=install_ui(rex,function()return ctx end,nil,function()return loading end)
  local active,depth={},0
  function rex.invoke(name,args)
    assert(not loading,'no invocations at load time');if type(name)~='string' or not defs[name] then return nil,'unknown action' end
    if type(args or {})~='table' then return nil,'expected argument table' end;if active[name] or depth>=16 then return nil,'recursive action or depth limit' end
    active[name]=true;depth=depth+1;local ok,v,e=pcall(uiScope,defs[name],ctx,args or object{});active[name]=nil;depth=depth-1;if not ok then return nil,tostring(v) end;return v,e
  end
  local env={rex=rex,assert=assert,error=error,ipairs=ipairs,pairs=pairs,next=next,select=select,tonumber=tonumber,tostring=tostring,type=type,unpack=unpack,math=math,string=string,table=table,pcall=pcall,xpcall=xpcall,_VERSION=_VERSION}
  assert(spec.source:byte(1)~=27,'bytecode is not allowed');local chunk,why=loadstring(spec.source,'@init.lua');if not chunk then error(why,0)end;setfenv(chunk,env);chunk();loading=false
  local result,err
  if spec.mode=='validate' then table.sort(names);result={actions=names,keymap=export(),hosts=hosts()}
  else
    ctx.client_id=rex.call('client.invocation_origin',{})
    if spec.mode=='binding' then result=uiScope(call_binding,tonumber(spec.name),ctx) else result,err=rex.invoke(spec.name,spec.arguments) end
  end
  if err~=nil then error(tostring(err),0)end;if result==nil then result=json.null end;send{op='done',value=result}
end
local ok,why=pcall(run);if not ok then local line=tostring(why):match('init%.lua:(%d+):');send{op='failed',line=tonumber(line)} end
