import {test} from 'node:test';import assert from 'node:assert/strict';import {createAttentionFeed} from '../wwwroot/shell-navigation.js';
test('unvisited sessions are sampled fairly without any parallel client requests',async()=>{
 let t=0;const calls=[],releases=[];const feed=createAttentionFeed(id=>{calls.push(id);return new Promise(r=>releases.push(()=>r({sessionId:id,layoutRevision:1})));},()=> 'a',()=>{},()=>t);feed.trim(new Set(['a','b','c']));const active={id:'a',revision:1};
 async function poll(){const pending=feed.sample(active);await feed.sample(active);releases.shift()?.();await pending;}
 await poll();t=2500;await poll();t=5000;await poll();t=7500;await poll();t=10000;await poll();assert.deepEqual(calls,['a','b','c','a']);
 t=35000;const p=feed.sample(active);releases.shift()();await p;t=35500;const q=feed.sample(active);feed.trim(new Set(['a','c']));releases.shift()();await q;assert.equal(feed.summaries.has('b'),false);
});
