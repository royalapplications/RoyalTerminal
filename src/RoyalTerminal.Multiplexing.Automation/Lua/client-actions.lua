-- UI actions have no PTY authority. Only an explicitly successful outer action publishes its staged batch.
local dir=assert(debug.getinfo(1,'S').source:match('^@(.*[/\\])'))
local install_launch=assert(loadfile(dir..'focus-launch.lua'))()
return function(rex, context, default_client, is_loading)
  local frame
  local function object(t) return setmetatable(t,{__jsontype='object'}) end
  local function target(args)
    local ctx=context();local s=assert(args.session_id or ctx.session_id,'client action requires a session')
    local c=args.client_id or ctx.client_id or default_client
    if not c then
      local views,e=rex.call('client.views',{sessionId=s});assert(views,e)
      assert(#views==1,'choose exactly one remote-control-enabled client');c=views[1].id
    end
    assert(type(c)=='string' and #c>0,'expected client ID');return s,c
  end
  local function capacity(s,c)
    if frame then
      assert(not frame.client or frame.client==c and frame.session==s,'one action batch cannot target multiple views')
      assert(#frame.commands<16,'client action batch is full')
    end
  end
  local function publish(f)
    local ticket,e=rex.call('client.queue',{sessionId=f.session,clientId=f.client,actions=f.commands});assert(ticket,e or 'client queue refused')
    assert(ticket.clientId==f.client and type(ticket.sequence)=='number','invalid queue receipt');ticket.sessionId=f.session;return ticket
  end
  rex.client=rex.client or {}
  function rex.client.actions(args)
    assert(not is_loading(),'client discovery is unavailable at configuration load');args=args or {}
    assert(type(args)=='table','expected a client selector table')
    for k in pairs(args) do assert(k=='session_id' or k=='client_id','unknown client discovery selector') end
    local s,c=target(args);local catalog,err=rex.call('client.actions',{sessionId=s,clientId=c})
    if not catalog then return nil,err end
    assert(catalog.clientId==c and catalog.sessionId==s and type(catalog.forms)=='table','unexpected client catalog');return catalog
  end
  function rex.client.result(ticket,ms)
    if is_loading() or type(ticket)~='table' or type(ticket.sessionId)~='string' or type(ticket.clientId)~='string' or type(ticket.sequence)~='number' or ticket.sequence<1 or ticket.sequence>9007199254740991 or ticket.sequence~=math.floor(ticket.sequence) then return nil,'an already published receipt with sessionId, clientId and sequence is required' end
    ms=ms or 0;if type(ms)~='number' or ms~=math.floor(ms) or ms<0 or ms>20000 then return nil,'timeout must be an integer from 0 through 20000 ms' end
    return rex.call('client.view_result',{sessionId=ticket.sessionId,clientId=ticket.clientId,sequence=ticket.sequence,timeoutMs=ms})
  end
  local function enqueue(s,c,command,focus)
    capacity(s,c);local f=frame or {commands={},launches={}};local i=#f.commands+1
    f.client,f.session=c,s;f.commands[i]=command;f.launches[i]=focus
    if focus then focus.index=i end
    if not frame then return publish(f) end;return true
  end
  function rex.client.queue(name,args)
    assert(not is_loading(),'client actions are not permitted at configuration load');assert(type(name)=='string' and (args==nil or type(args)=='table'),'invalid client action')
    args=args or {};local command=object{name=name};local aliases={block_id='blockId',window_id='windowId',direction='direction'}
    for k,v in pairs(args) do if k~='client_id' and k~='session_id' then command[assert(aliases[k],'unknown client action argument')]=v end end
    local s,c=target(args);return enqueue(s,c,command)
  end
  install_launch(rex,capacity,enqueue)
  local methods={focus_block='pane.focus',focus_window='window.focus',focus_direction='pane.focus',focus_next_pane='pane.focus',focus_previous_pane='pane.focus',focus_next_window='client.tab.next',focus_previous_window='client.tab.previous'}
  for k,v in pairs(methods) do local key,action=k,v;rex.session[key]=function(args)
    local a={};for n,value in pairs(args or {})do a[n]=value end
    if key=='focus_next_pane' then a.direction='next' elseif key=='focus_previous_pane' then a.direction='previous' end
    return rex.client.queue(action,a)
  end end
  return function(fn,...)
    local outer=frame;frame=outer or {commands={},launches={}};local f=frame;local count,c,s=#f.commands,f.client,f.session
    local ok,value,err=pcall(fn,...);frame=outer
    if not ok or err~=nil then
      for i=#f.commands,count+1,-1 do if f.launches[i] then f.launches[i].state='discarded' end;f.commands[i],f.launches[i]=nil,nil end
      f.client,f.session=c,s;if not ok then error(value,0) end;return value,err
    end
    if not outer and #f.commands>0 then
      local sent,ticket=pcall(publish,f);local focus_only=true
      for i=1,#f.commands do local launch=f.launches[i]
        if launch then launch.state=sent and 'queued' or 'unconfirmed';if sent then launch.sequence=ticket.sequence else launch.error=tostring(ticket) end
        else focus_only=false end
      end
      if not sent and not focus_only then error(ticket,0) end
    end
    return value,err
  end
end
