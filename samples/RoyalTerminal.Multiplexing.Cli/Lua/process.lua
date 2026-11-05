-- Local binary-safe process bridge; stdout and stderr travel independently in bounded pages.
return function(rex,rpc)
local abc='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'
local rev={};for i=1,64 do rev[abc:byte(i)]=i-1 end
local function enc(s)
  local t={};for i=1,#s,3 do local a,b,c=s:byte(i,i+2);local n=a*65536+(b or 0)*256+(c or 0);local function d(k)local j=math.floor(n/k)%64+1;return abc:sub(j,j)end;t[#t+1]=d(262144)..d(4096)..(b and d(64) or '=')..(c and d(1) or '=') end;return table.concat(t)
end
local function dec(s)
  local t={};for i=1,#s,4 do local a,b,c,d=s:byte(i,i+3);local n=rev[a]*262144+rev[b]*4096+(rev[c] or 0)*64+(rev[d] or 0);t[#t+1]=string.char(math.floor(n/65536))..(rev[c] and string.char(math.floor(n/256)%256) or '')..(rev[d] and string.char(n%256) or '') end;return table.concat(t)
end
local current={}
local function scoped(fn,...)
  local outer=current;current={};local ok,v,err=pcall(fn,...);for h in pairs(current) do h:_release() end;current=outer;if not ok then error(v,0) end;return v,err
end
local function op(kind,id,fields)fields=fields or {};fields.op='process';fields.operation=kind;fields.id=id;return rpc(fields)end
local function clock()return assert(rpc{op='clock'})end
local signals={SIGHUP=1,SIGINT=2,SIGKILL=9,SIGTERM=15,HUP=1,INT=2,KILL=9,TERM=15}
local function start(argv,opts)
  opts=opts or {};if type(argv)~='table' or #argv==0 or type(opts)~='table' then return nil,'expected argv and options' end
  for i,v in pairs(argv) do if type(i)~='number' or i%1~=0 or i<1 or i>#argv or type(v)~='string' then return nil,'expected a dense list of strings' end end
  for i=1,#argv do if type(argv[i])~='string' then return nil,'missing argv entry' end end
  local config=setmetatable({},{__jsontype='object'});for _,k in ipairs{'cwd','env','clear_env','timeout','detach'}do config[k]=opts[k]end
  if type(config.env)=='table' then local env=setmetatable({},{__jsontype='object'});for k,v in pairs(config.env)do env[k]=v end;config.env=env end
  config.stdout=opts.stdout~=false;config.stderr=opts.stderr~=false;config.open=opts.stdin==true
  if type(opts.stdin)=='string' then config.input=enc(opts.stdin) elseif opts.stdin~=nil and opts.stdin~=true and opts.stdin~=false then return nil,'invalid stdin option' end
  local data,err=op('start',nil,{argv=argv,options=config});if not data then return nil,err end
  local id,owned=data.id,current;local h={pid=data.pid};owned[h]=true;local out,errout={},{};local receipt
  function h:_release()if owned[self] then owned[self]=nil;op('release',id)end end
  function h:running()if receipt or not owned[self] then return false end;return op('running',id)end
  function h:write(s)if type(s)~='string' then return nil,'write expects bytes' end;return op('write',id,{data=enc(s)})end
  function h:close_stdin()return op('close',id)end
  function h:kill(sig)local n=type(sig)=='number' and sig or signals[sig or 'SIGTERM'];if not n then return nil,'unknown signal' end;return op('signal',id,{signal=n})end
  function h:wait(seconds)
    if receipt then return receipt end
    if seconds~=nil and (type(seconds)~='number' or seconds~=seconds or seconds<0 or seconds>1800) then return nil,'invalid wait deadline' end
    local until_time=seconds and clock()+seconds
    repeat
      local ms=until_time and math.max(0,math.min(250,math.ceil((until_time-clock())*1000))) or 250
      local p,e=op('poll',id,{ms=ms});if not p then self:_release();return nil,e end
      local function append(buf,encoded,cb)if #encoded==0 then return end;local part=dec(encoded);buf[#buf+1]=part;if type(cb)=='function' then cb(part)end end
      append(out,p.out,opts.stdout);append(errout,p.err,opts.stderr)
      if p.code~=nil then
        local so,se=table.concat(out),table.concat(errout);if opts.text~=false and package.config:sub(1,1)=='\\' then so=so:gsub('\r\n','\n');se=se:gsub('\r\n','\n') end
        receipt={code=p.code,signal=type(p.signal)=='string' and p.signal or nil,timed_out=p.timed_out,ok=p.code==0 and not p.timed_out,stdout=so,stderr=se};self:_release();return receipt
      end
    until until_time and clock()>=until_time
    return nil,'timeout'
  end
  return h
end
rex.exec=setmetatable({start=start},{__call=function(_,argv,opts)local h,e=start(argv,opts);if not h then return nil,e end;return h:wait()end})
return scoped
end
