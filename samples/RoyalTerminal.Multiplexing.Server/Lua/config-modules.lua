-- This loader only sees strings in the immutable generation supplied by the broker. No package, filesystem or native-module access.
return function(env, sources)
  if not sources then return end
  local compiled, loaded, values, active = {}, {}, {}, {}
  for name, source in pairs(sources) do
    assert(type(source)=='string' and source:byte(1)~=27, 'invalid module source')
    local chunk, err=loadstring(source, '@modules/'..name:gsub('%.','/')..'.lua')
    assert(chunk,err);setfenv(chunk,env);compiled[name]=chunk
  end
  function env.require(name)
    assert(type(name)=='string' and compiled[name], 'module is not in the active configuration')
    if loaded[name] then return values[name] end
    assert(not active[name], 'circular configuration module: '..name)
    active[name]=true
    local ok,value=pcall(compiled[name],name)
    active[name]=nil
    if not ok then error(value,0) end
    if value==nil then value=true end
    loaded[name],values[name]=true,value
    return value
  end
end
