import { test,expect } from '@playwright/test';
const token=process.env.ROYALTERMINAL_MUX_TEST_TOKEN||'browser-test-token-012345678901234567890123456789';

test('real history eviction: recover a browser parser between UTF-8 bytes and follow its alternate screen',async({page,request})=>{
  test.setTimeout(120000);
  const invoke=async(method,args,bearer=token)=>{const r=await request.post('/api/call',{headers:{Authorization:`Bearer ${bearer}`},data:{method,arguments:args}});expect(r.ok(),await r.text()).toBe(true);return r.json();};
  const s=await invoke('session.create',{label:'WASM eviction'});
  try {
    const script=["import sys", "b=sys.stdout.buffer", "b.write(b'\\r.' * (5*1024*1024))", "b.write(b'\\x1b[2J\\x1b[HREADY\\x1b]2;cut-ready\\x07\\x1b[2;3H\\xe2\\x82')", "b.flush()", "sys.stdin.buffer.read(1)", "b.write(b'\\xac'); b.flush()", "sys.stdin.buffer.read(1)", "b.write(b'\\x1b[?1049h\\x1b[3;4HZ'); b.flush()", "sys.stdin.buffer.read(1)", "sys.exit(37)"].join('\n');
    const b=await invoke('block.create',{sessionId:s.id,block:{label:'split codepoint',transport:'pipe',command:{fileName:'/usr/bin/python3',arguments:['-u','-c',script]},dimensions:{columns:20,rows:4,widthPixels:0,heightPixels:0}}});
    const ids={sessionId:s.id,blockId:b.id};
    await expect.poll(async()=>JSON.stringify(await invoke('terminal.title',ids)),{timeout:60000}).toContain('cut-ready');
    expect((await invoke('session.view',{sessionId:s.id})).blocks[0].firstRetainedSequence).toBeGreaterThan(1);
    const share=await invoke('share.create',{sessionId:s.id,role:'observer'});
    await page.goto('/');
    await page.evaluate(async({b,capability})=>{
      const {TerminalPane}=await import('/workspace-terminal.js');
      const text=(tag,value,cls)=>{const e=document.createElement(tag);e.textContent=value??'';e.className=cls??'';return e;};
      const pane=new TerminalPane(b,{state:{token:capability,capability:{canControl:false}},text,button:()=>document.createElement('button'),toast:console.warn,focusBlock(){}});
      window.recoveryTestPane=pane;document.body.append(pane.element);pane.mount();
    },{b,capability:share.token});
    await expect(page.locator('[data-recovery=managed] .pane-state')).toHaveText('view only',{timeout:45000});
    const cell=(x,y)=>page.evaluate(([x,y])=>window.recoveryTestPane.terminal.buffer.active.getLine(y)?.getCell(x)?.getChars(),[x,y]);
    await expect.poll(()=>cell(0,0)).toBe('R');
    await invoke('block.input',{...ids,data:'YQ=='});await expect.poll(()=>cell(2,1)).toBe('€');
    await invoke('block.input',{...ids,data:'Yg=='});await expect.poll(()=>cell(3,2)).toBe('Z');
    expect(await page.evaluate(()=>window.recoveryTestPane.hasControl)).toBe(false);
    await invoke('block.input',{...ids,data:'Yw=='});await expect(page.locator('[data-recovery=managed] .pane-state')).toHaveText('exit 37');
    await page.evaluate(()=>window.recoveryTestPane.dispose());
  }finally{await page.close();await invoke('session.close',{sessionId:s.id});}
});
