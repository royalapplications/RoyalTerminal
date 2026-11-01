local entry=require('royal.workspace.invocation');local call=entry.call
local plan,err=call{op='setup'};if err then error(err,0) end
arg[1]=entry.file
local function module(name) return assert(loadfile(entry.directory..name))() end
local scoped=module('process.lua')(rex,call)
local events=module('workspace-events.lua')(rex,call,plan and plan.all==true,module('block-events.lua')(rex))
module('layout.lua')(rex,events.context)
for _,name in ipairs{'after','every'} do local original=rex[name];rex[name]=function(n,fn)return original(n,function(...)return scoped(fn,...)end)end end
local on=rex.on;rex.on=function(event,fn)return on(event,function(...)return scoped(fn,...)end)end
local declared,action={},rex.action
rex.action=function(def)
  local copy={};for k,v in pairs(def)do copy[k]=v end;local run=assert(copy.run);copy.run=function(...)return scoped(run,...)end;action(copy);declared[copy.name]=true
end
local invoke=rex.invoke
rex.invoke=function(name,args)
  if declared[name] then return invoke(name,args) end
  if type(name)~='string' or (args~=nil and type(args)~='table') then return nil,'invalid action invocation' end
  local params={};for k,v in pairs(args or {})do params[k]=v end;setmetatable(params,{__jsontype='object'})
  local ctx=events.context();return rex.call('lua.invoke',{name=name,arguments=params,session_id=ctx.session_id,block_id=ctx.block_id,window_id=ctx.window_id})
end
local chunk
if plan then rex.args=plan.args;chunk=assert(loadstring(plan.source,plan.name)) else chunk=assert(loadfile(entry.file)) end
local value=scoped(chunk);events.seal()
if plan and type(plan.action)=='string' then value,err=rex.invoke(plan.action,rex.args);if err then error(err,0) end end
if plan and value~=nil then assert(call{op='result',value=value}) end
