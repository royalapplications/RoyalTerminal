#!/usr/bin/env python3
"""Exercise production CLI, both Lua hosts, Kestrel and real PTYs through an isolated Unix socket."""
import json, os, secrets, subprocess, tempfile, time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
CLI=ROOT/'samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll'
HOST=ROOT/'samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll'
LUA='''
rex.action{name='bad',run=function() rex.client.queue('client.sidebar.toggle');error('fail frame') end}
rex.action{name='ordered',run=function(ctx,args)
  assert(ctx.client_id==args.client); rex.session.focus_block{block_id=args.block}
  local v,e=rex.invoke('bad');assert(v==nil and e);rex.client.queue('pane.zoom');return 'ok'
end}
rex.action{name='discover',run=function(ctx)
  local catalog=assert(rex.client.actions());assert(catalog.clientId==ctx.client_id)
  return catalog
end}
'''
with tempfile.TemporaryDirectory() as folder:
    d=Path(folder);socket=d/'mux.sock';config=d/'init.lua';config.write_text(LUA)
    env={k:v for k,v in os.environ.items() if not k.startswith('ROYALTERMINAL_')}
    env.update(ROYALTERMINAL_MUX_TOKEN=secrets.token_hex(32),ROYALTERMINAL_MUX_SOCKET=str(socket),ROYALTERMINAL_MUX_LUA_INIT=str(config))
    server=subprocess.Popen(['dotnet',HOST,'--Kestrel:Endpoints:Local:Url=http://unix:'+str(socket)],env=env,stdout=subprocess.DEVNULL)
    try:
        for _ in range(300):
            if socket.exists():break
            if server.poll() is not None:raise RuntimeError('Host failed to start')
            time.sleep(.05)
        def cmd(*args,code=0,extra=None):
            p=subprocess.run(['dotnet',CLI,*args],env={**env,**(extra or {})},capture_output=True,text=True,timeout=20)
            assert p.returncode==code,(args[0],p.returncode,p.stderr[-1200:]);return p.stdout.strip()
        def api(method,**args):return json.loads(cmd('api','call',method,json.dumps(args)))
        launch=json.loads(cmd('new','Project','--json','--','/bin/sleep','120'));s=launch['session_id'];b=launch['block_ids'][0]
        def open_view(label):return api('client.open_view',sessionId=s,label=label,kind='browser')
        a,c=open_view('alpha'),open_view('beta');aid=a['view']['id'];j={'sessionId':s,'clientId':aid,'readerKey':a['readerKey']}
        def poll(after):return api('client.read_view',**j,after=after,timeoutMs=0)
        assert len(json.loads(cmd('views','-s',s)))==2
        before=len(api('session.view',sessionId=s)['blocks']);cmd('split','-s',s,'-b',b,'--focus=true','--','/bin/true',code=3)
        assert len(api('session.view',sessionId=s)['blocks'])==before
        ticket=json.loads(cmd('focus','-s',s,'-C','alpha','-b',b));assert ticket['sequence']==1
        first=poll(0);assert poll(0)==first;assert first['batches'][0]['actions'][0]['blockId']==b
        assert api('client.read_view',sessionId=s,clientId=c['view']['id'],readerKey=c['readerKey'],after=0,timeoutMs=0)['batches']==[]
        assert api('lua.invoke',sessionId=s,clientId=aid,name='ordered',arguments={'client':aid,'block':b})=='ok'
        batch=poll(1)['batches'];assert len(batch)==1 and [x['name'] for x in batch[0]['actions']]==['pane.focus','pane.zoom']
        ctx={'ROYALTERMINAL_MUX_CLIENT':aid,'ROYALTERMINAL_MUX_SESSION':s}
        cmd('do','-e',"return rex.client.queue('client.tab.next')",extra=ctx)
        assert poll(2)['batches'][0]['actions'][0]['name']=='client.tab.next'
        r=json.loads(cmd('split','-s',s,'-b',b,'-C',aid,'--focus=true','--json','--','/bin/sleep','120'))
        assert poll(3)['batches'][0]['actions'][0]['blockId']==r['block_ids'][0]
        cmd('run','-s',s,'-C',aid,'--focus=true','--wait','--for','5s','--','/bin/sh','-c','exit 37',code=37)
        assert poll(4)['cursor']==5
        api('client.close_view',**j);after=len(api('session.view',sessionId=s)['blocks']);cmd('split','-s',s,'-b',b,'-C',aid,'--focus=true','--','/bin/true',code=3)
        assert len(api('session.view',sessionId=s)['blocks'])==after

        legacy=api('client.open_view',sessionId=s,label='Old',kind='native');modern=api('client.open_navigator',sessionId=s,label='Desk')
        mid=modern['view']['id'];lid=legacy['view']['id'];rev=api('session.view',sessionId=s)['revision']
        cat=json.loads(cmd('-C','Desk','actions','-s',s,'--json'))
        assert cat==json.loads(cmd('view-actions','-s',s,'-C',mid))==json.loads(cmd('actions','-C',mid,'-s',s))
        assert cat['sessionId']==s and cat['clientId']==mid and cat['navigationVersion']==2 and 'readerKey' not in cat
        assert modern['readerKey'] not in json.dumps(cat) and len(cat['forms'])==5
        assert json.loads(cmd('view-actions','-C',lid,'-s',s))['navigationVersion']==1
        assert len(json.loads(cmd('view-actions','-C','Old','-s',s))['forms'])==2
        cmd('view-actions','-s',s,code=3)
        assert json.loads(cmd('do','-e','return assert(rex.client.actions())',extra={**ctx,'ROYALTERMINAL_MUX_CLIENT':mid}))==cat
        assert api('lua.invoke',sessionId=s,clientId=mid,name='discover',arguments={})==cat
        grant=api('share.create',sessionId=s,role='observer')['token']
        result=subprocess.run(['dotnet',CLI,'api','call','client.actions',json.dumps({'sessionId':s,'clientId':mid})],env={**env,'ROYALTERMINAL_MUX_TOKEN':grant},text=True,capture_output=True,timeout=15)
        assert result.returncode!=0 and '403' in result.stderr and not result.stdout.strip(),result.stderr
        assert json.loads(cmd('focus','-s',s,'-C','Desk','--direction','right'))['sequence']==1
        page=api('client.read_view',sessionId=s,clientId=mid,readerKey=modern['readerKey'],after=0,timeoutMs=0)
        assert page['batches'][0]['actions'][0]['direction']=='right'
        assert api('session.view',sessionId=s)['revision']==rev
        api('client.close_view',sessionId=s,clientId=mid,readerKey=modern['readerKey']);cmd('view-actions','-s',s,'-C',mid,code=3)
        print('Verified per-client discovery from all CLI spellings and both Lua hosts, scoped denial, withdrawal, ordered presentation batches, focused splits and real exit 37.')
    finally:
        server.terminate();server.wait(timeout=15)
