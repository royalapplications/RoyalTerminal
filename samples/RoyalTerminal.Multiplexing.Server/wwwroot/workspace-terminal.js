import { TerminalPane as StreamPane } from './workspace-terminal-stream.js';
import { nextSequence } from './workspace-model.js';
import { openReplica, readCut } from './replica-client.js';

// Ordinary replay stays entirely in xterm. After eviction the managed worker owns all parsing;
// never feed a raw suffix into an xterm instance bootstrapped with only visible rows.
export class TerminalPane extends StreamPane {
  constructor(block,ctx){super(block,ctx);this.epoch=0;this.cuts=0;this.replica=null;this.dirty=false;this.paintTask=null;}
  async recover(c){
    if(++this.cuts>3)throw Error('The replay suffix expired three times during recovery. Reattach explicitly.');
    this.ready=this.hasControl=false;this.indicator.textContent='recovering';
    const epoch=this.epoch, abort=new AbortController();this.cutAbort=abort;
    const timeout=setTimeout(()=>abort.abort(),30000);let staged;
    const current=()=>!this.disposed&&!this.closed&&this.epoch===epoch&&this.connection===c;
    try{
      const cut=await readCut(this.sessionId,this.id,this.ctx.state.token,abort.signal);if(!current())return;
      if(cut.sequence<this.sequence)throw Error('Checkpoint cursor is older than the displayed state.');
      staged=await openReplica(cut.bytes);if(!current())return;
      if(this.paintTask)await this.paintTask;
      const frame=await staged.call('paint');if(!current())return;
      await this.showFrame(frame);if(!current())return;
      const previous=this.replica;this.replica=staged;staged=null;void previous?.dispose();
      this.dirty=false;this.sequence=cut.sequence;this.element.dataset.recovery='managed';
      this.access.textContent='Restored terminal · attaching to live output';
      if(cut.exit!==null){this.exited=true;this.indicator.textContent=`exit ${cut.exit}`;this.access.textContent='Process exited · recovered final viewport';}
      c.failed=true;c.socket.close();
    }finally{clearTimeout(timeout);if(this.cutAbort===abort)this.cutAbort=null;void staged?.dispose();}
  }
  async apply(c,f){
    if(this.closed||this.disposed)return;
    if(f.type==='error'&&f.code==='HistoryGap')return this.recover(c);
    const r=this.replica;
    if(r&&!f.type&&(f.kind==='output'||f.kind==='resize')){
      if(!nextSequence(this.sequence,f.sequence))return;
      await r.call(f.kind,f.kind==='resize'?f.dimensions:f.data);
      if(this.disposed||r!==this.replica)return;
      this.sequence=f.sequence;this.dirty=true;this.planPaint(c);return;
    }
    if(r&&(f.type==='ready'||f.kind==='exit'))await this.paint();
    await super.apply(c,f);if(f.type==='ready')this.cuts=0;
  }
  planPaint(c){if(this.paintTimer||this.disposed||this.closed)return;this.paintTimer=setTimeout(()=>{this.paintTimer=0;this.paint().catch(e=>this.fail(c,e.message));},32);}
  async paint(){
    while(this.dirty||this.paintTask){
      if(this.paintTask){await this.paintTask;continue;}
      const r=this.replica, epoch=this.epoch;if(!r||this.disposed||this.closed)return;this.dirty=false;
      const work=(async()=>{const f=await r.call('paint');if(epoch===this.epoch&&!this.disposed&&!this.closed&&r===this.replica)await this.showFrame(f);})();
      this.paintTask=work;try{await work;}finally{if(this.paintTask===work)this.paintTask=null;}
      // Further writes schedule their own presentation. Never keep the ingest queue waiting on an animation loop.
    }
  }
  async showFrame({cols,rows,vt}){
    if(!Number.isInteger(cols)||!Number.isInteger(rows)||cols<1||rows<1||cols>512||rows>512||typeof vt!=='string'||vt.length>4*1024*1024)throw Error('Invalid recovered viewport.');
    if(this.terminal.cols!==cols||this.terminal.rows!==rows)this.terminal.resize(cols,rows);
    this.geometry.textContent=`${cols} × ${rows}`;
    await new Promise(resolve=>this.terminal.write('\x1bc'+vt,resolve));
  }
  async detach(){++this.epoch;this.cutAbort?.abort();clearTimeout(this.paintTimer);this.paintTimer=0;this.cuts=0;await super.detach();}
  dispose(){++this.epoch;this.cutAbort?.abort();clearTimeout(this.paintTimer);void this.replica?.dispose();this.replica=null;super.dispose();}
}
