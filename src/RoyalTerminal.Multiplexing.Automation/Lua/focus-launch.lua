-- Resolve the destination before starting a process, and never re-resolve after a side effect.
return function(rex,check_capacity,enqueue)
  for _,key in ipairs{'new_window','new_split','new_layer'} do
    local name,create=key,rex.session[key]
    rex.session[name]=function(args)
      if type(args)~='table' then return nil,'expected layout arguments' end
      if args.focus~=nil and type(args.focus)~='boolean' then return nil,'focus must be boolean' end
      if args.clientId~=nil and args.client_id~=nil or args.sessionId~=nil and args.session_id~=nil then return nil,'duplicate target selector' end
      if args.focus==nil and args.client_id==nil and args.clientId==nil then return create(args) end
      local input={};for k,v in pairs(args)do if k~='focus' and k~='client_id' and k~='clientId' then input[k]=v end end
      if args.focus~=true then
        if args.client_id~=nil or args.clientId~=nil then return nil,'a client selector requires focus=true' end
        return create(input)
      end
      local kind,field='pane.focus','blockId';if name=='new_window' and not args.layout then kind,field='window.focus','windowId' end
      local ok,catalog,why=pcall(rex.client.actions,{session_id=args.session_id or args.sessionId,client_id=args.client_id or args.clientId})
      if not ok or not catalog then return nil,tostring(why or catalog) end
      local supported=false;for _,form in ipairs(catalog.forms)do if form.name==kind and form.requiredArgument==field then supported=true;break end end
      if not supported then return nil,'selected receiver does not support absolute focus' end
      local room,err=pcall(check_capacity,catalog.sessionId,catalog.clientId);if not room then return nil,tostring(err) end
      input.sessionId=nil;input.session_id=catalog.sessionId
      local created,failure=create(input);if not created then return nil,failure end
      local target=field=='windowId' and (created.window_id or created.id) or created.block_ids and created.block_ids[1]
      local status={sessionId=catalog.sessionId,clientId=catalog.clientId,state='unconfirmed'};created.focus=status
      if type(target)~='string' then status.error='created layout did not return a focus target';return created end
      local sent,ticket=pcall(enqueue,catalog.sessionId,catalog.clientId,{name=kind,[field]=target},status)
      if not sent then status.error=tostring(ticket)
      elseif ticket==true then status.state='staged'
      else status.state='queued';status.sequence=ticket.sequence end
      return created
    end
  end
end
