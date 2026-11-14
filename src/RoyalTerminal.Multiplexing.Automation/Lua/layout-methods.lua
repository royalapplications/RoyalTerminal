-- Shared by trusted local scripts and the operator's init.lua worker. Constructors never perform I/O.
return function(rex, original_context)
  local function object(t) return setmetatable(t or {}, {__jsontype='object'}) end
  local function copy(t) local r=object(); for k,v in pairs(t or {}) do r[k]=v end; return r end
  local current_event
  local function context() return current_event or original_context() end
  if rex.on then
    local on=rex.on
    function rex.on(name,fn)
      assert(type(fn)=='function','expected an event handler')
      return on(name,function(ctx,ev)
        local prior=current_event;current_event=ctx
        local ok,err=pcall(fn,ctx,ev);current_event=prior
        if not ok then error(err,0) end
      end)
    end
  end
  local extension=rex.block and rex.block.call
  local supported={format=true,process=true,pwd=true,title=true,size=true,write=true,reset=true,resize=true,set_theme=true,program_status=true,list_dir=true}
  rex.block=rex.block or {}
  function rex.block.call(creator,method,args)
    if creator~='terminal' and creator~='com.superlogical.terminal' then return nil,'unknown block creator' end
    if type(method)~='string' or args~=nil and type(args)~='table' then return nil,'invalid block method or arguments' end
    local c=context();local a=copy(args)
    if a.session_id and a.sessionId or a.block_id and a.blockId then return nil,'duplicate scope' end
    local s,b=a.session_id or a.sessionId or c.session_id,a.block_id or a.blockId or c.block_id
    if not s or not b then return nil,'this call requires a session and block' end
    a.session_id,a.sessionId,a.block_id,a.blockId=nil,nil,nil,nil
    if supported[method] then return rex.call('com.superlogical.terminal.'..method,object{sessionId=s,blockId=b,args=a}) end
    if extension then a.sessionId,a.blockId=s,b;return extension(creator,method,a) end
    return nil,'unknown block method'
  end
  local function branch(axis,r,a,b)
    assert(type(r)=='number' and r>0 and r<1 and type(a)=='table' and type(b)=='table','invalid split')
    return object{orientation=axis,ratio=r,first=a,second=b}
  end
  rex.layout={block=function(def)
    assert(type(def)=='table' and type(def.flavor)=='string','expected block definition')
    local node=copy(def); if node.options~=nil then assert(type(node.options)=='table'); node.options=copy(node.options) end; return node
  end,horizontal=function(r,a,b)return branch('horizontal',r,a,b)end, vertical=function(r,a,b)return branch('vertical',r,a,b)end}
  local previous=rex.session.new_window
  local function launch(args,split)
    if type(args)~='table' or type(args.layout)~='table' then return nil,'a layout is required' end
    local permitted={session_id=true,sessionId=true,layout=true,label=true,expected_revision=true,expectedRevision=true,focus=true}
    if split then permitted.block_id=true; permitted.blockId=true; permitted.direction=true; permitted.side=true; permitted.ratio=true end
    for k in pairs(args) do if not permitted[k] then return nil,'unknown layout argument: '..tostring(k) end end
    if args.focus~=nil and args.focus~=false then return nil,'this host cannot change another client’s focus' end
    local ctx=context(); local session=args.session_id or args.sessionId or ctx.session_id
    if not session then return nil,'a session is required' end
    local input=object{sessionId=session,layout=args.layout,label=args.label,expectedRevision=args.expected_revision or args.expectedRevision}
    if split then
      input.blockId=args.block_id or args.blockId or ctx.block_id; if not input.blockId then return nil,'a split anchor is required' end
      input.direction=args.direction;input.side=args.side;input.ratio=args.ratio
    end
    local result,err=rex.call('session.launch_layout', input); if not result then return nil,err end
    return object{window_id=result.windowId,block_ids=result.blockIds,revision=result.revision}
  end
  function rex.session.new_window(args) if args and args.layout~=nil then return launch(args,false) end; return previous(args) end
  function rex.session.new_split(args) return launch(args,true) end
  function rex.session.list_blocks(args)
    local view,err=rex.session.view(args or {}); if not view then return nil,err end
    return {blocks=view.blocks}
  end
  function rex.session.describe_block(args)
    if type(args)~='table' or not args.block_id then return nil,'block_id is required' end
    local view,e=rex.session.view{session_id=args.session_id}; if not view then return nil,e end
    for _,b in ipairs(view.blocks) do if b.id==args.block_id then return b end end; return nil,'block not found'
  end
  local function place(args,kind)
    if type(args)~='table' or not args.block_id then return nil,'block_id is required' end
    local session=args.session_id or context().session_id; local win=args.window_id; local rev=args.expected_revision
    if not win then
      local view,e=rex.session.view{session_id=session}; if not view then return nil,e end
      for _,b in ipairs(view.blocks) do if b.id==args.block_id then win=b.windowId;break end end
      if not win then return nil,'block not found' end; rev=rev or view.revision
    end
    return rex.call('block.place',object{sessionId=session,blockId=args.block_id,windowId=win,kind=kind,bounds=args.bounds,expectedRevision=rev})
  end
  function rex.session.detach_block(args)return place(args,'unplaced')end
  function rex.session.float_block(args)return place(args,'floating')end
end
