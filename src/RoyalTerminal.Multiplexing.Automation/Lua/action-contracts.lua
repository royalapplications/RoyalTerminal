-- Shared by the local script host and operator configuration. Transport is private,
-- local-only. Both runtimes use the same bounded managed JSON Schema validator.
local dk = require 'dkjson'
local function object(t) return setmetatable(t or {}, {__jsontype='object'}) end
local function array(t) return setmetatable(t or {}, {__jsontype='array'}) end
local types={string=true,number=true,integer=true,boolean=true,object=true,array=true,any=true}
local function schema(args)
  assert(type(args)=='table' and args~=dk.null,'action args must be a shorthand map or schema')
  if args.type~=nil then return args end
  local props,required,count=object(),array(),0
  for key,value in pairs(args) do
    count=count+1
    assert(count<=256 and type(key)=='string' and #key>0 and #key<=128 and type(value)=='string','invalid action argument declaration')
    local kind,opt=value:match('^([a-z]+)(%??)$')
    assert(types[kind],'unknown action argument type')
    props[key]=kind=='any' and true or object{type=kind}
    if opt=='' then required[#required+1]=key end
  end
  table.sort(required)
  return object{type='object',properties=props,required=required,additionalProperties=false}
end
local function text(value,default,limit)
  if value==nil then return default end
  assert(type(value)=='string' and #value<=limit,'invalid action metadata')
  return value
end
return function(request)
  local descriptors,typed=object(),{}
  local function declare(d)
    local name=d.name
    assert(type(name)=='string' and #name>0 and #name<=80 and not descriptors[name],'invalid or duplicate action name')
    assert(d.repeats==nil or type(d.repeats)=='boolean','repeats must be boolean')
    local desc=object{name=name,title=text(d.title,name,256),description=text(d.description,'',2048),category=text(d.category,'Actions',128),keywords=text(d.keywords,'',1024),repeats=d.repeats==true}
    if d.args~=nil then
      desc.inputSchema=schema(d.args)
      assert(request{op='contract',name=name,schema=desc.inputSchema})
      -- Manifest and worker must see identical schemas even if the configuration
      -- later mutates the original Lua table.
      desc.inputSchema=assert(dk.decode(assert(dk.encode(desc.inputSchema)),1,dk.null))
      typed[name]=true
    end
    descriptors[name]=desc
  end
  local function validate(name,args)
    if not typed[name] then return true end
    if args==nil then args={} end
    if type(args)~='table' or args==dk.null or (getmetatable(args) or {}).__jsontype=='array' then return nil,'expected action argument object' end
    local wire=object();for k,v in pairs(args) do if type(k)~='string' then return nil,'expected named arguments' end;wire[k]=v end
    return request{op='contract',name=name,arguments=wire}
  end
  return declare,validate,descriptors
end
