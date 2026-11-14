import {test,expect} from '@playwright/test';
const token=process.env.ROYALTERMINAL_MUX_TEST_TOKEN||'browser-test-token-012345678901234567890123456789';
const headers={Authorization:`Bearer ${token}`};
test('palette default works while a terminal is focused but does not override a configured shortcut',async({page,request})=>{
  const create=await request.post('/api/sessions',{headers,data:{label:'Palette shortcut'}});expect(create.ok()).toBe(true);const s=await create.json();
  const url=`/#${new URLSearchParams({token,session:s.id})}`;
  try{
    const block=await request.post(`/api/sessions/${s.id}/blocks`,{headers,data:{label:'Keyboard',transport:'pipe',command:{fileName:'/bin/cat',arguments:[]}}});expect(block.ok()).toBe(true);
    await page.goto(url);await expect(page.locator('.pane-state')).toHaveText('live');await page.locator('.xterm-helper-textarea').focus();await page.keyboard.press('Control+Shift+p');await expect(page.locator('#palette-query')).toBeFocused();await page.keyboard.press('Escape');
    // Mock just this browser's configuration read. No operator-global configuration is modified.
    const config={configuration:{actions:[],modes:[{name:'reserved',exclusive:false}],bindings:[{mode:'default',sequence:'ctrl+shift+p',enterMode:'reserved'}]}};
    await page.goto('about:blank');await page.route('**/api/config',r=>r.fulfill({json:config}));await page.goto(url);await expect(page.locator('.pane-state')).toHaveText('live');await page.locator('.xterm-helper-textarea').focus();await page.keyboard.press('Control+Shift+p');await expect(page.locator('#palette')).toBeHidden();await expect(page.locator('#chord-hint')).toContainText('reserved');
  }finally{await page.close();await request.delete(`/api/sessions/${s.id}`,{headers});}
});
