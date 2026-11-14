import {chromium, expect, test} from '@playwright/test';
import {mkdtemp, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

// This exercises the production worker and the browser's Notification API without assuming an enrolled FCM account.
// The push event is injected. No provider, OS lock screen, or native APNs integration is simulated as a successful delivery.
test('registered worker shows a generic alert without a workspace tab or bearer token', async ({baseURL}) => {
  const profile = await mkdtemp(join(tmpdir(), 'rt-push-'));
  const context = await chromium.launchPersistentContext(profile, {channel:'chromium',headless:true});
  try {
    const origin=new URL(baseURL).origin;
    await context.grantPermissions(['notifications'], {origin});
    const page=context.pages()[0];
    await page.goto(baseURL);
    const ready=page.evaluate(async()=>{
      const r=await navigator.serviceWorker.register('./workspace-alert-worker.js');
      await navigator.serviceWorker.ready;
      if (!r.active || r.active.state!=='activated') await new Promise(resolve=>{
        const w=r.installing || r.waiting || r.active;
        const changed=()=>{if(w.state==='activated'){w.removeEventListener('statechange',changed);resolve();}};
        w.addEventListener('statechange',changed);changed();
      });
    });
    const worker=context.serviceWorkers().find(w=>w.url().endsWith('/workspace-alert-worker.js')) ?? await context.waitForEvent('serviceworker');
    await ready;
    const blank=await context.newPage();await page.close();expect(context.pages()).toEqual([blank]);expect(blank.url()).toBe('about:blank');
    const displayed=await worker.evaluate(async()=>{
      const ev=new PushEvent('push',{data:'never display this private terminal content'});
      // A synthetic ExtendableEvent has no browser lifetime observer. Capture the real handler's waitUntil promise.
      let shown; Object.defineProperty(ev,'waitUntil',{value:promise=>{shown=promise;}}); self.dispatchEvent(ev);
      if(!shown)throw Error('No worker registered a push handler.');await shown;
      return (await self.registration.getNotifications()).map(n=>({title:n.title,body:n.body}));
    });
    expect(displayed).toEqual([{title:'RoyalTerminal',body:'A terminal needs your attention. Open your workspace for details.'}]);
  } finally { await context.close();await rm(profile,{recursive:true,force:true}); }
});
