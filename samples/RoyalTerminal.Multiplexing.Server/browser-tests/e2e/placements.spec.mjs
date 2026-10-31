import {test,expect} from '@playwright/test';
const token=process.env.ROYALTERMINAL_MUX_TEST_TOKEN||'browser-test-token-012345678901234567890123456789';
const headers={Authorization:`Bearer ${token}`};
async function palette(page,name){await page.keyboard.press('Control+k');await page.getByLabel('Search commands',{exact:true}).fill(name);await page.locator('#palette-query').press('Enter');}

test('float, keyboard move, unplace, restore and cross-window move keep the same terminal and live stream',async({page,request})=>{
  const create=await request.post('/api/sessions',{headers,data:{label:'Placement integration'}});expect(create.ok()).toBe(true);const s=await create.json();
  const api=async(method,args)=>{const r=await request.post('/api/call',{headers,data:{method,arguments:args}});expect(r.ok()).toBe(true);return r.json();};
  const snapshot=async()=>api('session.view',{sessionId:s.id});
  try{
    const w=await api('session.new_window',{sessionId:s.id,label:'Destination'});
    const b=await api('block.create',{sessionId:s.id,block:{label:'Persistent shell',transport:'pty'}});
    const sockets=[];page.on('websocket',ws=>sockets.push(ws.url()));
    await page.goto(`/#${new URLSearchParams({token,session:s.id})}`);
    const pane=page.locator(`[data-block-id="${b.id}"]`);await expect(pane.locator('.pane-state')).toHaveText('live');
    const surface=await pane.locator('.xterm').elementHandle();
    const before=(await snapshot()).blocks[0].controllerId;
    await palette(page,'Float focused terminal');await expect(page.locator('.floating-frame .terminal-pane')).toHaveCount(1);
    await page.getByRole('button',{name:'Move floating terminal Persistent shell'}).press('ArrowRight');
    await expect.poll(async()=>(await snapshot()).blocks[0].floatingBounds.x).toBe(1600);
    await palette(page,'Unplace focused terminal');await expect(pane).toHaveCount(0);
    await page.getByRole('button',{name:'Restore Persistent shell'}).click();await expect(pane).toHaveCount(1);
    expect(await surface.evaluate(n=>n.isConnected)).toBe(true);
    await palette(page,'Move focused terminal to window');await page.getByLabel('Destination window').selectOption(w.id);await page.locator('#dialog-submit').click();
    await page.getByRole('tab',{name:'Destination'}).click();await expect(pane.locator('.pane-state')).toHaveText('live');
    const after=(await snapshot()).blocks[0];expect(after.id).toBe(b.id);expect(after.windowId).toBe(w.id);expect(after.controllerId).toBe(before);expect(after.state).toBe('Running');
    expect(sockets.filter(url=>url.includes(b.id))).toHaveLength(1);expect(await surface.evaluate(n=>n.isConnected)).toBe(true);
  }finally{await page.close();await request.delete(`/api/sessions/${s.id}`,{headers});}
});

test('observer can watch revisions but cannot mutate placements, including by calling the API directly',async({request})=>{
  const s=await (await request.post('/api/sessions',{headers,data:{label:'Scope'}})).json();
  const call=(auth,method,args)=>request.post('/api/call',{headers:auth,data:{method,arguments:args}});
  try{
    const share=await (await call(headers,'share.create',{sessionId:s.id,role:'observer'})).json();const observer={Authorization:`Bearer ${share.token}`};
    const read=await call(observer,'session.wait_change',{sessionId:s.id,afterRevision:s.revision,timeoutMs:10});expect(read.ok()).toBe(true);expect((await read.json()).revision).toBe(s.revision);
    const bad=await call(observer,'session.move_window',{sessionId:s.id,windowId:s.windows[0].id,position:1});expect(bad.status()).toBe(403);
    const other=await (await request.post('/api/sessions',{headers,data:{label:'Private'}})).json();try{expect((await call(observer,'session.wait_change',{sessionId:other.id,afterRevision:0})).status()).toBe(403);}finally{await request.delete(`/api/sessions/${other.id}`,{headers});}
  }finally{await request.delete(`/api/sessions/${s.id}`,{headers});}
});
