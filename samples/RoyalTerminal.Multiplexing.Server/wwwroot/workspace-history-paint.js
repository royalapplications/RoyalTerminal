// No HTML, URL, escape language or arbitrary CSS is accepted from the terminal. Row data stays frozen.
const color=n=>`#${n.toString(16).padStart(6,'0')}`;
const uint=(n,max)=>Number.isInteger(n)&&n>=0&&n<=max;
const lines=['','solid','double','wavy','dotted','dashed'];
export function pagesForHistory(data){
  if(!data||!uint(data.background,0xffffff)||!Array.isArray(data.rows)||data.rows.length>4096)throw Error('Invalid formatted history');
  const pages=[];let start=0,inPage=0,total=0,chars=0;
  for(let i=0;i<data.rows.length;i++){
    const row=data.rows[i];if(!Array.isArray(row)||row.length>512)throw Error('Invalid formatted row');
    if(i>start&&(i-start>=48||inPage+row.length>2048)){pages.push([start,i]);start=i;inPage=0;}
    inPage+=row.length;total+=row.length;if(total>32768)throw Error('Formatted history run budget exceeded');
    for(const run of row){if(!Array.isArray(run)||run.length!==7)throw Error('Invalid formatted run');
      const [fg,bg,flags,under,uc,over,text]=run;
      if(!uint(fg,0xffffff)||!uint(bg,0xffffff)||!uint(flags,255)||!uint(under,5)||(uc!==-1&&!uint(uc,0xffffff))||typeof over!=='boolean'||typeof text!=='string'||text.length>4096)throw Error('Invalid formatted attributes');
      if((chars+=text.length)>4*1024*1024)throw Error('Formatted history text budget exceeded');
    }
  }
  if(start<data.rows.length||!pages.length)pages.push([start,data.rows.length]);return pages;
}
export function renderHistoryPage(host,data,[start,end]){
  const content=document.createDocumentFragment();
  for(let i=start;i<end;i++){
    for(const [fg,bg,flags,under,uc,over,text] of data.rows[i]){
      const span=document.createElement('span');span.textContent=text;
      const s=span.style;s.color=color(fg);s.backgroundColor=color(bg);
      if(flags&1)s.fontWeight='bold';if(flags&2)s.fontStyle='italic';if(flags&64)s.opacity='0.5';if(flags&128)s.visibility='hidden';
      const deco=[];if(under||flags&4)deco.push('underline');if(flags&8)deco.push('line-through');if(over)deco.push('overline');
      if(deco.length)s.textDecorationLine=deco.join(' ');if(under)s.textDecorationStyle=lines[under];if(uc!==-1)s.textDecorationColor=color(uc);
      content.append(span);
    }
    if(i+1<end)content.append(document.createTextNode('\n'));
  }
  host.style.backgroundColor=color(data.background);host.replaceChildren(content);host.scrollTop=0;
}
