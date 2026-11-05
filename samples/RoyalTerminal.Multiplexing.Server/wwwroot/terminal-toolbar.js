// The local reading controls intentionally have no reference to a socket, token or input callback.
export const readingActions = Object.freeze([
  ['Page up','Scroll one page up', terminal => terminal.scrollPages(-1)],
  ['Page down','Scroll one page down', terminal => terminal.scrollPages(1)],
  ['Latest','Return to live output', terminal => terminal.scrollToBottom()]
]);
export function keyBytes(key, applicationCursor) {
  const fixed={Esc:'\x1b',Tab:'\t','Ctrl C':'\x03'};
  const suffix={'↑':'A','↓':'B','←':'D','→':'C'};
  if(Object.hasOwn(fixed,key))return fixed[key];
  if(Object.hasOwn(suffix,key))return (applicationCursor?'\x1bO':'\x1b[')+suffix[key];
  throw new TypeError('Unknown touch key');
}
export function createTouchToolbar(pane, ctx) {
  const bar=ctx.text('div','','touch-keys');bar.setAttribute('role','group');let last=null;
  // Preserve focused buttons during output and polls. Replace only on an actual lease transition.
  function update() {
    const write=!!(pane.ready&&pane.hasControl&&!pane.closed&&!pane.disposed&&!pane.exited);
    if(last===write)return;last=write;pane.terminal.options.disableStdin=!write;
    bar.dataset.mode=write?'input':'reading';bar.setAttribute('aria-label',write?'Terminal keys':'Read terminal history');
    const controls=write?['Esc','Tab','Ctrl C','↑','↓','←','→'].map(key=>ctx.button(key,`Send ${key}`,()=>{
      if(pane.ready&&pane.hasControl&&!pane.closed)pane.sendInput(keyBytes(key,!!pane.terminal.modes.applicationCursorKeysMode));
    })):readingActions.map(([label,description,perform])=>ctx.button(label,description,()=>{if(!pane.disposed&&pane.opened)perform(pane.terminal);}));
    bar.replaceChildren(...controls);
  }
  update();return {element:bar,update};
}
