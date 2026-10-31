import {test,expect} from '@playwright/test';
const token=process.env.ROYALTERMINAL_MUX_TEST_TOKEN||'browser-test-token-012345678901234567890123456789';
async function command(page,name){await page.locator('#help-button').click();await page.locator('#palette-query').fill(name);await page.getByRole('button',{name,exact:true}).click();}
test('mobile observer sees new OSC activity and can clear it without acquiring a controller',async({page,request})=>{
 const api=async(method,args)=>{const r=await request.post('/api/call',{headers:{Authorization:`Bearer ${token}`},data:{method,arguments:args}});expect(r.ok()).toBe(true);return r.json();};
 await page.setViewportSize({width:390,height:844});const session=await api('session.create',{label:'Activities'});const sessionId=session.id;
 try{
  const b=await api('block.create',{sessionId,block:{label:'Events',transport:'pipe',command:{fileName:'/bin/cat',arguments:[]}}});const ids={sessionId,blockId:b.id};const share=await api('share.create',{sessionId,role:'observer'});
  await page.goto(`/#${new URLSearchParams({token:share.token,session:sessionId})}`);await expect(page.locator('.pane-state')).toHaveText('view only');
  const subscribed=page.waitForRequest(r=>r.url().endsWith('/api/call') && r.postDataJSON()?.method==='terminal.events' && r.postDataJSON().arguments.after!==undefined);
  await command(page,'Watch terminal activity');await subscribed;
  const osc='\x1b]777;notify;Build;Release output ready\x07';await api('block.input',{...ids,data:Buffer.from(osc).toString('base64')});
  await expect(page.locator('#toast')).toContainText('Release output ready');await command(page,'Read recent terminal activity');await expect(page.getByLabel('Recent activity',{exact:true})).toHaveValue(/Build · Release output ready/);await page.locator('#dialog-submit').click();
  await command(page,'Stop terminal activity alerts');await command(page,'Read recent terminal activity');await expect(page.getByLabel('Recent activity',{exact:true})).toHaveValue(/No activity/);
  const view=await api('session.view',{sessionId});expect(view.blocks[0].controllerId??null).toBeNull();
 }finally{await page.close();await api('session.close',{sessionId});}
});
