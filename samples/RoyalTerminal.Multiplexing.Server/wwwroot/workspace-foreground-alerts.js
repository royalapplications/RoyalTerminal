// Browser-only, opt-in observation. No token, event payload or cursor is stored on disk.
const clean = value => [...String(value ?? '').replace(/[\u0000-\u001f\u007f-\u009f\u202a-\u202e\u2066-\u2069]/gu, ' ')].slice(0, 240).join('');
export function activityLine(e) {
  switch (e.name) {
    case 'terminal.desktop_notification': return {line: [e.notification?.title,e.notification?.body].map(clean).filter(Boolean).join(' · ') || 'Terminal notification', alert:true};
    case 'terminal.bell': return {line:'Terminal bell',alert:true};
    case 'terminal.progress_report': {const p=e.progressReport;return {line:`Progress: ${clean(p?.state)}${Number.isFinite(p?.progress)?` · ${p.progress}%`:''}`};}
    case 'terminal.program_status_changed': {const r=e.record; if(!r)return null;return {line:`${clean(r.state)} · ${clean(r.title || r.message || e.recordId || 'Program')}`,alert:r.state==='Blocked'||r.state==='Error'};}
    case 'terminal.child_exited': return {line:`Process exited (${Number.isInteger(e.exitCode)?e.exitCode:'unknown'})`,alert:e.exitCode!==0};
    case 'terminal.pwd_changed': return {line:`Directory: ${clean(e.pwd)}`};
    case 'terminal.title_changed': return {line:`Title: ${clean(e.text)}`};
    default: return null;
  }
}
export const deviceMessage = Object.freeze({title:'RoyalTerminal',body:'A terminal needs your attention. Open your workspace for details.'});

// The first read establishes a cursor at NOW. Never turn backfilled output into new OS notifications.
export async function followActivity(read,valid,deliver,signal) {
  let after;
  while (!signal.aborted && valid()) {
    const page=await read(after,signal);
    if(signal.aborted || !valid())return;
    if(!Number.isSafeInteger(page.nextSequence) || page.nextSequence<0 || !Array.isArray(page.events))throw Error('Invalid activity cursor.');
    if(after!==undefined) {
      let expected=after;
      for(const event of page.events) {
        if(event.sequence!==++expected)throw Error('Activity history is incomplete. Restart observation to begin at the current cursor.');
      }
      if(expected!==page.nextSequence)throw Error('Activity page cursor is inconsistent.');
      const entries=page.events.map(activityLine).filter(Boolean);
      if(entries.length)await deliver(entries);
    }
    after=page.nextSequence;if(page.ended)return;
  }
}

export function alertActions(ctx) {
  let current,history=[],generation,sessionId,serial=0,rateAt=-Infinity;
  const valid=(g,s)=>!ctx.state.stopped && ctx.state.generation===g && ctx.state.sessionId===s;
  function stop(){++serial;current?.abort();current=null;history=[];generation=undefined;sessionId=undefined;}
  globalThis.addEventListener?.('pagehide',stop);
  async function read(ids,after,signal){
    const args=after===undefined?ids:{...ids,after,limit:32,timeoutMs:8000};
    const response=await fetch('api/call',{method:'POST',redirect:'error',cache:'no-store',credentials:'omit',signal,
      headers:{Authorization:`Bearer ${ctx.state.token}`,'Content-Type':'application/json'},body:JSON.stringify({method:'terminal.events',arguments:args})});
    if(!response.ok)throw Error(`Terminal activity unavailable (HTTP ${response.status}).`);return response.json();
  }
  async function start(device){
    stop(); const run=serial,g=ctx.state.generation,s=ctx.state.sessionId,b=ctx.state.focused;
    if(!s||!b)return;
    let registration=null;
    if(device){
      if(!globalThis.isSecureContext || !globalThis.Notification || !navigator.serviceWorker)throw Error('Device notifications are unavailable here. Use Watch terminal activity instead.');
      // Called directly from a user activation; do not ask on page load or on terminal output.
      if(await Notification.requestPermission()!=='granted')throw Error('Device notifications were not enabled.');
      if(run!==serial||!valid(g,s))return;
      await navigator.serviceWorker.register(new URL('./workspace-alert-worker.js',import.meta.url));
      registration=await navigator.serviceWorker.ready;
    }
    if(run!==serial||!valid(g,s))return;
    const control=new AbortController();current=control;generation=g;sessionId=s;rateAt=-Infinity;
    const isCurrent=()=>serial===run && valid(g,s);
    ctx.toast(device?'Private device alerts enabled for this terminal.':'Watching activity for this terminal.');
    void followActivity((after,signal)=>read({sessionId:s,blockId:b},after,signal),isCurrent,async entries=>{
      history.push(...entries.map(e=>e.line));history=history.slice(-64);
      if(!entries.some(e=>e.alert) || performance.now()-rateAt<5000)return;
      rateAt=performance.now();
      if(registration && document.hidden){await registration.showNotification(deviceMessage.title,{body:deviceMessage.body,tag:'royalterminal-attention',silent:true});}
      else ctx.toast(entries.findLast(e=>e.alert).line);
    },control.signal).catch(e=>{if(isCurrent()&&!control.signal.aborted){history=[];ctx.toast(e.message);}}).finally(()=>{if(current===control)current=null;});
  }
  return [
    {title:'Watch terminal activity',hint:'In-app alerts · current terminal',block:true,run:()=>start(false)},
    {title:'Enable private device alerts',hint:'Opt in · no terminal text on lock screen',block:true,run:()=>start(true)},
    {title:'Read recent terminal activity',hint:'This tab only',session:true,run:()=>ctx.dialog('Recent terminal activity','Up to 64 events from the watched terminal. Watching does not take control.',[{name:'activity',label:'Recent activity',type:'textarea',readOnly:true,value:valid(generation,sessionId)&&history.length?history.join('\n'):'No activity recorded in this workspace.'}],'Done')},
    {title:'Stop terminal activity alerts',hint:'Clear this tab’s activity',session:true,run:()=>{stop();ctx.toast('Activity cleared.');}},
  ];
}
