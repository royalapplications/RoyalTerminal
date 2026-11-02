// Pure conversion from the backwards-compatible flat depth order to visual frames.
import {leaves,replaceRatio} from './workspace-model.js';
import {floatFrame,deferPlacement} from './workspace-placement-commands.js';
export function floatingScenes(window,blocks){
  const groups=new Map((window?.floatingLayers??[]).map(g=>[g.id,g]));const seen=new Set(),result=[];
  for(const b of blocks){const key=b.floatingLayerId;if(!key){result.push({id:b.id,bounds:b.floatingBounds,tree:{blockId:b.id},block:b});continue;}
    if(seen.has(key))continue;const g=groups.get(key);if(!g)throw Error('Missing floating frame.');seen.add(key);result.push({id:key,bounds:g.bounds,tree:g.layout,block:b});}
  return result;
}
export function sharedTarget(window,visible){const id=window?.zoomedBlockId;return id&&visible.includes(id)?id:null;}
export function drawSplit(node,paneFor,edit,path=[]){
  if(node.blockId)return paneFor(node.blockId);
  const vertical=String(node.orientation).toLowerCase()==='vertical';
  const box=document.createElement('div'),first=document.createElement('div'),second=document.createElement('div'),bar=document.createElement('div');
  box.className=vertical?'split vertical':'split';first.className=second.className='split-child';bar.className='splitter';bar.tabIndex=edit?0:-1;bar.setAttribute('role','separator');bar.setAttribute('aria-label','Resize terminal split');bar.setAttribute('aria-orientation',vertical?'horizontal':'vertical');
  const paint=r=>{first.style.flex=`${r} 1 0`;second.style.flex=`${1-r} 1 0`;bar.setAttribute('aria-valuenow',String(Math.round(100*r)));};
  paint(node.ratio);first.append(drawSplit(node.first,paneFor,edit,[...path,'first']));second.append(drawSplit(node.second,paneFor,edit,[...path,'second']));box.append(first,bar,second);
  if(!edit)return box;
  const clamp=r=>Math.max(.05,Math.min(.95,r));bar.onpointerdown=e=>{if(e.button!==0||!e.isPrimary)return;e.preventDefault();const id=e.pointerId;const revision=edit.revision();let r=node.ratio;bar.setPointerCapture(id);bar.classList.add('dragging');
    const clean=()=>{bar.onpointermove=bar.onpointerup=bar.onpointercancel=bar.onlostpointercapture=null;bar.classList.remove('dragging');};
    bar.onpointermove=p=>{if(p.pointerId!==id)return;const b=box.getBoundingClientRect();if(!(b.width&&b.height))return;r=clamp(vertical?(p.clientY-b.top)/b.height:(p.clientX-b.left)/b.width);paint(r);};
    bar.onpointerup=p=>{if(p.pointerId!==id)return;clean();edit.commit(path,r,revision);};bar.onpointercancel=bar.onlostpointercapture=()=>{clean();paint(node.ratio);};};
  bar.onkeydown=e=>{const d=vertical?{ArrowUp:-1,ArrowDown:1}[e.key]:{ArrowLeft:-1,ArrowRight:1}[e.key];if(!d)return;e.preventDefault();edit.commit(path,clamp(node.ratio+d*.05),edit.revision());};return box;
}
export function drawScene(root,s,w,scene,paneFor,ctx,run){
  const editor=(tree,layerId)=>!ctx.state.capability?.administrator?null:{revision:()=>s.revision,commit:(path,r,rev)=>run(async()=>{try{await ctx.call(layerId?'session.set_layer_layout':'session.set_layout',{sessionId:s.id,...(layerId?{layerId}:{windowId:w.id}),expectedRevision:rev,layout:replaceRatio(tree,path,r)});}finally{await ctx.refresh();}})};
  if(scene.zoom){root.append(paneFor(scene.zoom));return;}
  if(w?.layout)root.append(drawSplit(w.layout,paneFor,editor(w.layout,null)));
  for(const frame of scene.frames){const grouped=!!frame.block.floatingLayerId;const b={...frame.block,id:frame.id,floatingBounds:frame.bounds};let commit=null;
    if(ctx.state.capability?.administrator){if(!grouped)commit=deferPlacement(ctx,s,w,b);else{commit=async(fields,rev=s.revision)=>{try{await ctx.call('session.resize_layer',{sessionId:s.id,blockId:frame.id,bounds:fields.bounds,expectedRevision:rev});}finally{await ctx.refresh();}};commit.revision=()=>s.revision;}}
    root.append(floatFrame(b,drawSplit(frame.tree,paneFor,editor(frame.tree,grouped?frame.id:null)),root,commit,run));}
}
