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
  const app=document.getElementById('application'),hint=document.getElementById('chord-hint');
  async function readLayout(){const ticket=++layoutTicket;layout=null;try{const map=await navigator.keyboard?.getLayoutMap?.();if(ticket===layoutTicket)layout=map;}catch{/* Explicit bracket bindings need no layout permission. */}}
  void readLayout();navigator.keyboard?.addEventListener?.('layoutchange',readLayout);
  async function refresh(reload=false){const ticket=++loading,gen=ctx.state.generation;if(ctx.state.stopped||!ctx.state.capability?.administrator){compiled=manifest=null;return;}
    let m;try{m=await ctx.call(reload?'lua.reload':'lua.actions');}catch(e){if(e.code==='method_not_found'){if(ticket===loading)compiled=manifest=null;return;}throw e;}
    if(ticket!==loading||gen!==ctx.state.generation||ctx.state.stopped)return;const built=compileLua(m);manifest=m;compiled=built;epoch=gen;}
  new MutationObserver(()=>{compiled=manifest=null;epoch=-1;void refresh().catch(e=>ctx.toast(e.message));}).observe(app,{attributes:true,attributeFilter:['hidden']});
  function state(){const s=ctx.state,v=s.remoteView;return {sessionId:s.sessionId,windowId:s.windowId,blockId:s.focused,clientId:v?.session===s.sessionId&&v.generation===s.generation?v.id:null,generation:s.generation,pane:s.panes.get(s.focused),authority:s.panes.get(s.focused)?.connection};}
  function current(x){return !ctx.state.stopped&&ctx.state.generation===x.generation&&ctx.state.sessionId===x.sessionId&&ctx.state.windowId===x.windowId&&ctx.state.focused===x.blockId;}
  const ids=x=>Object.fromEntries(['sessionId','windowId','blockId','clientId'].filter(k=>x[k]).map(k=>[k,x[k]]));
  async function send(x,method,args){const valid=()=>current(x)&&x.pane?.ready&&x.pane.hasControl&&x.pane.connection===x.authority&&!document.querySelector('dialog[open]');
    if(!valid())throw Error('The original terminal no longer has input control.');const {value}=await ctx.call(method,{sessionId:x.sessionId,blockId:x.blockId,...args});
    if(!valid())throw Error('Control changed; input was not sent.');if(value)x.pane.send({type:'input',data:value});}
  async function run(b,x,generation,actions,key){if(!current(x))return;const context=ids(x),args=b.args||{};
    if(b.callback||b.blocked){if(b.callback && await ctx.call('lua.key',{...context,id:b.callback,generation})===true)return;
      if(key)await send(x,'terminal.encode_event',key.wire);return;}
    if(actions.includes(b.action)){await ctx.call('lua.invoke',{...context,name:b.action,arguments:args,generation});await ctx.refresh();return;}
    if(b.action==='pane.send_key')return send(x,'terminal.encode_keys',{keys:[args.key]});if(await localAction(ctx,x,b.action,args))return;
    if(b.action==='client.tab.new'){document.getElementById('new-window').click();return;}
    if(b.action==='pane.split'){const dir=args.direction||'right';if(!['up','down','left','right'].includes(dir))throw Error('Unknown split direction.');await ctx.call('session.launch_layout',{sessionId:x.sessionId,blockId:x.blockId,direction:['left','right'].includes(dir)?'horizontal':'vertical',side:['up','left'].includes(dir)?'before':'after',layout:{flavor:'com.superlogical.terminal.shell'},expectedRevision:ctx.session().revision});await ctx.refresh();return;}
    throw Error('This client does not implement '+String(b.action));}
  document.addEventListener('keydown',e=>{if(!compiled||epoch!==ctx.state.generation||ctx.state.stopped||document.querySelector('dialog[open]'))return;
    if(e.target?.matches('input,select,[contenteditable=true],textarea:not(.xterm-helper-textarea)'))return;const key=captureKey(e,layout);if(!key)return;
    try{const d=compiled.engine.process(key.logical,key.physical);if(!d.consumed)return;e.preventDefault();e.stopImmediatePropagation();hint.textContent=d.pending?'Keys: '+d.pending:compiled.engine.mode==='default'?'':'Mode: '+compiled.engine.mode;
      if(d.actionId!==undefined){const b=compiled.entries.get(d.actionId);if(!e.repeat||b.blocked)void run(b,state(),manifest.generation,manifest.actions,key).catch(e=>ctx.toast(e.message));}}
    catch(err){e.preventDefault();e.stopImmediatePropagation();ctx.toast(err.message);}}, {capture:true});
  return [{title:'Reload server Lua shortcuts',hint:'Local init.lua',admin:true,run:async()=>{await refresh(true);ctx.toast(manifest?'Lua shortcuts reloaded.':'No trusted Lua configuration is enabled.');}},
    {title:'Run server Lua action',hint:'Configured callbacks',admin:true,run:async()=>{await refresh();if(!manifest?.actions.length)return ctx.toast('No configured Lua actions.');const x=state(),gen=manifest.generation;const v=await ctx.dialog('Run Lua action','Arguments are JSON. Nothing runs until you confirm.',[{name:'name',label:'Action',options:manifest.actions.map(n=>[n,n])},{name:'args',label:'Arguments',type:'textarea',value:'{}'}],'Run');if(v&&current(x))await run({action:v.name,args:JSON.parse(v.args)},x,gen,manifest.actions);}}];
}
