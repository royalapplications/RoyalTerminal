// Reconcile by immutable ID. In particular, never replace or move a focused tab on an unchanged poll.
export function syncRows(parent, rows, create, update) {
  const old=new Map(Array.from(parent.children,node=>[node.dataset.key,node]));
  let cursor=parent.firstElementChild;
  for(const row of rows){const node=old.get(row.id)??create(row.id);node.dataset.key=row.id;old.delete(row.id);
    if(node!==cursor){const focused=node.ownerDocument?.activeElement;parent.insertBefore(node,cursor);if(focused===node)node.focus({preventScroll:true});}
    cursor=node.nextElementSibling;update(node,row);}
  for(const node of old.values())node.remove();
}
export function attentionBadge(c) {
  if(!c)return null;
  for(const [key,symbol,label] of [['error','!','failed'],['permission','?','awaiting permission'],['auth','!','awaiting authentication'],['question','?','awaiting a reply'],['blocked','!','blocked'],['working','●','working'],['done','✓','completed']])
    if(c[key]>0)return {key,symbol,label:`${c[key]} ${label}`};
  return null;
}
export function createNavigation({sessions,tabs,panel,selectSession,selectWindow,formatState}) {
  const span=cls=>Object.assign(document.createElement('span'),{className:cls});
  const make=(cls,action,id)=>{const b=document.createElement('button');b.type='button';b.className=cls;b.onclick=()=>action(id);return b;};
  const put=(n,v)=>{if(n.textContent!==v)n.textContent=v;};
  tabs.addEventListener('keydown',e=>{const row=e.target.closest('[role=tab]');if(!row||e.altKey||e.ctrlKey||e.metaKey||!['ArrowLeft','ArrowRight','Home','End'].includes(e.key))return;
    const all=[...tabs.children],i=all.indexOf(row);const next=e.key==='Home'?0:e.key==='End'?all.length-1:(i+(e.key==='ArrowRight'?1:-1)+all.length)%all.length;
    e.preventDefault();selectWindow(all[next].dataset.key);all[next].focus();all[next].scrollIntoView({block:'nearest',inline:'nearest'});});
  function paint(node,counts,active){const mark=active?null:attentionBadge(counts);node.hidden=!mark;if(mark){put(node,mark.symbol);node.dataset.status=mark.key;}return mark;}
  function describe(button,mark){if(mark)button.setAttribute('aria-description',mark.label);else button.removeAttribute('aria-description');}
  return {render(all,session,window,summaries,renameWindow){
    const valid=s=>{const summary=s&&summaries.get(s.id);return summary?.layoutRevision===s?.revision?summary:null;};
    syncRows(sessions,all,id=>{const b=make('session-item',selectSession,id),info=span('session-info');info.append(span('session-label'),span('session-detail'));const badge=span('attention-badge');badge.setAttribute('aria-hidden','true');b.append(span('session-symbol'),info,badge);return b;},(b,s)=>{
      const active=s.id===session?.id;b.classList.toggle('active',active);if(active)b.setAttribute('aria-current','true');else b.removeAttribute('aria-current');
      put(b.children[0],'▱');put(b.children[1].firstChild,s.label);const count=s.blocks.filter(x=>formatState(x)==='running').length;put(b.children[1].lastChild,`${count} running · ${s.windows.length} ${s.windows.length===1?'window':'windows'}`);
      const mark=paint(b.lastChild,valid(s)?.counts,active);b.title=s.label+(mark?' · '+mark.label:'');b.setAttribute('aria-label',`Open workspace ${s.label}`);describe(b,mark);});
    const counts=new Map((valid(session)?.windows??[]).map(x=>[x.windowId,x.counts]));
    syncRows(tabs,session?.windows??[],id=>{const b=make('window-tab',selectWindow,id);b.id='tab-'+id;b.setAttribute('role','tab');b.setAttribute('aria-controls',panel.id);b.ondblclick=()=>renameWindow(id);
      const badge=span('attention-badge');badge.setAttribute('aria-hidden','true');b.append(span('tab-label'),badge);return b;},(b,w)=>{
      const active=w.id===window?.id;put(b.firstChild,w.label);b.setAttribute('aria-label',w.label);b.setAttribute('aria-selected',String(active));b.tabIndex=active?0:-1;
      const mark=paint(b.lastChild,counts.get(w.id),active);b.title=mark?`${w.label} · ${mark.label}`:w.label;describe(b,mark);});
    if(window)panel.setAttribute('aria-labelledby','tab-'+window.id);else panel.removeAttribute('aria-labelledby');
  }};
}

// Only one scoped request per poll. Prefer the active session; use the intervening polls for
// unvisited workspaces in oldest-sampled order. A revoked or removed grant must not repopulate the cache.
export function createAttentionFeed(read,current,changed,now=()=>Date.now()) {
  const summaries=new Map(),checked=new Map();let eligible=null,epoch=0,busy=false;
  return {summaries,reset(){epoch++;busy=false;eligible=null;summaries.clear();checked.clear();},
    trim(ids){eligible=ids;for(const k of checked.keys())if(!ids.has(k)){checked.delete(k);summaries.delete(k);}},
    async sample(s){
      if(!s||busy||current()!==s.id)return;
      const at=now();let id=s.id;
      if(at-(checked.get(id)??-Infinity)<6000){
        let oldest=Infinity;id=null;for(const k of eligible??[]){const last=checked.get(k)??-Infinity;if(k!==s.id&&at-last>=30000&&last<oldest){oldest=last;id=k;}}
        if(id===null)return;
      }
      const version=epoch;checked.set(id,at);busy=true;
      try{const result=await read(id);if(epoch!==version||eligible&&!eligible.has(id)||!current()||result.sessionId!==id||id===s.id&&result.layoutRevision!==s.revision)return;
        summaries.set(id,result);changed();
      }catch{/* Status is optional; navigation does not depend on it. */}
      finally{if(epoch===version)busy=false;}
    }
  };
}
