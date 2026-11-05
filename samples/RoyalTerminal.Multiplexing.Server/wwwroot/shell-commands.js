import {safeFilename} from './workspace-model.js';

// Resolve targets before awaiting user input. A dialog or slow request may outlive the selection.
export function createCommands(ctx) {
  const {state,request,call,dialog,refresh,toast,session,window:win}=ctx;
  const here=(sid,wid)=>!state.stopped&&session()?.id===sid&&(!wid||win()?.id===wid);
  async function launch(sid,wid,orientation,target){
    const data={label:'Shell',transport:'pty',windowId:wid};
    if(orientation&&target){data.splitBlockId=target;data.orientation=orientation;data.expectedRevision=session()?.id===sid?session().revision:undefined;delete data.windowId;}
    const b=await request(`/api/sessions/${sid}/blocks`,'POST',data);
    if(here(sid,wid)){state.focused=b.id;state.zoom=false;}await refresh();
    requestAnimationFrame(()=>{if(here(sid,wid)&&state.focused===b.id)state.panes.get(b.id)?.terminal.focus();});
  }
  async function createSession(){
    const f=await dialog('New workspace','Group related terminals in a session.',[{name:'label',label:'Workspace name',value:'My workspace',required:true}],'Create workspace');if(!f)return;
    const s=await request('/api/sessions','POST',{label:f.label});state.sessionId=s.id;state.windowId=s.windows[0].id;await launch(s.id,s.windows[0].id);
  }
  async function newShell(orientation,target=state.focused){const s=session(),w=win();if(!s)return createSession();if(w)await launch(s.id,w.id,orientation,target);}
  async function newWindow(){
    const s=session();if(!s)return;const f=await dialog('New window','Create another terminal layout.',[{name:'label',label:'Window name',value:'Window',required:true}],'Create window');if(!f)return;
    const w=await call('session.new_window',{sessionId:s.id,label:f.label,expectedRevision:s.revision});if(here(s.id))state.windowId=w.id;await launch(s.id,w.id);
  }
  async function rename(kind,id){
    const s=session(),block=kind==='block',entity=(block?s?.blocks:s?.windows)?.find(x=>x.id===id),label=block?'Terminal':'Window';if(!entity||!state.capability?.administrator)return;
    const f=await dialog(`Rename ${label.toLowerCase()}`,'The running process keeps its identity.',[{name:'label',label:`${label} name`,value:entity.label,required:true}],'Rename');
    if(f){await call(`session.set_${kind}_label`,{sessionId:s.id,[`${kind}Id`]:id,label:f.label,expectedRevision:s.revision});await refresh();}
  }
  async function closeBlock(id){const s=session();if(!state.capability?.administrator)return toast('Read-only access cannot close a process.');if(!s)return;
    if(await dialog('Close terminal process?',`Stop ${s.blocks.find(b=>b.id===id)?.label??'this process'}? Detaching keeps it running.`,[],'Close process')){await request(`/api/sessions/${s.id}/blocks/${id}`,'DELETE');await refresh();}}
  async function workspaceMenu(){const s=session();if(!s)return;const f=await dialog('Workspace settings','Closing this workspace terminates all its processes.',[{name:'label',label:'Name',value:s.label,required:true},{name:'action',label:'Action',options:[['rename','Rename workspace'],['close','Close workspace and processes']]}],'Apply');if(!f)return;
    if(f.action==='close'){if(!await dialog('Close workspace?',`Stop every terminal in ${s.label}? This cannot be undone.`,[],'Close workspace'))return;await request(`/api/sessions/${s.id}`,'DELETE');}
    else await call('session.set_label',{sessionId:s.id,label:f.label,expectedRevision:s.revision});await refresh();}
  async function captureBlock(id=state.focused){const s=session();if(!s||!id)return;
    const f=await dialog('Capture terminal','Export the server-side snapshot.',[{name:'format',label:'Format',options:[['text','Plain text'],['html','Styled HTML'],['vt','Styled VT escape sequences']]}],'Download');if(!f)return;
    const c=await request(`/api/sessions/${s.id}/blocks/${id}/capture?format=${f.format}`),uri=URL.createObjectURL(new Blob([c.content],{type:f.format==='html'?'text/html':'text/plain'}));
    const a=document.createElement('a');a.href=uri;a.download=safeFilename(s.blocks.find(b=>b.id===id)?.label,{text:'txt',html:'html',vt:'ansi'}[f.format]);a.click();setTimeout(()=>URL.revokeObjectURL(uri),1000);}
  async function shareSession(){const s=session();if(!s)return;const f=await dialog('Share workspace','Observers can read output. Controllers can execute commands as the server OS account, including accessing its files and processes.',[{name:'role',label:'Access',options:[['observer','Observe only'],['controller','Input and resize control']]},{name:'lifetime',label:'Expires in',options:[['60','1 hour'],['240','4 hours'],['1440','24 hours']]}],'Create share');if(!f)return;
    const share=await call('share.create',{sessionId:s.id,role:f.role,lifetimeMinutes:Number(f.lifetime)});const url=new URL(location.href);url.hash=new URLSearchParams({token:share.token,session:share.sessionId});
    await dialog('Workspace share created','Anyone with this link can use its permissions until it expires or is revoked.',[{name:'link',label:'Share link',value:url.href,readOnly:true}],'Done');}
  async function manageShares(){const s=session();if(!s)return;const {shares}=await call('share.list',{sessionId:s.id});if(!shares.length)return toast('No active shares.');const f=await dialog('Revoke workspace share','Clients using this link will disconnect.',[{name:'id',label:'Share',options:shares.map(x=>[x.id,`${x.role} · ${x.expiresUtc} · ${x.id.slice(0,8)}`])}],'Revoke');if(f)await call('share.revoke',{sessionId:s.id,shareId:f.id});}
  const pane=()=>state.panes.get(state.focused);
  async function takeControl(){const p=pane();if(!p)return;for(const c of (await call('client.list')).clients.filter(c=>c.blockId===p.id&&c.role==='controller'))await call('client.disconnect',{clientId:c.id});await p.reconnectController();}
  async function showApi(){const d=await request('/api/methods');await dialog('Server API','Methods advertised by this host.',[{name:'api',label:'Available methods',type:'textarea',value:d.methods.map(m=>`${m.name}\n  ${m.description}`).join('\n\n'),readOnly:true}],'Done');}
  async function showStatus(){const r=await call('terminal.status',{sessionId:state.sessionId,blockId:state.focused});await dialog('Program status','Status reported by the focused process.',[{name:'status',label:'Current records',type:'textarea',value:JSON.stringify(r,null,2),readOnly:true}],'Done');}
  async function setShared(zoom){const s=session(),id=zoom?state.focused:win()?.zoomedBlockId;if(!s||!id)return;await call('session.zoom_block',{sessionId:s.id,blockId:id,zoom,expectedRevision:s.revision});await refresh();}
  return {createSession,newShell,newWindow,renameBlock:id=>rename('block',id),renameWindow:id=>rename('window',id),closeBlock,workspaceMenu,captureBlock,shareSession,manageShares,takeControl,showApi,showStatus,setShared};
}
