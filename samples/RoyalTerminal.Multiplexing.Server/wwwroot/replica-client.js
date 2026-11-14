const mime='application/vnd.royalterminal.ghostty-snapshot-v1';
const limit=8*1024*1024;
function integer(value,min,max){if(typeof value!=='string'||!/^(-?[0-9]+)$/.test(value))throw Error('Missing or malformed checkpoint header.');const n=Number(value);if(!Number.isSafeInteger(n)||n<min||n>max)throw Error('Checkpoint header out of range.');return n;}
export async function readCut(session,block,token,signal){
  const url=`/api/sessions/${encodeURIComponent(session)}/blocks/${encodeURIComponent(block)}/checkpoint`;
  const r=await fetch(url,{headers:{Authorization:`Bearer ${token}`,Accept:mime},cache:'no-store',credentials:'omit',mode:'same-origin',redirect:'error',signal});
  if(!r.ok)throw Error(`Checkpoint HTTP ${r.status}`);
  const h=r.headers;
  if(h.get('Content-Type')?.split(';')[0].toLowerCase()!==mime||h.get('X-Workspace-Session')!==session||h.get('X-Workspace-Block')!==block)throw Error('Unexpected checkpoint type or identity.');
  const sequence=integer(h.get('X-Workspace-Sequence'),1,Number.MAX_SAFE_INTEGER),exit=h.has('X-Workspace-Exit')?integer(h.get('X-Workspace-Exit'),-2147483648,2147483647):null;
  const bytes=new Uint8Array(integer(h.get('Content-Length'),1,limit));const reader=r.body.getReader();let offset=0;
  try{for(;;){const {done,value}=await reader.read();if(done)break;if(value.length>bytes.length-offset)throw Error('Oversized checkpoint body.');bytes.set(value,offset);offset+=value.length;}if(offset!==bytes.length)throw Error('Truncated checkpoint body.');}
  catch(e){await reader.cancel().catch(()=>{});throw e;}finally{reader.releaseLock();}
  return {sequence,exit,bytes:bytes.buffer};
}
let shared=null;
class WorkerHost {
  constructor(){this.next=0;this.users=0;this.pending=new Map();this.worker=new Worker('/checkpoint-worker.mjs',{type:'module'});this.worker.onmessage=({data})=>{const p=this.pending.get(data.id);if(!p)return;this.pending.delete(data.id);clearTimeout(p.timer);if(data.error)p.reject(Error(data.error));else p.resolve(data.result);};this.worker.onerror=e=>{e.preventDefault();this.stop('Recovery worker failed to load. Build the managed browser assets.');};this.worker.onmessageerror=()=>this.stop('Recovery worker returned an invalid message.');}
  call(op,handle,value,transfer=[]){if(this.dead)return Promise.reject(Error('Checkpoint worker stopped.'));if(this.pending.size>=128)return Promise.reject(Error('Checkpoint worker queue full.'));const id=++this.next;return new Promise((resolve,reject)=>{const timer=setTimeout(()=>this.stop('Checkpoint worker deadline exceeded.'),30000);this.pending.set(id,{resolve,reject,timer});try{this.worker.postMessage({id,op,handle,value},transfer);}catch(e){clearTimeout(timer);this.pending.delete(id);reject(e);}});}
  stop(reason){if(this.dead)return;this.dead=true;this.worker.terminate();if(shared===this)shared=null;for(const p of this.pending.values()){clearTimeout(p.timer);p.reject(Error(reason));}this.pending.clear();}
  release(){if(--this.users===0)this.stop('Last replica closed.');}
}
export async function openReplica(bytes){const host=shared??=new WorkerHost();++host.users;try{const handle=await host.call('open',0,bytes,[bytes]);let disposed=false;return {call:(op,v)=>host.call(op,handle,v),async dispose(){if(disposed)return;disposed=true;try{await host.call('close',handle);}catch{}finally{host.release();}}};}catch(e){host.release();throw e;}}
