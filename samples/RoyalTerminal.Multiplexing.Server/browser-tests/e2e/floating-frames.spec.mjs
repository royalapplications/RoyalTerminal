import {test,expect} from '@playwright/test';

// Runs with the existing opt-in browser profile, not a new per-push runner.
test('a nested float is one frame, late observers see shared zoom, and explicit restore reuses their screen',async({page,request})=>{
  const token=process.env.ROYALTERMINAL_MUX_TEST_TOKEN||'browser-test-token-012345678901234567890123456789';const headers={Authorization:`Bearer ${token}`};
  const call=async(method,args)=>{const r=await request.post('/api/call',{headers,data:{method,arguments:args}});expect(r.ok()).toBe(true);return r.json();};
  const s=await call('session.create',{label:'Floating group'}),sid=s.id,w=s.windows[0].id;
  const leaf=label=>({flavor:'com.superlogical.terminal.shell',label,options:{command:['/bin/cat']}});
  const zoom=(blockId,value)=>call('session.zoom_block',{sessionId:sid,blockId,zoom:value});const snapshot=()=>call('session.view',{sessionId:sid});
  try{
    const receipt=await call('session.new_layer',{sessionId:sid,windowId:w,layout:{orientation:'horizontal',ratio:.6,first:leaf('Editor'),second:{orientation:'vertical',ratio:.4,first:leaf('Build'),second:leaf('Logs')}}});
    const [a,b,c]=receipt.blockIds;const share=await call('share.create',{sessionId:sid,role:'observer'});await zoom(c,true);const revision=(await snapshot()).revision;await zoom(c,true);expect((await snapshot()).revision).toBe(revision);
    const sockets=[];page.on('websocket',ws=>sockets.push(ws.url()));await page.goto(`/#${new URLSearchParams({token:share.token,session:sid})}`);
    const logs=page.locator(`[data-block-id="${c}"]`);await expect(logs.locator('.pane-state')).toHaveText('view only');await expect(page.locator('.terminal-pane')).toHaveCount(1);const surface=await logs.locator('.xterm').elementHandle();
    await zoom(c,false);await zoom(c,false);const frame=page.locator('.floating-frame');await expect(frame).toHaveCount(1);await expect(frame.locator('.terminal-pane')).toHaveCount(3);await expect(frame.getByRole('separator')).toHaveCount(2);
    const A=await page.locator(`[data-block-id="${a}"]`).boundingBox(),B=await page.locator(`[data-block-id="${b}"]`).boundingBox(),C=await logs.boundingBox();expect(A.x).toBeLessThan(B.x);expect(B.x).toBeCloseTo(C.x,0);expect(B.y).toBeLessThan(C.y);expect(await surface.evaluate(el=>el.isConnected)).toBe(true);
    expect(sockets.filter(url=>url.includes(c))).toHaveLength(1);expect((await snapshot()).blocks.every(x=>x.controllerId==null)).toBe(true);
  }finally{await page.close();await request.delete(`/api/sessions/${sid}`,{headers});}
});
