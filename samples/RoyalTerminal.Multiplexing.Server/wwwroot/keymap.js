/** Browser event-loop implementation of the documented MultiplexerKeyEngine rules. */
export class Keymap {
  constructor(configuration, now=()=>performance.now()) {
    this.now=now;this.stack=[{name:'default',once:false}];this.pending=null;
    this.modes=new Map([['default',{exclusive:false,bindings:new Map(),prefixes:new Set()}]]);
    for(const mode of configuration.modes||[])this.modes.set(mode.name,{exclusive:!!mode.exclusive,bindings:new Map(),prefixes:new Set()});
    for(const binding of configuration.bindings||[]){const mode=this.modes.get(binding.mode||'default');if(!mode)throw new Error('Undefined key mode.');mode.bindings.set(binding.sequence,binding);const parts=binding.sequence.split('>');for(let i=1;i<parts.length;i++)mode.prefixes.add(parts.slice(0,i).join('>'));}
  }
  get mode(){return this.stack.at(-1).name;}
  process(key) {
    if(this.pending&&this.now()-this.pending.at>=1500)this.pending=null;
    if(this.pending){const {sequence,frame}=this.pending;const mode=this.modes.get(frame.name);
      if(key==='escape'){this.pending=null;return {consumed:true};}
      const match=this.match(mode,frame,`${sequence}>${key}`);if(match)return match;
      this.pending=null;if(mode.exclusive)return {consumed:true};
    }
    for(let i=this.stack.length-1;i>=0;i--){const frame=this.stack[i],mode=this.modes.get(frame.name);const match=this.match(mode,frame,key);if(match)return match;
      if(key==='escape'&&i===this.stack.length-1&&i>0){this.stack.pop();return {consumed:true};}
      if(mode.exclusive)return {consumed:true};
    }
    return {consumed:false};
  }
  match(mode,frame,sequence){
    const binding=mode.bindings.get(sequence);
    if(binding){this.pending=null;
      if(binding.exitAllModes){this.stack.splice(1);return {consumed:true};}
      if(binding.exitMode){if(this.stack.length>1)this.stack.pop();return {consumed:true};}
      if(frame.once){const index=this.stack.indexOf(frame);if(index>0)this.stack.splice(index,1);}
      if(binding.enterMode){if(this.stack.length>=16)throw new Error('Key mode stack limit reached.');this.stack.push({name:binding.enterMode,once:binding.once});return {consumed:true};}
      return {consumed:true,actionId:binding.actionId};
    }
    if(mode.prefixes.has(sequence)){this.pending={sequence,frame,at:this.now()};return {consumed:true,pending:sequence};}
    return null;
  }
}
export function keyStroke(event){
  if(['Control','Alt','Shift','Meta','Dead','Process'].includes(event.key)||event.isComposing)return null;
  const key=event.key===' '?'space':event.key==='+'?'plus':event.key.toLowerCase();
  return `${event.ctrlKey?'ctrl+':''}${event.altKey?'alt+':''}${event.shiftKey?'shift+':''}${event.metaKey?'meta+':''}${key}`;
}
