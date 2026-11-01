// Presentation-only. Never synthesize a key, request control, launch a shell, or restore a shelved process.
const nextFrame = () => new Promise(resolve => requestAnimationFrame(resolve));
export function neighbor(items, current, direction) {
  const i = items.findIndex(x => x.id === current);
  if (i < 0 || !items.length) return null;
  if (direction === 'next' || direction === 'previous') return items[(i + items.length + (direction === 'next' ? 1 : -1)) % items.length].id;
  if (!['left','right','up','down'].includes(direction)) return null;
  const vertical = direction === 'up' || direction === 'down', sign = direction === 'up' || direction === 'left' ? -1 : 1;
  const axes = r => vertical ? [r.top,r.bottom,r.left,r.right] : [r.left,r.right,r.top,r.bottom];
  const [a,b,c,d] = axes(items[i].rect), mid=(a+b)/2, cross=(c+d)/2;
  const choices=[];
  for (const item of items) {
    const [x,y,z,w]=axes(item.rect), distance=sign*((x+y)/2-mid);
    if (item.id===current || distance<=0) continue;
    const offset=Math.abs((z+w)/2-cross), overlap=Math.min(d,w)-Math.max(c,z);
    choices.push({id:item.id,overlap:overlap>0 ? 0 : 1,distance,offset});
  }
  choices.sort((p,q)=>p.overlap-q.overlap||p.distance-q.distance||p.offset-q.offset||p.id.localeCompare(q.id));
  return choices[0]?.id ?? null;
}

export async function present(ctx, action, current, remote) {
  if (!current() || document.querySelector('dialog[open]')) return false;
  const {state}=ctx, s=ctx.session(); if (!s) return false;
  const tabs=()=>[...document.querySelectorAll('#window-tabs [role=tab]')];
  const choose = id => {
    const index=s.windows.findIndex(w=>w.id===id); if(index<0) return false;
    // New panes mounted by remote navigation must attach as observers even on an administrator page.
    state.remoteNavigation=remote; try {tabs()[index]?.click();} finally {state.remoteNavigation=false;} return state.windowId===id;
  };
  switch(action.name) {
    case 'client.palette': document.getElementById('palette-button').click(); return true;
    case 'client.sidebar.toggle': document.getElementById('sidebar').classList.toggle('visible'); return true;
    case 'pane.zoom': if(!state.focused)return false;document.getElementById('zoom-block').click();return true;
    case 'window.focus':return choose(action.windowId);
    case 'client.tab.next':case 'client.tab.previous': {
      const i=s.windows.findIndex(w=>w.id===state.windowId);return i>=0 && choose(s.windows[(i+s.windows.length+(action.name.endsWith('next')?1:-1))%s.windows.length].id);
    }
    case 'pane.focus':break;
    default:return false;
  }
  const items=[...document.querySelectorAll('#pane-layout .terminal-pane')].map(el=>({id:el.dataset.blockId,rect:el.getBoundingClientRect()}));
  const id=action.blockId || neighbor(items,state.focused,action.direction);const b=s.blocks.find(v=>v.id===id);
  if(!b || b.placement==='Unplaced')return false;
  if(state.zoom && state.focused!==id) {state.remoteNavigation=remote;try{document.getElementById('zoom-block').click();}finally{state.remoteNavigation=false;}}
  if(state.windowId!==b.windowId && !choose(b.windowId))return false;
  await nextFrame();const pane=state.panes.get(id);
  if(!current() || document.querySelector('dialog[open]') || state.windowId!==b.windowId || !pane?.element.isConnected)return false;
  pane.terminal.focus();return state.focused===id;
}

export function remoteViewActions(ctx) {
  if(typeof document==='undefined')return [];
  let active=null, serial=0;
  const same = v => active===v && !ctx.state.stopped && ctx.state.generation===v.gen && ctx.state.sessionId===v.session;
  async function rpc(v,method,fields,signal) {
    const r=await fetch('/api/call',{method:'POST',credentials:'omit',redirect:'error',cache:'no-store',signal,headers:{Authorization:`Bearer ${v.token}`,'Content-Type':'application/json'},body:JSON.stringify({method,arguments:{sessionId:v.session,...fields}})});
    const result=await r.json();if(!r.ok)throw Error(result.message||`HTTP ${r.status}`);return result;
  }
  function stop(v=active) {
    ++serial;if(!v)return;if(active===v)active=null;v.abort.abort();
    if(v.identity)void rpc(v,'client.close_view',v.identity,AbortSignal.timeout(3000)).catch(()=>{});
  }
  new MutationObserver(()=>{if(active&&!same(active))stop();}).observe(document.getElementById('application'),{attributes:true,attributeFilter:['hidden'],childList:true,subtree:true});
  window.addEventListener('pagehide',()=>stop());
  async function read(v) {
    let after=0;
    try {
      while(same(v)) {
        const page=await rpc(v,'client.read_view',{...v.identity,after},v.abort.signal);
        if(!same(v))break;
        if(page.batches.length)await ctx.refresh();
        for(const batch of page.batches){
          if(!same(v))return;if(batch.sequence!==after+1)throw Error('Unexpected remote-view sequence.');
          for(const a of batch.actions){if(!same(v))return;if(!await present(ctx,a,()=>same(v),true)){ctx.toast('Remote UI action skipped: target unavailable or a dialog is open.');break;}}
          after=batch.sequence;
        }
        if(after!==page.cursor)throw Error('Invalid remote-view delivery cursor.');
      }
    } catch(e){if(same(v))ctx.toast('Remote control stopped: '+e.message);}finally{if(active===v)stop(v);}
  }
  async function enable(){
    if(active){ctx.toast('This view already accepts remote actions.');return;}
    const ticket=++serial,session=ctx.state.sessionId,gen=ctx.state.generation;
    const f=await ctx.dialog('Allow remote UI actions','An administrator may navigate this tab within this workspace. This does not grant terminal input. Disconnecting forgets this permission.',[{name:'label',label:'Client label',value:'Browser',required:true}],'Enable for this session');
    if(!f||ticket!==serial||!session||ctx.state.generation!==gen||ctx.state.sessionId!==session)return;
    const v={session,gen,token:ctx.state.token,abort:new AbortController()};active=v;
    try{const lease=await rpc(v,'client.open_view',{kind:'browser',label:f.label},v.abort.signal);v.identity={clientId:lease.view.id,readerKey:lease.readerKey};if(!same(v)){stop(v);return;}ctx.toast(`Remote UI enabled: ${lease.view.label}`);void read(v);}catch(e){if(active===v)stop(v);throw e;}
  }
  return [{title:'Enable remote UI actions for this tab',session:true,run:enable},{title:'Disable remote UI actions for this tab',session:true,run:()=>{stop();ctx.toast('Remote UI disabled.');}}];
}
