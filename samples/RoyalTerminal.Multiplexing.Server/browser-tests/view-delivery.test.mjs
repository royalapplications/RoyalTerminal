import test from 'node:test';
import assert from 'node:assert/strict';
import {applyViewPage} from '../wwwroot/view-delivery.js';
const a={name:'pane.zoom'}, b={name:'client.tab.next'};
const page=(...lists)=>({cursor:lists.length,batches:lists.map((actions,i)=>({sequence:i+1,actions}))});
test('report only the applied prefix, then process the next independent batch',async()=>{
 const seen=[],reports=[];
 assert.equal(await applyViewPage(page([a,b,a],[a]),0,()=>true,x=>{seen.push(x.name);return x!==b;},(seq,n)=>reports.push([seq,n])),2);
 assert.deepEqual(seen,['pane.zoom','client.tab.next','pane.zoom']);assert.deepEqual(reports,[[1,1],[2,1]]);
});
test('validate the complete offer before presenting even one action',async()=>{
 for(const bad of [page([a],[{name:'block.input'}]),page([{name:'pane.zoom',blockId:'injected'}]),{cursor:2,batches:[{sequence:1,actions:[a]}]}]){
 let effects=0;await assert.rejects(applyViewPage(bad,0,()=>true,()=>++effects,null));assert.equal(effects,0);}
});
test('withdrawal during a callback reports nothing and applies nothing else',async()=>{
 let active=true,calls=0,reports=0;const cursor=await applyViewPage(page([a,b]),0,()=>active,()=>{++calls;active=false;return true;},()=>++reports);
 assert.deepEqual([cursor,calls,reports],[0,1,0]);
});
test('failed result submission is not a reason to retry a visible toggle',async()=>{
 let n=0;await assert.rejects(applyViewPage(page([a],[b]),0,()=>true,()=>++n,()=>{throw Error('disconnected');}));assert.equal(n,1);
});
