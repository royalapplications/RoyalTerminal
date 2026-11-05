// Capture the press synchronously. Never retain an Event, keyboard-layout map, or DOM target in an async callback.
import {keyStroke} from './keymap.js';
const oem = new Map(Object.entries({Minus:['-','_','OemMinus'],Equal:['=','+','OemPlus'],BracketLeft:['[','{','OemOpenBrackets'],BracketRight:[']','}','OemCloseBrackets'],Backslash:['\\','|','OemBackslash'],Semicolon:[';',':','OemSemicolon'],Quote:["'",'"','OemQuotes'],Backquote:['`','~','OemTilde'],Comma:[',','<','OemComma'],Period:['.','>','OemPeriod'],Slash:['/','?','Oem2']}));
const named = new Map(Object.entries({ArrowLeft:'Left',ArrowRight:'Right',ArrowUp:'Up',ArrowDown:'Down',Enter:'Return',Backspace:'Back',Space:'Space',NumpadEnter:'NumPadEnter',NumpadAdd:'Add',NumpadSubtract:'Subtract',NumpadMultiply:'Multiply',NumpadDivide:'Divide',NumpadDecimal:'Decimal'}));
function scalar(s) { return typeof s==='string' && [...s].length===1 && !/^[\u0000-\u001f\u007f\ud800-\udfff]$/u.test(s); }
export function captureKey(e, layout) {
  if (!keyStroke(e)) return null;
  const code = /^[A-Za-z][A-Za-z0-9]{0,39}$/.test(e.code || '') && e.code!=='Unidentified' ? e.code : null;
  const text=scalar(e.key)?e.key:null;
  let base = text?.toLowerCase() ?? e.key.toLowerCase();
  const mapped=code && layout?.get(code);
  if (text && scalar(mapped)) base=mapped.toLowerCase();
  else if (text && e.shiftKey && code) {
    // Without a layout map never reinterpret a non-US character just because it sits on a US digit key.
    if (oem.get(code)?.[1]===text) base=oem.get(code)[0];
    else if (/^Digit\d$/.test(code) && ')!@#$%^&*('[Number(code.at(-1))]===text) base=code.at(-1);
  }
  const logicalKey=({' ':'space','+':'plus',arrowleft:'left',arrowright:'right',arrowup:'up',arrowdown:'down'})[base] ?? base;
  const modifiers=(e.shiftKey?1:0)|(e.ctrlKey?2:0)|(e.altKey?4:0)|(e.metaKey?8:0)|(e.getModifierState?.('CapsLock')?16:0)|(e.getModifierState?.('NumLock')?32:0);
  const prefix=`${e.ctrlKey?'ctrl+':''}${e.altKey?'alt+':''}${e.shiftKey?'shift+':''}${e.metaKey?'meta+':''}`;
  let id=named.get(code)??oem.get(code)?.[2]??code;
  if (/^Key[A-Z]$/.test(code)) id=code.slice(3);
  if (/^Digit\d$/.test(code)) id='D'+code.at(-1);
  if (/^Numpad\d$/.test(code)) id='NumPad'+code.at(-1);
  return {logical:prefix+logicalKey,physical:code?prefix+'['+code.toLowerCase()+']':null,
    wire:{keyId:id??named.get(e.key)??e.key,text:text??'',unshifted:scalar(base)?base.codePointAt(0):0,modifiers,consumed:text && e.shiftKey?1:0,repeat:!!e.repeat}};
}
