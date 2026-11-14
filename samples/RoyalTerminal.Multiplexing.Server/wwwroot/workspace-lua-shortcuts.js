import {Keymap} from './keymap.js';
import {localAction} from './workspace-key-actions.js';
import {captureKey} from './workspace-key-event.js';
export function compileLua(manifest){
  const entries=new Map(),bindings=[],modes=manifest.keymap.modes;
  for(const [i,b] of manifest.keymap.bindings.entries()){const key={mode:b.mode,sequence:b.sequence};
    if(b.action==='client.mode.enter'){if(!modes.some(m=>m.name===b.args?.name))throw Error('Unknown Lua mode: '+b.args?.name);bindings.push({...key,enterMode:b.args.name,once:b.args.once===true});}
    else if(b.action==='client.mode.exit')bindings.push({...key,exitMode:true});else if(b.action==='client.mode.exit_all')bindings.push({...key,exitAllModes:true});else if(b.blocked&&b.mode!=='default')bindings.push({...key,block:true});else{const actionId=String(i);entries.set(actionId,b);bindings.push({...key,actionId});}}
  return {engine:new Keymap({modes,bindings}),entries};
}
export function luaShortcutActions(ctx){
  if(typeof document==='undefined')return [];let manifest=null,compiled=null,epoch=-1,loading=0,layout=null,layoutTicket=0;
  const app=document.getElementById('application'),hint=document.getElementById('chord-hint'),inFlight=new Set();
  async function readLayout(){const ticket=++layoutTicket;layout=null;try{const map=await navigator.keyboard?.getLayoutMap?.();if(ticket===layoutTicket)layout=map;}catch{/* Physical bindings need no layout permission. */}}
  void readLayout();navigator.keyboard?.addEventListener?.('layoutchange',readLayout);
  async function refresh(reload=false){const ticket=++loading,gen=ctx.state.generation;if(ctx.state.stopped||!ctx.state.capability?.administrator){compiled=manifest=null;return;}
    let m;try{m=await ctx.call(reload?'lua.reload':'lua.actions');}catch(e){if(e.code==='method_not_found'){if(ticket===loading)compiled=manifest=null;return;}throw e;}
    if(ticket!==loading||gen!==ctx.state.generation||ctx.state.stopped)return;const built=compileLua(m);manifest=m;compiled=built;epoch=gen;ctx.paletteChanged?.();}
  new MutationObserver(()=>{compiled=manifest=null;epoch=-1;void refresh().catch(e=>ctx.toast(e.message));}).observe(app,{attributes:true,attributeFilter:['hidden']});
  function state(){const s=ctx.state,v=s.remoteView;return {sessionId:s.sessionId,windowId:s.windowId,blockId:s.focused,clientId:v?.session===s.sessionId&&v.generation===s.generation?v.id:null,generation:s.generation,pane:s.panes.get(s.focused),authority:s.panes.get(s.focused)?.connection};}
  function current(x){return !ctx.state.stopped&&ctx.state.generation===x.generation&&ctx.state.sessionId===x.sessionId&&ctx.state.windowId===x.windowId&&ctx.state.focused===x.blockId;}
  const ids=x=>Object.fromEntries(['sessionId','windowId','blockId','clientId'].filter(k=>x[k]).map(k=>[k,x[k]]));
  function valid(x){return current(x)&&x.pane?.ready&&x.pane.hasControl&&x.pane.connection===x.authority&&!document.querySelector('dialog[open]');}
  async function encode(x,method,args){if(!valid(x))throw Error('The original terminal no longer has input control.');
    const {value}=await ctx.call(method,{sessionId:x.sessionId,blockId:x.blockId,...args});if(!valid(x))throw Error('Control changed; input was not sent.');return value?{type:'input',data:value}:null;}
  async function run(b,x,m,key){if(!current(x))return;const context=ids(x),args=b.args||{};
    if(b.callback||b.blocked){const work=async()=>{if(!current(x))return null;if(b.callback&&await ctx.call('lua.key',{...context,id:b.callback,generation:m.generation})===true)return null;
      return key?encode(x,'terminal.encode_event',key.wire):null;};const lane=x.pane?.inputLane();if(lane)return lane.enqueue(work);await work();return;}
    if(m.actions.includes(b.action)){await ctx.call('lua.invoke',{...context,name:b.action,arguments:args,generation:m.generation});await ctx.refresh();return;}
    if(b.action==='pane.send_key'){const lane=x.pane?.inputLane();if(!lane)throw Error('Attach as controller before sending keys.');return lane.enqueue(()=>encode(x,'terminal.encode_keys',{keys:[args.key]}));}
    if(await localAction(ctx,x,b.action,args))return;
    if(b.action==='client.tab.new'){document.getElementById('new-window').click();return;}
    if(b.action==='pane.split'){const dir=args.direction||'right';if(!['up','down','left','right'].includes(dir))throw Error('Unknown split direction.');await ctx.call('session.launch_layout',{sessionId:x.sessionId,blockId:x.blockId,direction:['left','right'].includes(dir)?'horizontal':'vertical',side:['up','left'].includes(dir)?'before':'after',layout:{flavor:'com.superlogical.terminal.shell'},expectedRevision:ctx.session().revision});await ctx.refresh();return;}
    throw Error('This client does not implement '+String(b.action));}
  const info=(m,name)=>Object.hasOwn(m.actionDetails??{},name)?m.actionDetails[name]:{name,title:name};
  async function fromPalette(name,m,x){if(!current(x)||manifest!==m)return;const d=info(m,name),f=await ctx.dialog(d.title||name,d.description||'Run the configured server action.',[
      ...(d.inputSchema?[{name:'schema',label:'Accepted arguments',type:'textarea',readOnly:true,value:JSON.stringify(d.inputSchema,null,2)}]:[]),
      {name:'args',label:'Arguments (JSON object)',type:'textarea',value:'{}'}],'Run action');
    if(!f||!current(x)||manifest!==m)return;const args=JSON.parse(f.args);if(!args||Array.isArray(args)||typeof args!=='object')throw Error('Arguments must be a JSON object.');await run({action:name,args},x,m);}
  ctx.addPaletteSource?.(()=>{const m=manifest;if(!m||ctx.state.stopped||epoch!==ctx.state.generation||!ctx.state.capability?.administrator)return [];
    return m.actions.map(name=>{const d=info(m,name);return {title:d.title||name,hint:d.category||'Lua action',keywords:[name,d.keywords,d.description].filter(Boolean).join(' '),admin:true,run:()=>fromPalette(name,m,state())};});});
  document.addEventListener('keydown',e=>{if(!compiled||epoch!==ctx.state.generation||ctx.state.stopped||document.querySelector('dialog[open]'))return;
    if(e.target?.matches('input,select,[contenteditable=true],textarea:not(.xterm-helper-textarea)'))return;const key=captureKey(e,layout);if(!key)return;
    try{const d=compiled.engine.process(key.logical,key.physical);if(!d.consumed)return;e.preventDefault();e.stopImmediatePropagation();hint.textContent=d.pending?'Keys: '+d.pending:compiled.engine.mode==='default'?'':'Mode: '+compiled.engine.mode;
      if(d.actionId===undefined)return;const b=compiled.entries.get(d.actionId),m=manifest,x=state();
      const repeatable=b.action&&info(m,b.action).repeats===true;
      if(e.repeat&&!b.blocked&&!b.callback&&!repeatable)return;
      // At most one invocation in flight for a held, repeatable action. A distinct
      // deliberate press is never suppressed because an older action is running.
      const gate=`${x.generation}/${m.generation}/${x.blockId}/${d.actionId}`;
      if(e.repeat&&repeatable&&inFlight.has(gate))return;
      if(repeatable)inFlight.add(gate);void run(b,x,m,key).catch(err=>ctx.toast(err.message)).finally(()=>{if(repeatable)inFlight.delete(gate);});}
    catch(err){e.preventDefault();e.stopImmediatePropagation();ctx.toast(err.message);}}, {capture:true});
  return [{title:'Reload server Lua shortcuts',hint:'Local init.lua',admin:true,run:async()=>{await refresh(true);ctx.toast(manifest?'Lua shortcuts reloaded.':'No trusted Lua configuration is enabled.');}},
    {title:'Run server Lua action',hint:'Configured callbacks',admin:true,run:async()=>{await refresh();const m=manifest;if(!m?.actions.length)return ctx.toast('No configured Lua actions.');const x=state();const v=await ctx.dialog('Run server Lua action','Choose an action to inspect its arguments.',[{name:'name',label:'Action',options:m.actions.map(n=>[n,info(m,n).title||n])}],'Choose');if(v)await fromPalette(v.name,m,x);}}];
}
