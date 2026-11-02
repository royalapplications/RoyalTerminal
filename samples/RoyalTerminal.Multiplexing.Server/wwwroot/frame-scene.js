import {replaceRatio} from './workspace-model.js';
import {floatFrame,deferPlacement} from './workspace-placement-commands.js';

// A legacy snapshot has one block per frame. New snapshots still expose the flat depth order.
export function floatingScenes(window,blocks){
  const groups=new Map((window?.floatingLayers??[]).map(group=>[group.id,group]));
  const seen=new Set(),result=[];
  for(const block of blocks){
    const key=block.floatingLayerId;
    if(!key){result.push({id:block.id,bounds:block.floatingBounds,tree:{blockId:block.id},block});continue;}
    if(seen.has(key))continue;
    const group=groups.get(key);if(!group)throw Error('Missing floating frame.');
    seen.add(key);result.push({id:key,bounds:group.bounds,tree:group.layout,block});
  }
  return result;
}
export function sharedTarget(window,visible){const id=window?.zoomedBlockId;return id&&visible.includes(id)?id:null;}
export function drawSplit(node,paneFor,edit,path=[]){
  if(node.blockId)return paneFor(node.blockId);
  const vertical=String(node.orientation).toLowerCase()==='vertical';
  const box=document.createElement('div'),first=document.createElement('div'),second=document.createElement('div'),bar=document.createElement('div');
  box.className=vertical?'split vertical':'split';first.className=second.className='split-child';bar.className='splitter';bar.tabIndex=edit?0:-1;
  bar.setAttribute('role','separator');bar.setAttribute('aria-label','Resize terminal split');bar.setAttribute('aria-orientation',vertical?'horizontal':'vertical');
  const paint=r=>{first.style.flex=`${r} 1 0`;second.style.flex=`${1-r} 1 0`;bar.setAttribute('aria-valuenow',String(Math.round(100*r)));};
  paint(node.ratio);first.append(drawSplit(node.first,paneFor,edit,[...path,'first']));second.append(drawSplit(node.second,paneFor,edit,[...path,'second']));box.append(first,bar,second);
  if(!edit)return box;
  const clamp=r=>Math.max(.05,Math.min(.95,r));
  bar.onpointerdown=e=>{
    if(e.button!==0||!e.isPrimary)return;e.preventDefault();const id=e.pointerId,revision=edit.revision();let r=node.ratio;
    bar.setPointerCapture(id);bar.classList.add('dragging');
    const clean=()=>{bar.onpointermove=bar.onpointerup=bar.onpointercancel=bar.onlostpointercapture=null;bar.classList.remove('dragging');};
    bar.onpointermove=p=>{if(p.pointerId!==id)return;const bounds=box.getBoundingClientRect();if(!bounds.width||!bounds.height)return;r=clamp(vertical?(p.clientY-bounds.top)/bounds.height:(p.clientX-bounds.left)/bounds.width);paint(r);};
    bar.onpointerup=p=>{if(p.pointerId!==id)return;clean();if(r!==node.ratio)edit.commit(path,r,revision);};
    bar.onpointercancel=bar.onlostpointercapture=()=>{clean();paint(node.ratio);};
  };
  bar.onkeydown=e=>{const d=vertical?{ArrowUp:-1,ArrowDown:1}[e.key]:{ArrowLeft:-1,ArrowRight:1}[e.key];if(!d)return;e.preventDefault();edit.commit(path,clamp(node.ratio+d*.05),edit.revision());};
  return box;
}
export function drawScene(root,s,w,scene,paneFor,ctx,run){
  if(scene.zoom){root.append(paneFor(scene.zoom));return;}
  if(!s||!w)return;
  // Capture the revision when the gesture starts, not when this DOM was built. Label changes can
  // advance it without rebuilding geometry; a change after pointer-down still rejects the edit.
  const revision=()=>ctx.session()?.id===s.id?ctx.session().revision:s.revision;
  const send=async(method,fields,rev)=>{
    if(ctx.state.stopped||ctx.session()?.id!==s.id||ctx.window()?.id!==w.id)throw Error('Workspace changed during edit.');
    try{await ctx.call(method,{sessionId:s.id,expectedRevision:rev,...fields});}finally{await ctx.refresh();}
  };
  const editor=(tree,layerId)=>!ctx.state.capability?.administrator?null:{revision,commit:(path,r,rev)=>run(()=>send(layerId?'session.set_layer_layout':'session.set_layout',{...(layerId?{layerId}:{windowId:w.id}),layout:replaceRatio(tree,path,r)},rev))};
  if(w.layout)root.append(drawSplit(w.layout,paneFor,editor(w.layout,null)));
  for(const frame of scene.frames){
    const grouped=!!frame.block.floatingLayerId,block={...frame.block,id:frame.id,floatingBounds:frame.bounds};let commit=null;
    if(ctx.state.capability?.administrator){
      if(!grouped)commit=deferPlacement(ctx,s,w,block);
      else{commit=(fields,rev=revision())=>send('session.resize_layer',{blockId:frame.id,bounds:fields.bounds},rev);commit.revision=revision;}
    }
    root.append(floatFrame(block,drawSplit(frame.tree,paneFor,editor(frame.tree,grouped?frame.id:null)),root,commit,run));
  }
}
