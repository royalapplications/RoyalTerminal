import {test,expect} from '@playwright/test';
const token=process.env.ROYALTERMINAL_MUX_TEST_TOKEN||'browser-test-token-012345678901234567890123456789';
const headers={Authorization:`Bearer ${token}`};
const gate=()=>{let release;const promise=new Promise(r=>{release=r;});return {promise,release};};
async function call(request,method,args){const r=await request.post('/api/call',{headers,data:{method,arguments:args}});expect(r.ok(),method).toBe(true);return r.json();}
async function pipe(request,session,label){return call(request,'block.create',{sessionId:session.id,block:{label,transport:'pipe',command:{fileName:'/bin/cat',arguments:[]}}});}
async function open(page,s){const loaded=page.waitForResponse(r=>r.url().endsWith('/api/call')&&r.request().postDataJSON()?.method==='lua.actions');await page.goto(`/#${new URLSearchParams({token,session:s.id})}`);await loaded;}
test('palette searches operator metadata; the dialog sends typed arguments to the advertised generation',async({page,request})=>{
 const s=await call(request,'session.create',{label:'palette contract'});const calls=[];
 const manifest={generation:'catalog-v1',actions:['mark'],actionDetails:{mark:{title:'Mark workspace',category:'Build tools',keywords:'release annotate',description:'Attach a build number',inputSchema:{type:'object',required:['build'],properties:{build:{type:'integer'}}}}},keymap:{modes:[],bindings:[]}};
 try{await page.route('**/api/call',r=>{const f=r.request().postDataJSON();if(f.method==='lua.actions')return r.fulfill({json:manifest});if(f.method==='lua.invoke'){calls.push(f.arguments);return r.fulfill({json:true});}return r.continue();});await open(page,s);
 await page.keyboard.press('Control+k');await page.locator('#palette-query').fill('tools annotate');await page.getByRole('option',{name:'Mark workspace',exact:true}).click();await expect(page.getByLabel('Accepted arguments')).toHaveValue(/integer/);await page.getByLabel('Arguments (JSON object)').fill('{"build":17}');await page.getByRole('button',{name:'Run action',exact:true}).click();await expect.poll(()=>calls.length).toBe(1);expect(calls[0]).toMatchObject({name:'mark',generation:'catalog-v1',sessionId:s.id,arguments:{build:17}});
 }finally{await page.close();await call(request,'session.close',{sessionId:s.id});}
});
test('a delayed decline cannot reorder normal input or deliver a key to the newly focused block',async({page,request})=>{
 const s=await call(request,'session.create',{label:'ordered input'});let pause=gate(),started=gate();
 try{const a=await pipe(request,s,'first'),b=await pipe(request,s,'second');const manifest={generation:'order-v1',actions:[],keymap:{modes:[],bindings:[{mode:'default',sequence:'[keya]',callback:1}]}};
 await page.route('**/api/call',async r=>{const f=r.request().postDataJSON();if(f.method==='lua.actions')return r.fulfill({json:manifest});if(f.method==='lua.key'){const held=pause.promise;started.release();await held;return r.fulfill({json:false});}return r.continue();});await open(page,s);
 const pane=id=>page.locator(`[data-block-id="${id}"]`),text=id=>pane(id).locator('textarea.xterm-helper-textarea');for(const id of [a.id,b.id])await expect(pane(id).locator('.pane-state')).toHaveText('live');
 const capture=async id=>(await call(request,'terminal.format',{sessionId:s.id,blockId:id,format:'text'})).content.trim();
 const emit=()=>text(a.id).evaluate(el=>el.dispatchEvent(new KeyboardEvent('keydown',{key:'ą',code:'KeyA',bubbles:true,cancelable:true})));
 await text(a.id).focus();await emit();await started.promise;await page.keyboard.type('x');expect(await capture(a.id)).toBe('');pause.release();await expect.poll(()=>capture(a.id)).toBe('ąx');
 pause=gate();started=gate();await emit();await started.promise;await text(b.id).focus();await page.keyboard.type('y');pause.release();await expect(page.locator('#toast')).toContainText('control');await expect.poll(()=>capture(b.id)).toBe('y');expect(await capture(a.id)).toBe('ąx');
 }finally{pause.release();await page.close();await call(request,'session.close',{sessionId:s.id});}
});
