// Dedicated same-origin module worker. The capability token never crosses this boundary.
import { dotnet } from './vendor/managed/_framework/dotnet.js';
const exportsPromise=dotnet.create().then(async runtime=>(await runtime.getAssemblyExports('RoyalTerminal.BrowserReplica.dll')).RoyalTerminal.BrowserReplica.Replica);
self.onmessage=async({data:{id,op,handle,value}})=>{
  try {
    const r=await exportsPromise;let result;
    switch(op){
      case 'open': result=r.Open(new Uint8Array(value));break;
      case 'close': r.Close(handle);break;
      case 'output': r.Write(handle,value);break;
      case 'resize': r.Resize(handle,value.columns,value.rows,value.widthPixels??0,value.heightPixels??0);break;
      case 'paint': result={cols:r.Columns(handle),rows:r.Rows(handle),vt:r.Paint(handle)};break;
      default: throw Error('Unknown replica operation.');
    }
    self.postMessage({id,result});
  } catch(e){self.postMessage({id,error:String(e?.message??e).slice(0,512)});}
};
