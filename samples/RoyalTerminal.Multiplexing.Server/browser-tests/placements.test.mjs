import test from 'node:test';
import assert from 'node:assert/strict';
import {dragBounds,windowPlanes,deferPlacement} from '../wwwroot/workspace-placements.js';

test('drag and resize clamp instead of overflowing the normalized plane',()=>{
  const r={x:9999,y:6000,width:1,height:2000};
  assert.deepEqual(dragBounds(r,500,8000,false),{x:9999,y:8000,width:1,height:2000});
  assert.deepEqual(dragBounds(r,-500,-8000,true),{x:9999,y:6000,width:1,height:500});
  assert.deepEqual(dragBounds(r,1e9,1e9,true),{x:9999,y:6000,width:1,height:4000});
  assert.throws(()=>dragBounds(r,NaN,0,false));
});
test('only floating and unplaced blocks of the selected window enter those planes',()=>{
  const a={windowId:'a',placement:'Floating'},b={windowId:'a',placement:'Unplaced'};
  assert.deepEqual(windowPlanes({blocks:[a,b,{...a,windowId:'b'},{windowId:'a',placement:'Tiled'}]},{id:'a'}),{floats:[a],shelf:[b]});
});
test('a stale drag never borrows the new selection or retries a conflicting edit',async()=>{
  const calls=[];let session={id:'s',revision:7};let refreshes=0;
  const ctx={state:{stopped:false},session:()=>session,call:async(...args)=>{calls.push(args);throw Error('conflict');},refresh:async()=>{refreshes++;}};
  const commit=deferPlacement(ctx,session,{id:'w'},{id:'b'}),rev=commit.revision();session={id:'s',revision:9};
  await assert.rejects(commit({kind:'floating'},rev),/conflict/);
  assert.deepEqual(calls,[['block.place',{sessionId:'s',blockId:'b',windowId:'w',kind:'floating',expectedRevision:7}]]);assert.equal(refreshes,1);
  session={id:'other',revision:1};await assert.rejects(commit({kind:'tiled'},rev),/changed/);assert.equal(calls.length,1);
});
