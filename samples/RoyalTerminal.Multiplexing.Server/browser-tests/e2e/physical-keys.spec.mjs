import {test,expect} from '@playwright/test';
const token=process.env.ROYALTERMINAL_MUX_TEST_TOKEN||'browser-test-token-012345678901234567890123456789';
test('physical shortcut dispatch and declined callbacks use the original layout text and no extra PTY lease',async({page,request})=>{
  const api=async(method,args)=>{const response=await request.post('/api/call',{headers:{Authorization:`Bearer ${token}`},data:{method,arguments:args}});expect(response.ok()).toBe(true);return response.json();};
  const session=await api('session.create',{label:'physical keys'});
  try{
    const b=await api('block.create',{sessionId:session.id,block:{label:'echo',transport:'pipe',command:{fileName:'/bin/cat',arguments:[]}}});
    const manifest={generation:'fixture',actions:[],keymap:{modes:[],bindings:[{mode:'default',sequence:'[keya]',callback:1},{mode:'default',sequence:'[keyb]',action:'pane.send_key',args:{key:'z'}}]}};
    let declined=0;
    await page.route('**/api/call',route=>{switch(route.request().postDataJSON()?.method){case 'lua.actions':return route.fulfill({json:manifest});case 'lua.key':declined++;return route.fulfill({json:false});default:return route.continue();}});
    const loaded=page.waitForResponse(r=>r.request().postDataJSON?.() && r.url().endsWith('/api/call') && r.request().postDataJSON().method==='lua.actions');
    await page.goto(`/#${new URLSearchParams({token,session:session.id})}`);await loaded;await expect(page.locator('.pane-state')).toHaveText('live');
    const term=page.locator('.xterm-helper-textarea');await term.focus();
    // Synthetic key and code deliberately disagree, as on a remapped keyboard. An unhandled synthetic DOM event cannot produce native text input.
    const emit=(code,key)=>term.evaluate((el,{code,key})=>el.dispatchEvent(new KeyboardEvent('keydown',{code,key,bubbles:true,cancelable:true})),{code,key});
    const screen=async()=>(await api('terminal.format',{sessionId:session.id,blockId:b.id,format:'text'})).content.trim();
    await emit('KeyA','ą');await expect.poll(screen).toBe('ą');expect(declined).toBe(1);await emit('KeyB','x');await expect.poll(screen).toBe('ąz');
    expect((await api('session.view',{sessionId:session.id})).blocks[0].attachmentCount).toBe(1);
  }finally{await page.close();await api('session.close',{sessionId:session.id});}
});
