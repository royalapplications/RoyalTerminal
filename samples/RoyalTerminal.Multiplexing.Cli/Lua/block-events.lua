-- Scoped terminal event adapter. Keep one cursor per subscription, independent of VT replay.
return function(rex, rpc)
  local function canonical(name)
    if type(name) ~= 'string' then return name end
    return (name:gsub('^com%.superlogical%.terminal%.', 'terminal.'))
  end
  local function is_block(name)
    return name == 'block_event' or (type(name) == 'string' and name:match('^terminal%.[%w_]+$') ~= nil)
  end
  local function cursor(session, block)
    if not session or not block then return nil, 'a session and block are required' end
    local p, e = rex.call('terminal.events', {sessionId=session, blockId=block})
    return p and p.nextSequence, e
  end
  local function page(session, block, after, ms)
    return rex.call('terminal.events', {sessionId=session, blockId=block, after=after, timeoutMs=ms, limit=64})
  end
  local function shape(item, session, block, generic)
    local short = item.name:gsub('^terminal%.', '')
    local payload = {exit_code=item.exitCode, text=item.text, pwd=item.pwd, id=item.recordId,
      record_id=item.recordId, record=item.record, reason=item.reason}
    if short == 'title_changed' then payload.title = item.text end
    if item.notification then payload.title, payload.body = item.notification.title, item.notification.body end
    if item.progressReport then payload.state, payload.progress = item.progressReport.state, item.progressReport.progress end
    local ctx = {origin='event', session_id=session, block_id=block}
    -- Existing named handlers use ev.type == 'terminal.bell'. The generic envelope uses Rex-style fields.
    local ev = {type=generic and 'block_event' or item.name, name=generic and short or item.name,
      qualified_name=item.name, creator_name='com.superlogical.terminal', sequence=item.sequence,
      session_id=session, block_id=block, payload=payload, raw=item}
    for k, v in pairs(payload) do ev[k] = v end
    return ev, ctx
  end
  local function matches(filter, ev, ctx)
    if type(filter) == 'function' then return filter(ev,ctx) end
    for k,v in pairs(filter or {}) do if ev[k] ~= v then return false end end
    return true
  end
  local previous_wait = rex.wait
  function rex.wait(name, filter, timeout)
    name = canonical(name)
    if not is_block(name) then return previous_wait(name,filter,timeout) end
    if type(filter) == 'number' and timeout == nil then timeout,filter = filter,nil end
    if filter ~= nil and type(filter) ~= 'table' and type(filter) ~= 'function' then return nil,'invalid filter' end
    if timeout ~= nil and (type(timeout) ~= 'number' or timeout ~= timeout or timeout < 0 or timeout > 1800) then return nil,'invalid timeout' end
    local session = type(filter) == 'table' and filter.session_id or rex.session_id
    local block = type(filter) == 'table' and filter.block_id or rex.block_id
    local after,e = cursor(session,block); if not after then return nil,e end
    local function now() return assert(rpc{op='clock'}) end
    local deadline = timeout and now()+timeout
    repeat
      local ms = deadline and math.min(25000, math.max(0, math.ceil(1000*(deadline-now())))) or 25000
      local p,err = page(session,block,after,ms); if not p then return nil,err end
      for _,item in ipairs(p.events) do
        if name == 'block_event' or item.name == name then
          local ev,ctx = shape(item,session,block,name == 'block_event')
          if matches(filter,ev,ctx) then return ev,ctx end
        end
      end
      after = p.nextSequence; if p.ended then return nil,'ended' end
    until deadline and now() >= deadline
    return nil,'timeout'
  end
  local previous_on = rex.on
  local handlers, timer = {}, nil
  function rex.on(name,fn)
    name = canonical(name); if not is_block(name) then return previous_on(name,fn) end
    assert(type(fn) == 'function', 'expected a function')
    if not timer then
      local session,block = rex.session_id,rex.block_id
      local after = assert(cursor(session,block))
      timer = rex.every(0.1,function()
        local p,err = page(session,block,after,0); if not p then timer:cancel(); error(err) end
        for _,item in ipairs(p.events) do
          for _,entry in ipairs(handlers) do
            if entry.name == 'block_event' or entry.name == item.name then
              local ev,ctx = shape(item,session,block,entry.name == 'block_event')
              local ok,why = pcall(entry.fn,ctx,ev); if not ok then rex.log('error',why) end
            end
          end
        end
        after = p.nextSequence; if p.ended then timer:cancel() end
      end)
    end
    handlers[#handlers+1] = {name=name,fn=fn}
  end
  rex.block = {call=function(kind,method,args)
    if kind ~= 'terminal' and kind ~= 'com.superlogical.terminal' then return nil,'unknown block type' end
    local a={}; for k,v in pairs(args or {}) do a[k]=v end
    a.sessionId=a.sessionId or a.session_id or rex.session_id; a.session_id=nil
    a.blockId=a.blockId or a.block_id or rex.block_id; a.block_id=nil
    return rex.call('terminal.'..method,a)
  end}
end
