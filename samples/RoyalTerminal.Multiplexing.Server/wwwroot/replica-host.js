// This worker has no child-process transport or credential. Install the message handler before loading .NET.
let runtime;
function api(){
  return runtime ??= import('./vendor/managed/_framework/dotnet.js')
    .then(({dotnet})=>dotnet.create())
    .then(r=>r.getAssemblyExports('RoyalTerminal.BrowserReplica.dll'))
    .then(exports=>exports.RoyalTerminal.BrowserReplica.Replica);
}
self.addEventListener('message',async({data:{id,op,handle,value}})=>{
  try {
    const r=await api();let result;
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
});
