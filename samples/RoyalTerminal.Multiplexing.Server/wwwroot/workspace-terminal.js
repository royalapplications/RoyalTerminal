import {TerminalPane as RecoveredPane} from './workspace-recovered-pane.js';
import {HistoryDialog} from './workspace-history.js';
export class TerminalPane extends RecoveredPane {
  constructor(block,ctx){super(block,ctx);const s=ctx.state;this.remoteObserver=s.remoteNavigation===true||!!s.remoteView&&s.remoteView.session===s.sessionId&&s.remoteView.generation===s.generation;
    this.history=new HistoryDialog();const button=document.createElement('button');button.type='button';button.hidden=true;button.textContent='History';button.title='Read and search recovered scrollback (Shift+Page Up)';button.setAttribute('aria-label','Read recovered scrollback');this.historyButton=button;this.element.querySelector('.pane-header').append(button);button.onclick=()=>void this.readHistory();
    this.element.addEventListener('keydown',e=>{if(this.replica&&e.shiftKey&&e.key==='PageUp'&&!e.ctrlKey&&!e.metaKey){e.preventDefault();e.stopImmediatePropagation();void this.readHistory();}},true);
  }
  connect(observer=false){super.connect(observer||this.remoteObserver);}
  async reconnectController(){this.remoteObserver=false;await super.reconnectController();}
  async recover(c){await super.recover(c);if(this.replica&&!this.disposed)this.historyButton.hidden=false;}
  async readHistory(){const r=this.replica;if(!r||this.disposed||this.history.open)return;try{await this.history.show(this.element,()=>r.call('history'),()=>this.replica===r&&!this.disposed);}catch(e){if(!this.disposed)this.ctx.toast(e.message);}}
  fail(c,message){this.history?.clear();super.fail(c,message);}
  async detach(){this.history?.clear();await super.detach();}
  dispose(){this.history?.clear();super.dispose();}
}
