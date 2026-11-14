import {test,expect} from '@playwright/test';
const token=process.env.ROYALTERMINAL_MUX_TEST_TOKEN||'browser-test-token-012345678901234567890123456789';
test('Lua chord sends the key using the already-held lease; exclusive mode suppresses unrelated input',async({page,request})=>{
  const api=async(method,args)=>{const r=await request.post('/api/call',{headers:{Authorization:`Bearer ${token}`},data:{method,arguments:args}});expect(r.ok()).toBe(true);return r.json();};
  const s=await api('session.create',{label:'Lua shortcut'});const block=await api('block.create',{sessionId:s.id,block:{label:'pipe',transport:'pipe',command:{fileName:'/bin/cat',arguments:[]}}});
  try{
    const manifest={generation:'ui-fixture',actions:[],keymap:{modes:[{name:'send',exclusive:true}],bindings:[{mode:'default',sequence:'ctrl+j',action:'client.mode.enter',args:{name:'send',once:true}},{mode:'send',sequence:'g>g',action:'pane.send_key',args:{key:'k'}}]}};
    await page.route('**/api/call',async route=>{if(route.request().postDataJSON()?.method==='lua.actions')return route.fulfill({json:manifest});return route.continue();});
    const loaded=page.waitForResponse(r=>r.url().endsWith('/api/call')&&r.request().postDataJSON()?.method==='lua.actions');
    await page.goto(`/#${new URLSearchParams({token,session:s.id})}`);await loaded;await expect(page.locator('.pane-state')).toHaveText('live');await page.locator('.xterm-helper-textarea').focus();
    await page.keyboard.press('Control+j');await expect(page.locator('#chord-hint')).toContainText('send');await page.keyboard.press('x');await page.keyboard.press('g');await page.keyboard.press('g');
    await expect.poll(async()=>(await api('terminal.format',{sessionId:s.id,blockId:block.id,format:'text'})).content.trim()).toBe('k');
    const state=await api('session.view',{sessionId:s.id});expect(state.blocks[0].attachmentCount).toBe(1);expect(state.blocks[0].controllerId).toBeTruthy();
  }finally{await page.close();await api('session.close',{sessionId:s.id});}
});
