-- Shared text contract. Run unchanged against a trusted local script or operator configuration action.
local function verify()
  local j=rex.json
  local v,e=j.decode('null');assert(v==nil and e==nil)
  assert(j.encode({})=='{}' and j.encode(false)=='false' and j.encode(nil)=='null')
  local a=assert(j.decode('[null, false, null, 1, null, 2]'));assert(#a==3 and a[1]==false and a[2]==1 and a[3]==2)
  local o=assert(j.decode('{"x":null,"y":false,"z":[]}'));assert(o.x==nil and o.y==false and next(o.z)==nil)
  assert(j.encode(o)=='{"y":false,"z":{}}')
  assert(j.decode('"\\ud83d\\ude80"')=='🚀');assert(j.decode('"💻"')=='💻')
  assert(j.decode('1.5e1')==15);assert(j.encode({a=1},{indent=true}):find('\n'))
  local cycle={};cycle.a=cycle
  for _,bad in ipairs{cycle,function() end,0/0,math.huge,{[1]='a',['1']='b'}} do local text,why=j.encode(bad);assert(text==nil and why) end
  for _,s in ipairs{'[1,]','01','-01','.1','1e','1.','false true','1e999','{"x":1,"x":2}','"\\ud800"','"\\udc00"','"'..string.char(192,128)..'"',string.rep('[',70)..string.rep(']',70)} do local x,why=j.decode(s);assert(x==nil and why,s) end
  assert(j.encode({[2]='two',label='x'})=='{"2":"two","label":"x"}')
  return 'json-parity-ok'
end
if rex.session_id then return verify() end
rex.action{name='json.contract',run=verify}
