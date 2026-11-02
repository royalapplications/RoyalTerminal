// Persistent controls: a session poll must never replace a keyboard user's focused tab.
export function syncRows(parent, rows, create, update) {
  const old = new Map(Array.from(parent.children, node => [node.dataset.key, node]));
  let cursor = parent.firstElementChild;
  for (const row of rows) {
    const node = old.get(row.id) ?? create(row.id);
    node.dataset.key = row.id;
    old.delete(row.id);
    if (node !== cursor) parent.insertBefore(node, cursor);
    cursor = node.nextElementSibling;
    update(node, row);
  }
  for (const node of old.values()) node.remove();
}

export function attentionBadge(c) {
  if (!c) return null;
  for (const [key, symbol, label] of [['error','!','failed'],['permission','?','awaiting permission'],['auth','!','awaiting authentication'],['question','?','awaiting a reply'],['blocked','!','blocked'],['working','●','working'],['done','✓','completed']]) {
    if (c[key] > 0) return { key, symbol, label: `${c[key]} ${label}` };
  }
  return null;
}

export function createNavigation({ sessions, tabs, panel, selectSession, selectWindow, formatState }) {
  const span = (cls) => Object.assign(document.createElement('span'), {className: cls});
  const make = (cls, action, id) => {
    const b = document.createElement('button'); b.type = 'button'; b.className = cls;
    b.onclick = () => action(id); return b;
  };
  const put = (node, value) => { if (node.textContent !== value) node.textContent = value; };
  tabs.addEventListener('keydown', e => {
    const keys = ['ArrowLeft','ArrowRight','Home','End'];
    const row = e.target.closest('[role=tab]');
    if (!row || e.altKey || e.ctrlKey || e.metaKey || !keys.includes(e.key)) return;
    const all = [...tabs.children], i = all.indexOf(row);
    const next = e.key === 'Home' ? 0 : e.key === 'End' ? all.length-1 : (i + (e.key === 'ArrowRight' ? 1 : -1) + all.length) % all.length;
    e.preventDefault(); selectWindow(all[next].dataset.key); all[next].focus();
    all[next].scrollIntoView({block:'nearest',inline:'nearest'});
  });
  function paintBadge(node, counts, active) {
    const mark = active ? null : attentionBadge(counts);
    node.hidden = !mark;
    if (mark) { put(node, mark.symbol); node.dataset.status = mark.key; }
    return mark;
  }
  return {
    render(all, session, window, summaries, renameWindow) {
      syncRows(sessions, all, id => {
        const b = make('session-item', selectSession, id), info = span('session-info');
        info.append(span('session-label'),span('session-detail'));
        const badge=span('attention-badge'); badge.setAttribute('aria-hidden','true');
        b.append(span('session-symbol'),info,badge); return b;
      }, (b,s) => {
        const active=s.id===session?.id;
        b.classList.toggle('active',active); if(active)b.setAttribute('aria-current','true');else b.removeAttribute('aria-current');
        put(b.children[0],'▱'); put(b.children[1].firstChild,s.label);
        const count=s.blocks.filter(x=>formatState(x)==='running').length;
        put(b.children[1].lastChild,`${count} running · ${s.windows.length} ${s.windows.length===1?'window':'windows'}`);
        const summary=summaries.get(s.id), mark=paintBadge(b.lastChild,summary?.counts,active);
        b.title=s.label+(mark?' · '+mark.label:''); b.setAttribute('aria-label',`Open workspace ${s.label}`);
        if(mark)b.setAttribute('aria-description',mark.label);else b.removeAttribute('aria-description');
      });
      const counts = new Map((summaries.get(session?.id)?.windows ?? []).map(x=>[x.windowId,x.counts]));
      syncRows(tabs, session?.windows ?? [], id => {
        const b=make('window-tab',selectWindow,id); b.id='tab-'+id; b.setAttribute('role','tab');
        b.setAttribute('aria-controls', panel.id); b.ondblclick=()=>renameWindow(id);
        const badge=span('attention-badge');badge.setAttribute('aria-hidden','true'); b.append(span('tab-label'),badge);return b;
      },(b,w)=>{
        const active=w.id===window?.id; put(b.firstChild,w.label);b.setAttribute('aria-label',w.label);
        b.setAttribute('aria-selected',String(active)); b.tabIndex=active?0:-1;
        const mark=paintBadge(b.lastChild,counts.get(w.id),active); b.title=mark?`${w.label} · ${mark.label}`:w.label;
        if(mark)b.setAttribute('aria-description',mark.label);else b.removeAttribute('aria-description');
      });
      if(window)panel.setAttribute('aria-labelledby','tab-'+window.id);else panel.removeAttribute('aria-labelledby');
    }
  };
}

// At most one scoped request in flight. Cached summaries can be shown for previously visited sessions;
// never ask for a hidden session's data using a different connection generation.
export function createAttentionFeed(read, current, changed, now=()=>Date.now()) {
  const summaries = new Map(), checked = new Map(); let epoch=0, busy=false;
  return {summaries, reset(){epoch++;busy=false;summaries.clear();checked.clear();},
    trim(ids){for(const key of checked.keys())if(!ids.has(key)){checked.delete(key);summaries.delete(key);}},
    async sample(s){
      if(!s||busy||current()!==s.id)return;
      const at=now(),previous=checked.get(s.id); if(previous!==undefined&&at-previous<6000)return;
      const version=epoch; checked.set(s.id,at); busy=true;
      try{const result=await read(s.id);if(epoch!==version||current()!==s.id||result.sessionId!==s.id||result.layoutRevision!==s.revision)return;
        summaries.set(s.id,result);changed();
      }catch{/* A missing optional inspection must not prevent navigation. */}
      finally{if(epoch===version)busy=false;}
    }
  };
}
