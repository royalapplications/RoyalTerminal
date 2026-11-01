import test from 'node:test';
import assert from 'node:assert/strict';
import {captureKey} from '../wwwroot/workspace-key-event.js';
import {Keymap} from '../wwwroot/keymap.js';
const press=(m,e,layout)=>{const k=captureKey(e,layout);return m.process(k.logical,k.physical);};
test('physical and logical aliases are tried in the same mode, not as two key presses',()=>{
  const m=new Keymap({modes:[{name:'edit',exclusive:true}],bindings:[{sequence:'ctrl+[keya]',actionId:'physical'},{sequence:'ctrl+q',actionId:'logical'},{sequence:'enter',enterMode:'edit',once:true},{mode:'edit',sequence:'ctrl+q>[slash]',actionId:'chord'}]});
  const q={key:'q',code:'KeyA',ctrlKey:true};assert.equal(press(m,q).actionId,'physical');m.process('enter');assert.equal(press(m,q).pending,'ctrl+q');assert.equal(press(m,{key:':',code:'Slash'}).actionId,'chord');assert.equal(m.mode,'default');
});
test('captured physical key, generated text and unshifted scalar stay distinct on an international layout',()=>{
  const k=captureKey({key:'Ö',code:'Semicolon',shiftKey:true},new Map([['Semicolon','ö']]));assert.equal(k.physical,'shift+[semicolon]');assert.equal(k.logical,'shift+ö');assert.deepEqual(k.wire,{keyId:'OemSemicolon',text:'Ö',unshifted:246,modifiers:1,consumed:1,repeat:false});
  assert.equal(captureKey({key:'É',code:'Digit2',shiftKey:true}).logical,'shift+é');assert.equal(captureKey({key:'%',code:'Digit5',shiftKey:true}).logical,'shift+5');
});
test('composition, dead keys and AltGraph never consume a key mode or command prefix',()=>{
  for(const e of [{key:'Process'},{key:'Dead'},{key:'a',keyCode:229},{key:'@',getModifierState:k=>k==='AltGraph'}])assert.equal(captureKey(e),null);
});
