#!/usr/bin/env python3
"""Real Kestrel + PTY + both Lua hosts, using an isolated private Unix socket. No public network or external credentials."""
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
BIN = 'bin/Release/net10.0'
SERVER = ROOT / 'samples/RoyalTerminal.Multiplexing.Server' / BIN / 'RoyalTerminal.Multiplexing.Server.dll'
CLI = ROOT / 'samples/RoyalTerminal.Multiplexing.Cli' / BIN / 'royalterminal-mux.dll'
OPERATOR = '''
local leaf=rex.layout.block{flavor='com.superlogical.terminal.shell',options={command={'/bin/cat'}}}
rex.action{name='launch_float',run=function(ctx,args)
  local launch,err=rex.session.new_layer{window_id=args.window,layout=leaf,focus=true}
  assert(launch,err);assert(launch.focus.state=='staged');return launch
end}
'''

def run(tmp):
    sock=tmp/'broker.sock';init=tmp/'init.lua';init.write_text(OPERATOR)
    env={k:v for k,v in os.environ.items() if not k.startswith('ROYALTERMINAL_')}
    env.update(ROYALTERMINAL_MUX_TOKEN=secrets.token_hex(32),ROYALTERMINAL_MUX_SOCKET=str(sock),ROYALTERMINAL_MUX_LUA_INIT=str(init))
    p=subprocess.Popen(['dotnet',SERVER,'--Kestrel:Endpoints:Local:Url=http://unix:'+str(sock)],env=env,stdout=subprocess.DEVNULL)
    try:
        for _ in range(150):
            if sock.exists(): break
            if p.poll() is not None: raise RuntimeError('broker exited before startup')
            time.sleep(.05)
        def cli(*args,extra=None,fail=False):
            r=subprocess.run(['dotnet',CLI,*args],env={**env,**(extra or {})},capture_output=True,text=True,timeout=15)
            if fail: assert r.returncode!=0;return None
            assert r.returncode==0,(args[0],r.stderr[-1200:]);return json.loads(r.stdout)
        def api(method,**args): return cli('api','call',method,json.dumps(args))
        s=api('session.create',label='acceptance');sid=s['id'];w=s['windows'][0]['id'];ctx={'ROYALTERMINAL_MUX_SESSION':sid}
        def session(): return api('session.view',sessionId=sid)
        def script(source,extra=None): return cli('do','-e',source,extra={**ctx,**(extra or {})})
        leaf="{flavor='com.superlogical.terminal.shell',options={command={'/bin/cat'}}}"
        hidden=script('return assert(rex.session.new_block{layout='+leaf+'})');hidden_id=hidden['block_id']
        before=session();assert before['blocks'][0]['placement']=='Unplaced' and before['windows'][0].get('layout') is None
        view=api('client.negotiate_view',sessionId=sid,kind='mobile',label='target',maxVersion=3);cid=view['view']['id'];assert view['completionProtocol']==1
        other=api('client.open_view',sessionId=sid,kind='native',label='other');reader=dict(sessionId=sid,clientId=cid,readerKey=view['readerKey'])
        # Ambiguity is rejected before starting a process or mutating workspace revision.
        assert script("local v,e=rex.session.new_window{focus=true,layout="+leaf+"};return not v and e~=nil") is True
        assert session()['revision']==before['revision']
        created=api('lua.invoke',name='launch_float',sessionId=sid,clientId=cid,arguments={'window':w}); floating=created['block_id'];ticket=created['focus']
        assert ticket['state']=='queued' and ticket['sequence']==1 and ticket['clientId']==cid
        state=session();assert state['windows'][0].get('layout') is None and state['windows'][0]['floatingOrder']==[floating]
        delivery=api('client.read_view',**reader,after=0,timeoutMs=0);assert delivery['batches'][0]['actions'][0]['blockId']==floating
        api('client.finish_view',**reader,sequence=1,applied=1)
        result=script('return assert(rex.client.result({sessionId=rex.session_id,clientId="'+cid+'",sequence=1},0))');assert result['state']=='applied'
        other_page=api('client.read_view',sessionId=sid,clientId=other['view']['id'],readerKey=other['readerKey'],after=0,timeoutMs=0);assert other_page['batches']==[]
        receipt=script('return assert(rex.session.new_window{focus=true,layout='+leaf+'})',{'ROYALTERMINAL_MUX_CLIENT':cid});assert receipt['focus']['sequence']==2
        new=script('return assert(rex.session.new_split{focus=true,block_id="'+receipt['block_ids'][0]+'",layout='+leaf+'})',{'ROYALTERMINAL_MUX_CLIENT':cid});assert new['focus']['sequence']==3
        old=session();cli('api','call','session.close_layer',json.dumps(dict(sessionId=sid,blockId=hidden_id,expectedRevision=old['revision'])),fail=True)
        assert session()['revision']==old['revision'];script('return assert(rex.session.close_layer{layer_id="'+floating+'"})')
        assert floating not in [b['id'] for b in session()['blocks']] and hidden_id in [b['id'] for b in session()['blocks']]
        observer=api('share.create',sessionId=sid,role='observer')['token'];old=session();cli('api','call','session.new_block',json.dumps(dict(sessionId=sid,layout={'flavor':'com.superlogical.terminal.shell'})),extra={'ROYALTERMINAL_MUX_TOKEN':observer},fail=True)
        assert old['revision']==session()['revision']
        print('PASS: unplaced launch, floating lifecycle, stale-kind safety, two Lua runtimes, three focused launches, receiver outcome and observer denial.')
    finally:
        p.terminate()
        try: p.wait(timeout=10)
        except subprocess.TimeoutExpired: p.kill();p.wait()

if __name__=='__main__':
    with tempfile.TemporaryDirectory() as d: run(Path(d))
