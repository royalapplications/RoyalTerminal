-- Handler subscriptions share a cursor; synchronous waits do not steal their events.
return function(rex,rpc,all,block_shape,options)
  local loading=true
  local handlers,order={},{}
  local timer,after,active
  local origin={session_id=rex.session_id,block_id=rex.block_id,window_id=os.getenv('ROYALTERMINAL_MUX_WINDOW')}
  local session=origin.session_id;local epoch=0
  local function context()return active or origin end
  local function read(cursor,ms,at)
    if not all and not at then return nil,'attach a session or use --all' end
    local a={after=cursor,timeoutMs=ms,limit=64};if not all then a.sessionId=at end
    return rex.call(all and 'server.all_events' or 'session.events',a)
  end
  local function canonical(n)assert(type(n)=='string' and #n>0 and #n<=256,'invalid event name');return(n:gsub('^com%.superlogical%.terminal%.','terminal.'))end
  local function event(item)
    local t,p=item.type,{}
    if t=='block_event' then return block_shape(item.detail,item.sessionId,item.blockId,item.sequence) end
    if t=='session_created' then p.session={session_id=item.sessionId,label=item.label}
    elseif t=='session_destroyed' then p.session_id=item.sessionId
    elseif t=='session_view_changed' then p.revision=item.revision
    elseif t=='keymap_changed' or t=='hosts_changed' then p.generation,p.hash=item.generation,item.hash
    elseif t:match('^client_') then
      local c={client_id=item.clientId,principal=item.detail.principal,connected_at=item.detail.connectedAt,role=item.detail.role}
      if t=='client_joined' then p.client=c else p=c end
    else p.label=item.label;p.window_id=item.windowId;p.block_id=item.blockId end
    local ev={type=t,session_id=item.sessionId,block_id=item.blockId,sequence=item.sequence,payload=p}
    for k,v in pairs(p)do ev[k]=v end;return ev
  end
  local function matches(n,ev)return n==ev.type or n==ev.qualified_name end
  local function call_handler(fn,ctx,ev)
    local previous=active;active=ctx;local ok,value=pcall(fn,ctx,ev);active=previous
    if not ok then rex.log('error',value);if options and options.failFast then rex.stop(1)end end
  end
  local function stop_timer()if timer then timer:cancel();timer=nil end end
  local function dispatch()
    if not all and not session then return end
    local start=epoch;local page,reason=read(after,0,session)
    if not page then stop_timer();rex.log('error',reason or 'event stream ended without a page');rex.stop(1) end
    for _,item in ipairs(page.events) do
      assert(item.sequence==after+1,'activity cursor gap');after=item.sequence
      for i=1,#order do
        local name=order[i]
        if name==item.type or item.type=='block_event' and name:sub(1,9)=='terminal.' then
          local ev=event(item)
          if matches(name,ev)then call_handler(handlers[i],{origin='event',session_id=ev.session_id,block_id=ev.block_id,window_id=ev.window_id},ev)end
          if epoch~=start then return end -- callback changed its subscription; abandon the old page
        end
      end
    end
    assert(after==page.nextSequence,'inconsistent activity fence')
    if page.ended then stop_timer();if not all then rex.stop(0)end end
  end
  local function start_timer()if not timer then timer=rex.every(.05,dispatch)end end
  function rex.on(name,fn)
    assert(loading,'register event handlers at script load');name=canonical(name);assert(type(fn)=='function','expected handler');assert(#order<256,'handler quota exceeded')
    if not timer then local first,why=read(nil,0,session);assert(first,why);after=first.nextSequence;start_timer()end
    order[#order+1]=name;handlers[#order]=fn
  end
  function rex.wait(name,filter,timeout)
    name=canonical(name);if type(filter)=='number' and timeout==nil then timeout,filter=filter,nil end
    if filter~=nil and type(filter)~='table' and type(filter)~='function' then return nil,'invalid filter' end
    if timeout~=nil and(type(timeout)~='number' or timeout~=timeout or timeout<0 or timeout>1800)then return nil,'invalid timeout' end
    local function clock()return assert(rpc{op='clock'})end
    local deadline=timeout and clock()+timeout;local at=session;local first,err=read(nil,0,at);if not first then return nil,err end;local cursor=first.nextSequence
    repeat
      local ms=deadline and math.min(25000,math.max(0,math.ceil((deadline-clock())*1000)))or 25000
      local page,why=read(cursor,ms,at);if not page then return nil,why end
      for _,item in ipairs(page.events)do
        if item.sequence~=cursor+1 then return nil,'activity cursor gap' end;cursor=item.sequence
        local ev=event(item);local ctx={origin='event',session_id=ev.session_id,block_id=ev.block_id,window_id=ev.window_id}
        if matches(name,ev)then local ok=true;if type(filter)=='function'then ok=filter(ev,ctx)else for k,v in pairs(filter or{})do if ev[k]~=v then ok=false;break end end end;if ok then return ev,ctx end end
      end
      if cursor~=page.nextSequence then return nil,'inconsistent activity fence' end;if page.ended then return nil,'ended' end
    until deadline and clock()>=deadline;return nil,'timeout'
  end
  local original=rex.session
  rex.session=setmetatable({},{__index=function(_,name)
    local fn=original[name];if type(fn)~='function'then return fn end
    return function(args)
      if active and name~='list' and name~='create' then
        if args~=nil and type(args)~='table'then return nil,'expected session arguments' end
        local a={};for k,v in pairs(args or{})do a[k]=v end
        if not a.session_id and not a.sessionId then if not active.session_id then return nil,'this server event has no session' end;a.session_id=active.session_id end;args=a
      end
      return fn(args)
    end
  end})
  function rex.session.attach(a)
    if all then return nil,'--all already observes every session' end
    local id=type(a)=='table' and (a.session_id or a.sessionId) or nil;if type(id)~='string' then return nil,'explicit session_id required' end
    -- Authorize and establish the new fence before replacing the old scope.
    local first,why=read(nil,0,id);if not first then return nil,why end
    epoch=epoch+1;session=id;after=first.nextSequence;if #order>0 then start_timer() end;return {session_id=id,sequence=after}
  end
  function rex.session.detach()
    if all then return nil,'--all has no single-session attachment' end
    epoch=epoch+1;local old=session;session=nil;after=nil;stop_timer();return {session_id=old}
  end
  rex.block={call=function(kind,method,args)
    if kind~='terminal' and kind~='com.superlogical.terminal'then return nil,'unknown block type' end
    local a={};for k,v in pairs(args or{})do a[k]=v end;local c=context();a.sessionId=a.sessionId or a.session_id or c.session_id;a.session_id=nil;a.blockId=a.blockId or a.block_id or c.block_id;a.block_id=nil
    return rex.call('terminal.'..method,a)
  end}
  return {context=context,seal=function()loading=false end}
end
