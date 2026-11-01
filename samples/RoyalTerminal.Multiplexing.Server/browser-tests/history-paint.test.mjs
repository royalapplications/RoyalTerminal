import test from 'node:test';
import assert from 'node:assert/strict';
import {pagesForHistory} from '../wwwroot/workspace-history-paint.js';
const run=[0xff1122,0,1,2,0x123456,false,'<img onerror=run()>'];
test('page boundaries cover each row once and stay within both DOM limits',()=>{
  const rows=Array.from({length:92},(_,i)=>Array(i<10?480:2).fill(run));
  const pages=pagesForHistory({background:0,rows});let end=0;
  for(const [a,b] of pages){assert.equal(a,end);assert.ok(b>a&&b-a<=48);assert.ok(rows.slice(a,b).reduce((n,r)=>n+r.length,0)<=2048);end=b;}
  assert.equal(end,rows.length);
});
test('styles cannot contain CSS, URLs, oversized text or extra slots',()=>{
  for(const bad of [['url(https://invalid)',...run.slice(1)], [...run,'url'],[0,0,0,6,-1,false,'a'],[0,0,0,0,-1,true,'x'.repeat(4097)]])assert.throws(()=>pagesForHistory({background:0,rows:[[bad]]}));
  assert.throws(()=>pagesForHistory({background:0,rows:[Array(513).fill(run)]}));
  assert.deepEqual(pagesForHistory({background:0,rows:[[run]]}),[[0,1]]);
});
