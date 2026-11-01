-- UI commands only. Scripts issue immediately; actions stage commands until their outer frame succeeds.
return function(rex, context, default_client, is_loading)
  local frame
  local function object(t) return setmetatable(t,{__jsontype='object'}) end
  local function target(args)
    local ctx=context()
    local s=assert(args.session_id or ctx.session_id,'client action requires a session')
    local c=args.client_id or ctx.client_id or default_client
    if not c then
      local views,e=rex.call('client.views',{sessionId=s}); assert(views,e)
      assert(#views==1,'choose exactly one remote-control-enabled client'); c=views[1].id
    end
    assert(type(c)=='string' and #c>0,'expected client ID'); return s,c
  end
  local function publish(f)
    if #f.commands>0 then
      local r,e=rex.call('client.queue',{sessionId=f.session,clientId=f.client,actions=f.commands}); assert(r,e or 'client queue refused'); return r
    end
  end
  rex.client=rex.client or {}
  -- Discovery is read-only and live. Never stage a discovery call, infer a catalog from the OS, or cache past revocation.
  function rex.client.actions(args)
    assert(not is_loading(),'client discovery is unavailable at configuration load'); args=args or {}
    assert(type(args)=='table','expected a client selector table')
    for k in pairs(args) do assert(k=='session_id' or k=='client_id','unknown client discovery selector') end
    local s,c=target(args); local catalog,err=rex.call('client.actions',{sessionId=s,clientId=c})
    if not catalog then return nil,err end
    assert(catalog.clientId==c and catalog.sessionId==s and type(catalog.forms)=='table','unexpected client catalog'); return catalog
  end
  function rex.client.queue(name,args)
    assert(not is_loading(),'client actions are not permitted at configuration load'); assert(type(name)=='string' and (args==nil or type(args)=='table'),'invalid client action')
    args=args or {}; local command=object{name=name}; local aliases={block_id='blockId',window_id='windowId',direction='direction'}
    for k,v in pairs(args) do if k~='client_id' and k~='session_id' then local key=assert(aliases[k],'unknown client action argument');command[key]=v end end
    local s,c=target(args); local f=frame or {commands={}}; assert(not f.client or f.client==c and f.session==s,'one action batch cannot target multiple views')
    assert(#f.commands<16,'client action batch is full'); f.client,f.session=c,s; f.commands[#f.commands+1]=command
    if not frame then return publish(f) end; return true
  end
  local methods={focus_block='pane.focus',focus_window='window.focus',focus_direction='pane.focus',focus_next_pane='pane.focus',focus_previous_pane='pane.focus',focus_next_window='client.tab.next',focus_previous_window='client.tab.previous'}
  for k,v in pairs(methods) do local key,action=k,v; rex.session[key]=function(args)
    local a={};for n,value in pairs(args or {})do a[n]=value end
    if key=='focus_next_pane' then a.direction='next' elseif key=='focus_previous_pane' then a.direction='previous' end
    return rex.client.queue(action,a)
  end end
  return function(fn,...)
    local outer=frame; frame=outer or {commands={}}; local f=frame; local count,c,s=#f.commands,f.client,f.session
    local ok,value,err=pcall(fn,...); frame=outer
    if not ok or err~=nil then for i=#f.commands,count+1,-1 do f.commands[i]=nil end;f.client,f.session=c,s;if not ok then error(value,0) end;return value,err end
    if not outer then publish(f) end; return value,err
  end
end
