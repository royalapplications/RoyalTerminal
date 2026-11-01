import { test, expect } from '@playwright/test';
const admin={Authorization:'Bearer '+(process.env.ROYALTERMINAL_MUX_TEST_TOKEN||'browser-test-token-012345678901234567890123456789')};
async function palette(page,title){await page.keyboard.press('Control+k');await page.locator('#palette-query').fill(title);await page.locator('#palette-query').press('Enter');}
test('an opted-in tab alone follows cross-window focus; no control lease is obtained',async({page,browser,request})=>{
  const api=(method,args,headers=admin)=>request.post('/api/call',{headers,data:{method,arguments:args}});
  const ok=async(m,a)=>{const r=await api(m,a);expect(r.ok(),await r.text()).toBe(true);return r.json();};
  const s=await ok('session.create',{label:'Remote view test'}),sid={sessionId:s.id};const second=await browser.newPage();
  try{
    const w=await ok('session.new_window',{...sid,label:'Target'});
    await ok('block.create',{...sid,block:{windowId:s.windows[0].id,label:'Left',transport:'pty'}});
    const b=await ok('block.create',{...sid,block:{windowId:w.id,label:'Remote target',transport:'pty'}});
    const token=admin.Authorization.slice(7),url=`/#${new URLSearchParams({token,session:s.id})}`;
    await page.goto(url);await second.goto(new URL(url,page.url()).href);await expect(page.getByRole('tab',{name:'Target'})).toBeVisible();
    expect(await ok('client.views',sid)).toEqual([]);
    await palette(page,'Enable remote UI actions for this tab');await page.getByLabel('Client label').fill('A');await page.locator('#dialog-submit').click();
    await expect.poll(async()=>(await ok('client.views',sid)).length).toBe(1);const [v]=await ok('client.views',sid);
    const share=await ok('share.create',{...sid,role:'observer'});const observer={Authorization:`Bearer ${share.token}`};
    const args={...sid,clientId:v.id,actions:[{name:'pane.focus',blockId:b.id}]};
    expect((await api('client.queue',args,observer)).status()).toBe(403);
    expect((await api('client.read_view',{...sid,clientId:v.id,readerKey:'0'.repeat(64),after:0,timeoutMs:0})).status()).toBe(403);
    await ok('client.queue',args);
    await expect(page.getByRole('tab',{name:'Target'})).toHaveAttribute('aria-selected','true');
    await expect(page.locator(`[data-block-id="${b.id}"] .xterm-helper-textarea`)).toBeFocused();
    await expect(page.locator(`[data-block-id="${b.id}"] .pane-state`)).toHaveText('view only');
    await expect(second.getByRole('tab',{name:'Target'})).toHaveAttribute('aria-selected','false');
    expect((await ok('session.view',sid)).blocks.find(x=>x.id===b.id).controllerId??null).toBeNull();
    await palette(page,'Disable remote UI actions for this tab');await expect.poll(async()=>(await ok('client.views',sid)).length).toBe(0);
    expect((await api('client.queue',args)).status()).toBe(404);
  } finally {await second.close();await page.close();await request.delete(`/api/sessions/${s.id}`,{headers:admin});}
});
