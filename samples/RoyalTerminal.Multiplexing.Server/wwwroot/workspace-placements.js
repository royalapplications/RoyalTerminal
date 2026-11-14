import {placementActions as layoutActions,windowPlanes as legacyPlanes} from './workspace-placement-commands.js';
import {alertActions} from './workspace-alerts.js';
import {luaShortcutActions} from './workspace-lua-shortcuts.js';
import {remoteViewActions} from './workspace-remote-view.js';
export {deferPlacement,dragBounds,floatFrame} from './workspace-placement-commands.js';
export function windowPlanes(session,window){const planes=legacyPlanes(session,window);if(window?.floatingOrder==null)return planes;
  const blocks=new Map(planes.floats.map(b=>[b.id,b]));const sorted=[];for(const id of window.floatingOrder){const b=blocks.get(id);if(!b)throw Error('Invalid floating order.');sorted.push(b);blocks.delete(id);}if(blocks.size)throw Error('Missing floating layer.');return {...planes,floats:sorted};}
export function formatAttention(summary){
  const c=summary.counts,lines=[`${c.blocked} blocked · ${c.error} failed · ${c.working} working · ${c.done} done · ${c.idle} idle`,`Blocked: ${c.permission} approval · ${c.question} question · ${c.auth} authentication`];
  if(summary.unavailableBlocks)lines.push(`${summary.unavailableBlocks} terminal(s) could not be inspected.`);
  for(const w of summary.windows){lines.push('',w.label);for(const b of w.blocks){const x=b.counts;lines.push(x?`  ${b.label}: ${x.blocked} blocked, ${x.error} failed, ${x.working} working, ${x.done} done, ${x.idle} idle`:`  ${b.label}: unavailable`);}}
  return lines.join('\n');
}
export function placementActions(ctx){const depth=[['Raise floating terminal','raise',false],['Lower floating terminal','lower',false],['Bring floating terminal to front','raise',true],['Send floating terminal to back','lower',true]].map(([title,verb,toEdge])=>({title,admin:true,block:true,run:async()=>{const s=ctx.session(),id=ctx.state.focused;if(!s||s.blocks.find(b=>b.id===id)?.placement!=='Floating')return ctx.toast('Focus a floating terminal first.');await ctx.call(`session.${verb}_layer`,{sessionId:s.id,blockId:id,toEdge,expectedRevision:s.revision});await ctx.refresh();}}));
  return [...layoutActions(ctx),...depth,...alertActions(ctx),...luaShortcutActions(ctx),...remoteViewActions(ctx),{title:'Inspect workspace attention',hint:'Program status',session:true,run:async()=>{
  const s=ctx.session();if(!s)return;const result=await ctx.call('session.attention',{sessionId:s.id});if(ctx.state.stopped||ctx.session()?.id!==s.id)return;
  await ctx.dialog('Workspace attention','Reported program records, including floating and unplaced terminals. No input or control lease is required.',[{name:'activity',label:'Activity by terminal',type:'textarea',readOnly:true,value:formatAttention(result)}],'Done');}}];}
