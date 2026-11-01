-- One ordered subscription for all handlers; independent fenced subscription for each synchronous wait.
return function(rex,rpc,all,block_shape)
  local loading=true
  local handlers,order={},{}
  local timer,after,active
  local origin={session_id=rex.session_id,block_id=rex.block_id,window_id=os.getenv('ROYALTERMINAL_MUX_WINDOW')}
  local function context() return active or origin end
  local function read(cursor,ms)
    if not all and not origin.session_id then return nil,'set a session or launch the script with --all' end
    local args={after=cursor,timeoutMs=ms,limit=64}
    if not all then args.sessionId=origin.session_id end
    return rex.call(all and 'server.all_events' or 'session.events',args)
  end
  local function canonical(name)
    assert(type(name)=='string' and #name>0 and #name<=256,'invalid event name');return (name:gsub('^com%.superlogical%.terminal%.','terminal.'))
  end
  local function event(item)
    local t=item.type;local p={}
    if t=='block_event' then return block_shape(item.detail,item.sessionId,item.blockId,item.sequence) end
    if t=='session_created' then p.session={session_id=item.sessionId,label=item.label}
    elseif t=='session_destroyed' then p.session_id=item.sessionId
    elseif t=='session_view_changed' then p.revision=item.revision
    elseif t:match('^client_') then
      local c={client_id=item.clientId,principal=item.detail.principal,connected_at=item.detail.connectedAt,role=item.detail.role}
      if t=='client_joined' then p.client=c else p=c end
    else p.label=item.label;p.window_id=item.windowId;p.block_id=item.blockId end
    local ev={type=t,session_id=item.sessionId,block_id=item.blockId,sequence=item.sequence,payload=p}
    for k,v in pairs(p) do ev[k]=v end;return ev
  end
  local function matches(name,ev) return name==ev.type or name==ev.qualified_name end
  local function with_context(fn,ctx,ev)
    local previous=active;active=ctx;local ok,value=pcall(fn,ctx,ev);active=previous
    if not ok then rex.log('error',value) end
  end
  local function dispatch()
    local page,reason=read(after,0)
    if not page then timer:cancel();error(reason or 'workspace event subscription failed') end
    for _,item in ipairs(page.events) do
      assert(item.sequence==after+1,'activity cursor gap'); after=item.sequence
      for i=1,#order do
        local name=order[i]; if name==item.type or item.type=='block_event' and name:sub(1,9)=='terminal.' then
          local ev=event(item)
          if matches(name,ev) then with_context(handlers[i],{origin='event',session_id=ev.session_id,block_id=ev.block_id,window_id=ev.window_id},ev) end
        end
      end
    end
    assert(after==page.nextSequence,'inconsistent activity fence');if page.ended then timer:cancel() end
  end
  function rex.on(name,fn)
    assert(loading,'register event handlers at script load'); name=canonical(name);assert(type(fn)=='function','expected an event handler');assert(#order<256,'event handler capacity exceeded')
    if not timer then local first,why=read(nil,0);assert(first,why);after=first.nextSequence;timer=rex.every(.05,dispatch) end
    order[#order+1]=name;handlers[#order]=fn
  end
  function rex.wait(name,filter,timeout)
    name=canonical(name);if type(filter)=='number' and timeout==nil then timeout,filter=filter,nil end
    if filter~=nil and type(filter)~='table' and type(filter)~='function' then return nil,'invalid filter' end
    if timeout~=nil and (type(timeout)~='number' or timeout~=timeout or timeout<0 or timeout>1800) then return nil,'invalid timeout' end
    local function clock()return assert(rpc{op='clock'})end
    local deadline=timeout and clock()+timeout;local first,err=read(nil,0);if not first then return nil,err end;local cursor=first.nextSequence
    repeat
      local ms=deadline and math.min(25000,math.max(0,math.ceil((deadline-clock())*1000))) or 25000
      local page,why=read(cursor,ms);if not page then return nil,why end
      for _,item in ipairs(page.events)do
        if item.sequence~=cursor+1 then return nil,'activity cursor gap' end;cursor=item.sequence
        local ev=event(item);local ctx={origin='event',session_id=ev.session_id,block_id=ev.block_id,window_id=ev.window_id}
        if matches(name,ev) then
          local ok=true
          if type(filter)=='function' then ok=filter(ev,ctx) else for k,v in pairs(filter or {})do if ev[k]~=v then ok=false;break end end end
          if ok then return ev,ctx end
        end
      end
      if cursor~=page.nextSequence then return nil,'inconsistent activity fence' end
      if page.ended then return nil,'ended' end
    until deadline and clock()>=deadline
    return nil,'timeout'
  end
  -- Methods installed by layout.lua receive the dynamic context directly; legacy methods
  -- are also scoped explicitly before forwarding, so an --all handler cannot act on the wrong session.
  local original=rex.session
  rex.session=setmetatable({},{__index=function(_,name)
    local method=original[name]; if type(method)~='function' then return method end
    return function(args)
      if active and name~='list' and name~='create' then
        if args~=nil and type(args)~='table' then return nil,'expected session arguments' end
        local a={};for k,v in pairs(args or {})do a[k]=v end
        if not a.session_id and not a.sessionId then a.session_id=active.session_id end
        args=a
      end
      return method(args)
    end
  end})
  rex.block={call=function(kind,method,args)
    if kind~='terminal' and kind~='com.superlogical.terminal' then return nil,'unknown block type' end
    local a={};for k,v in pairs(args or {}) do a[k]=v end;local c=context()
    a.sessionId=a.sessionId or a.session_id or c.session_id;a.session_id=nil;a.blockId=a.blockId or a.block_id or c.block_id;a.block_id=nil
    return rex.call('terminal.'..method,a)
  end}
  return {context=context,seal=function()loading=false end}
end
