return function(rex,rpc)
local known={['terminal.bell']=true,['terminal.title_changed']=true,['terminal.child_exited']=true,['terminal.program_status_changed']=true}
local function canonical(name)return type(name)=='string' and name:gsub('^com%.superlogical%.','') or name end
local function cursor(block)if not rex.session_id or not block then return nil,'a session and block are required' end;local p,e=rex.call('terminal.events',{sessionId=rex.session_id,blockId=block});return p and p.nextSequence,e end
local function page(block,after,ms)return rex.call('terminal.events',{sessionId=rex.session_id,blockId=block,after=after,timeoutMs=ms,limit=64})end
local function shape(item,block)local ctx={origin='event',session_id=rex.session_id,block_id=block};local ev={type=item.name,name=item.name,sequence=item.sequence,session_id=ctx.session_id,block_id=block,exit_code=item.exitCode,text=item.text,record_id=item.recordId,record=item.record,reason=item.reason,payload=item};return ev,ctx end
local original_wait=rex.wait
function rex.wait(name,filter,timeout)
  name=canonical(name);if not known[name]then return original_wait(name,filter,timeout)end
  if type(filter)=='number' and timeout==nil then timeout=filter;filter=nil end
  if filter~=nil and type(filter)~='table' and type(filter)~='function' then return nil,'invalid filter' end
  if timeout~=nil and (type(timeout)~='number' or timeout~=timeout or timeout<0 or timeout>1800)then return nil,'invalid timeout' end
  local block=type(filter)=='table' and filter.block_id or rex.block_id;local after,e=cursor(block);if not after then return nil,e end
  local function now()return assert(rpc{op='clock'})end;local deadline=timeout and now()+timeout
  repeat local ms=deadline and math.min(25000,math.max(0,math.ceil(1000*(deadline-now())))) or 25000
    local p,err=page(block,after,ms);if not p then return nil,err end
    for _,item in ipairs(p.events)do if item.name==name then local ev,ctx=shape(item,block);local match=true
      if type(filter)=='function' then match=filter(ev,ctx) elseif filter then for k,v in pairs(filter)do if ev[k]~=v then match=false;break end end end
      if match then return ev,ctx end end end
    after=p.nextSequence;if p.ended then return nil,'ended' end
  until deadline and now()>=deadline;return nil,'timeout'
end
local original_on=rex.on;local handlers={};local timer,after
function rex.on(name,fn)
  name=canonical(name);if not known[name]then return original_on(name,fn)end;assert(type(fn)=='function');handlers[name]=handlers[name] or {};table.insert(handlers[name],fn)
  if timer then return end;local block=rex.block_id;after=assert(cursor(block));timer=rex.every(0.1,function()
    local p,err=page(block,after,0);if not p then timer:cancel();error(err)end
    for _,item in ipairs(p.events)do local ev,ctx=shape(item,block);for _,cb in ipairs(handlers[item.name] or {})do local ok,e=pcall(cb,ctx,ev);if not ok then rex.log('error',e)end end end
    after=p.nextSequence;if p.ended then timer:cancel() end
  end)
end
rex.block={call=function(kind,method,args)
  if kind~='terminal' and kind~='com.superlogical.terminal' then return nil,'unknown block type' end
  local a={};for k,v in pairs(args or {})do a[k]=v end;a.sessionId=a.sessionId or a.session_id or rex.session_id;a.session_id=nil;a.blockId=a.blockId or a.block_id or rex.block_id;a.block_id=nil;return rex.call('terminal.'..method,a)
end}
end
