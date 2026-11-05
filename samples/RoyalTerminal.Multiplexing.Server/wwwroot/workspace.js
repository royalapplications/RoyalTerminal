import {deferPlacement,placementActions,windowPlanes} from './workspace-placements.js';
import {floatingScenes,sharedTarget,drawScene} from './frame-scene.js';
import {TerminalPane} from './workspace-terminal.js';
import {leaves,formatState} from './workspace-model.js';
import {Keymap,keyStroke} from './keymap.js';
import {createAttentionFeed,createNavigation} from './shell-navigation.js';
import {createPalette} from './shell-palette.js';
import {createDialog} from './shell-dialog.js';
import {createCommands} from './shell-commands.js';

const $=id=>document.getElementById(id);
const state={token:'',capability:null,sessions:[],sessionId:null,windowId:null,focused:null,zoom:false,sharedZoom:null,panes:new Map(),layoutKey:null,stopped:true,generation:0};
const session=()=>state.sessions.find(s=>s.id===state.sessionId),win=()=>session()?.windows.find(w=>w.id===state.windowId),focused=()=>state.panes.get(state.focused);
const text=(tag,value,cls='')=>Object.assign(document.createElement(tag),{textContent:value,className:cls});
const status=s=>{$('status-text').textContent=s;};
let toastTimer,issued=0,applied=0,polling=0,chord=false,chordTimer;
function toast(s){$('toast').textContent=String(s);$('toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>{$('toast').hidden=true;},4000);}
async function run(fn){try{await fn();}catch(e){if(e.name!=='AbortError'){toast(e.message);status(e.message);}}}
function button(label,title,action,cls=''){const n=text('button',label,cls);n.type='button';n.title=title;n.setAttribute('aria-label',title||label);n.addEventListener('click',e=>{e.stopPropagation();void run(action);});return n;}
async function request(path,method='GET',body){
  const generation=state.generation;
  const r=await fetch(path,{method,credentials:'omit',redirect:'error',cache:'no-store',headers:{Authorization:`Bearer ${state.token}`,...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});
  const stale=()=>{if(generation!==state.generation)throw new DOMException('Connection changed','AbortError');};stale();
  if(r.status===401&&!state.stopped){disconnect('Access expired or was revoked. Connect with a valid token.');throw new DOMException('Access expired','AbortError');}
  const result=await r.json();stale();if(!r.ok)throw Object.assign(new Error(result.message||`HTTP ${r.status}`),{code:result.code});return result;
}
const call=(method,args={})=>request('/api/call','POST',{method,arguments:args});
const dialog=createDialog($('dialog'),$('dialog-form'),$('dialog-content'),$('dialog-cancel'),$('dialog-submit'));
const sources=[],ctx={state,session,window:win,request,call,dialog,refresh,toast,addPaletteSource:s=>sources.push(s),paletteChanged:()=>{if($('palette').open)palette.render();}};
const cmd=createCommands(ctx),nav=createNavigation({sessions:$('session-list'),tabs:$('window-tabs'),panel:$('workspace-content'),selectSession,selectWindow,formatState});
const attention=createAttentionFeed(id=>call('session.attention',{sessionId:id}),()=>state.sessionId,paintNavigation);
function paintNavigation(){nav.render(state.sessions,session(),win(),attention.summaries,id=>void run(()=>cmd.renameWindow(id)));}
async function refresh(){
  if(state.stopped)return;const ticket=++issued;polling++;
  try{const {sessions}=await request('/api/sessions');if(ticket<applied||state.stopped)return;applied=ticket;state.sessions=sessions;
    if(!session())state.sessionId=sessions[0]?.id??null;attention.trim(new Set(sessions.map(s=>s.id)));render();if(!document.hidden)void attention.sample(session());
  }finally{polling--;}
}
function focusBlock(id){state.focused=id;for(const [key,p] of state.panes)p.element.classList.toggle('focused',key===id);}
function clearPanes(){for(const p of state.panes.values())p.dispose();state.panes.clear();state.layoutKey=null;}
function paneFor(id){let p=state.panes.get(id);if(!p){const block=session().blocks.find(b=>b.id===id);if(!block)throw Error('Layout refers to a missing terminal.');
  p=new TerminalPane(block,{state,text,button,toast,status,focusBlock,captureBlock:cmd.captureBlock,closeBlock:cmd.closeBlock,renameBlock:cmd.renameBlock});state.panes.set(id,p);
  const h=p.element.querySelector('.pane-header');if(!state.capability?.administrator){h.querySelector('[aria-label="Close process"]')?.remove();p.name.title=block.label;}
  else{const split=button('⊞','Split this terminal to the right',()=>cmd.newShell('horizontal',id),'pane-action');h.insertBefore(split,h.querySelector('[aria-label="Capture terminal"]'));}
  }p.lastVisible=performance.now();return p.element;}
function render(){
  const s=session(),admin=!!state.capability?.administrator;
  if(!win())state.windowId=s?.windows[0]?.id??null;const w=win(),planes=windowPlanes(s,w);
  paintNavigation();$('workspace-title').textContent=s?.label??'Workspaces';$('workspace-meta').textContent=s?`${s.blocks.length} ${s.blocks.length===1?'terminal':'terminals'}`:'';
  for(const id of ['new-session','empty-create'])$(id).disabled=!admin;
  for(const id of ['new-window','new-shell','share-session','workspace-menu'])$(id).disabled=!s||!admin;
  const visible=[...leaves(w?.layout),...planes.floats.map(b=>b.id)];if(!visible.includes(state.focused))focusBlock(visible[0]??null);
  if(!state.focused)state.zoom=false;const shared=sharedTarget(w,visible);if(shared!==state.sharedZoom)state.zoom=false;state.sharedZoom=shared;if(shared)focusBlock(shared);
  for(const id of ['split-horizontal','split-vertical'])$(id).disabled=!admin||!state.focused;
  $('zoom-block').disabled=!state.focused||!!shared;$('zoom-block').setAttribute('aria-pressed',String(!!(state.zoom||shared)));
  const scene={zoom:shared??(state.zoom?state.focused:null),frames:floatingScenes(w,planes.floats)};
  $('empty-state').hidden=visible.length>0;$('empty-create').hidden=!admin;$('empty-create').textContent=s?'Open a shell':'Create workspace';
  $('empty-title').textContent=s?'No terminals in this window':'No workspaces yet';$('empty-detail').textContent=admin?(s?'Open a shell here, or restore a shelved terminal.':'Create a workspace to start your first shell.'):'Waiting for the host to add a terminal.';
  const key=JSON.stringify([s?.id,w?.id,w?.layout,scene.zoom,scene.frames.map(f=>[f.id,f.bounds,f.tree]),planes.shelf.map(b=>[b.id,b.label]),admin]);
  const existing=new Set(state.sessions.flatMap(x=>x.blocks.map(b=>b.id)));for(const [id,p] of state.panes)if(!existing.has(id)){p.dispose();state.panes.delete(id);}
  if(key!==state.layoutKey){state.layoutKey=key;const root=$('pane-layout');root.replaceChildren();
    const old=state.remoteNavigation;try{if(shared)state.remoteNavigation=true;drawScene(root,s,w,scene,paneFor,ctx,run);}finally{state.remoteNavigation=old;}
    const shelf=$('unplaced-blocks');shelf.replaceChildren();shelf.hidden=!planes.shelf.length;
    if(planes.shelf.length){shelf.append(text('span','Shelf'));for(const b of planes.shelf){const n=button(b.label,`Restore ${b.label}`,()=>deferPlacement(ctx,s,w,b)({kind:'tiled'}));n.disabled=!admin;shelf.append(n);}}
    const cold=[...state.panes.values()].filter(p=>!visible.includes(p.id)).sort((a,b)=>a.lastVisible-b.lastVisible);while(state.panes.size>32&&cold.length){const p=cold.shift();p.dispose();state.panes.delete(p.id);}
    requestAnimationFrame(()=>{for(const p of state.panes.values())if(p.element.isConnected){p.mount();p.scheduleFit();}focusBlock(state.focused);});}
  for(const b of s?.blocks??[])state.panes.get(b.id)?.update(b);
}
function selectSession(id){if(id!==state.sessionId){state.sessionId=id;state.windowId=null;state.zoom=false;render();void attention.sample(session());}setDrawer(false);}
function selectWindow(id){state.windowId=id;state.zoom=false;render();}
function setDrawer(open){$('sidebar').classList.toggle('visible',open);$('drawer-shade').hidden=!open;$('toggle-sidebar').setAttribute('aria-expanded',String(open));}
function toggleZoom(){if(state.sharedZoom)return toast('Restore shared zoom from the command palette.');if(state.focused){state.zoom=!state.zoom;render();}}
let config={actions:[],bindings:[],modes:[]},keys=new Keymap(config);
async function reloadConfig(){if(!state.capability?.administrator)return;config=(await request('/api/config')).configuration;keys=new Keymap(config);}
async function editConfig(){await reloadConfig();const f=await dialog('Workspace automation','Use ${sessionId}, ${blockId} and ${windowId} for context.',[{name:'json',label:'Configuration',type:'textarea',value:JSON.stringify(config,null,2)}],'Validate and save');if(f){await request('/api/config','PUT',JSON.parse(f.json));await reloadConfig();}}
async function invoke(id){const r=await request(`/api/actions/${encodeURIComponent(id)}/invoke`,'POST',{sessionId:state.sessionId,blockId:state.focused||null,windowId:state.windowId||null});await refresh();const end=r.steps.at(-1)?.result;if(end?.content!==undefined)await dialog(id,'Action result',[{name:'result',label:'Output',type:'textarea',value:end.content,readOnly:true}],'Done');else toast(`Action ${id} completed (${r.steps.length} steps).`);}
function disconnect(message=''){
  state.generation++;state.stopped=true;attention.reset();clearPanes();setDrawer(false);chord=false;clearTimeout(chordTimer);clearTimeout(toastTimer);
  Object.assign(state,{token:'',capability:null,sessions:[],sessionId:null,windowId:null,focused:null,zoom:false,sharedZoom:null});
  config={actions:[],bindings:[],modes:[]};keys=new Keymap(config);for(const d of document.querySelectorAll('dialog[open]'))d.close();
  for(const id of ['dialog-content','session-list','window-tabs','pane-layout','unplaced-blocks','palette-results','chord-hint','toast','workspace-title','workspace-meta'])$(id).replaceChildren();
  $('application').hidden=true;$('connect-screen').hidden=false;$('toast').hidden=true;$('access-token').value='';$('palette-query').value='';$('connect-error').textContent=message;status('Detached');$('access-token').focus();
}
const action=(title,fn,flags={})=>({title,run:fn,...flags});
const actions=[action('New workspace',cmd.createSession,{admin:true}),action('Open a new shell',()=>cmd.newShell(),{session:true,admin:true}),action('New window',cmd.newWindow,{hint:'Ctrl B, C',admin:true,session:true}),
  action('Zoom focused terminal',toggleZoom,{block:true}),action('Zoom focused terminal for all viewers',()=>cmd.setShared(true),{block:true,admin:true}),action('Restore shared window layout',()=>cmd.setShared(false),{block:true,admin:true}),
  action('Split side by side',()=>cmd.newShell('horizontal'),{block:true,admin:true}),action('Split above and below',()=>cmd.newShell('vertical'),{block:true,admin:true}),action('Detach / reconnect focused terminal',()=>focused()?.toggleAttachment(),{block:true}),action('Take control of focused terminal',cmd.takeControl,{block:true,admin:true}),action('Capture terminal output',()=>cmd.captureBlock(),{block:true}),action('Inspect program status',cmd.showStatus,{block:true}),action('Rename focused terminal',()=>cmd.renameBlock(state.focused),{block:true,admin:true}),action('Workspace settings',cmd.workspaceMenu,{session:true,admin:true}),action('Share workspace',cmd.shareSession,{session:true,admin:true}),action('Revoke workspace share',cmd.manageShares,{session:true,admin:true}),action('Explore server API',cmd.showApi),action('Edit actions and key modes',editConfig,{admin:true}),action('Reload actions and key modes',reloadConfig,{admin:true}),action('Disconnect all views',()=>disconnect()),...placementActions(ctx)];
const palette=createPalette($('palette'),$('palette-query'),$('palette-results'),()=>[...actions,...sources.flatMap(s=>s()),...config.actions.map(a=>action(a.id.replaceAll('_',' '),()=>invoke(a.id),{hint:'Custom action',admin:true}))].filter(a=>(!a.admin||state.capability?.administrator)&&(!a.session||state.sessionId)&&(!a.block||state.focused)),run);
function openPalette(){if(!state.stopped){setDrawer(false);palette.open();}}
document.addEventListener('keydown',e=>{
  if(state.stopped||e.isComposing||document.querySelector('dialog[open]'))return;
  if(e.key==='Escape'&&$('sidebar').classList.contains('visible')){setDrawer(false);$('toggle-sidebar').focus();return;}
  const consume=()=>{e.preventDefault();e.stopImmediatePropagation();};const stroke=keyStroke(e);
  if(stroke){const d=keys.process(stroke);if(d.consumed){consume();$('chord-hint').textContent=d.pending?`Keys: ${d.pending}`:keys.mode==='default'?'':`Mode: ${keys.mode}`;if(d.actionId)void run(()=>invoke(d.actionId));return;}}
  if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='k'){consume();openPalette();return;}
  if(!chord&&e.ctrlKey&&!e.shiftKey&&e.key.toLowerCase()==='b'){consume();chord=true;$('chord-hint').textContent='Ctrl B: % split right · " split down · C window · D detach';clearTimeout(chordTimer);chordTimer=setTimeout(()=>{chord=false;$('chord-hint').textContent='';},2000);return;}
  if(chord){consume();chord=false;clearTimeout(chordTimer);$('chord-hint').textContent='';const k=e.key.toLowerCase();if(k==='d')void run(()=>focused()?.toggleAttachment());else if(state.capability?.administrator){if(k==='c')void run(cmd.newWindow);if(k==='%'||k==='"')void run(()=>cmd.newShell(k==='%'?'horizontal':'vertical'));}}
},{capture:true});
for(const [id,fn] of Object.entries({'new-session':cmd.createSession,'empty-create':()=>session()?cmd.newShell():cmd.createSession(),'new-shell':()=>cmd.newShell(),'new-window':cmd.newWindow,'split-horizontal':()=>cmd.newShell('horizontal'),'split-vertical':()=>cmd.newShell('vertical'),'zoom-block':toggleZoom,'share-session':cmd.shareSession,'workspace-menu':cmd.workspaceMenu,'disconnect':()=>disconnect(),'palette-button':openPalette,'help-button':openPalette,'toggle-sidebar':()=>setDrawer(!$('sidebar').classList.contains('visible')),'drawer-shade':()=>{setDrawer(false);$('toggle-sidebar').focus();}}))$(id).onclick=()=>void run(fn);
for(const id of ['connect-host','server-host'])$(id).textContent=location.host;
const modifier=/Mac|iPad|iPhone/.test(navigator.platform)?'⌘':'Ctrl';$('command-hint').textContent=modifier+' K';
$('connect-form').onsubmit=async e=>{e.preventDefault();const generation=++state.generation;$('connect-error').textContent='';state.token=$('access-token').value.trim();try{if(!window.Terminal||!window.FitAddon)throw Error('Build the local xterm assets and rebuild the server.');state.capability=await request('/api/context');await reloadConfig();state.stopped=false;await refresh();if(generation!==state.generation)return;$('access-token').value='';$('connect-screen').hidden=true;$('application').hidden=false;for(const p of state.panes.values())p.scheduleFit();status('Connected');$('role-label').textContent=state.capability.administrator?'Administrator':state.capability.canControl?'Controller access':'Read only';}catch(err){if(generation===state.generation)disconnect(err.message);}};
const fragment=new URLSearchParams(location.hash.slice(1));if(fragment.has('token')){$('access-token').value=fragment.get('token');state.sessionId=fragment.get('session');history.replaceState(null,'',location.pathname+location.search);$('connect-form').requestSubmit();}
setInterval(()=>{if(!state.stopped&&!polling)void refresh().catch(e=>{if(e.name!=='AbortError')status(e.message);});},2500);
window.addEventListener('beforeunload',()=>{state.stopped=true;clearPanes();});
