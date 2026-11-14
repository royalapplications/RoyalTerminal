import test from 'node:test';import assert from 'node:assert/strict';
import {activityLine,deviceMessage,followActivity} from '../wwwroot/workspace-alerts.js';
test('fresh subscription skips historical events, then handles a complete final page',async()=>{
 const pages=[{nextSequence:9,events:[{sequence:9,name:'terminal.bell'}]}, {nextSequence:11,ended:true,events:[{sequence:10,name:'terminal.progress_report',progressReport:{state:'Set',progress:0}},{sequence:11,name:'terminal.desktop_notification',notification:{title:'Build',body:'private\x00information'}}]}];
 const seen=[],cursors=[];await followActivity(async after=>{cursors.push(after);return pages.shift();},()=>true,async rows=>seen.push(...rows),new AbortController().signal);
 assert.deepEqual(cursors,[undefined,9]);assert.equal(seen.length,2);assert.match(seen[0].line,/0%/);assert.match(seen[1].line,/private information/);assert.doesNotMatch(JSON.stringify(deviceMessage),/Build|private information/);
});
test('stale subscriptions and discontinuous sequences never produce an alert',async()=>{
 let current=true;let calls=0;const seen=[];const deliver=rows=>seen.push(...rows);
 await followActivity(async()=>{if(++calls===1)return {nextSequence:0,events:[]};current=false;return {ended:true,nextSequence:1,events:[{sequence:1,name:'terminal.bell'}]};},()=>current,deliver,new AbortController().signal);
 assert.deepEqual(seen,[]);calls=0;await assert.rejects(()=>followActivity(async()=> ++calls===1?{nextSequence:0,events:[]}:{nextSequence:4,events:[{sequence:4,name:'terminal.bell'}]},()=>true,deliver,new AbortController().signal),/incomplete/);assert.deepEqual(seen,[]);
 assert.equal(activityLine({name:'something-else'}),null);
});
