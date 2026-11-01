-- Installed from disk in both Lua hosts before evaluating any user source.
local src=assert(debug.getinfo(1,'S').source:match('^@(.*[/\\])'),'no workspace module directory')
local json=assert(loadfile(src..'public-json.lua'))()
local methods=assert(loadfile(src..'layout-methods.lua'))()
return function(rex,context)
  json(rex.json)
  return methods(rex,context)
end
