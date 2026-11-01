// State belongs to one client and one compiled keymap generation.
export class Keymap {
  constructor(configuration, now = () => performance.now()) {
    this.now = now;
    this.stack = [{name:'default', once:false}];
    this.pending = null;
    this.modes = new Map();
    const mode = exclusive => ({exclusive, bindings:new Map(), prefixes:new Set()});
    this.modes.set('default',mode(false));
    for (const m of configuration.modes || []) this.modes.set(m.name,mode(!!m.exclusive));
    for (const b of configuration.bindings || []) {
      const m = this.modes.get(b.mode || 'default');
      if (!m) throw Error('Undefined key mode.');
      m.bindings.set(b.sequence,b);
      const parts = b.sequence.split('>');
      for (let i=1;i<parts.length;i++) m.prefixes.add(parts.slice(0,i).join('>'));
    }
  }
  get mode() { return this.stack.at(-1).name; }
  // A DOM event has two identities. Probe both in the same frame BEFORE consuming or
  // popping anything. An explicit physical binding/prefix wins over its logical alias.
  process(logical, physical = null) {
    if (this.pending && this.now()-this.pending.at >= 1500) this.pending = null;
    const tryFrame = (frame,prefix) => {
      const m = this.modes.get(frame.name);
      for (const key of physical ? [physical,logical] : [logical]) {
        if (!key) continue;
        const sequence = prefix ? `${prefix}>${key}` : key;
        const b = m.bindings.get(sequence);
        if (b) return this.dispatch(frame,b);
        if (m.prefixes.has(sequence)) {
          this.pending = {sequence,frame,at:this.now()};
          return {consumed:true,pending:sequence};
        }
      }
      return null;
    };
    if (this.pending) {
      const {sequence,frame} = this.pending;
      this.pending = null;
      if (logical === 'escape') return {consumed:true};
      const result = tryFrame(frame,sequence);
      if (result) return result;
      if (this.modes.get(frame.name).exclusive) return {consumed:true};
    }
    for (let i=this.stack.length-1;i>=0;i--) {
      const frame = this.stack[i], result = tryFrame(frame,'');
      if (result) return result;
      if (logical === 'escape' && i === this.stack.length-1 && i>0) { this.stack.pop(); return {consumed:true}; }
      if (this.modes.get(frame.name).exclusive) return {consumed:true};
    }
    return {consumed:false};
  }
  dispatch(frame,b) {
    this.pending = null;
    if (b.exitAllModes) { this.stack.splice(1); return {consumed:true}; }
    if (b.exitMode) { if (this.stack.length>1) this.stack.pop(); return {consumed:true}; }
    if (frame.once) { const i=this.stack.indexOf(frame); if(i>0) this.stack.splice(i,1); }
    if (b.enterMode) {
      if (this.stack.length>=16) throw Error('Key mode stack limit reached.');
      this.stack.push({name:b.enterMode,once:b.once}); return {consumed:true};
    }
    return {consumed:true,actionId:b.actionId};
  }
}
export function keyStroke(e) {
  if (!e.key || e.isComposing || e.keyCode===229 || e.getModifierState?.('AltGraph') || ['Control','Alt','Shift','Meta','AltGraph','Dead','Process','Unidentified'].includes(e.key)) return null;
  const key = e.key === ' ' ? 'space' : e.key === '+' ? 'plus' : e.key.toLowerCase();
  return `${e.ctrlKey?'ctrl+':''}${e.altKey?'alt+':''}${e.shiftKey?'shift+':''}${e.metaKey?'meta+':''}${key}`;
}
