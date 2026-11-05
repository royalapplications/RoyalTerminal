export function createDialog(dialog,form,content,cancel,accept) {
  cancel.onclick=()=>dialog.close();
  return (title,description,fields=[],submit='Continue')=>{
    if(dialog.open)return Promise.resolve(null);
    const heading=document.createElement('h3');heading.id='dialog-heading';heading.textContent=title;content.replaceChildren(heading);
    if(description){const p=document.createElement('p');p.textContent=description;content.append(p);}
    const controls=new Map();
    for(const f of fields){
      const label=document.createElement('label'),n=document.createElement(f.options?'select':f.type==='textarea'?'textarea':'input');
      label.textContent=f.label;label.htmlFor=n.id='field-'+f.name;n.name=f.name;
      if(f.options)for(const [value,s] of f.options)n.add(new Option(s,value));else if(n.tagName==='INPUT')n.type=f.type||'text';
      if(f.value!==undefined)n.value=f.value;n.required=!!f.required;n.readOnly=!!f.readOnly;n.autocomplete='off';n.spellcheck=false;
      if(f.placeholder)n.placeholder=f.placeholder;controls.set(f.name,n);content.append(label,n);
    }
    accept.textContent=submit;dialog.showModal();
    if(controls.size)controls.values().next().value.focus();else cancel.focus();
    return new Promise(resolve=>{
      let result=null;
      const done=e=>{e.preventDefault();result=Object.fromEntries([...controls].map(([k,n])=>[k,n.value]));dialog.close();};
      form.addEventListener('submit',done);
      dialog.addEventListener('close',()=>{form.removeEventListener('submit',done);content.replaceChildren();resolve(result);},{once:true});
    });
  };
}
