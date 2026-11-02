import {test,expect} from '@playwright/test';
import {mkdir,writeFile} from 'node:fs/promises';
import {join} from 'node:path';
const token=process.env.ROYALTERMINAL_MUX_TEST_TOKEN||'browser-test-token-012345678901234567890123456789';
const headers={Authorization:`Bearer ${token}`};
async function api(request,method,args){const r=await request.post('/api/call',{headers,data:{method,arguments:args}});expect(r.ok(),method).toBe(true);return r.json();}
async function save(page,name){const dir=process.env.WORKSPACE_REVIEW_EVIDENCE;if(!dir)return;await mkdir(dir,{recursive:true});await page.screenshot({path:join(dir,name+'.png'),animations:'disabled'});}
async function connect(page,sid,bearer){await page.goto(`/#${new URLSearchParams({token:bearer,session:sid})}`);await expect(page.locator('#application')).toBeVisible();expect(new URL(page.url()).hash).toBe('');}

// Shell output is produced by the processes below; program status is emitted by a child,
// not planted in the DOM. No remote network, Git credentials or provider keys are used.
test('workspace review: real PTYs, stable navigation, background attention, palette and revoked mobile view',async({page,request,browser})=>{
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  const s=await api(request,'session.create',{label:'Release engineering'}),sid=s.id,w=s.windows[0].id;
  let mobile;
  try{
    await api(request,'session.set_window_label',{sessionId:sid,windowId:w,label:'Development'});
    const review=await api(request,'session.new_window',{sessionId:sid,label:'Review'});
    const shell=async(label,start,geometry)=>{const r=await request.post(`/api/sessions/${sid}/blocks`,{headers,data:{label,transport:'pty',command:{fileName:'/bin/sh',arguments:['-c',start+'; exec /bin/cat']},...geometry}});expect(r.ok(),label).toBe(true);return r.json();};
    const src=await shell('Repository',"printf '\\033[1;36mSOURCE TREE\\033[0m\\n\\n'; git ls-files 'src/*/*.csproj' | head -10 | sed 's|src/||;s|/.*||'",{windowId:w});
    await shell('Runtime',"printf '\\033[1;32mRUNTIME\\033[0m\\n\\n'; uname -sm; dotnet --version; printf '\\nWorkspace host online.\\n'",{splitBlockId:src.id,orientation:'horizontal',ratio:.52});
    await shell('Recent commits',"printf '\\033[1;33mGIT HISTORY\\033[0m\\n\\n'; git log -4 --format='%h  %s' | cut -c1-62",{splitBlockId:src.id,orientation:'vertical',ratio:.52});
    const job=await shell('Release review',"printf '\\033[1;33mRELEASE REVIEW\\033[0m\\n\\nWaiting for approval.\\n\\033]7501;state=blocked:kind=permission:id=approval\\033\\\\'",{windowId:review.id});
    await expect.poll(async()=>(await api(request,'session.attention',{sessionId:sid})).counts.permission).toBe(1);
    await connect(page,sid,token);
    await expect(page.locator('.terminal-pane')).toHaveCount(3);
    await expect(page.locator('.pane-state').filter({hasText:/^live$/})).toHaveCount(3);
    const first=page.getByRole('tab',{name:'Development',exact:true}),other=page.getByRole('tab',{name:'Review',exact:true});
    await expect(other).toHaveAttribute('aria-description',/awaiting permission/);
    const element=await first.elementHandle();await first.focus();await page.waitForResponse(r=>r.url().endsWith('/api/sessions')&&r.request().method()==='GET');
    await expect(first).toBeFocused();expect(await element.evaluate(n=>n===document.activeElement&&n.isConnected)).toBe(true);
    await first.press('ArrowRight');await expect(other).toHaveAttribute('aria-selected','true');await expect(page.locator('.pane-title')).toHaveText('Release review');
    await other.press('Home');await expect(first).toHaveAttribute('aria-selected','true');await expect(page.locator('.terminal-pane')).toHaveCount(3);await save(page,'workspace-desktop');
    const sizes=await page.evaluate(()=>({viewport:[innerWidth,innerHeight],header:document.querySelector('.workspace-header').offsetHeight,tabs:document.querySelector('.workspace-toolbar').offsetHeight,panes:[...document.querySelectorAll('.terminal-pane')].map(p=>({width:p.clientWidth,height:p.clientHeight}))}));expect(sizes.header+sizes.tabs).toBeLessThan(90);
    await page.keyboard.press('Control+k');const q=page.getByRole('combobox',{name:'Search commands'});await q.fill('not-a-real-command-555');await expect(page.locator('.palette-empty')).toBeVisible();await q.press('Enter');await expect(q).toBeFocused();await q.fill('terminal');await q.press('ArrowDown');await expect(q).toHaveAttribute('aria-activedescendant',/command-option/);await save(page,'workspace-palette');await q.press('Escape');
    const share=await api(request,'share.create',{sessionId:sid,role:'observer'});
    mobile=await browser.newPage({viewport:{width:390,height:844},isMobile:true,hasTouch:true});mobile.on('pageerror',e=>errors.push(e.message));
    await connect(mobile,sid,share.token);await mobile.getByRole('tab',{name:'Review',exact:true}).click();await expect(mobile.locator('.pane-state')).toHaveText('view only');await expect(mobile.locator('[aria-label="Close process"]')).toHaveCount(0);
    await mobile.locator('#toggle-sidebar').click();await expect(mobile.locator('#drawer-shade')).toBeVisible();await mobile.keyboard.press('Escape');await expect(mobile.locator('#drawer-shade')).toBeHidden();
    expect(await mobile.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await save(mobile,'workspace-phone');
    await mobile.locator('#help-button').click();await expect(mobile.locator('#palette')).toBeVisible();await api(request,'share.revoke',{sessionId:sid,shareId:share.id});
    await expect(mobile.locator('#connect-screen')).toBeVisible();await expect(mobile.locator('dialog[open]')).toHaveCount(0);await expect(mobile.locator('.terminal-pane')).toHaveCount(0);await expect(mobile.locator('#access-token')).toHaveValue('');
    const snapshot=await api(request,'session.view',{sessionId:sid});expect(snapshot.blocks.find(b=>b.id===job.id).state).toBe('Running');expect(errors).toEqual([]);
    if(process.env.WORKSPACE_REVIEW_EVIDENCE)await writeFile(join(process.env.WORKSPACE_REVIEW_EVIDENCE,'layout.json'),JSON.stringify(sizes,null,2));
  }finally{await mobile?.close();await page.close();await api(request,'session.close',{sessionId:sid});}
});
