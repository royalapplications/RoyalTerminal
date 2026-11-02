import test from 'node:test';import assert from 'node:assert/strict';
import {createTouchToolbar,keyBytes} from '../wwwroot/terminal-toolbar.js';
test('touch controls follow the actual ready lease, not the original capability',()=>{
  const log=[],pane={ready:false,hasControl:true,opened:true,terminal:{options:{},modes:{applicationCursorKeysMode:true},scrollPages:n=>log.push(n),scrollToBottom:()=>log.push('end')},sendInput:s=>log.push(s)};
  const el={dataset:{},setAttribute(){},replaceChildren(...children){this.children=children;}};
  const bar=createTouchToolbar(pane,{text:()=>el,button:(label,title,fn)=>({label,title,fn})});
  assert.equal(el.dataset.mode,'reading');assert.equal(pane.terminal.options.disableStdin,true);el.children[0].fn();el.children[2].fn();assert.deepEqual(log,[-1,'end']);
  pane.ready=true;bar.update();assert.equal(el.dataset.mode,'input');const up=el.children.find(b=>b.label==='↑');up.fn();assert.equal(log.at(-1),'\x1bOA');const old=el.children;bar.update();assert.equal(el.children,old);
  pane.hasControl=false;bar.update();assert.equal(el.dataset.mode,'reading');up.fn();assert.equal(log.length,3);
  assert.equal(keyBytes('↑',false),'\x1b[A');assert.equal(keyBytes('Ctrl C',true),'\x03');
});
