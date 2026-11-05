import {TerminalPane as RecoveredPane} from './workspace-recovered-pane.js';
import {HistoryDialog} from './workspace-history.js';
import {PaneInputLane} from './workspace-input-lane.js';
export class TerminalPane extends RecoveredPane {
  constructor(block,ctx){super(block,ctx);const s=ctx.state;this.remoteObserver=s.remoteNavigation===true||!!s.remoteView&&s.remoteView.session===s.sessionId&&s.remoteView.generation===s.generation;
    this.history=new HistoryDialog();const button=document.createElement('button');button.type='button';button.hidden=true;button.textContent='History';button.title='Read and search recovered scrollback (Shift+Page Up)';button.setAttribute('aria-label','Read recovered scrollback');this.historyButton=button;this.element.querySelector('.pane-header').append(button);button.onclick=()=>void this.readHistory();
    this.element.addEventListener('keydown',e=>{if(this.replica&&e.shiftKey&&e.key==='PageUp'&&!e.ctrlKey&&!e.metaKey){e.preventDefault();e.stopImmediatePropagation();void this.readHistory();}},true);
    // Only forward focus on the terminal textarea, not clicks on pane chrome. Parser replies remain suppressed.
    for(const [event,sequence] of [['focusin','\x1b[I'],['focusout','\x1b[O']])this.host.addEventListener(event,e=>{
      if(e.target===this.terminal.textarea&&this.terminal.modes.sendFocusMode)this.sendInput(sequence);
    });
  }
  inputLane(){const c=this.connection;if(!c||!this.ready||!this.hasControl||this.closed||this.disposed)return null;
    return c.inputLane??=new PaneInputLane(()=>this.connection===c&&this.ready&&this.hasControl&&!this.closed&&!this.disposed,
      f=>super.send(f),e=>this.ctx.toast(e.message));}
  send(frame){if(frame.type==='resize'&&this.ctx.state.sharedZoom)return;if(frame.type!=='input'){super.send(frame);return;}this.inputLane()?.write(frame);}
  connect(observer=false){super.connect(observer||this.remoteObserver);}
  async reconnectController(){this.remoteObserver=false;await super.reconnectController();}
  async recover(c){await super.recover(c);if(this.replica&&!this.disposed)this.historyButton.hidden=false;}
  async readHistory(){const r=this.replica;if(!r||this.disposed||this.history.open)return;try{await this.history.show(this.element,()=>r.call('history'),()=>this.replica===r&&!this.disposed);}catch(e){if(!this.disposed)this.ctx.toast(e.message);}}
  fail(c,message){this.history?.clear();super.fail(c,message);}
  async detach(){this.history?.clear();await super.detach();}
  dispose(){this.history?.clear();super.dispose();}
}
