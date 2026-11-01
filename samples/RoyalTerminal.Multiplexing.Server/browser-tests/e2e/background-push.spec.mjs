import {test,expect} from '@playwright/test';
// Chromium delivers a protocol push to the registered service worker with no workspace tab. This does not use a vendor delivery network.
test('push wakes the workspace worker after the last workspace page closes and reveals no terminal text',async({browser,baseURL})=>{
  const context=await browser.newContext({permissions:['notifications']});
  try{
    const blank=await context.newPage();const page=await context.newPage();await page.goto(baseURL);
    const cdp=await context.newCDPSession(blank);await cdp.send('ServiceWorker.enable');
    const update=new Promise(resolve=>cdp.on('ServiceWorker.workerRegistrationUpdated',({registrations})=>{const r=registrations.find(r=>r.scopeURL===new URL('./',baseURL).href&&!r.isDeleted);if(r)resolve(r.registrationId);}));
    await page.evaluate(async()=>{await navigator.serviceWorker.register('/workspace-alert-worker.js');await navigator.serviceWorker.ready;});const registrationId=await update;
    await page.close();expect(context.pages().some(p=>p.url().startsWith(baseURL))).toBe(false);
    await cdp.send('ServiceWorker.deliverPushMessage',{origin:new URL(baseURL).origin,registrationId,data:'DO_NOT_SHOW_PRIVATE_COMMAND'});
    const worker=context.serviceWorkers().find(w=>w.url().endsWith('/workspace-alert-worker.js'))??await context.waitForEvent('serviceworker');
    await expect.poll(()=>worker.evaluate(async()=>(await self.registration.getNotifications()).map(n=>[n.title,n.body]))).toEqual([['RoyalTerminal','A terminal needs your attention. Open your workspace for details.']]);
  }finally{await context.close();}
});
