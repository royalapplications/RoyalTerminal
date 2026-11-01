import { TerminalPane as RecoveredPane } from './workspace-recovered-pane.js';
import { HistoryDialog } from './workspace-history.js';

// Normal replay stays entirely in xterm. Only a managed-recovered pane needs this adapter.
export class TerminalPane extends RecoveredPane {
  constructor(block,ctx){
    super(block,ctx);
    this.remoteObserver=ctx.state.remoteNavigation===true;
    this.history = new HistoryDialog();
    const button=document.createElement('button');button.type='button';button.hidden=true;
    button.textContent='History';button.title='Read and search recovered scrollback (Shift+Page Up)';
    button.setAttribute('aria-label','Read recovered scrollback');this.historyButton=button;
    this.element.querySelector('.pane-header').append(button);
    button.onclick=()=>void this.readHistory();
    this.element.addEventListener('keydown',e=>{
      if(this.replica && e.shiftKey && e.key==='PageUp' && !e.ctrlKey && !e.metaKey){e.preventDefault();e.stopImmediatePropagation();void this.readHistory();}
    },true);
  }
  connect(observer=false){super.connect(observer || this.remoteObserver);}
  async reconnectController(){this.remoteObserver=false;await super.reconnectController();}
  async recover(connection){await super.recover(connection);if(this.replica && !this.disposed)this.historyButton.hidden=false;}
  async readHistory(){
    const replica=this.replica;if(!replica||this.disposed||this.history.open)return;
    try{await this.history.show(this.element,()=>replica.call('history'),()=>this.replica===replica&&!this.disposed);}
    catch(e){if(!this.disposed)this.ctx.toast(e.message);}
  }
  fail(connection,message){this.history?.clear();super.fail(connection,message);}
  async detach(){this.history?.clear();await super.detach();}
  dispose(){this.history?.clear();super.dispose();}
}
