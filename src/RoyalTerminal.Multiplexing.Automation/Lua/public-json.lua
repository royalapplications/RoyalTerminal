-- Public script codec. Private RPC frames deliberately keep their explicit null sentinels.
-- Bounded, locale-independent JSON grammar; dkjson supplies scalar quoting and number formatting.
local dk=require 'dkjson'
local floor,huge=math.floor,math.huge
local LIMIT,DEPTH=262144,64
local function scalar(s,at)
  local a,b,c,d=s:byte(at,at+3)
  local function tail(n) return n and n>=128 and n<=191 end
  if a<128 then return 1 end
  if a>=194 and a<=223 and tail(b) then return 2 end
  if a>=224 and a<=239 and tail(b) and tail(c) and not(a==224 and b<160) and not(a==237 and b>=160) then return 3 end
  if a>=240 and a<=244 and tail(b) and tail(c) and tail(d) and not(a==240 and b<144) and not(a==244 and b>=144) then return 4 end
  error('invalid UTF-8',0)
end
local function checktext(s)
  if #s>LIMIT then error('JSON byte limit exceeded',0) end
  local i=1;while i<=#s do i=i+scalar(s,i) end
end
local function utf8(cp)
  if cp<128 then return string.char(cp) end
  if cp<2048 then return string.char(192+floor(cp/64),128+cp%64) end
  if cp<65536 then return string.char(224+floor(cp/4096),128+floor(cp/64)%64,128+cp%64) end
  return string.char(240+floor(cp/262144),128+floor(cp/4096)%64,128+floor(cp/64)%64,128+cp%64)
end
local function decode(s)
  if type(s)~='string' or #s>LIMIT then error('expected bounded JSON text',0) end
  local i,n=1,#s
  local function fail() error('invalid JSON at byte '..i,0) end
  local function ws() local _,j=s:find('^[ \t\r\n]*',i);i=(j or i-1)+1 end
  local function hex() local h=s:sub(i,i+3);if #h~=4 or not h:match('^%x%x%x%x$') then fail() end;i=i+4;return tonumber(h,16) end
  local esc={['"']='"',['\\']='\\',['/']='/',b='\b',f='\f',n='\n',r='\r',t='\t'}
  local function text()
    i=i+1;local pieces,start={},i
    while i<=n do
      local b=s:byte(i)
      if b==34 then pieces[#pieces+1]=s:sub(start,i-1);i=i+1;return table.concat(pieces) end
      if b==92 then
        pieces[#pieces+1]=s:sub(start,i-1);local e=s:sub(i+1,i+1);i=i+2
        if e=='u' then
          local cp=hex();if cp>=55296 and cp<=56319 then
            if s:sub(i,i+1)~='\\u' then fail() end;i=i+2;local lo=hex();if lo<56320 or lo>57343 then fail() end;cp=65536+(cp-55296)*1024+lo-56320
          elseif cp>=56320 and cp<=57343 then fail() end
          pieces[#pieces+1]=utf8(cp)
        elseif esc[e] then pieces[#pieces+1]=esc[e] else fail() end
        start=i
      elseif b<32 then fail() else i=i+scalar(s,i) end
    end;fail()
  end
  local function digits() local start=i;while (s:byte(i) or 0)>=48 and (s:byte(i) or 0)<=57 do i=i+1 end;if i==start then fail() end end
  local function number()
    local start=i;if s:sub(i,i)=='-' then i=i+1 end
    if s:sub(i,i)=='0' then i=i+1 else digits() end
    if s:sub(i,i)=='.' then i=i+1;digits() end
    if s:sub(i,i):lower()=='e' then i=i+1;if s:sub(i,i)=='+' or s:sub(i,i)=='-' then i=i+1 end;digits() end
    local v=dk.decode(s:sub(start,i-1));if type(v)~='number' or v~=v or math.abs(v)==huge then fail() end;return v
  end
  local value
  value=function(depth)
    if depth>DEPTH then error('JSON depth limit exceeded',0) end
    ws();local ch=s:sub(i,i)
    if ch=='"' then return text() end
    if ch=='-' or ch:match('%d') then return number() end
    if ch=='[' or ch=='{' then
      local array=ch=='[';local tail=array and ']' or '}';local t,seen={},{};i=i+1;ws()
      if s:sub(i,i)==tail then i=i+1;return setmetatable(t,{__jsontype='object'}) end
      while true do
        if array then local v=value(depth+1);if v~=nil then t[#t+1]=v end
        else
          ws();if s:sub(i,i)~='"' then fail() end;local key=text();if seen[key] then error('duplicate JSON key',0) end;seen[key]=true
          ws();if s:sub(i,i)~=':' then fail() end;i=i+1;t[key]=value(depth+1)
        end
        ws();local sep=s:sub(i,i);i=i+1;if sep==tail then break end;if sep~=',' then fail() end
      end
      return setmetatable(t,{__jsontype=array and #t>0 and 'array' or 'object'})
    end
    if s:sub(i,i+3)=='true' then i=i+4;return true end
    if s:sub(i,i+4)=='false' then i=i+5;return false end
    if s:sub(i,i+3)=='null' then i=i+4;return nil end
    fail()
  end
  local result=value(0);ws();if i<=n then fail() end;return result
end
local function encode(v,opts)
  if opts~=nil and type(opts)~='table' then error('expected encoding options',0) end
  local pretty=opts and opts.indent==true;local buf,total,visiting={},0,{}
  local function put(s) total=total+#s;if total>LIMIT then error('JSON byte limit exceeded',0) end;buf[#buf+1]=s end
  local function line(d) if pretty then put('\n'..string.rep('  ',d)) end end
  local function emit(x,depth)
    if depth>DEPTH then error('JSON depth limit exceeded',0) end
    if x==nil or rawequal(x,dk.null) then put('null');return end
    local k=type(x)
    if k=='string' then checktext(x);put(dk.quotestring(x));return end
    if k=='boolean' then put(x and 'true' or 'false');return end
    if k=='number' then if x~=x or math.abs(x)==huge then error('nonfinite JSON number',0) end;put(dk.encode(x));return end
    if k~='table' or visiting[x] then error('uncodable or cyclic JSON value',0) end
    visiting[x]=true;local count,max,onlyints=0,0,true
    for key in next,x do
      count=count+1;if count>LIMIT/2 then error('JSON entry limit exceeded',0) end
      if type(key)=='number' and key>=1 and key==floor(key) then max=math.max(max,key) else onlyints=false end
    end
    if count>0 and onlyints and max==count then
      put('[');for j=1,count do if j>1 then put(',') end;line(depth+1);emit(rawget(x,j),depth+1) end;line(depth);put(']')
    else
      local keys,original={},{}
      for key in next,x do
        if type(key)~='string' and type(key)~='number' then error('invalid JSON object key',0) end
        local name=type(key)=='number' and dk.encode(key) or key;checktext(name)
        if original[name]~=nil then error('colliding JSON object keys',0) end;original[name]=key;keys[#keys+1]=name
      end
      table.sort(keys);put('{');for j,name in ipairs(keys) do if j>1 then put(',') end;line(depth+1);put(dk.quotestring(name));put(pretty and ': ' or ':');emit(rawget(x,original[name]),depth+1) end
      if count>0 then line(depth) end;put('}')
    end
    visiting[x]=nil
  end
  emit(v,0);return table.concat(buf)
end
return function(api)
  local function safe(fn) return function(...) local ok,v=pcall(fn,...);if ok then return v end;return nil,tostring(v) end end
  api.encode=safe(encode);api.decode=safe(decode)
end
