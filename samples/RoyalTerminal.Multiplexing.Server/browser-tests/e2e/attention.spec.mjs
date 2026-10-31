import {test,expect} from '@playwright/test';
const token = process.env.ROYALTERMINAL_MUX_TEST_TOKEN || 'browser-test-token-012345678901234567890123456789';
const auth = value => ({Authorization:`Bearer ${value}`});

test('observer reads shell-reported approval for a shelved process without attaching or taking control', async ({page,request}) => {
  const call = async (bearer,method,args) => request.post('/api/call',{headers:auth(bearer),data:{method,arguments:args}});
  const invoke = async (...args) => {const r=await call(...args);expect(r.ok()).toBe(true);return r.json();};
  const session = await invoke(token,'session.create',{label:'Attention test'});
  try {
    const s=session.id, w=session.windows[0].id;
    const b=await invoke(token,'block.create',{sessionId:s,block:{label:'Waiting build',transport:'pipe',command:{fileName:'/bin/cat',arguments:[]}}});
    await invoke(token,'block.place',{sessionId:s,blockId:b.id,windowId:w,kind:'unplaced'});
    const share=await invoke(token,'share.create',{sessionId:s,role:'observer'});
    const ids={sessionId:s,blockId:b.id}; const from=await invoke(share.token,'terminal.events',ids);
    const report='\x1b]7501;state=blocked:kind=permission:id=deploy\x1b\\';
    await invoke(token,'block.input',{...ids,data:Buffer.from(report).toString('base64')});
    const changes=await invoke(share.token,'terminal.events',{...ids,after:from.nextSequence,timeoutMs:3000});
    expect(changes.events.some(e=>e.name==='terminal.program_status_changed'&&e.recordId==='deploy'&&e.record?.state==='Blocked')).toBe(true);
    const summary=await invoke(share.token,'session.attention',{sessionId:s});expect(summary.counts.permission).toBe(1);expect(summary.windows[0].blocks[0].blockId).toBe(b.id);
    await page.goto(`/#${new URLSearchParams({token:share.token,session:s})}`);
    await expect(page.locator('#application')).toBeVisible();await page.keyboard.press('Control+k');await page.locator('#palette-query').fill('Inspect workspace attention');await page.keyboard.press('Enter');
    await expect(page.getByLabel('Activity by terminal')).toHaveValue(/Waiting build: 1 blocked/);
    const view=await invoke(token,'session.view',{sessionId:s});expect(view.blocks[0].attachmentCount).toBe(0);
    const foreign=await invoke(token,'session.create',{label:'Not shared'});
    try {expect((await call(share.token,'session.attention',{sessionId:foreign.id})).status()).toBe(403);}
    finally {await invoke(token,'session.close',{sessionId:foreign.id});}
  } finally {await page.close();await invoke(token,'session.close',{sessionId:session.id});}
});
