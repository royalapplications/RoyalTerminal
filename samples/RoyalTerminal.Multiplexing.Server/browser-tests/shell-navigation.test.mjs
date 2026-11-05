import test from 'node:test';
import assert from 'node:assert/strict';
import {attentionBadge,createAttentionFeed} from '../wwwroot/shell-navigation.js';

test('attention does not report unknown or idle as a task; blocked outranks working',()=>{
  assert.equal(attentionBadge(null),null);assert.equal(attentionBadge({idle:5}),null);
  assert.deepEqual(attentionBadge({working:4,permission:2}),{key:'permission',symbol:'?',label:'2 awaiting permission'});
});
test('old attention response cannot publish across disconnect; a new connection is not blocked',async()=>{
  const resolves=[],renders=[];let selected='a';const feed=createAttentionFeed(()=>new Promise(r=>resolves.push(r)),()=>selected,()=>renders.push(selected));
  const old=feed.sample({id:'a',revision:2});await feed.sample({id:'a',revision:2});assert.equal(resolves.length,1);
  feed.reset();selected='b';const fresh=feed.sample({id:'b',revision:3});
  resolves[0]({sessionId:'a',layoutRevision:2});await old;assert.equal(feed.summaries.size,0);
  resolves[1]({sessionId:'b',layoutRevision:3});await fresh;assert.deepEqual(renders,['b']);assert.deepEqual([...feed.summaries.keys()],['b']);
  await feed.sample({id:'b',revision:3});assert.equal(resolves.length,2);feed.trim(new Set());assert.equal(feed.summaries.size,0);
});
