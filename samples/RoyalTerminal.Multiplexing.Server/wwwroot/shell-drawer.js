// A remote request and a local key/button go through the same responsive sidebar control.
// The DOM class is owned by the narrow-view drawer; desktop collapse is independent of it.
export function initializeDrawer() {
  const $=id=>document.getElementById(id);
  const side=$('sidebar'),main=$('workspace-content').closest('main'),app=$('application');
  const button=$('toggle-sidebar'),shade=$('drawer-shade'),narrow=matchMedia('(max-width:640px)');
  let collapsed=false,wasOpen=false;
  const focusables=()=>[...side.querySelectorAll('button:not(:disabled),[tabindex="0"]')].filter(n=>n.getClientRects().length>0);
  function sync(){
    const phone=narrow.matches,open=phone&&!app.hidden&&side.classList.contains('visible');
    side.hidden=!phone&&collapsed;app.style.gridTemplateColumns=side.hidden?'minmax(0,1fr)':'';
    shade.hidden=!open;side.inert=phone&&!open;
    // On mobile the backplate remains mouse-accessible; the workspace underneath isn't a keyboard destination.
    main.inert=open; button.setAttribute('aria-expanded',String(phone?open:!collapsed));
    if(open&&!wasOpen&&document.hasFocus())focusables()[0]?.focus({preventScroll:true});
    else if(!open&&wasOpen&&!app.hidden&&!document.querySelector('dialog[open]')&&(side.contains(document.activeElement)||document.activeElement===document.body))button.focus({preventScroll:true});
    wasOpen=open;
  }
  button.addEventListener('click',e=>{if(narrow.matches)return;e.stopImmediatePropagation();collapsed=!collapsed;sync();},true);
  side.addEventListener('keydown',e=>{
    if(e.key!=='Tab'||!wasOpen)return;
    const all=focusables(),at=all.indexOf(document.activeElement);
    if(all.length&&((e.shiftKey&&at<=0)||(!e.shiftKey&&at===all.length-1))){e.preventDefault();all[e.shiftKey?all.length-1:0].focus();}
  });
  new MutationObserver(sync).observe(side,{attributes:true,attributeFilter:['class']});
  new MutationObserver(()=>{if(app.hidden){collapsed=false;side.classList.remove('visible');}sync();}).observe(app,{attributes:true,attributeFilter:['hidden']});
  narrow.addEventListener('change',()=>{side.classList.remove('visible');sync();});sync();
}
