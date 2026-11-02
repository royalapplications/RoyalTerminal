import test from 'node:test';import assert from 'node:assert/strict';import {isPaletteShortcut} from '../wwwroot/shell-palette.js';
test('Rex palette shortcut is a fallback; configured, composing or modified keystrokes remain untouched',()=>{
 const e={key:'P',ctrlKey:false,metaKey:true,shiftKey:true,altKey:false,isComposing:false,defaultPrevented:false};
 for(const change of [{},{metaKey:false,ctrlKey:true}])assert.equal(isPaletteShortcut({...e,...change}),true);
 for(const change of [{defaultPrevented:true},{isComposing:true},{altKey:true},{shiftKey:false},{ctrlKey:true},{metaKey:false},{key:'v'}])assert.equal(isPaletteShortcut({...e,...change}),false);
});
