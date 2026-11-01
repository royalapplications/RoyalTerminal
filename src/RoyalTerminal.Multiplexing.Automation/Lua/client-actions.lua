-- User-interface commands only. A script issues immediately; configured and local action frames stage until the outer callback succeeds.
return function(rex, context, default_client, is_loading)
  local frame
  local function object(t)return setmetatable(t,{__jsontype='object'}) end
  local function target(ctx,id)
    local s=assert(ctx.session_id,'client action requires a session');local c=id or ctx.client_id or default_client
    if not c then local views,e=rex.call('client.views',{sessionId=s});assert(views,e);assert(#views==1,'choose exactly one remote-control-enabled client');c=views[1].id end
    assert(type(c)=='string' and #c>0,'expected client ID');return s,c
  end
  local function publish(f)
    if #f.commands>0 then local r,e=rex.call('client.queue',{sessionId=f.session,clientId=f.client,actions=f.commands});assert(r,e or 'client queue refused');return r end
  end
  rex.client=rex.client or {}
  function rex.client.queue(name,args)
    assert(not is_loading(),'client actions are not permitted at configuration load');assert(type(name)=='string' and (args==nil or type(args)=='table'),'invalid client action')
    args=args or {};local command=object{name=name};local aliases={block_id='blockId',window_id='windowId',direction='direction'}
    for k,v in pairs(args)do if k~='client_id' and k~='session_id' then local key=assert(aliases[k],'unknown client action argument');command[key]=v end end
    local ctx=context();local s,c=target({session_id=args.session_id or ctx.session_id,client_id=ctx.client_id},args.client_id)
    local f=frame or {commands={}};assert(not f.client or f.client==c and f.session==s,'one action batch cannot target multiple views');assert(#f.commands<16,'client action batch is full');f.client,f.session=c,s;f.commands[#f.commands+1]=command
    if not frame then return publish(f) end;return true
  end
  local methods={focus_block='pane.focus',focus_window='window.focus',focus_direction='pane.focus',focus_next_pane='pane.focus',focus_previous_pane='pane.focus',focus_next_window='client.tab.next',focus_previous_window='client.tab.previous'}
  for k,v in pairs(methods)do local key,action=k,v;rex.session[key]=function(args)local a={};for n,value in pairs(args or {})do a[n]=value end;if key=='focus_next_pane' then a.direction='next' elseif key=='focus_previous_pane' then a.direction='previous' end;return rex.client.queue(action,a) end end
  return function(fn,...)
    local outer=frame;frame=outer or {commands={}};local f=frame;local count,c,s=#f.commands,f.client,f.session
    local ok,value,err=pcall(fn,...)
    frame=outer
    if not ok or err~=nil then for i=#f.commands,count+1,-1 do f.commands[i]=nil end;f.client,f.session=c,s;if not ok then error(value,0) end;return value,err end
    if not outer then publish(f) end;return value,err
  end
end
