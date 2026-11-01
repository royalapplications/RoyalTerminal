import {keyStroke} from './keymap.js';
import {replaceRatio} from './workspace-model.js';
import {present} from './workspace-remote-view.js';
const arrows={arrowleft:'left',arrowright:'right',arrowup:'up',arrowdown:'down'};
const punctuation={Quote:"'",Backquote:'`',Semicolon:';',Comma:',',Period:'.',Minus:'-',Equal:'=',BracketLeft:'[',BracketRight:']',Backslash:'\\',Slash:'/'};
export function physicalStroke(e){if(e.isComposing||e.getModifierState?.('AltGraph')||['Dead','Process','Unidentified'].includes(e.key))return null;
  const base=keyStroke(e);if(!base)return null;let k=arrows[e.key.toLowerCase()];if(e.shiftKey)k=k??(/^Digit[0-9]$/.test(e.code)?e.code.slice(-1):punctuation[e.code]);
  return k?`${e.ctrlKey?'ctrl+':''}${e.altKey?'alt+':''}${e.shiftKey?'shift+':''}${e.metaKey?'meta+':''}${k}`:base;}
// Balance equal-area leaves even when the original tree is asymmetric; never flatten it or move a block.
export function balanced(tree){function visit(n){if(!n||n.blockId)return [n,n?1:0];const [a,l]=visit(n.first),[b,r]=visit(n.second);return [{...n,ratio:l/(l+r),first:a,second:b},l+r];}return visit(tree)[0];}
export function resizePlan(tree,id,direction,amount,cell,extent){
  if(!['left','right','up','down'].includes(direction)||!Number.isSafeInteger(amount)||amount<1||amount>512||!Number.isFinite(cell)||cell<=0||!Number.isFinite(extent)||extent<=0)throw Error('Invalid divider movement.');
  const axis=['left','right'].includes(direction)?'horizontal':'vertical',sign=['left','up'].includes(direction)?-1:1;
  function walk(n,path,size,match){if(!n)return null;if(n.blockId)return n.blockId===id?match:null;
    const same=n.orientation.toLowerCase()===axis,nearest=same?{path,ratio:n.ratio,size}:match;
    return walk(n.first,[...path,'first'],same?size*n.ratio:size,nearest)||walk(n.second,[...path,'second'],same?size*(1-n.ratio):size,nearest);}
  const m=walk(tree,[],extent,null);if(!m)return null;const r=Math.min(.95,Math.max(.05,m.ratio+sign*amount*cell/m.size));return Math.abs(r-m.ratio)<1e-9?null:replaceRatio(tree,m.path,r);
}
export async function localAction(ctx,x,name,args){
  const s=ctx.session(),w=s?.windows.find(w=>w.id===x.windowId);if(!w)return false;
  if(['pane.focus','window.focus','client.palette','client.sidebar.toggle','client.tab.next','client.tab.previous','pane.zoom'].includes(name)){
    await present(ctx,{name,blockId:args.block_id,windowId:args.window_id,direction:args.direction},()=>ctx.state.sessionId===x.sessionId&&!ctx.state.stopped);return true;}
  if(name==='client.tab.goto'){const i=args.index;if(!Number.isSafeInteger(i)||i<1||i>s.windows.length)throw Error('Window index is out of range.');await present(ctx,{name:'window.focus',windowId:s.windows[i-1].id},()=>ctx.state.sessionId===x.sessionId);return true;}
  if(name!=='pane.resize'&&name!=='pane.balance')return false;
  if(!ctx.state.capability?.administrator||ctx.state.zoom||!w.layout)throw Error('An editable, unzoomed tiled window is required.');
  let layout;if(name==='pane.balance')layout=balanced(w.layout);else{
    const block=s.blocks.find(b=>b.id===x.blockId);if(!block||block.placement!=='Tiled')throw Error('Focus a tiled terminal.');
    const axis=['left','right'].includes(args.direction)?'width':'height',rect=x.pane.element.getBoundingClientRect(),span=document.getElementById('pane-layout').getBoundingClientRect()[axis];const count=axis==='width'?x.pane.terminal.cols:x.pane.terminal.rows;
    layout=resizePlan(w.layout,x.blockId,args.direction,args.amount??1,rect[axis]/count,span);if(!layout)return true;}
  await ctx.call('session.set_layout',{sessionId:s.id,windowId:w.id,expectedRevision:s.revision,layout});await ctx.refresh();return true;
}
