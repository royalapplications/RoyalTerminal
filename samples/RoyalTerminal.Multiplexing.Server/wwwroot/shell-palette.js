export function createPalette(dialog, input, results, available, execute) {
  let items=[],selected=0;
  function highlight(index,scroll=false){
    if(!items.length){input.removeAttribute('aria-activedescendant');return;}
    selected=(index+items.length)%items.length;
    for(const [i,el] of [...results.children].entries()){
      el.classList.toggle('selected',i===selected);el.setAttribute('aria-selected',String(i===selected));
    }
    const el=results.children[selected];input.setAttribute('aria-activedescendant',el.id);
    if(scroll)el.scrollIntoView({block:'nearest'});
  }
  function choose(i){const action=items[i];if(action){dialog.close();execute(action.run);}}
  function render(){
    const query=input.value.trim().toLowerCase(),words=query.split(/\s+/);
    items=available().filter(a=>{const hay=[a.title,a.hint,a.keywords,a.description].filter(Boolean).join(' ').toLowerCase();return words.every(w=>hay.includes(w));});
    // Exact titles beat longer substring matches, e.g. local zoom versus 'for all viewers'.
    if(query)items.sort((a,b)=>Number(b.title.toLowerCase()===query)-Number(a.title.toLowerCase()===query));
    selected=Math.min(selected,Math.max(0,items.length-1));results.replaceChildren();
    if(!items.length){const n=document.createElement('p');n.className='palette-empty';n.textContent='No matching commands. Try a different word.';results.append(n);}
    items.forEach((a,i)=>{
      const n=document.createElement('button');n.type='button';n.tabIndex=-1;n.id='command-option-'+i;n.className='palette-action';n.setAttribute('role','option');n.setAttribute('aria-label',a.title);
      const title=document.createElement('span'),hint=document.createElement('small');title.textContent=a.title;hint.textContent=a.hint??'';n.append(title,hint);
      n.onpointermove=()=>highlight(i);n.onclick=()=>choose(i);results.append(n);
    });highlight(selected);
  }
  input.oninput=()=>{selected=0;render();};
  input.onkeydown=e=>{
    if(e.isComposing)return;
    if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();highlight(selected+(e.key==='ArrowDown'?1:-1),true);}
    else if(e.key==='Enter'){e.preventDefault();choose(selected);}
  };
  const open=()=>{if(dialog.open||document.querySelector('dialog[open]'))return;selected=0;input.value='';render();dialog.showModal();input.setAttribute('aria-expanded','true');input.focus();};
  dialog.addEventListener('close',()=>{input.setAttribute('aria-expanded','false');input.removeAttribute('aria-activedescendant');});
  return {open,render};
}
