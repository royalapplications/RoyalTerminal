import { test, expect } from '@playwright/test';
const token = process.env.ROYALTERMINAL_MUX_TEST_TOKEN || 'browser-test-token-012345678901234567890123456789';
const headers = { Authorization: `Bearer ${token}` };
async function login(page) { await page.goto('/'); await page.getByLabel('Server access token').fill(token); await page.getByRole('button', { name: 'Open workspaces' }).click(); await expect(page.locator('#application')).toBeVisible(); }
async function sessions(request) { return (await (await request.get('/api/sessions', { headers })).json()).sessions; }

test('real shell, split, rename, detach, and fresh browser reattachment', async ({page,request,browser},testInfo) => {
  await login(page);
  await page.getByRole('button',{name:'New workspace',exact:true}).click();
  await page.getByLabel('Workspace name').fill('Browser integration');
  await page.locator('#dialog-submit').click();
  await expect(page.locator('.terminal-pane')).toHaveCount(1);
  await expect(page.locator('.pane-state').first()).toHaveText('live');
  await page.locator('.xterm-helper-textarea').first().focus();
  await page.keyboard.type("printf 'R_%s_DONE\\n' 7316");
  await page.keyboard.press('Enter');
  let session;
  await expect.poll(async()=>{session=(await sessions(request)).find(s=>s.label==='Browser integration');return session?.blocks.length;}).toBe(1);
  const blockId=session.blocks[0].id;
  await expect.poll(async()=>(await (await request.get(`/api/sessions/${session.id}/blocks/${blockId}/capture`,{headers})).json()).content).toContain('R_7316_DONE');
  await page.getByRole('button',{name:'Split side by side',exact:true}).click();
  await expect(page.locator('.terminal-pane')).toHaveCount(2);
  await page.locator('.pane-title').first().dblclick();
  await page.getByLabel('Terminal name').fill('Persistent shell');await page.locator('#dialog-submit').click();
  await expect(page.locator('.pane-title').first()).toHaveText('Persistent shell');
  await page.screenshot({path:testInfo.outputPath('workspace-desktop.png')});
  await page.locator('.terminal-pane').first().getByRole('button',{name:'Detach or reconnect this view'}).click();
  await expect(page.locator('.pane-state').first()).toHaveText('detached');
  const running=(await sessions(request)).find(s=>s.id===session.id).blocks.find(b=>b.id===blockId);
  expect(running.state).toBe('Running');
  await page.close();
  const resumed=await browser.newPage();await login(resumed);
  await expect(resumed.locator('.pane-title').first()).toHaveText('Persistent shell');
  await expect(resumed.locator('.pane-state').first()).toHaveText('live');
  await resumed.locator('.xterm-helper-textarea').first().focus();await resumed.keyboard.type("printf 'R_%s_DONE\\n' 9021");await resumed.keyboard.press('Enter');
  await expect.poll(async()=>(await (await request.get(`/api/sessions/${session.id}/blocks/${blockId}/capture`,{headers})).json()).content).toContain('R_9021_DONE');
  await resumed.close();await request.delete(`/api/sessions/${session.id}`,{headers});
});

test('scoped read-only share strips fragment, disables mutation, and adapts to mobile', async ({page,request}) => {
  const session=await (await request.post('/api/sessions',{headers,data:{label:'Shared observation'}})).json();
  await request.post(`/api/sessions/${session.id}/blocks`,{headers,data:{label:'Shared shell',transport:'pty'}});
  const share=await (await request.post('/api/call',{headers,data:{method:'share.create',arguments:{sessionId:session.id,role:'observer',lifetimeMinutes:5}}})).json();
  await page.setViewportSize({width:390,height:844});
  await page.goto(`/#${new URLSearchParams({token:share.token,session:session.id})}`);
  await expect(page.locator('#application')).toBeVisible();await expect(page.locator('.pane-state')).toHaveText('view only');
  expect(new URL(page.url()).hash).toBe('');
  await expect(page.locator('#new-shell')).toBeDisabled();await expect(page.locator('#share-session')).toBeDisabled();
  await expect(page.locator('.touch-keys')).toBeVisible();
  await request.post('/api/call',{headers,data:{method:'share.revoke',arguments:{sessionId:session.id,shareId:share.id}}});
  await expect(page.locator('.pane-state')).not.toHaveText('view only');
  await page.close();await request.delete(`/api/sessions/${session.id}`,{headers});
});
