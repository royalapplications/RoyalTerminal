#!/usr/bin/env python3
"""Acceptance against .NET, Lua 5.1, HTTP over a private Unix socket, and a real binary pipe child."""
from contextlib import ExitStack
import base64, http.client, json, os, pathlib, secrets, socket, subprocess, tempfile, time
ROOT=pathlib.Path(__file__).resolve().parents[2]
CLI=ROOT/'samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll'
HOST=ROOT/'samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll'
CREATOR='com.superlogical.terminal'

def run():
    with tempfile.TemporaryDirectory() as tmp, ExitStack() as cleanup:
        private=pathlib.Path(tmp); endpoint=str(private/'mux.sock');token=secrets.token_hex(32)
        init=private/'init.lua';init.write_text("rex.action{name='read.title',run=function(ctx,args) return assert(rex.block.call('com.superlogical.terminal','title')).title end}")
        env={**os.environ,'ROYALTERMINAL_MUX_SOCKET':endpoint,'ROYALTERMINAL_MUX_TOKEN':token,'ROYALTERMINAL_MUX_LUA_INIT':str(init)}
        class Connection(http.client.HTTPConnection):
            def connect(self):
                self.sock=socket.socket(socket.AF_UNIX);self.sock.settimeout(8);self.sock.connect(endpoint)
        def req(path,payload=None,key=token,expect=200):
            c=Connection('localhost');data=json.dumps(payload).encode() if payload is not None else None
            try:
                c.request('GET' if data is None else 'POST',path,data,{'Authorization':'Bearer '+key,'Content-Type':'application/json'})
                r=c.getresponse();value=json.loads(r.read());assert r.status==expect,(r.status,expect,value);return value
            finally:c.close()
        def call(name,args,key=token,expect=200):return req('/api/call',{'method':name,'arguments':args},key,expect)
        def poll(fn):
            end=time.monotonic()+10
            while time.monotonic()<end:
                v=fn()
                if v:return v
                time.sleep(.04)
            raise TimeoutError('state did not converge')
        host=subprocess.Popen(['dotnet',HOST,'--Kestrel:Endpoints:Local:Url=http://unix:'+endpoint],env=env,stdout=subprocess.DEVNULL);cleanup.callback(lambda:(host.terminate(),host.wait(timeout=10)))
        poll(lambda:os.path.exists(endpoint));req('/healthz')
        names={m['name']:m for m in req('/api/methods')['methods']};required='format process pwd title size write reset resize set_theme program_status list_dir'.split()
        for name in required:
            schema=names[CREATOR+'.'+name]['inputSchema']['properties']['args'];assert schema['additionalProperties'] is False
        s=call('session.create',{'label':'protocol'})['id']
        py="import os,sys;sys.stdout.buffer.write(b'\\033]2;typed-title\\a\\033]7;file:///sandbox/project\\a\\033]7501;state=working:id=build\\aBEFORE '+str(os.getpid()).encode()+b'\\n');sys.stdout.buffer.flush();data=sys.stdin.buffer.read(3);print('HEX='+data.hex(),flush=True);sys.exit(37)"
        b=call('block.create',{'sessionId':s,'block':{'label':'child','transport':'pipe','command':{'fileName':'/usr/bin/python3','arguments':['-u','-c',py]}}})['id']
        target={'sessionId':s,'blockId':b};obs=call('share.create',{'sessionId':s,'role':'observer'})['token']
        def block(name,args=None,key=token,expect=200):return call(CREATOR+'.'+name,{**target,'args':args or {}},key,expect)
        poll(lambda:block('title',key=obs)['title']=='typed-title');snap=block('format',{'trim':True,'unwrap':True},obs)
        pid=int(snap['content'].split('BEFORE ')[-1].strip());assert block('process',key=obs)['child']['pid']==pid
        assert block('pwd',key=obs)['pwd'].endswith('/sandbox/project');assert block('program_status',key=obs)['records'][0]['id']=='build'
        assert call('lua.invoke',{**target,'name':'read.title'})=='typed-title'
        assert call('block.method',{**target,'method':'title','creator':CREATOR},obs)['title']=='typed-title'
        call('block.method',{**target,'method':'share.create','creator':CREATOR},obs,404)
        (private/'dirs').mkdir();(private/'dirs'/'beta').mkdir();(private/'dirs'/'alpha').mkdir();(private/'dirs'/'ignored').write_text('file');(private/'dirs'/'link').symlink_to(private/'dirs'/'alpha',target_is_directory=True)
        assert block('list_dir',{'path':str(private/'dirs')})['directories']==['alpha','beta']
        for name,args in [('reset',{}),('resize',{'columns':70,'rows':20}),('write',{'text':'poison'}),('set_theme',{'cursor':'#abcdef'}),('list_dir',{'path':tmp})]:block(name,args,obs,403)
        block('set_theme',{'foreground':'#00FF00','cursor':'#11\u001b]52;evil'},expect=400)
        block('write',{'text':'bad','data':'AAA='},expect=400);block('resize',{'columns':70,'rows':0},expect=400)
        block('set_theme',{'foreground':'#44AAFF','background':'#000000','cursor':'#ffbb00'});block('resize',{'columns':70,'rows':20,'widthPixels':700,'heightPixels':400});assert block('size')['rows']==20
        block('reset');assert 'BEFORE' not in block('format')['content'];assert block('process')['child']['pid']==pid
        e={**env,'ROYALTERMINAL_MUX_SESSION':s,'ROYALTERMINAL_MUX_BLOCK':b,'ROYALTERMINAL_MUX_TOKEN':obs}
        script="local t=assert(rex.block.call('com.superlogical.terminal','size'));return {r=t.rows,c=t.columns}"
        output=subprocess.run(['dotnet',CLI,'do','-e',script],env=e,capture_output=True,text=True,timeout=10,check=True);assert json.loads(output.stdout)=={'r':20,'c':70}
        block('write',{'data':base64.b64encode(bytes([0,255,65])).decode()});poll(lambda:'HEX=00ff41' in block('format')['content']);poll(lambda:block('process')['child'] is None)
        assert call('session.view',{'sessionId':s})['blocks'][0]['exitCode']==37
        print('11 discovered typed methods, real child PID and exit, exact binary input, display-only reset, scoped denials, config and CLI Lua verified.')
if __name__=='__main__': run()
