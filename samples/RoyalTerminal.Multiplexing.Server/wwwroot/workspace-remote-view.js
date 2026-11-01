// Remote UI actions cannot send terminal input, request a lease or create a process.
import {applyViewPage} from './view-delivery.js';
export function neighbor(items,current,dir){
  const i=items.findIndex(x=>x.id===current);if(i<0)return null;
  if(dir==='next'||dir==='previous')return items[(i+items.length+(dir==='next'?1:-1))%items.length].id;
  if(!['left','right','up','down'].includes(dir))return null;
  const vertical=dir==='up'||dir==='down',sign=dir==='up'||dir==='left'?-1:1;
  const axes=r=>vertical?[r.top,r.bottom,r.left,r.right]:[r.left,r.right,r.top,r.bottom];
  const [a,b,c,d]=axes(items[i].rect),choices=[];
  for(const item of items){const [x,y,z,w]=axes(item.rect),distance=sign*(x+y-a-b)/2;if(item.id===current||distance<=0)continue;
    choices.push({id:item.id,offAxis:Math.min(d,w)>Math.max(c,z)?0:1,distance,offset:Math.abs(z+w-c-d)/2});}
  choices.sort((p,q)=>p.offAxis-q.offAxis||p.distance-q.distance||p.offset-q.offset||p.id.localeCompare(q.id));return choices[0]?.id??null;
}
export async function present(ctx,a,current,remote=false){
  if(!current()||document.querySelector('dialog[open]'))return false;const {state}=ctx,s=ctx.session();if(!s)return false;
  const click=id=>document.getElementById(id).click();
  const choose=id=>{const i=s.windows.findIndex(w=>w.id===id);if(i<0)return false;state.remoteNavigation=remote;try{document.querySelectorAll('#window-tabs [role=tab]')[i]?.click();}finally{state.remoteNavigation=false;}return state.windowId===id;};
  switch(a.name){
    case 'client.palette':click('palette-button');return true;
    case 'client.sidebar.toggle':{const app=document.getElementById('application'),side=document.getElementById('sidebar');if(matchMedia('(max-width:640px)').matches)side.classList.toggle('visible');else{const hide=!side.hidden;side.hidden=hide;app.style.gridTemplateColumns=hide?'minmax(0,1fr)':'';}return true;}
    case 'pane.zoom':if(!state.focused)return false;{const previous=state.zoom;click('zoom-block');return state.zoom!==previous;}
    case 'window.focus':return choose(a.windowId);
    case 'client.tab.next':case 'client.tab.previous':{const i=s.windows.findIndex(w=>w.id===state.windowId);return i>=0&&choose(s.windows[(i+s.windows.length+(a.name.endsWith('next')?1:-1))%s.windows.length].id);}
    case 'pane.focus':break; default:return false;
  }
  const all=[...document.querySelectorAll('#pane-layout .terminal-pane')].map(el=>({id:el.dataset.blockId,rect:el.getBoundingClientRect()}));
  const id=a.blockId||neighbor(all,state.focused,a.direction),b=s.blocks.find(x=>x.id===id);if(!b||b.placement==='Unplaced')return false;
  if(state.zoom&&state.focused!==id)click('zoom-block');if(state.windowId!==b.windowId&&!choose(b.windowId))return false;
  await new Promise(resolve=>setTimeout(resolve,0));const p=state.panes.get(id);
  if(!current()||document.querySelector('dialog[open]')||state.windowId!==b.windowId||!p?.element.isConnected)return false;p.mount();p.terminal.focus();return state.focused===id;
}
export function remoteViewActions(ctx){
  if(typeof document==='undefined')return [];
  let active=null,serial=0;const state=ctx.state,stopButton=document.createElement('button');stopButton.type='button';stopButton.hidden=true;stopButton.textContent='Stop remote UI';stopButton.onclick=()=>stop();document.getElementById('help-button').before(stopButton);
  const same=v=>active===v&&!state.stopped&&state.generation===v.gen&&state.sessionId===v.session;
  async function rpc(v,method,fields,signal){const r=await fetch('/api/call',{method:'POST',credentials:'omit',redirect:'error',cache:'no-store',signal,headers:{Authorization:`Bearer ${v.token}`,'Content-Type':'application/json'},body:JSON.stringify({method,arguments:{sessionId:v.session,...fields}})});const result=await r.json();if(!r.ok)throw Error(result.message||`HTTP ${r.status}`);return result;}
  function stop(v=active){++serial;if(!v)return;if(active===v){active=null;state.remoteView=null;stopButton.hidden=true;}v.abort.abort();if(v.identity)void rpc(v,'client.close_view',v.identity,AbortSignal.timeout(3000)).catch(()=>{});}
  function check(){if(active&&!same(active))stop();}
  const watcher=new MutationObserver(check);watcher.observe(document.getElementById('application'),{attributes:true,attributeFilter:['hidden']});watcher.observe(document.getElementById('workspace-title'),{childList:true});window.addEventListener('pagehide',()=>stop());
  async function read(v){let after=0;try{while(same(v)){
    const page=await rpc(v,'client.read_view',{...v.identity,after},v.abort.signal);if(!same(v))break;if(page.batches?.length)await ctx.refresh();
    after=await applyViewPage(page,after,()=>same(v),async a=>{const ok=await present(ctx,a,()=>same(v),true);if(!ok&&same(v))ctx.toast('Remote action skipped: target unavailable or dialog open.');return ok;},v.reports?(sequence,applied)=>rpc(v,'client.finish_view',{...v.identity,sequence,applied},v.abort.signal):null);
  }}catch(e){if(same(v))ctx.toast('Remote UI stopped: '+e.message);}finally{if(active===v)stop(v);}}
  async function enable(){if(active){ctx.toast('Remote UI is already enabled.');return;}const ticket=++serial,session=state.sessionId,gen=state.generation;
    const f=await ctx.dialog('Allow remote UI actions','An administrator can navigate this tab. Newly shown terminals open read-only. Stop remote UI at any time.',[{name:'label',label:'Client label',value:'Browser',required:true}],'Enable for this session');
    if(!f||ticket!==serial||!session||state.generation!==gen||state.sessionId!==session)return;
    const v={session,gen,token:state.token,abort:new AbortController()};active=v;state.remoteView={session,generation:gen,id:null};stopButton.hidden=false;
    try{const lease=await rpc(v,'client.open_view',{kind:'browser',label:f.label},v.abort.signal);v.identity={clientId:lease.view.id,readerKey:lease.readerKey};v.reports=lease.completionProtocol===1;if(!same(v)){stop(v);return;}state.remoteView.id=lease.view.id;ctx.toast(`Remote UI enabled: ${lease.view.label}`);void read(v);}catch(e){if(active===v)stop(v);throw e;}}
  return [{title:'Enable remote UI actions for this tab',session:true,run:enable},{title:'Disable remote UI actions for this tab',session:true,run:()=>{stop();ctx.toast('Remote UI disabled.');}}];
}
