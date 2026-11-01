-- Named and generic terminal subscriptions observe the session by default. A wait may explicitly restrict its block.
return function(rex, rpc)
  local function canonical(name)
    if type(name) ~= 'string' then return name end
    return (name:gsub('^com%.superlogical%.terminal%.', 'terminal.'))
  end
  local function terminal(name)
    return name == 'block_event' or type(name) == 'string' and name:match('^terminal%.[%w_]+$') ~= nil
  end
  local function read(session, block, after, ms)
    if not session then return nil,'a session is required' end
    return rex.call(block and 'terminal.events' or 'session.terminal_events',
      {sessionId=session,blockId=block,after=after,timeoutMs=ms,limit=64})
  end
  local function optional(value) if value ~= rex.json.null then return value end end
  local function shape(item, session, block, generic)
    local short=item.name:gsub('^terminal%.',''); block = block or optional(item.blockId)
    local fields={exit_code=optional(item.exitCode),text=optional(item.text),pwd=optional(item.pwd),id=optional(item.recordId),record_id=optional(item.recordId),record=optional(item.record),reason=optional(item.reason)}
    if short=='title_changed' then fields.title=fields.text end
    if fields.exit_code and fields.exit_code~=0 then fields.error='process exited with status '..fields.exit_code end
    if optional(item.notification) then fields.title,fields.body=item.notification.title,item.notification.body end
    if optional(item.progressReport) then fields.state,fields.progress=item.progressReport.state,optional(item.progressReport.progress) end
    local ctx={origin='event',session_id=session,block_id=block}
    local ev={type=generic and 'block_event' or item.name,name=generic and short or item.name,qualified_name=item.name,
      creator_name='com.superlogical.terminal',sequence=item.sequence,block_sequence=optional(item.blockSequence) or item.sequence,
      session_id=session,block_id=block,payload=fields,raw=item}
    for k,v in pairs(fields) do ev[k]=v end
    return ev,ctx
  end
  local function matches(filter,ev,ctx)
    if type(filter)=='function' then return filter(ev,ctx) end
    for k,v in pairs(filter or {}) do if ev[k]~=v then return false end end
    return true
  end
  local previous_wait=rex.wait
  function rex.wait(name,filter,timeout)
    name=canonical(name);if not terminal(name) then return previous_wait(name,filter,timeout) end
    if type(filter)=='number' and timeout==nil then timeout,filter=filter,nil end
    if filter~=nil and type(filter)~='table' and type(filter)~='function' then return nil,'invalid filter' end
    if timeout~=nil and (type(timeout)~='number' or timeout~=timeout or timeout<0 or timeout>1800) then return nil,'invalid timeout' end
    local session=type(filter)=='table' and filter.session_id or rex.session_id
    local block=type(filter)=='table' and filter.block_id or nil
    local function now() return assert(rpc{op='clock'}) end
    local deadline=timeout and now()+timeout
    local initial,e=read(session,block,nil,0); if not initial then return nil,e end
    local after=initial.nextSequence
    repeat
      local ms=deadline and math.min(25000,math.max(0,math.ceil(1000*(deadline-now())))) or 25000
      local p,err=read(session,block,after,ms);if not p then return nil,err end
      for _,item in ipairs(p.events) do
        if name=='block_event' or item.name==name then
          local ev,ctx=shape(item,session,block,name=='block_event');if matches(filter,ev,ctx) then return ev,ctx end
        end
      end
      after=p.nextSequence;if p.ended then return nil,'ended' end
    until deadline and now()>=deadline
    return nil,'timeout'
  end
  local previous_on=rex.on;local handlers,timer={},nil
  function rex.on(name,fn)
    name=canonical(name);if not terminal(name) then return previous_on(name,fn) end
    assert(type(fn)=='function','expected a function'); assert(#handlers<256,'event handler limit exceeded')
    if not timer then
      local session=rex.session_id;local initial=assert(read(session,nil,nil,0));local after=initial.nextSequence
      timer=rex.every(.1,function()
        local p,err=read(session,nil,after,0);if not p then timer:cancel();error(err) end
        for _,item in ipairs(p.events) do
          for _,entry in ipairs(handlers) do
            if entry.name=='block_event' or entry.name==item.name then
              local ev,ctx=shape(item,session,nil,entry.name=='block_event');local ok,why=pcall(entry.fn,ctx,ev);if not ok then rex.log('error',why) end
            end
          end
        end
        after=p.nextSequence;if p.ended then timer:cancel() end
      end)
    end
    handlers[#handlers+1]={name=name,fn=fn}
  end
  rex.block={call=function(kind,method,args)
    if kind~='terminal' and kind~='com.superlogical.terminal' then return nil,'unknown block type' end
    local a={};for k,v in pairs(args or {}) do a[k]=v end
    a.sessionId=a.sessionId or a.session_id or rex.session_id;a.session_id=nil
    a.blockId=a.blockId or a.block_id or rex.block_id;a.block_id=nil
    return rex.call('terminal.'..method,a)
  end}
end
