import { execFileSync } from 'node:child_process';
import { cp, mkdir, mkdtemp, rm, access } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
const project=fileURLToPath(new URL('../../RoyalTerminal.Multiplexing.BrowserReplica/RoyalTerminal.Multiplexing.BrowserReplica.csproj',import.meta.url));
const output=fileURLToPath(new URL('../wwwroot/vendor/managed/',import.meta.url));
const stage=await mkdtemp(join(tmpdir(),'royal-workspace-browser-'));
const run=(args)=>execFileSync(process.env.ROYALTERMINAL_DOTNET || 'dotnet',args,{stdio:'inherit'});
try {
  if(process.env.GITHUB_ACTIONS==='true') run(['workload','restore',project,'--skip-manifest-update']);
  run(['build',project,'-c','Release','-p:WasmAppDir='+stage]);
  await access(join(stage,'_framework','dotnet.js'));
  await rm(output,{recursive:true,force:true}); await mkdir(output,{recursive:true});
  await cp(join(stage,'_framework'),join(output,'_framework'),{recursive:true,filter:path=>!path.endsWith('.pdb')&&!path.endsWith('.gz')&&!path.endsWith('.br')});
} finally {await rm(stage,{recursive:true,force:true});}
