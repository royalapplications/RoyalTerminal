import { execFileSync } from 'node:child_process';
import {cp,mkdir,mkdtemp,rm,access,readdir,writeFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
const project=fileURLToPath(new URL('../../RoyalTerminal.Multiplexing.BrowserReplica/RoyalTerminal.Multiplexing.BrowserReplica.csproj',import.meta.url));
const output=fileURLToPath(new URL('../wwwroot/vendor/managed/',import.meta.url));
const stage=await mkdtemp(join(tmpdir(),'royal-workspace-browser-'));
try {
  // Publish, rather than build: the WebAssembly SDK materializes the static web assets on publish.
  // This is an interpreted, framework-only build. It does not install workloads or compile a native runtime.
  execFileSync(process.env.ROYALTERMINAL_DOTNET||'dotnet',['publish',project,'-c','Release','-r','browser-wasm','--self-contained','true','-p:WasmBuildNative=false','-p:RunAOTCompilation=false','-o',stage],{stdio:'inherit'});
  const framework=join(stage,'wwwroot','_framework');const names=await readdir(framework);
  const loader=names.includes('dotnet.js')?'dotnet.js':names.find(n=>/^dotnet\.[a-z0-9_-]+\.js$/i.test(n)&&!/^dotnet\.(runtime|native|diagnostics)\./.test(n));
  if(!loader||!names.some(n=>n.endsWith('.wasm')))throw Error('A complete browser runtime was not published: '+names.join(', '));
  await rm(output,{recursive:true,force:true});await mkdir(output,{recursive:true});
  await cp(framework,join(output,'_framework'),{recursive:true,filter:path=>!path.endsWith('.pdb')&&!path.endsWith('.br')&&!path.endsWith('.gz')});
  if(loader!=='dotnet.js')await writeFile(join(output,'_framework','dotnet.js'),`export {dotnet} from './${loader}';\n`);
}finally{await rm(stage,{recursive:true,force:true});}
