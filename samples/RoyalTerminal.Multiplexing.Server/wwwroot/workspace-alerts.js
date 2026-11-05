import {alertActions as foreground} from './workspace-foreground-alerts.js';
export {activityLine,deviceMessage,followActivity} from './workspace-foreground-alerts.js';
const receiptKey='royalterminal.push.receipts.v1';
const registrationURL=new URL('./workspace-alert-worker.js',import.meta.url);
// Store opaque removal receipts only, never bearer tokens, block/session IDs, subscription URLs or terminal output.
function receipts(){try{return JSON.parse(localStorage.getItem(receiptKey)||'[]').filter(x=>typeof x==='string'&&/^[0-9a-f-]{36}$/i.test(x)).slice(-64);}catch{return [];}}
function store(ids){try{localStorage.setItem(receiptKey,JSON.stringify(ids.slice(-64)));}catch{/* Browser unsubscribe remains available with storage disabled. */}}
if(typeof document!=='undefined'){const link=document.createElement('link');link.rel='manifest';link.href=new URL('./workspace.webmanifest',import.meta.url);document.head.append(link);}
function bytes(key){const raw=atob(key.replace(/-/g,'+').replace(/_/g,'/'));return Uint8Array.from(raw,c=>c.charCodeAt(0));}
async function request(path,token,method='GET',body){const r=await fetch(new URL('./api/push/'+path,registrationURL),{method,credentials:'omit',redirect:'error',cache:'no-store',headers:{Authorization:`Bearer ${token}`,...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined});if(!r.ok)throw Error(`Push service returned HTTP ${r.status}.`);return r.json();}
export function alertActions(ctx){
  let busy=false;
  const enable=async()=>{
    if(busy)return;busy=true;const {token,sessionId,focused:blockId,generation}=ctx.state;
    try{
      if(!isSecureContext||!globalThis.Notification||!navigator.serviceWorker||!globalThis.PushManager)throw Error('Web Push is unavailable. On iOS, open the installed Home Screen web app.');
      if(await Notification.requestPermission()!=='granted')throw Error('Notifications were not enabled.');
      const settings=await request('settings',token);if(!settings.enabled)throw Error('The operator has not enabled background Web Push. Foreground alerts remain available.');
      await navigator.serviceWorker.register(registrationURL);const reg=await navigator.serviceWorker.ready;
      const key=bytes(settings.applicationServerKey);let sub=await reg.pushManager.getSubscription();
      if(sub && !new Uint8Array(sub.options.applicationServerKey).every((v,i)=>key[i]===v))throw Error('Server push key changed. Disable background alerts on this device and register again.');
      sub??=await reg.pushManager.subscribe({userVisibleOnly:true,applicationServerKey:key});
      if(ctx.state.generation!==generation||ctx.state.stopped||ctx.state.sessionId!==sessionId||ctx.state.focused!==blockId)throw Error('Workspace selection changed; alert was not registered.');
      const receipt=await request('watch',token,'POST',{sessionId,blockId,endpoint:sub.endpoint});
      if(ctx.state.generation!==generation||ctx.state.stopped){await request('watch/'+receipt.id,token,'DELETE');return;}
      store([...new Set([...receipts(),receipt.id])]);ctx.toast('Background alerts enabled until '+new Date(receipt.expiresUtc).toLocaleString()+'. Terminal text is never sent.');
    }finally{busy=false;}
  };
  const disable=async(token)=>{
    // Unsubscribe the local browser even when the grant is expired or the broker is offline.
    const reg=await navigator.serviceWorker?.getRegistration(registrationURL);const sub=await reg?.pushManager?.getSubscription();
    if(sub && !await sub.unsubscribe())throw Error('The browser could not remove this subscription.');
    const ids=receipts();await Promise.allSettled(ids.map(id=>request('watch/'+id,token,'DELETE')));store([]);
  };
  document.getElementById('disconnect')?.addEventListener('click',()=>{if(receipts().length)void disable(ctx.state.token).catch(()=>{});},{capture:true});
  return [...foreground(ctx),{title:'Enable background push for this terminal',hint:'Opt in · works after closing the tab',block:true,run:enable},
    {title:'Disable background push on this device',hint:'Remove all browser push watches',session:true,run:async()=>{await disable(ctx.state.token);ctx.toast('Background push disabled on this device.');}}];
}
