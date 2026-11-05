-- Configuration-local key declarations. Raw callbacks never cross the JSON boundary.
return function(rex, array, object, is_loading)
  local rows,modes,order={}, {},{};local issued=setmetatable({},{__mode='k'});local serial=0
  local function loading() assert(is_loading(),'key declarations are load-time only') end
  local function mode_ok(s)return type(s)=='string' and #s>0 and #s<=64 and s:match('^[a-z0-9_-]+$')end
  local function mode(name)
    assert(mode_ok(name),'invalid key mode');if not modes[name] then assert(#order<128,'mode quota');modes[name]={name=name,exclusive=false};order[#order+1]=name end;return modes[name]
  end
  local aliases={control='ctrl',cmd='meta',command='meta',super='meta',win='meta',option='alt',esc='escape',['return']='enter',spacebar='space'}
  local function stroke(key)
    assert(#key>0 and #key<=128 and not key:find('%s') and not key:find('%c'),'invalid stroke')
    local mods,main={},nil;for p in (key..'+'):gmatch('(.-)%+') do p=aliases[p] or p
      if p=='ctrl' or p=='meta' or p=='alt' or p=='shift' then assert(not mods[p],'duplicate modifier');mods[p]=true
      else assert(not main and #p>0 and #p<=64 and not p:find('/',1,true),'expected one key');main=p end end
    assert(main,'missing key');local result='';for _,m in ipairs{'ctrl','alt','shift','meta'}do if mods[m]then result=result..m..'+' end end;return result..main
  end
  local function split(raw)
    assert(type(raw)=='string' and #raw>0 and #raw<=1024,'invalid binding');raw=raw:lower()
    local m,k=raw:match('^([^/]+)/(.+)$');k=k or raw;if m then mode(m)end
    local keys={};for key in (k..'>'):gmatch('(.-)>')do assert(#keys<8,'chord too long');keys[#keys+1]=stroke(key) end;return m or 'default',table.concat(keys,'>')
  end
  local function remove(m,k,target,one)
    local count=0;for i=#rows,1,-1 do local b=rows[i];if b.mode==m and (not k or b.key==k) and (not target or b.target==target) and (not one or b.id==one) then table.remove(rows,i);count=count+1 end end;return count
  end
  function rex.mode(name,options)loading();local m=mode(name);options=options or {};for k,v in pairs(options)do assert(k=='exclusive' and type(v)=='boolean','invalid mode option')end;m.exclusive=options.exclusive or false end
  local function source()local p=debug.getinfo(3,'l');return 'init.lua:'..tostring(p and p.currentline or 0)end
  function rex.bind(key,target,args)
    loading();local m,k=split(key);assert(type(target)=='string' or type(target)=='function' and m=='default','invalid target');assert(args==nil or type(args)=='table','invalid binding arguments');assert(#rows<2048,'binding quota')
    if type(target)=='string' then assert(#target>0 and #target<=128,'invalid action name');remove(m,k)end
    serial=serial+1;rows[#rows+1]={mode=m,key=k,target=target,args=object(args),id=serial,source=source()}
  end
  function rex.unbind(key,target)
    loading();if type(key)=='table' then local ref=issued[key];if ref then return remove(ref.mode,ref.key,nil,ref.id) end
      assert(mode_ok(key.mode) and key.mode~='default','invalid mode removal');local count=remove(key.mode);modes[key.mode]=nil;for i=#order,1,-1 do if order[i]==key.mode then table.remove(order,i) end end;return count end
    local m,k=split(key);local n=remove(m,k,target);if target==nil then serial=serial+1;assert(#rows<2048);rows[#rows+1]={mode=m,key=k,id=serial,blocked=true,source=source()}end;return n
  end
  function rex.bindings(filter)loading();filter=filter or {};local result=array{};for _,b in ipairs(rows)do if not b.blocked and (not filter.mode or filter.mode==b.mode) and (not filter.key or filter.key==b.key) and (not filter.action or b.target==filter.action) then local item={key=b.key,mode=b.mode~='default' and b.mode or nil,action=type(b.target)=='string' and b.target or nil,fn=type(b.target)=='function' and b.target or nil,args=b.args,source=b.source};issued[item]=b;result[#result+1]=item end end;return result end
  function rex.modes()loading();local r=array{};for _,name in ipairs(order)do local m=modes[name];r[#r+1]={name=name,exclusive=m.exclusive}end;return r end
  local function export()
    local entries=array{};for _,b in ipairs(rows)do entries[#entries+1]={mode=b.mode,sequence=b.key,action=type(b.target)=='string' and b.target or nil,callback=type(b.target)=='function' and b.id or nil,blocked=b.blocked,args=b.args,source=b.source} end
    local ms=array{};for _,n in ipairs(order)do ms[#ms+1]=modes[n]end;return {bindings=entries,modes=ms}
  end
  local function invoke(id,ctx)
    local last;for i=#rows,1,-1 do local b=rows[i];if b.id==id then last=b;break end end
    assert(last and type(last.target)=='function','unknown binding');local keys={};for k in last.key:gmatch('[^>]+')do keys[#keys+1]=k end;local event={keybinding=table.remove(keys),prefix=#keys>0 and keys or nil};ctx.origin='keybinding'
    for i=#rows,1,-1 do local b=rows[i];if b.key==last.key and b.mode==last.mode and type(b.target)=='function' and b.target(ctx,event)==true then return true end end;return false
  end
  return export,invoke
end
