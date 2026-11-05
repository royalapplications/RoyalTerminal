// Coordinates are integral ten-thousandths, not device pixels. Keep policy pure for Node and DOM tests.
const UNIT=10000, MIN_SIZE=500;
const clamp=(x,lo,hi)=>Math.max(lo,Math.min(hi,x));
export function windowPlanes(session,window){const blocks=(session?.blocks||[]).filter(b=>b.windowId===window?.id);return {floats:blocks.filter(b=>b.placement==='Floating'),shelf:blocks.filter(b=>b.placement==='Unplaced')};}
export function dragBounds(original,dx,dy,resize){
  const dX=Math.round(dx),dY=Math.round(dy);
  if(!Number.isFinite(dX)||!Number.isFinite(dY))throw Error('Invalid drag delta');
  if(resize)return {...original,width:clamp(original.width+dX,Math.min(MIN_SIZE,UNIT-original.x),UNIT-original.x),height:clamp(original.height+dY,Math.min(MIN_SIZE,UNIT-original.y),UNIT-original.y)};
  return {...original,x:clamp(original.x+dX,0,UNIT-original.width),y:clamp(original.y+dY,0,UNIT-original.height)};
}
if(typeof document!=='undefined'){const link=document.createElement('link');link.rel='stylesheet';link.href=new URL('./workspace-placements.css',import.meta.url).href;document.head.append(link);}

export function deferPlacement(ctx,session,window,block){
  // The IDs bind to this specific pane, not whatever happens to be focused at pointer-up.
  const revision=()=>ctx.session()?.id===session.id?ctx.session().revision:session.revision;
  const commit=async(fields,expectedRevision=revision())=>{if(ctx.state.stopped||ctx.session()?.id!==session.id)throw Error('Workspace changed during edit.');try{await ctx.call('block.place',{sessionId:session.id,blockId:block.id,windowId:window.id,...fields,expectedRevision});}finally{await ctx.refresh();}};
  commit.revision=revision;return commit;
}
export function floatFrame(block,pane,root,commit,run){
  const frame=document.createElement('div');frame.className='floating-frame';frame.dataset.floatId=block.id;
  const rect=block.floatingBounds||{x:1500,y:1500,width:7000,height:7000};
  const paint=r=>Object.assign(frame.style,{left:`${r.x/100}%`,top:`${r.y/100}%`,width:`${r.width/100}%`,height:`${r.height/100}%`});paint(rect);
  frame.append(pane);
  if(!commit)return frame;
  for(const resize of [false,true]){
    const handle=document.createElement('button');handle.type='button';handle.className=resize?'float-size':'float-move';handle.textContent=resize?'◢':'⠿';
    handle.setAttribute('aria-label',`${resize?'Resize':'Move'} floating terminal ${block.label}`);handle.title='Drag or use arrow keys; hold Shift for a larger step.';frame.append(handle);
    handle.onpointerdown=e=>{
      if(e.button!==0||!e.isPrimary)return;e.preventDefault();const id=e.pointerId,rev=commit.revision(),r=root.getBoundingClientRect(),sx=e.clientX,sy=e.clientY;if(!r.width||!r.height)return;
      let preview=rect,changed=false;handle.setPointerCapture(id);
      const reset=()=>{handle.onpointermove=handle.onpointerup=handle.onpointercancel=handle.onlostpointercapture=null;};
      handle.onpointermove=p=>{if(p.pointerId!==id)return;preview=dragBounds(rect,(p.clientX-sx)*UNIT/r.width,(p.clientY-sy)*UNIT/r.height,resize);changed=true;paint(preview);};
      handle.onpointerup=p=>{if(p.pointerId!==id)return;reset();if(changed)void run(()=>commit({kind:'floating',bounds:preview},rev).catch(err=>{paint(rect);throw err;}));};
      handle.onlostpointercapture=handle.onpointercancel=()=>{reset();paint(rect);};
    };
    handle.onkeydown=e=>{const delta={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[e.key];if(!delta)return;e.preventDefault();const step=e.shiftKey?500:100;void run(()=>commit({kind:'floating',bounds:dragBounds(rect,delta[0]*step,delta[1]*step,resize)}));};
  }
  return frame;
}

export function placementActions(ctx){
  const snapshot=()=>{const s=ctx.session(),w=ctx.window(),b=s?.blocks.find(b=>b.id===ctx.state.focused);return {s,w,b};};
  const choices=items=>items.map(i=>[i.id,`${i.label} · ${i.id.slice(0,8)}`]);
  const edit=async(fn)=>{try{return await fn();}finally{await ctx.refresh();}};
  const asBlock=(title,run)=>({title,hint:'Placement',admin:true,block:true,run:async()=>{const {s,w,b}=snapshot();if(b&&s&&w)await run(s,w,b);}});
  const asWindow=(title,run)=>({title,hint:'Windows',admin:true,session:true,run:async()=>{const {s,w}=snapshot();if(s&&w)await run(s,w);}});
  return [
    asBlock('Float focused terminal',(s,w,b)=>deferPlacement(ctx,s,w,b)({kind:'floating'})),
    asBlock('Tile focused terminal',(s,w,b)=>deferPlacement(ctx,s,w,b)({kind:'tiled'})),
    asBlock('Unplace focused terminal',(s,w,b)=>deferPlacement(ctx,s,w,b)({kind:'unplaced'})),
    asBlock('Move focused terminal to window',async(s,w,b)=>{const f=await ctx.dialog('Move terminal','The shell and its active connections continue running.',[{name:'windowId',label:'Destination window',options:choices(s.windows),value:w.id},{name:'kind',label:'Placement',options:[['tiled','Tile'],['floating','Float'],['unplaced','Unplaced']]}],'Move');if(f)await deferPlacement(ctx,s,w,b)(f,s.revision);}),
    asBlock('Swap focused terminal with another',async(s,w,b)=>{const others=s.blocks.filter(i=>i.id!==b.id);if(!others.length)return ctx.toast('No other terminal in this workspace.');const f=await ctx.dialog('Swap terminals','Exchange placements, not process identities.',[{name:'other',label:'Other terminal',options:choices(others)}],'Swap');if(f)await edit(()=>ctx.call('block.swap',{sessionId:s.id,firstId:b.id,secondId:f.other,expectedRevision:s.revision}));}),
    asWindow('Reorder current window',async(s,w)=>{const f=await ctx.dialog('Reorder window','Choose its new position.',[{name:'position',label:'Position',value:String(s.windows.findIndex(x=>x.id===w.id)+1),options:s.windows.map((_,i)=>[String(i+1),String(i+1)])}],'Reorder');if(f)await edit(()=>ctx.call('session.move_window',{sessionId:s.id,windowId:w.id,position:Number(f.position),expectedRevision:s.revision}));}),
    asWindow('Close window and keep terminals',async(s,w)=>{const others=s.windows.filter(i=>i.id!==w.id);if(!others.length)return ctx.toast('The last window cannot be removed.');const f=await ctx.dialog('Close window','All its terminals remain running and appear on the destination window’s unplaced shelf.',[{name:'preserveIn',label:'Destination window',options:choices(others)}],'Close window');if(f)await edit(async()=>{await ctx.call('session.close_window',{sessionId:s.id,windowId:w.id,preserveIn:f.preserveIn,expectedRevision:s.revision});if(ctx.state.sessionId===s.id)ctx.state.windowId=f.preserveIn;});}),
  ];
}
