import { base64Bytes, encodeInput, nextSequence, leaves, replaceRatio, formatState, safeFilename } from './workspace-model.js';

const $ = id => document.getElementById(id);
const state = { token:'', capability:null, sessions:[], sessionId:null, windowId:null, focused:null, zoom:false, panes:new Map(), layoutKey:'', polling:false, stopped:true };
const text = (tag, content, className='') => { const node=document.createElement(tag); node.textContent=content; if(className)node.className=className; return node; };
const button = (label, title, action, className='') => { const node=text('button',label,className); node.type='button'; node.title=title; node.setAttribute('aria-label',title); node.addEventListener('click',event=>{event.stopPropagation();void run(action);}); return node; };
const currentSession = () => state.sessions.find(s=>s.id===state.sessionId);
const currentWindow = () => currentSession()?.windows.find(w=>w.id===state.windowId);
const focusedPane = () => state.panes.get(state.focused);
let toastTimer;
function toast(message) { $('toast').textContent=String(message);$('toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('toast').hidden=true,5000); }
function status(message) { $('status-text').textContent=String(message); }
async function run(action) { try { await action(); } catch(error) { toast(error.message);status(error.message); } }
async function request(path, method='GET', body) {
  const response=await fetch(path,{method,headers:{Authorization:`Bearer ${state.token}`,...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body),cache:'no-store'});
  const data=await response.json();
  if(!response.ok) { const error=new Error(data.message||`HTTP ${response.status}`); error.code=data.code; throw error; }
  return data;
}
const call=(method,args={})=>request('/api/call','POST',{method,arguments:args});

function showDialog(title, description, fields=[], submit='Continue') {
  const dialog=$('dialog'), content=$('dialog-content');content.replaceChildren(text('h3',title));
  if(description)content.append(text('p',description));
  const controls=new Map();
  for(const field of fields){
    const id=`field-${field.name}`;const label=text('label',field.label);label.htmlFor=id;content.append(label);
    const input=document.createElement(field.type==='textarea'?'textarea':field.options?'select':'input');input.id=id;input.name=field.name;
    if(field.options)for(const [value,label] of field.options){const option=text('option',label);option.value=value;input.append(option);}
    else if(field.type!=='textarea')input.type=field.type||'text';
    input.value=field.value??'';input.placeholder=field.placeholder||'';input.autocomplete='off';input.spellcheck=false;
    input.required=!!field.required;if(field.readOnly)input.readOnly=true;content.append(input);controls.set(field.name,input);
  }
  $('dialog-submit').textContent=submit;dialog.showModal();
  queueMicrotask(()=>controls.values().next().value?.focus());
  return new Promise(resolve=>{
    let result=null;
    const submitHandler=event=>{event.preventDefault();result=Object.fromEntries([...controls].map(([key,value])=>[key,value.value]));dialog.close();};
    const closeHandler=()=>{$('dialog-form').removeEventListener('submit',submitHandler);resolve(result);};
    $('dialog-form').addEventListener('submit',submitHandler);dialog.addEventListener('close',closeHandler,{once:true});
  });
}
$('dialog-cancel').addEventListener('click',()=>$('dialog').close());

class TerminalPane {
  constructor(block) {
    this.id=block.id;this.sessionId=block.sessionId;this.block=block;this.sequence=0;this.ready=false;this.hasControl=false;this.closed=false;this.disposed=false;this.pending=Promise.resolve();this.socket=null;this.retries=0;this.userGesture=false;this.applyingResize=false;
    this.element=document.createElement('section');this.element.className='terminal-pane';this.element.dataset.blockId=this.id;
    this.header=text('div','','pane-header');this.name=text('span',block.label,'pane-title');this.name.title='Double-click to rename';this.indicator=text('span','connecting','pane-state');
    this.header.append(text('span','›'),this.name,this.indicator,
      button('↗','Capture terminal',()=>captureBlock(this.id)),
      button('◇','Detach or reconnect this view',()=>this.toggleAttachment()),
      button('×','Close process',()=>closeBlock(this.id)));
    this.host=text('div','','terminal-host');this.footer=text('div','','pane-footer');this.geometry=text('span','');this.access=text('span','');this.footer.append(this.geometry,this.access);
    this.element.append(this.header,this.host,this.footer);
    this.element.addEventListener('pointerdown',()=>focusBlock(this.id));
    this.element.addEventListener('focusin',()=>focusBlock(this.id));
    this.name.addEventListener('dblclick',()=>void run(()=>renameBlock(this.id)));
    this.terminal=new window.Terminal({fontFamily:'"SFMono-Regular",Consolas,"Liberation Mono",monospace',fontSize:12,lineHeight:1.18,cursorBlink:true,scrollback:15000,allowProposedApi:false,convertEol:false,theme:{background:'#0e1217',foreground:'#c6d2dd',cursor:'#8be6c9',selectionBackground:'#3c625b80',black:'#15202a',red:'#df8d8d',green:'#8fc9a4',yellow:'#d9c18a',blue:'#8bb4da',magenta:'#b69ed4',cyan:'#86c9c7',white:'#ccd8df',brightBlack:'#667b8e',brightRed:'#f2aaaa',brightGreen:'#b2e8c4',brightYellow:'#f0d8a2',brightBlue:'#b5d7f3',brightMagenta:'#d3b8ef',brightCyan:'#b4e5df',brightWhite:'#eff7fc'}});
    this.fit=new window.FitAddon.FitAddon();this.terminal.loadAddon(this.fit);
    // Only user-originated DOM input may reach the process. Parser-generated DSR/DA
    // replies have no gesture and are suppressed; the server's VT is authoritative.
    for(const event of ['keydown','keypress','beforeinput','input','paste','compositionend','mousedown','mouseup','mousemove','wheel','touchstart','touchend'])
      this.host.addEventListener(event,()=>{this.userGesture=true;queueMicrotask(()=>this.userGesture=false);},{capture:true,passive:true});
    this.terminal.onData(data=>{if(this.userGesture)this.sendInput(data);});
    this.terminal.onBinary(data=>{if(this.userGesture&&this.ready&&this.hasControl)this.send({type:'input',data:btoa(data)});});
    this.terminal.onResize(size=>{if(this.ready&&this.hasControl&&!this.applyingResize)this.send({type:'resize',columns:size.cols,rows:size.rows,widthPixels:0,heightPixels:0});});
    const keys=text('div','','touch-keys');for(const [label,value] of [['Esc','\u001b'],['Tab','\t'],['Ctrl C','\u0003'],['↑','\u001b[A'],['↓','\u001b[B'],['←','\u001b[D'],['→','\u001b[C']])keys.append(button(label,`Send ${label}`,()=>this.sendInput(value)));this.element.append(keys);
    this.resizeObserver=new ResizeObserver(()=>this.scheduleFit());
  }
  mount() {
    if(this.opened)return;this.opened=true;this.terminal.open(this.host);this.resizeObserver.observe(this.host);
    void this.connect();
  }
  async connect(observer=false) {
    if(this.disposed)return;
    if(this.socket&&this.socket.readyState<2)return;
    this.closed=false;this.ready=false;this.hasControl=false;
    const role=observer||!state.capability.canControl?'observer':'controller';
    this.desiredRole=role;this.indicator.textContent='connecting';
    const address=new URL(`/api/sessions/${this.sessionId}/blocks/${this.id}/stream`,location.href);address.protocol=location.protocol==='https:'?'wss:':'ws:';address.searchParams.set('after',this.sequence);address.searchParams.set('role',role);
    const socket=new WebSocket(address);this.socket=socket;
    socket.addEventListener('open',()=>socket.send(JSON.stringify({type:'authenticate',token:state.token})));
    socket.addEventListener('message',event=>{
      if(event.data.length>1024*1024){this.fail('Server message exceeds the client budget.');return;}
      this.pending=this.pending.then(()=>this.applyFrame(JSON.parse(event.data))).catch(error=>this.fail(error.message));
    });
    socket.addEventListener('close',()=>{
      this.pending=this.pending.then(()=>{
        if(this.socket!==socket)return;
        this.ready=false;this.hasControl=false;
        if(this.disposed||this.closed)return;
        this.indicator.textContent='reconnecting';this.access.textContent='Connection lost · process retained';
        const delay=Math.min(1000*2**Math.min(this.retries++,4),15000);
        this.reconnectTimer=setTimeout(()=>void this.connect(this.desiredRole==='observer'),delay);
      });
    });
  }
  async applyFrame(frame) {
    if(this.disposed)return;
    if(frame.type==='attached'){this.hasControl=frame.hasControl;this.attachmentId=frame.attachmentId;return;}
    if(frame.type==='ready'){
      if(frame.sequence!==this.sequence)throw new Error('Server replay fence does not match applied history.');
      this.ready=true;this.retries=0;this.indicator.textContent=this.hasControl?'live':'view only';this.access.textContent=this.hasControl?'Input + resize control':'Shared read-only view';this.scheduleFit();status('All terminal history applied');return;
    }
    if(frame.type==='error'){
      if(frame.code==='Conflict'&&this.desiredRole==='controller'){
        this.closed=true;this.socket?.close();this.access.textContent='Another client has control';setTimeout(()=>void this.connect(true),100);return;
      }
      this.fail(`${frame.code}: ${frame.message}`);return;
    }
    if(!nextSequence(this.sequence,frame.sequence))return;
    if(frame.kind==='resize'){
      const d=frame.dimensions;if(!d||d.columns<1||d.rows<1||d.columns>512||d.rows>512)throw new Error('Invalid shared geometry.');
      this.applyingResize=true;this.terminal.resize(d.columns,d.rows);this.applyingResize=false;this.geometry.textContent=`${d.columns} × ${d.rows}`;
    }else if(frame.kind==='output'){
      const bytes=base64Bytes(frame.data||'');
      await new Promise(resolve=>this.terminal.write(bytes,resolve));
    }else if(frame.kind==='exit'){
      this.hasControl=false;this.indicator.textContent=`exit ${frame.exitCode}`;this.access.textContent='Process exited · output retained';
    }else if(frame.kind==='closed'){
      this.closed=true;this.ready=false;this.hasControl=false;this.indicator.textContent='closed';this.access.textContent='Process closed';
    }else throw new Error(`Unknown stream event ${frame.kind}`);
    this.sequence=frame.sequence;
  }
  fail(message){this.closed=true;this.ready=false;this.hasControl=false;this.socket?.close();this.indicator.textContent='detached';this.access.textContent=message;this.access.title=message;toast(message);}
  send(frame){if(this.socket?.readyState===WebSocket.OPEN)this.socket.send(JSON.stringify(frame));}
  sendInput(data){if(!this.ready||!this.hasControl)return;const encoded=encodeInput(data);if(encoded.length>88000){toast('Paste exceeds 64 KiB.');return;}this.send({type:'input',data:encoded});}
  scheduleFit(){clearTimeout(this.fitTimer);this.fitTimer=setTimeout(()=>{if(!this.ready||!this.hasControl||!this.element.isConnected)return;const size=this.fit.proposeDimensions();if(size&&size.cols>=2&&size.rows>=1){const cols=Math.min(512,size.cols),rows=Math.min(256,size.rows);if(cols!==this.terminal.cols||rows!==this.terminal.rows)this.terminal.resize(cols,rows);}},100);}
  update(block){this.block=block;this.name.textContent=block.label;if(formatState(block)==='exited'){this.indicator.textContent=`exit ${block.exitCode}`;this.hasControl=false;}}
  async toggleAttachment(){if(this.closed){await this.connect();return;}this.closed=true;this.ready=false;this.hasControl=false;clearTimeout(this.reconnectTimer);this.socket?.close();this.indicator.textContent='detached';this.access.textContent='Process still running · ◇ reconnects';}
  dispose(){this.disposed=true;this.closed=true;clearTimeout(this.reconnectTimer);clearTimeout(this.fitTimer);this.socket?.close();this.resizeObserver.disconnect();this.terminal.dispose();this.element.remove();}
}

function focusBlock(id){state.focused=id;for(const [key,pane] of state.panes)pane.element.classList.toggle('focused',key===id);}
function clearPanes(){for(const pane of state.panes.values())pane.dispose();state.panes.clear();state.layoutKey='';}
async function refresh(){
  if(state.polling||state.stopped)return;state.polling=true;
  try{state.sessions=(await request('/api/sessions')).sessions;if(!state.sessions.some(s=>s.id===state.sessionId))state.sessionId=state.sessions[0]?.id??null;render();}
  finally{state.polling=false;}
}
function render(){
  const list=$('session-list');list.replaceChildren();
  for(const session of state.sessions){
    const item=button('','Open workspace',()=>selectSession(session.id),'session-item');item.classList.toggle('active',session.id===state.sessionId);
    const info=text('span','','session-info');info.append(text('span',session.label,'session-label'),text('span',`${session.blocks.filter(b=>formatState(b)==='running').length} running · ${session.windows.length} windows`,'session-detail'));
    item.append(text('span','▱','session-symbol'),info,text('span',String(session.blocks.length),'session-count'));list.append(item);
  }
  const session=currentSession();const admin=state.capability?.administrator;
  for(const id of ['new-session','empty-create'])$(id).disabled=!admin;
  for(const id of ['new-window','new-shell','split-horizontal','split-vertical','share-session','workspace-menu'])$(id).disabled=!session||!admin;
  $('workspace-title').textContent=session?.label??'Your workspaces';$('workspace-meta').textContent=session?`${session.blocks.length} terminals · revision ${session.revision}`:'';
  $('empty-state').hidden=!!session?.blocks.length;$('empty-create').textContent=session?'Open a shell':'Create workspace';
  if(session&&!session.windows.some(w=>w.id===state.windowId))state.windowId=session.windows[0]?.id;
  const tabs=$('window-tabs');tabs.replaceChildren();
  for(const window of session?.windows??[]){const tab=button(window.label,'Open window',()=>selectWindow(window.id),'window-tab');tab.setAttribute('role','tab');tab.setAttribute('aria-selected',String(window.id===state.windowId));tab.addEventListener('dblclick',()=>void run(()=>renameWindow(window.id)));tabs.append(tab);}
  const window=currentWindow();let layout=window?.layout;
  if(state.zoom&&state.focused&&leaves(layout).includes(state.focused))layout={blockId:state.focused};
  const key=`${session?.id}/${window?.id}/${JSON.stringify(layout)}`;
  if(key!==state.layoutKey){
    state.layoutKey=key;const keep=new Set(leaves(layout));for(const [id,pane] of state.panes)if(!keep.has(id)){pane.dispose();state.panes.delete(id);}
    const root=$('pane-layout');root.replaceChildren();if(layout)root.append(renderNode(layout,[]));
    requestAnimationFrame(()=>{for(const pane of state.panes.values()){pane.mount();pane.scheduleFit();}if(!state.panes.has(state.focused))focusBlock(state.panes.keys().next().value);});
  }
  for(const block of session?.blocks??[])state.panes.get(block.id)?.update(block);
  if(session&&session.blocks.length&&!layout){$('empty-state').hidden=false;$('empty-create').textContent='Open a shell';}
}
function renderNode(layout,path){
  if(layout.blockId){let pane=state.panes.get(layout.blockId);if(!pane){const block=currentSession().blocks.find(b=>b.id===layout.blockId);pane=new TerminalPane(block);state.panes.set(block.id,pane);}return pane.element;}
  const split=text('div','','split');const vertical=String(layout.orientation).toLowerCase()==='vertical';split.classList.toggle('vertical',vertical);
  const first=text('div','','split-child'),second=text('div','','split-child'),divider=text('div','','splitter');divider.setAttribute('role','separator');divider.setAttribute('aria-label','Resize terminal split');divider.setAttribute('aria-orientation',vertical?'horizontal':'vertical');divider.tabIndex=0;
  function ratio(value){first.style.flex=`${value} 1 0`;second.style.flex=`${1-value} 1 0`;divider.setAttribute('aria-valuenow',String(Math.round(value*100)));}
  ratio(layout.ratio);first.append(renderNode(layout.first,[...path,'first']));second.append(renderNode(layout.second,[...path,'second']));split.append(first,divider,second);
  const persist=async value=>{const session=currentSession(),window=currentWindow();await call('session.set_layout',{sessionId:session.id,windowId:window.id,layout:replaceRatio(window.layout,path,value),expectedRevision:session.revision});await refresh();};
  divider.addEventListener('pointerdown',event=>{if(!state.capability.administrator||state.zoom)return;event.preventDefault();divider.setPointerCapture(event.pointerId);divider.classList.add('dragging');let value=layout.ratio;
    const move=e=>{const bounds=split.getBoundingClientRect();value=Math.max(.1,Math.min(.9,vertical?(e.clientY-bounds.top)/bounds.height:(e.clientX-bounds.left)/bounds.width));ratio(value);};
    const end=()=>{divider.removeEventListener('pointermove',move);divider.classList.remove('dragging');void run(()=>persist(value));};divider.addEventListener('pointermove',move);divider.addEventListener('pointerup',end,{once:true});divider.addEventListener('pointercancel',end,{once:true});});
  divider.addEventListener('keydown',event=>{if(!state.capability.administrator)return;if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key)){event.preventDefault();void run(()=>persist(Math.max(.1,Math.min(.9,layout.ratio+(['ArrowRight','ArrowDown'].includes(event.key)?.05:-.05)))));}});
  return split;
}
async function selectSession(id){if(state.sessionId===id)return;clearPanes();state.sessionId=id;state.windowId=null;state.zoom=false;render();$('sidebar').classList.remove('visible');}
async function selectWindow(id){if(state.windowId===id)return;clearPanes();state.windowId=id;state.zoom=false;render();}
async function createSession(){const fields=await showDialog('New workspace','Group related terminals into one durable session.',[{name:'label',label:'Workspace name',value:'My workspace',required:true}],'Create workspace');if(!fields)return;const session=await request('/api/sessions','POST',{label:fields.label});clearPanes();state.sessionId=session.id;state.windowId=null;await refresh();await newShell();}
async function newShell(orientation){const session=currentSession();if(!session)return createSession();const requestBody={label:'Shell',transport:'pty',windowId:state.windowId};if(orientation&&state.focused){requestBody.splitBlockId=state.focused;requestBody.orientation=orientation;requestBody.expectedRevision=session.revision;delete requestBody.windowId;}const block=await request(`/api/sessions/${session.id}/blocks`,'POST',requestBody);state.focused=block.id;state.zoom=false;await refresh();requestAnimationFrame(()=>focusedPane()?.terminal.focus());}
async function newWindow(){const fields=await showDialog('New window','A window holds an independent layout of terminals.',[{name:'label',label:'Window name',value:'Window',required:true}],'Create window');if(!fields)return;const window=await call('session.new_window',{sessionId:state.sessionId,label:fields.label,expectedRevision:currentSession().revision});state.windowId=window.id;await refresh();await newShell();}
async function renameBlock(id){if(!state.capability.administrator)return;const block=currentSession().blocks.find(b=>b.id===id);const fields=await showDialog('Rename terminal','The running process keeps its identity.',[{name:'label',label:'Terminal name',value:block.label,required:true}],'Rename');if(fields){await call('session.set_block_label',{sessionId:state.sessionId,blockId:id,label:fields.label,expectedRevision:currentSession().revision});await refresh();}}
async function renameWindow(id){if(!state.capability.administrator)return;const window=currentSession().windows.find(w=>w.id===id);const fields=await showDialog('Rename window','', [{name:'label',label:'Window name',value:window.label,required:true}],'Rename');if(fields){await call('session.set_window_label',{sessionId:state.sessionId,windowId:id,label:fields.label,expectedRevision:currentSession().revision});await refresh();}}
async function closeBlock(id){if(!state.capability.administrator)return toast('This capability cannot close processes.');const fields=await showDialog('Close terminal process?','This terminates the process. To keep it running, use the ◇ detach button.');if(fields){await request(`/api/sessions/${state.sessionId}/blocks/${id}`,'DELETE');await refresh();}}
async function workspaceMenu(){const fields=await showDialog('Workspace settings','Closing a workspace terminates all of its processes.',[{name:'label',label:'Name',value:currentSession().label,required:true},{name:'action',label:'Action',options:[['rename','Rename workspace'],['close','Close workspace and processes']]}],'Apply');if(!fields)return;if(fields.action==='close'){await request(`/api/sessions/${state.sessionId}`,'DELETE');clearPanes();}else await call('session.set_label',{sessionId:state.sessionId,label:fields.label,expectedRevision:currentSession().revision});await refresh();}
async function captureBlock(id=state.focused){if(!id)return;const fields=await showDialog('Capture terminal','Export a server-side terminal snapshot, including retained scrollback.',[{name:'format',label:'Format',options:[['text','Plain text'],['html','Styled HTML'],['vt','Styled VT escape sequences']]}],'Download');if(!fields)return;const capture=await request(`/api/sessions/${state.sessionId}/blocks/${id}/capture?format=${fields.format}`);const extension=fields.format==='text'?'txt':fields.format==='vt'?'ansi':'html';const url=URL.createObjectURL(new Blob([capture.content],{type:fields.format==='html'?'text/html':'text/plain'}));const link=document.createElement('a');link.href=url;link.download=safeFilename(currentSession().blocks.find(b=>b.id===id)?.label,extension);link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
async function shareSession(){const fields=await showDialog('Share workspace','Anyone with this capability can access this session until expiry or revocation.',[{name:'role',label:'Access',options:[['observer','Observe only'],['controller','Input and resize control when available']]},{name:'lifetime',label:'Expires in',options:[['60','1 hour'],['240','4 hours'],['1440','24 hours']]}],'Create share');if(!fields)return;const share=await call('share.create',{sessionId:state.sessionId,role:fields.role,lifetimeMinutes:Number(fields.lifetime)});const url=new URL(location.href);url.hash=new URLSearchParams({token:share.token,session:share.sessionId});await showDialog('Workspace share created','The token stays in the URL fragment and is removed after opening. Revoke shares from the command palette.',[{name:'link',label:'Share link',value:url.href,readOnly:true}],'Done');}
async function manageShares(){const list=await call('share.list',{sessionId:state.sessionId});if(!list.shares.length)return toast('No active shares for this workspace.');const fields=await showDialog('Revoke workspace share','Revocation also detaches connected clients using that capability.',[{name:'id',label:'Share',options:list.shares.map(s=>[s.id,`${s.role} · expires ${new Date(s.expiresUtc).toLocaleString()} · ${s.id.slice(0,8)}`])}],'Revoke');if(fields){await call('share.revoke',{sessionId:state.sessionId,shareId:fields.id});toast('Share revoked.');}}
async function takeControl(){const pane=focusedPane();if(!pane)return;const clients=(await call('client.list')).clients.filter(c=>c.blockId===pane.id&&c.role==='controller');for(const client of clients)await call('client.disconnect',{clientId:client.id});pane.closed=true;pane.socket?.close();await new Promise(resolve=>setTimeout(resolve,150));await pane.connect();}
async function showApi(){const data=await request('/api/methods');await showDialog('Server API','The same registry is used by the CLI and custom actions.',[{name:'api',label:'Available methods',type:'textarea',value:data.methods.map(m=>`${m.name}\n  ${m.description}`).join('\n\n'),readOnly:true}],'Done');}
async function showStatus(){const records=await call('terminal.status',{sessionId:state.sessionId,blockId:state.focused});await showDialog('Program status','OSC 7501 records reported by the focused process.',[{name:'status',label:'Current records',type:'textarea',value:JSON.stringify(records,null,2),readOnly:true}],'Done');}
function toggleZoom(){state.zoom=!state.zoom;state.layoutKey='';render();}
async function disconnect(){state.stopped=true;clearPanes();state.token='';state.capability=null;$('application').hidden=true;$('connect-screen').hidden=false;$('access-token').value='';status('Detached');}

const actions=[
  {title:'New workspace',hint:'Workspace',run:createSession,admin:true},
  {title:'Open a new shell',hint:'Terminal',run:()=>newShell(),admin:true,session:true},
  {title:'New window',hint:'Ctrl B, C',run:newWindow,admin:true,session:true},
  {title:'Split side by side',hint:'Ctrl B, %',run:()=>newShell('horizontal'),admin:true,block:true},
  {title:'Split above and below',hint:'Ctrl B, "',run:()=>newShell('vertical'),admin:true,block:true},
  {title:'Zoom focused terminal',hint:'Layout',run:toggleZoom,block:true},
  {title:'Detach / reconnect focused terminal',hint:'Ctrl B, D',run:()=>focusedPane()?.toggleAttachment(),block:true},
  {title:'Take control of focused terminal',hint:'Sharing',run:takeControl,admin:true,block:true},
  {title:'Capture terminal output',hint:'Export',run:()=>captureBlock(),block:true},
  {title:'Inspect program status',hint:'OSC 7501',run:showStatus,block:true},
  {title:'Rename focused terminal',hint:'Terminal',run:()=>renameBlock(state.focused),admin:true,block:true},
  {title:'Workspace settings',hint:'Workspace',run:workspaceMenu,admin:true,session:true},
  {title:'Share workspace',hint:'Sharing',run:shareSession,admin:true,session:true},
  {title:'Revoke workspace share',hint:'Sharing',run:manageShares,admin:true,session:true},
  {title:'Explore server API',hint:'Automation',run:showApi},
  {title:'Disconnect all views',hint:'Processes remain running',run:disconnect},
];
let paletteItems=[],paletteIndex=0;
function renderPalette(){const query=$('palette-query').value.toLowerCase();paletteItems=actions.filter(a=>(!a.admin||state.capability.administrator)&&(!a.session||state.sessionId)&&(!a.block||state.focused)&&`${a.title} ${a.hint}`.toLowerCase().includes(query));paletteIndex=Math.min(paletteIndex,Math.max(0,paletteItems.length-1));const result=$('palette-results');result.replaceChildren();paletteItems.forEach((action,index)=>{const node=button('','',()=>{$('palette').close();return action.run();},'palette-action');node.append(text('span',action.title),text('small',action.hint));node.classList.toggle('selected',index===paletteIndex);node.addEventListener('mouseenter',()=>{paletteIndex=index;for(const [i,n]of [...result.children].entries())n.classList.toggle('selected',i===index);});result.append(node);});}
function openPalette(){if(state.stopped)return;$('palette-query').value='';paletteIndex=0;renderPalette();$('palette').showModal();$('palette-query').focus();}
$('palette-query').addEventListener('input',()=>{paletteIndex=0;renderPalette();});
$('palette-query').addEventListener('keydown',event=>{if(event.key==='ArrowDown'||event.key==='ArrowUp'){event.preventDefault();paletteIndex=(paletteIndex+(event.key==='ArrowDown'?1:-1)+paletteItems.length)%Math.max(1,paletteItems.length);renderPalette();}if(event.key==='Enter'&&paletteItems[paletteIndex]){event.preventDefault();const action=paletteItems[paletteIndex];$('palette').close();void run(action.run);}});
let chord=false,chordTimer;
document.addEventListener('keydown',event=>{
  if(state.stopped)return;
  if((event.metaKey||event.ctrlKey)&&event.key.toLowerCase()==='k'){event.preventDefault();event.stopImmediatePropagation();openPalette();return;}
  if($('dialog').open||$('palette').open)return;
  if(event.ctrlKey&&!event.shiftKey&&event.key.toLowerCase()==='b'){event.preventDefault();event.stopImmediatePropagation();chord=true;$('chord-hint').textContent='Prefix: % split ↔ · " split ↕ · c window · d detach';clearTimeout(chordTimer);chordTimer=setTimeout(()=>{chord=false;$('chord-hint').textContent='';},2000);return;}
  if(chord){chord=false;clearTimeout(chordTimer);$('chord-hint').textContent='';event.preventDefault();event.stopImmediatePropagation();const key=event.key.toLowerCase();const action=key==='%'?()=>newShell('horizontal'):key==='"'?()=>newShell('vertical'):key==='c'?newWindow:key==='d'?()=>focusedPane()?.toggleAttachment():null;if(action)void run(action);}
},{capture:true});

for(const [id,action]of Object.entries({'new-session':createSession,'empty-create':()=>currentSession()?newShell():createSession(),'new-shell':()=>newShell(),'new-window':newWindow,'split-horizontal':()=>newShell('horizontal'),'split-vertical':()=>newShell('vertical'),'zoom-block':toggleZoom,'share-session':shareSession,'workspace-menu':workspaceMenu,'disconnect':disconnect,'palette-button':openPalette,'help-button':openPalette,'toggle-sidebar':()=>$('sidebar').classList.toggle('visible')}))$(id).addEventListener('click',()=>void run(action));
$('connect-form').addEventListener('submit',async event=>{event.preventDefault();$('connect-error').textContent='';state.token=$('access-token').value.trim();try{if(!window.Terminal||!window.FitAddon)throw new Error('Terminal assets are missing. Run npm ci and npm run build in the server sample, then rebuild the server.');state.capability=await request('/api/context');state.stopped=false;await refresh();$('connect-screen').hidden=true;$('application').hidden=false;for(const pane of state.panes.values())pane.scheduleFit();status('Connected · closing this tab only detaches');}catch(error){$('connect-error').textContent=error.message;state.stopped=true;}});
const fragment=new URLSearchParams(location.hash.slice(1));if(fragment.has('token')){$('access-token').value=fragment.get('token');state.sessionId=fragment.get('session');history.replaceState(null,'',location.pathname+location.search);$('connect-form').requestSubmit();}
setInterval(()=>{if(!state.stopped)void refresh().catch(error=>status(error.message));},2500);
window.addEventListener('beforeunload',()=>{state.stopped=true;clearPanes();});
