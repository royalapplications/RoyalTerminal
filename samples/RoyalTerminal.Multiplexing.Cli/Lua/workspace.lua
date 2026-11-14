-- Keep the framed streams private before runtime.lua redirects ordinary script output to stderr.
assert(_VERSION=='Lua 5.1','Lua 5.1 is required')
local directory=assert(arg[0]:match('^(.*[/\\])'))
local json,wire,input=require('dkjson'),io.stdout,io.stdin
local function local_call(message)
  local data,err=json.encode(message);if not data then return nil,err end
  if #data>262144 then return nil,'RPC frame exceeds 256 KiB' end
  wire:write(data,'\n');wire:flush()
  local reply,_,reason=json.decode(assert(input:read('*l'),'RPC pipe closed'),1,json.null)
  if not reply then error(reason) end;if reply.error then return nil,reply.error end;return reply.value
end
local file=arg[1]
package.preload['royal.workspace.invocation']=function()return {file=file,directory=directory,call=local_call}end
arg[1]=directory..'entry.lua'
dofile(directory..'runtime.lua')
