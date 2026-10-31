import {base64Bytes,encodeInput,formatState,nextSequence} from './workspace-model.js';
import {bindUserInput} from './terminal-input.js';

// One parser and cursor per server block, independent of the DOM element's parent or presentation plane.
export class TerminalPane {
  constructor(block,ctx){
    this.ctx=ctx;this.id=block.id;this.sessionId=block.sessionId;this.sequence=0;this.lastVisible=performance.now();this.ready=false;this.hasControl=false;this.closed=false;this.disposed=false;this.exited=false;this.retries=0;this.pending=Promise.resolve();this.connection=null;
    const {text,button}=ctx;this.element=text('section','','terminal-pane');this.element.dataset.blockId=this.id;
    const header=text('div','','pane-header');this.name=text('span',block.label,'pane-title');this.name.title='Double-click to rename';this.indicator=text('span','connecting','pane-state');
    header.append(text('span','›'),this.name,this.indicator,button('↗','Capture terminal',()=>ctx.captureBlock(this.id)),button('◇','Detach or reconnect this view',()=>this.toggleAttachment()),button('×','Close process',()=>ctx.closeBlock(this.id)));
    this.host=text('div','','terminal-host');const footer=text('div','','pane-footer');this.geometry=text('span','');this.access=text('span','');footer.append(this.geometry,this.access);
    this.element.append(header,this.host,footer);for(const type of ['pointerdown','focusin'])this.element.addEventListener(type,()=>ctx.focusBlock(this.id));
    this.name.ondblclick=()=>void ctx.renameBlock(this.id).catch(e=>ctx.toast(e.message));
    this.terminal=new window.Terminal({fontFamily:'"SFMono-Regular",Consolas,"Liberation Mono",monospace',fontSize:12,lineHeight:1.18,cursorBlink:true,scrollback:15000,allowProposedApi:false,convertEol:false,theme:{background:'#0e1217',foreground:'#c6d2dd',cursor:'#8be6c9',selectionBackground:'#3c625b80',black:'#15202a',red:'#df8d8d',green:'#8fc9a4',yellow:'#d9c18a',blue:'#8bb4da',magenta:'#b69ed4',cyan:'#86c9c7',white:'#ccd8df',brightBlack:'#667b8e',brightRed:'#f2aaaa',brightGreen:'#b2e8c4',brightYellow:'#f0d8a2',brightBlue:'#b5d7f3',brightMagenta:'#d3b8ef',brightCyan:'#b4e5df',brightWhite:'#eff7fc'}});
    this.fit=new window.FitAddon.FitAddon();this.terminal.loadAddon(this.fit);
    this.userInput=bindUserInput(this.terminal,data=>this.sendInput(data),data=>{if(this.ready&&this.hasControl)this.send({type:'input',data:btoa(data)});});
    const touch=text('div','','touch-keys');for(const [label,v] of [['Esc','\u001b'],['Tab','\t'],['Ctrl C','\u0003'],['↑','\u001b[A'],['↓','\u001b[B'],['←','\u001b[D'],['→','\u001b[C']])touch.append(button(label,`Send ${label}`,()=>this.sendInput(v)));this.element.append(touch);
    this.resizeObserver=new ResizeObserver(()=>this.scheduleFit());
  }
  mount(){if(this.opened)return;this.opened=true;this.terminal.open(this.host);this.resizeObserver.observe(this.host);
    for(const [name,data] of [['focus','\u001b[I'],['blur','\u001b[O']])this.terminal.textarea.addEventListener(name,()=>{if(this.terminal._core.coreService.decPrivateModes.sendFocus)this.sendInput(data);});
    this.connect();
  }
  connect(observer=false){
    if(this.disposed||this.connection||this.exited)return;clearTimeout(this.retryTimer);this.closed=false;this.ready=false;this.hasControl=false;
    const role=observer||!this.ctx.state.capability.canControl?'observer':'controller';this.indicator.textContent='connecting';
    const address=new URL(`/api/sessions/${this.sessionId}/blocks/${this.id}/stream`,location.href);address.protocol=location.protocol==='https:'?'wss:':'ws:';address.searchParams.set('after',this.sequence);address.searchParams.set('role',role);
    const socket=new WebSocket(address);let finish;const c={socket,role,failed:false,pendingBytes:0,pendingFrames:0,done:new Promise(r=>{finish=r;})};this.connection=c;
    socket.onopen=()=>{if(!this.closed&&!this.disposed)socket.send(JSON.stringify({type:'authenticate',token:this.ctx.state.token}));};
    socket.onmessage=({data})=>{
      if(this.disposed||c.failed)return;
      if(typeof data!=='string'||data.length>1048576||c.pendingBytes+data.length>8*1048576||c.pendingFrames>=4096){this.fail(c,'Client receive queue exceeded its bound; reconnect from the last applied event.');return;}
      ++c.pendingFrames;c.pendingBytes+=data.length;
      this.pending=this.pending.then(async()=>{if(!c.failed&&!this.disposed)await this.apply(c,JSON.parse(data));}).catch(e=>this.fail(c,e.message)).finally(()=>{--c.pendingFrames;c.pendingBytes-=data.length;});
    };
    socket.onclose=()=>{this.pending=this.pending.then(()=>{
      if(this.connection===c){this.connection=null;this.ready=this.hasControl=false;
        if(!this.disposed&&!this.closed&&!this.exited){this.indicator.textContent='reconnecting';this.access.textContent='Connection lost · process retained';this.retryTimer=setTimeout(()=>this.connect(c.observerFallback||role==='observer'),c.observerFallback?0:Math.min(1000*2**Math.min(this.retries++,4),15000));}}
      finish();});};
  }
  async apply(c,f){
    if(f.type==='attached'){this.hasControl=!this.closed&&f.hasControl===true;return;}
    if(f.type==='ready'){if(f.sequence!==this.sequence)throw Error('Replay fence does not match applied history.');this.ready=!this.closed;this.retries=0;if(!this.exited&&!this.closed){this.indicator.textContent=this.hasControl?'live':'view only';this.access.textContent=this.hasControl?'Input + resize control':'Shared read-only view';}this.scheduleFit();return;}
    if(f.type==='error'){if(f.code==='Conflict'&&c.role==='controller'&&!this.closed){c.observerFallback=true;c.failed=true;c.socket.close();return;}throw Error(`${f.code}: ${f.message}`);}
    if(!nextSequence(this.sequence,f.sequence))return;
    switch(f.kind){
      case 'resize':{const d=f.dimensions;if(!Number.isInteger(d?.columns)||!Number.isInteger(d?.rows)||d.columns<1||d.rows<1||d.columns>512||d.rows>512)throw Error('Invalid shared geometry.');this.terminal.resize(d.columns,d.rows);this.geometry.textContent=`${d.columns} × ${d.rows}`;break;}
      case 'output':{const bytes=base64Bytes(f.data||'');await new Promise(resolve=>this.terminal.write(bytes,resolve));break;}
      case 'exit':this.exited=true;this.hasControl=false;this.indicator.textContent=`exit ${f.exitCode}`;this.access.textContent='Process exited · output retained';break;
      case 'closed':this.closed=true;this.hasControl=this.ready=false;this.indicator.textContent='closed';break;
      default:throw Error(`Unknown stream event ${f.kind}`);
    }
    this.sequence=f.sequence;
  }
  fail(c,message){if(c.failed||this.disposed)return;c.failed=true;this.closed=true;this.ready=this.hasControl=false;c.socket.close();this.indicator.textContent='detached';this.access.textContent=message;this.ctx.toast(message);}
  send(frame){const socket=this.connection?.socket;if(socket?.readyState===WebSocket.OPEN)socket.send(JSON.stringify(frame));}
  sendInput(data){if(!this.ready||!this.hasControl||this.closed)return;const bytes=new TextEncoder().encode(data);if(bytes.length>65536){this.ctx.toast('Paste exceeds 64 KiB.');return;}this.send({type:'input',data:encodeInput(data)});}
  scheduleFit(){clearTimeout(this.fitTimer);this.fitTimer=setTimeout(()=>{if(!this.ready||!this.hasControl||!this.element.isConnected)return;const size=this.fit.proposeDimensions();if(!size||size.cols<2||size.rows<1)return;const cols=Math.min(512,size.cols),rows=Math.min(256,size.rows);if(cols!==this.terminal.cols||rows!==this.terminal.rows)this.send({type:'resize',columns:cols,rows,widthPixels:0,heightPixels:0});},100);}
  update(b){this.name.textContent=b.label;if(formatState(b)==='exited'){this.indicator.textContent=`exit ${b.exitCode}`;this.hasControl=false;}}
  async detach(){this.closed=true;this.ready=this.hasControl=false;clearTimeout(this.retryTimer);const c=this.connection;if(c){c.socket.close();await c.done;}}
  async toggleAttachment(){if(this.exited)return;if(this.closed){if(this.connection)await this.connection.done;this.connect();}else{await this.detach();this.indicator.textContent='detached';this.access.textContent='Process still running · ◇ reconnects';}}
  async reconnectController(){await this.detach();this.connect();}
  dispose(){this.disposed=this.closed=true;clearTimeout(this.retryTimer);clearTimeout(this.fitTimer);this.connection?.socket.close();this.resizeObserver.disconnect();this.userInput.dispose();this.terminal.dispose();this.element.remove();}
}
