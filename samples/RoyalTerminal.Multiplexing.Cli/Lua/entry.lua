local entry = require('royal.workspace.invocation')
local call = entry.call
local plan, err = call {op='setup'}
if err then error(err, 0) end
arg[1] = entry.file

-- loadfile returns a chunk; executing that chunk returns the module's installation function.
local function install(name)
  local module = assert(loadfile(entry.directory .. name))()
  return module(rex, call)
end
local scoped = install('process.lua')
install('block-events.lua')
for _, name in ipairs {'after','every'} do
  local original = rex[name]
  rex[name] = function(n, fn) return original(n, function(...) return scoped(fn, ...) end) end
end
local on = rex.on
rex.on = function(event, fn) return on(event, function(...) return scoped(fn, ...) end) end
local action = rex.action
rex.action = function(def)
  local copy = {}; for k, v in pairs(def) do copy[k] = v end
  local run = assert(copy.run)
  copy.run = function(...) return scoped(run, ...) end
  return action(copy)
end
local chunk
if plan then rex.args = plan.args; chunk = assert(loadstring(plan.source, plan.name))
else chunk = assert(loadfile(entry.file)) end
local value = scoped(chunk)
if plan and type(plan.action) == 'string' then
  value, err = rex.invoke(plan.action, rex.args)
  if err then error(err, 0) end
end
if plan and value ~= nil then assert(call {op='result',value=value}) end
