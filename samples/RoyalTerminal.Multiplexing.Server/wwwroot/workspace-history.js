import {pagesForHistory,renderHistoryPage} from './workspace-history-paint.js';
const css = new URL('./workspace-history.css',import.meta.url).href;
function styles(){if(!document.querySelector('link[data-workspace-history]')){const link=document.createElement('link');link.rel='stylesheet';link.href=css;link.dataset.workspaceHistory='';document.head.append(link);}}
function element(tag,text){const e=document.createElement(tag);if(text!==undefined)e.textContent=text;return e;}
function button(label){const b=element('button',label);b.type='button';return b;}

export class HistoryDialog {
  get open(){return !!this.dialog;}
  async show(owner,read,current){
    if(this.dialog)return;styles();
    const d=element('dialog');d.className='workspace-history';d.setAttribute('aria-label','Recovered terminal scrollback');this.dialog=d;
    const heading=element('h2','Recovered scrollback'),note=element('p','Frozen while you read. Live output continues in the terminal.');
    const bar=element('div'),query=element('input'),find=button('Find next'),toggle=button('Show terminal colors'),close=button('Return to live terminal');
    query.type='search';query.placeholder='Find literal text';query.setAttribute('aria-label','Find in scrollback');toggle.disabled=true;toggle.setAttribute('aria-pressed','false');
    const area=element('textarea');area.readOnly=true;area.spellcheck=false;area.wrap='off';area.setAttribute('aria-label','Retained terminal text');
    const paint=element('pre');paint.tabIndex=0;paint.hidden=true;paint.setAttribute('role','region');paint.setAttribute('aria-label','Colored terminal history');
    const nav=element('nav'),prev=button('Previous history page'),next=button('Next history page'),number=element('span');nav.hidden=true;nav.setAttribute('aria-label','Formatted history pages');nav.append(prev,number,next);
    const message=element('output','Reading retained history…');message.setAttribute('aria-live','polite');
    bar.append(query,find,toggle,close);d.append(heading,note,bar,area,paint,nav,message);owner.append(d);
    let previous='',data=null,pages=[],index=0,showing=false;
    const render=()=>{renderHistoryPage(paint,data,pages[index]);prev.disabled=index===0;next.disabled=index+1===pages.length;number.textContent=`Rows ${pages[index][0]+1}–${pages[index][1]} of ${data.rows.length} · page ${index+1}/${pages.length}`;};
    const mode=styled=>{
      showing=!!(styled&&data);area.hidden=showing;paint.hidden=nav.hidden=!showing;toggle.setAttribute('aria-pressed',String(showing));toggle.textContent=showing?'Show plain text':'Show terminal colors';
      if(showing){render();message.textContent='Colors and styles from the retained terminal cells.';paint.focus();}
      else{message.textContent='Select text to copy, or search above.';paint.replaceChildren();}
    };
    toggle.onclick=()=>mode(!showing);prev.onclick=()=>{if(index>0){index--;render();}};next.onclick=()=>{if(index+1<pages.length){index++;render();}};
    const match=()=>{
      const q=query.value;if(!q){message.textContent='Enter text to find.';return;}
      if(showing)mode(false);const start=previous===q?area.selectionEnd:0;previous=q;
      let at=area.value.indexOf(q,start);if(at<0&&start)at=area.value.indexOf(q);
      if(at<0){message.textContent='No match.';return;}
      area.focus();area.setSelectionRange(at,at+q.length);message.textContent='Match selected.';
    };
    const keys=e=>{
      if(!d.open||!e.composedPath().includes(d))return;
      e.stopImmediatePropagation();
      if(e.key==='Escape'){e.preventDefault();this.clear();}
      else if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='f'){e.preventDefault();query.focus();query.select();}
      else if(e.key==='Enter'&&(e.target===query||e.target===find)){e.preventDefault();match();}
    };
    this.release=()=>{window.removeEventListener('keydown',keys,true);area.value='';query.value='';paint.replaceChildren();data=null;pages=[];};
    window.addEventListener('keydown',keys,true);close.onclick=()=>this.clear();find.onclick=match;
    d.addEventListener('close',()=>{if(this.dialog===d)this.clear();},{once:true});
    try{
      d.showModal();query.focus();const snap=await read();
      if(this.dialog!==d)return;if(!current()){this.clear();return;}
      const text=typeof snap==='string'?snap:snap?.text;
      if(typeof text!=='string'||text.length>4*1024*1024)throw Error('Invalid recovered scrollback.');
      area.value=text;message.textContent=text?'Select text to copy, or search above.':'No retained text.';
      if(snap?.styled){try{pages=pagesForHistory(snap.styled);data=snap.styled;toggle.disabled=false;}catch{message.textContent='Formatting unavailable; the plain transcript is intact.';}}
      else if(snap?.styleUnavailable)message.textContent='Formatting exceeds the history view budget; the plain transcript is intact.';
    }catch(e){if(this.dialog===d)this.clear();throw e;}
  }
  clear(){const d=this.dialog;if(!d)return;this.dialog=null;this.release?.();this.release=null;if(d.open)d.close();d.remove();}
}
