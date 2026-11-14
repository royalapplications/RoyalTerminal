-- Shared declaration and bound-peer API. Credentials and transport selection stay in the host.
return function(rex, is_loading, configured)
  local function array(t) return setmetatable(t or {}, {__jsontype='array'}) end
  local function object(t) return setmetatable(t or {}, {__jsontype='object'}) end
  local declared,ids,labels=array(),{},{}
  function rex.host(def)
    assert(is_loading(), 'host declarations are load-time only')
    if not configured then return end -- declarations in scripts never modify server state
    assert(type(def)=='table' and type(def.label)=='string' and #def.label>0,'expected host label')
    for k in pairs(def) do assert(k=='label' or k=='id' or k=='icon' or k=='endpoint' or k=='endpoints','unknown host field') end
    local id=def.id or def.label
    assert(type(id)=='string' and #id>0 and not ids[id] and not labels[def.label] and #declared<64,'duplicate or invalid host')
    assert(not(def.endpoint and def.endpoints),'choose endpoint or endpoints')
    local src=def.endpoints or {def.endpoint};local ep=array()
    assert(type(src)=='table' and #src>0 and #src<=8,'expected 1-8 endpoints')
    for k,v in pairs(src) do assert(type(k)=='number' and k==math.floor(k) and k>=1 and k<=#src and type(v)=='string','expected dense string endpoints') end
    for i=1,#src do assert(type(src[i])=='string');ep[i]=src[i] end
    labels[def.label]=true;ids[id]=true;declared[#declared+1]=object{id=id,label=def.label,icon=def.icon,endpoints=ep}
  end
  local function available()
    if configured then return declared end
    local v,e=rex.call('server.hosts',{});if not v then error(e or 'unable to list hosts',2) end;return v.hosts
  end
  function rex.servers()
    local names=array();for _,peer in ipairs(available())do names[#names+1]=peer.label end;return names
  end
  function rex.server(label)
    assert(type(label)=='string','expected host label');local id
    for _,peer in ipairs(available())do if peer.label==label then id=peer.id;break end end
    assert(id,'unknown host: '..label)
    local bound={}
    function bound.call(method,args)
      if type(method)~='string' or args~=nil and type(args)~='table' then return nil,'expected a method and object arguments' end
      local input=object();for k,v in pairs(args or {})do input[k]=v end
      return rex.call('server.call_host',object{id=id,method=method,arguments=input})
    end
    bound.session=setmetatable({},{__index=function(_,name)return function(args)
      local a=object();for k,v in pairs(args or {})do local field=k:gsub('_(%l)',string.upper);if a[field]~=nil then return nil,'duplicate argument' end;a[field]=v end
      return bound.call('session.'..name,a)
    end end})
    bound.block={call=function(kind,method,args)
      if kind~='terminal' and kind~='com.superlogical.terminal' then return nil,'unknown block creator' end
      if type(method)~='string' or type(args)~='table' then return nil,'explicit session_id and block_id are required for a peer' end
      local s,b=args.session_id or args.sessionId,args.block_id or args.blockId
      if not s or not b or args.session_id and args.sessionId or args.block_id and args.blockId then return nil,'invalid remote block scope' end
      local rest=object();for k,v in pairs(args)do if k~='session_id' and k~='sessionId' and k~='block_id' and k~='blockId' then rest[k]=v end end
      return bound.call('com.superlogical.terminal.'..method,object{sessionId=s,blockId=b,args=rest})
    end}
    return bound
  end
  return function()return declared end
end
