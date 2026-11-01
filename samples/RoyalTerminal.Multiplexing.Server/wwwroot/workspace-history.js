const css = new URL('./workspace-history.css',import.meta.url).href;
function styles(){if(!document.querySelector('link[data-workspace-history]')){const link=document.createElement('link');link.rel='stylesheet';link.href=css;link.dataset.workspaceHistory='';document.head.append(link);}}
function element(tag,text){const e=document.createElement(tag);if(text!==undefined)e.textContent=text;return e;}

// One stable plain-text value per open dialog. No timer, DOM markup from the terminal, or clipboard permission.
export class HistoryDialog {
  get open(){return !!this.dialog;}
  async show(owner,read,current){
    if(this.dialog)return;styles();
    const d=element('dialog');d.className='workspace-history';d.setAttribute('aria-label','Recovered terminal scrollback');this.dialog=d;
    const heading=element('h2','Recovered scrollback'),note=element('p','Frozen while you read. Live output continues in the terminal.');
    const bar=element('div'),query=element('input'),find=element('button','Find next'),close=element('button','Return to live terminal');
    query.type='search';query.placeholder='Find literal text';query.setAttribute('aria-label','Find in scrollback');find.type=close.type='button';
    const area=element('textarea');area.readOnly=true;area.spellcheck=false;area.wrap='off';area.setAttribute('aria-label','Retained terminal text');
    const message=element('output','Reading retained history…');message.setAttribute('aria-live','polite');
    bar.append(query,find,close);d.append(heading,note,bar,area,message);owner.append(d);
    let previous='';
    const match=()=>{
      const q=query.value;if(!q){message.textContent='Enter text to find.';return;}
      const start=previous===q?area.selectionEnd:0;previous=q;
      let at=area.value.indexOf(q,start);if(at<0&&start)at=area.value.indexOf(q);
      if(at<0){message.textContent='No match.';return;}
      area.focus();area.setSelectionRange(at,at+q.length);message.textContent='Match selected.';
    };
    const keys=e=>{
      if(!d.open || !e.composedPath().includes(d))return;
      // The global workspace keymap uses document capture. Isolate this modal before it can turn a copy/search into a terminal action.
      e.stopImmediatePropagation();
      if(e.key==='Escape'){e.preventDefault();this.clear();}
      else if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='f'){e.preventDefault();query.focus();query.select();}
      else if(e.key==='Enter'&&(e.target===query||e.target===find)){e.preventDefault();match();}
    };
    this.release=()=>{window.removeEventListener('keydown',keys,true);area.value='';query.value='';};
    window.addEventListener('keydown',keys,true);close.onclick=()=>this.clear();find.onclick=match;
    d.addEventListener('close',()=>{if(this.dialog===d)this.clear();},{once:true});
    try{
      d.showModal();query.focus();const text=await read();
      if(this.dialog!==d)return;if(!current()){this.clear();return;}
      if(typeof text!=='string'||text.length>4*1024*1024)throw Error('Invalid recovered scrollback.');
      area.value=text;message.textContent=text?'Select text to copy, or search above.':'No retained text.';
    }catch(e){if(this.dialog===d)this.clear();throw e;}
  }
  clear(){const d=this.dialog;if(!d)return;this.dialog=null;this.release?.();this.release=null;if(d.open)d.close();d.remove();}
}
