-- Installed in both trusted Lua hosts before user source is evaluated.
local src=assert(debug.getinfo(1,'S').source:match('^@(.*[/\\])'),'no workspace module directory')
local json=assert(loadfile(src..'public-json.lua'))()
local methods=assert(loadfile(src..'layout-methods.lua'))()
return function(rex,context)
  json(rex.json); methods(rex,context)
  local aliases={session_id='sessionId',block_id='blockId',target_id='targetId',first_id='firstId',second_id='secondId',window_id='windowId',to_edge='toEdge',expected_revision='expectedRevision',layer_id='layerId'}
  for _,key in ipairs{'help','list_blocks','list_clients','describe_block','move_block','swap_blocks','resize_pane','resize_layer','raise_layer','lower_layer','set_layer_order','new_block','new_layer','close_layer','zoom_block','set_layer_layout','group_layer'} do
    local name=key
    rex.session[name]=function(args)
      if args~=nil and type(args)~='table' then return nil,'expected session arguments' end
      local a=setmetatable({},{__jsontype='object'})
      for k,v in pairs(args or {}) do
        local mapped=aliases[k] or k
        if mapped=='layerId' and (name=='close_layer' or name=='raise_layer' or name=='lower_layer' or name=='resize_layer') then mapped='blockId' end
        if type(mapped)~='string' or a[mapped]~=nil then return nil,'invalid or duplicated session argument' end;a[mapped]=v
      end
      a.sessionId=a.sessionId or context().session_id
      local value,e=rex.call('session.'..name,a)
      if not value then return nil,e end
      if name=='describe_block' then local b=value.block;b.creator=value.creator;b.methods=value.methods;return b end
      if name=='new_block' or name=='new_layer' then
        return {block_id=value.id,window_id=value.windowId,layer_id=name=='new_layer' and (value.layerId or value.id) or nil,block_ids=value.blockIds or {value.id},block=value}
      end
      return value,e
    end
  end
end
