-- Local, trusted Lua 5.1 script adapter. The host owns credentials and all HTTP calls.
-- No Lua file or bytecode is ever accepted by the workspace server.
if _VERSION ~= 'Lua 5.1' then error('This adapter requires Lua 5.1, not '.._VERSION) end
local json = require('dkjson')
local wire, replies = io.stdout, io.stdin
wire:setvbuf('no'); io.output(io.stderr); io.stdout = io.stderr
local function logline(...)
  local v={}; for i=1,select('#',...) do v[i]=tostring(select(i,...)) end
  io.stderr:write(table.concat(v,'\t'),'\n'); io.stderr:flush()
end
print=logline
-- Match the script-facing restriction; this is not an OS sandbox for local code.
os.execute=function() error('os.execute is unavailable in workspace scripts',2) end
io.popen=function() error('io.popen is unavailable in workspace scripts',2) end
local function object(t) return setmetatable(t or {},{__jsontype='object'}) end
local function encode(v,opts)
  local ok,s=pcall(json.encode,v,{indent=opts and opts.indent});if ok then return s end;return nil,tostring(s)
end
local function decode(s)
  if type(s)~='string' then return nil,'expected JSON text' end
  local v,p,err=json.decode(s,1,json.null);if err then return nil,err end
  if s:sub(p):find('%S') then return nil,'trailing JSON data' end
  return v
end
local function rpc(op,fields)
  fields=fields or {};fields.op=op;local s,err=encode(fields);if not s then return nil,err end
  if #s>262144 then return nil,'RPC frame exceeds 256 KiB' end
  wire:write(s,'\n');wire:flush();local line=replies:read('*l');if not line then error('workspace RPC pipe closed',2) end
  local reply,msg=decode(line);if not reply then error(msg or 'invalid host response',2) end
  if reply.error then return nil,reply.error end;return reply.value
end
local function clock()return assert(rpc('clock'))end
local session_id,block_id=os.getenv('ROYALTERMINAL_MUX_SESSION'),os.getenv('ROYALTERMINAL_MUX_BLOCK')
if session_id=='' then session_id=nil end;if block_id=='' then block_id=nil end
local function context(origin)return {origin=origin,session_id=session_id,block_id=block_id}end
local rex={args={},session_id=session_id,block_id=block_id,json={encode=encode,decode=decode,null=json.null}};_G.rex=rex
for i=2,#arg do rex.args[i-1]=arg[i] end
local names={session_id='sessionId',window_id='windowId',block_id='blockId',expected_revision='expectedRevision',first_id='firstId',second_id='secondId',after_revision='afterRevision',relative_to='relativeTo',preserve_in='preserveIn',timeout_ms='timeoutMs'}
local function copyargs(t)
  if t~=nil and type(t)~='table' then return nil,'arguments must be a table' end
  local out=object();for k,v in pairs(t or {}) do
    if type(k)~='string' then return nil,'method argument keys must be strings' end
    local key=names[k] or k;if out[key]~=nil then return nil,'duplicate argument: '..key end;out[key]=v
  end;return out
end
function rex.call(method,args)
  if type(method)~='string' then return nil,'method must be a string' end
  local a,err=copyargs(args);if not a then return nil,err end
  return rpc('call',{method=method,args=a})
end
rex.session=setmetatable({},{__index=function(_,name)return function(args)
  local a,err=copyargs(args);if not a then return nil,err end
  if name~='list' and name~='create' then a.sessionId=a.sessionId or session_id end
  return rex.call('session.'..name,a)
end end})
function rex.log(level,message)logline(message and ('['..tostring(level)..']') or '[info]',message or level)end
local function seconds(n,allow_zero)
  if type(n)~='number' or n~=n or n==math.huge or n<0 or (not allow_zero and n==0) or n>1800 then error('invalid duration (maximum 1800 seconds)',3) end;return n
end
function rex.sleep(n) return assert(rpc('sleep',{seconds=seconds(n,true)})) end
function rex.stop(code) code=code or 0;if type(code)~='number' or code~=math.floor(code) or code<0 or code>255 then error('stop expects an exit code from 0 through 255',2) end;os.exit(code) end
local timers,count,ordinal={},0,0
local function timer(n,fn,repeat_)
  seconds(n,not repeat_);assert(type(fn)=='function','timer callback must be a function');if count>=1024 then logline('timer capacity exceeded');os.exit(1) end
  ordinal=ordinal+1;local t={at=clock()+n,order=ordinal,period=repeat_ and n or nil,fn=fn};timers[t]=true;count=count+1
  function t:active() return timers[self]==true end
  function t:cancel() if timers[self] then timers[self]=nil;count=count-1 end end
  return t
end
function rex.after(n,fn)return timer(n,fn,false)end
function rex.every(n,fn)return timer(n,fn,true)end
local function nexttimer()
  local best;for t in pairs(timers) do if not best or t.at<best.at or (t.at==best.at and t.order<best.order) then best=t end end;return best
end
local actions,depth={},0;local loading=true
function rex.action(def)
  assert(loading,'actions are registered at script load');assert(type(def)=='table' and type(def.name)=='string' and def.name:match('^[a-z0-9_]+$') and type(def.run)=='function','invalid action');assert(not actions[def.name],'duplicate action');actions[def.name]=def.run
end
function rex.invoke(name,args)
  local fn=actions[name];if not fn then return nil,'unknown local action: '..tostring(name) end
  if depth>=16 then return nil,'nested action limit exceeded' end;depth=depth+1;local ok,value,err=pcall(fn,context('action'),args or {});depth=depth-1;if not ok then return nil,tostring(value) end;return value,err
end
local revision,handlers=nil,{}
local function subscribe()
  if not session_id then return nil,'set ROYALTERMINAL_MUX_SESSION to a session GUID' end
  if not revision then local s,e=rex.session.view();if not s then return nil,e end;revision=s.revision end;return true
end
local function eventwait(duration)
  local s,err=rex.session.wait_change{after_revision=revision,timeout_ms=math.max(1,math.min(25000,math.ceil(duration*1000)))}
  if not s then return nil,err end;if s.revision==revision then return nil,'timeout' end
  revision=s.revision;return {type='session_view_changed',session_id=session_id,revision=revision,payload={revision=revision}},context('event')
end
function rex.on(event,fn)
  assert(loading,'register event handlers at load time');assert(event=='session_view_changed','only session_view_changed is supported');assert(type(fn)=='function');assert(subscribe());handlers[#handlers+1]=fn
end
function rex.wait(event,filter,timeout)
  if event~='session_view_changed' then return nil,'unsupported event' end
  if type(filter)=='number' and timeout==nil then timeout=filter;filter=nil end
  if filter~=nil and type(filter)~='function' and type(filter)~='table' then return nil,'invalid filter' end
  local ok,err=subscribe();if not ok then return nil,err end;local deadline=timeout and clock()+seconds(timeout,true)
  repeat
    local remaining=deadline and deadline-clock() or 25;if remaining<=0 then return nil,'timeout' end
    local ev,ctx=eventwait(math.min(remaining,25));if ev then
      local match=true;if type(filter)=='function' then match=filter(ev,ctx) elseif filter then for k,v in pairs(filter) do if ev[k]~=v then match=false;break end end end
      if match then return ev,ctx end
    elseif ctx~='timeout' then return nil,ctx end
  until false
end
local script=assert(loadfile(assert(arg[1],'missing script filename')));script();loading=false
while count>0 or #handlers>0 do
  local t=nexttimer();local now=clock()
  if t and t.at<=now then
    if not t.period then t:cancel() end
    local ok,err=pcall(t.fn,context('timer'));if not ok then logline('timer error:',err) end
    if t:active() then local elapsed=clock()-t.at;t.at=t.at+(math.floor(elapsed/t.period)+1)*t.period end
  else
    local wait=t and math.min(25,math.max(0,t.at-now)) or 25
    if #handlers>0 then local ev,ctx=eventwait(wait);if ev then for _,fn in ipairs(handlers) do local ok,e=pcall(fn,ctx,ev);if not ok then logline('event handler error:',e) end end elseif ctx~='timeout' then error(ctx) end
    elseif wait>0 then rex.sleep(wait) end
  end
end
