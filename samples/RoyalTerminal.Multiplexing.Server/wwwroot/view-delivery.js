// Validate the entire offered page before producing UI effects. A failed report must not replay a non-idempotent toggle.
const directions=new Set(['left','right','up','down','next','previous']);
const uuid=s=>typeof s==='string'&&/^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(s)&&s!=='00000000-0000-0000-0000-000000000000';
function valid(a){
  if(!a||typeof a!=='object')return false;
  switch(a.name){
    case 'pane.focus':return a.windowId==null&&((uuid(a.blockId)&&a.direction==null)||(a.blockId==null&&directions.has(a.direction)));
    case 'window.focus':return uuid(a.windowId)&&a.blockId==null&&a.direction==null;
    case 'client.tab.next':case 'client.tab.previous':case 'client.palette':case 'client.sidebar.toggle':case 'pane.zoom':return a.blockId==null&&a.windowId==null&&a.direction==null;
    default:return false;
  }
}
export async function applyViewPage(page,after,current,present,finish){
  const batches=page?.batches;
  if(!Array.isArray(batches)||batches.length>8||!Number.isSafeInteger(page.cursor)||page.cursor!==after+batches.length||
    batches.some((b,i)=>!b||b.sequence!==after+i+1||!Array.isArray(b.actions)||b.actions.length<1||b.actions.length>16||!b.actions.every(valid)))throw Error('Invalid remote UI page.');
  for(const batch of batches){
    let applied=0;
    for(const action of batch.actions){if(!current())return after;if(!await present(action))break;++applied;}
    if(!current())return after;
    if(finish)await finish(batch.sequence,applied);
    after=batch.sequence;
  }
  return after;
}
