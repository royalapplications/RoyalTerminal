import test from 'node:test';
import assert from 'node:assert/strict';
import {PaneInputLane} from '../wwwroot/workspace-input-lane.js';
const gate=()=>{let release;const promise=new Promise(r=>{release=r;});return {promise,release};};
test('declined key precedes ordinary typing and binary input; later callback cannot overtake it',async()=>{
 const sent=[],g=gate(),lane=new PaneInputLane(()=>true,f=>sent.push(f.data),assert.fail);
 lane.write({data:'immediate'});const first=lane.enqueue(async()=>{await g.promise;return {data:'key'};});lane.write({data:'paste'});const last=lane.enqueue(()=>({data:'next'}));
 assert.deepEqual(sent,['immediate']);g.release();await Promise.all([first,last]);assert.deepEqual(sent,['immediate','key','paste','next']);assert.equal(lane.count,0);
});
test('connection loss suppresses the complete queued suffix; errors do not poison the next valid operation',async()=>{
 let ok=true;const sent=[],g=gate(),lane=new PaneInputLane(()=>ok,f=>sent.push(f),assert.fail);const first=lane.enqueue(async()=>{await g.promise;return {data:'old lease'};});await Promise.resolve();lane.write({data:'other'});ok=false;g.release();await first;await lane.tail;assert.deepEqual(sent,[]);
 ok=true;const fails=lane.enqueue(()=>{throw Error('declined');});lane.write({data:'new'});await assert.rejects(fails);await lane.tail;assert.deepEqual(sent,[{data:'new'}]);
});
test('bounded lane rejects excess input without truncation or a retry',async()=>{const g=gate(),seen=[],lane=new PaneInputLane(()=>true,()=>{},err=>seen.push(err.message));const held=lane.enqueue(()=>g.promise);lane.write({data:'x'.repeat(256*1024+1)});await Promise.resolve();assert.equal(seen.length,1);g.release(null);await held;assert.equal(lane.bytes,0);});
