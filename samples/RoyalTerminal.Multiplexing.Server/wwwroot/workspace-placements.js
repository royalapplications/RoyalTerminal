import {placementActions as layoutActions} from './workspace-placement-commands.js';
import {alertActions} from './workspace-alerts.js';
import {luaShortcutActions} from './workspace-lua-shortcuts.js';
import {remoteViewActions} from './workspace-remote-view.js';
export {deferPlacement,dragBounds,floatFrame,windowPlanes} from './workspace-placement-commands.js';
export function formatAttention(summary){
  const c=summary.counts,lines=[`${c.blocked} blocked · ${c.error} failed · ${c.working} working · ${c.done} done · ${c.idle} idle`,`Blocked: ${c.permission} approval · ${c.question} question · ${c.auth} authentication`];
  if(summary.unavailableBlocks)lines.push(`${summary.unavailableBlocks} terminal(s) could not be inspected.`);
  for(const w of summary.windows){lines.push('',w.label);for(const b of w.blocks){const x=b.counts;lines.push(x?`  ${b.label}: ${x.blocked} blocked, ${x.error} failed, ${x.working} working, ${x.done} done, ${x.idle} idle`:`  ${b.label}: unavailable`);}}
  return lines.join('\n');
}
export function placementActions(ctx){return [...layoutActions(ctx),...alertActions(ctx),...luaShortcutActions(ctx),...remoteViewActions(ctx),{title:'Inspect workspace attention',hint:'Program status',session:true,run:async()=>{
  const s=ctx.session();if(!s)return;const result=await ctx.call('session.attention',{sessionId:s.id});if(ctx.state.stopped||ctx.session()?.id!==s.id)return;
  await ctx.dialog('Workspace attention','Reported program records, including floating and unplaced terminals. No input or control lease is required.',[{name:'activity',label:'Activity by terminal',type:'textarea',readOnly:true,value:formatAttention(result)}],'Done');}}];}
