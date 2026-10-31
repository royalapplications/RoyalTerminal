import { execFileSync } from 'node:child_process';
import { cp, mkdir, mkdtemp, rm, readdir, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const project=fileURLToPath(new URL('../../RoyalTerminal.Multiplexing.BrowserReplica/RoyalTerminal.Multiplexing.BrowserReplica.csproj',import.meta.url));
const output=fileURLToPath(new URL('../wwwroot/vendor/managed/',import.meta.url));
const stage=await mkdtemp(join(tmpdir(),'royal-workspace-browser-'));
const dotnet=(...args)=>execFileSync(process.env.ROYALTERMINAL_DOTNET||'dotnet',args,{stdio:'inherit'});
try {
  if(process.env.GITHUB_ACTIONS==='true') dotnet('workload','install','wasm-tools','--skip-manifest-update');
  // The SDK selects the correct interpreter relinking policy; explicitly disabling it prevents valid Release builds.
  dotnet('publish',project,'-c','Release','-o',stage);
  const framework=join(stage,'wwwroot','_framework');
  const files=await readdir(framework);
  const loader=files.includes('dotnet.js')?'dotnet.js':files.find(n=>/^dotnet\.[a-z0-9_-]+\.js$/i.test(n)&&!/^dotnet\.(runtime|native|diagnostics)\./.test(n));
  if(!loader||!files.some(n=>n.endsWith('.wasm')))throw Error('Incomplete browser publication: '+files.join(', '));
  await rm(output,{recursive:true,force:true});await mkdir(output,{recursive:true});
  await cp(framework,join(output,'_framework'),{recursive:true,filter:p=>!p.endsWith('.pdb')&&!p.endsWith('.br')&&!p.endsWith('.gz')});
  if(loader!=='dotnet.js')await writeFile(join(output,'_framework','dotnet.js'),`export {dotnet} from './${loader}';\n`);
} finally {await rm(stage,{recursive:true,force:true});}
