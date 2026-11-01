import {chromium,expect,test} from '@playwright/test';
import {mkdtemp,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

test('workspace worker handles browser-dispatched push without an open workspace page', async({baseURL})=>{
  const profile=await mkdtemp(join(tmpdir(),'rt-push-'));
  const ctx=await chromium.launchPersistentContext(profile,{channel:'chromium',headless:true});
  try {
    const origin=new URL(baseURL).origin, root=new URL('./',baseURL).href;
    await ctx.grantPermissions(['notifications'],{origin});
    const probe=ctx.pages()[0]; await probe.goto(new URL('healthz',root).href);
    const cdp=await ctx.newCDPSession(probe);
    const id=new Promise(resolve=>cdp.on('ServiceWorker.workerRegistrationUpdated',({registrations})=>{for(const item of registrations)if(item.scopeURL===root&&!item.isDeleted) resolve(item.registrationId);}));
    await cdp.send('ServiceWorker.enable');
    const page=await ctx.newPage();await page.goto(root);
    await page.evaluate(async()=>{await navigator.serviceWorker.register('./workspace-alert-worker.js');await navigator.serviceWorker.ready;});
    const registrationId=await id;
    await page.close();expect(ctx.pages().map(p=>p.url())).toEqual([new URL('healthz',root).href]);
    // web origin remains observable via a JSON diagnostic page; no workspace client or credentials remain open.
    await cdp.send('ServiceWorker.deliverPushMessage',{origin,registrationId,data:'DO_NOT_SHOW_PRIVATE_COMMAND'});
    await expect.poll(()=>probe.evaluate(async()=>{const r=await navigator.serviceWorker.getRegistration();return (await r.getNotifications()).map(n=>[n.title,n.body]);})).toEqual([['RoyalTerminal','A terminal needs your attention. Open your workspace for details.']]);
  } finally {await ctx.close();await rm(profile,{recursive:true,force:true});}
});
