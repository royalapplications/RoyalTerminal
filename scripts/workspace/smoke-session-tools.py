#!/usr/bin/env python3
"""Operate on real server-owned shells. Check both grid geometry and the unchanged process/cursor identities."""
import http.client,json,os,pathlib,secrets,socket,subprocess,tempfile,time
root=pathlib.Path(__file__).resolve().parents[2]; base=root/'samples'; cli=base/'RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll';host=base/'RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll'
with tempfile.TemporaryDirectory() as td:
    sock=td+'/broker.sock';admin=secrets.token_hex(32);env=dict(os.environ,ROYALTERMINAL_MUX_TOKEN=admin,ROYALTERMINAL_MUX_SOCKET=sock)
    p=subprocess.Popen(['dotnet',host,'--Kestrel:Endpoints:Private:Url=http://unix:'+sock],env=env,stdout=subprocess.DEVNULL)
    try:
        for i in range(200):
            if os.path.exists(sock):break
            if p.poll() is not None:raise RuntimeError('host startup failed')
            time.sleep(.05)
        class Conn(http.client.HTTPConnection):
            def connect(self):self.sock=socket.socket(socket.AF_UNIX);self.sock.settimeout(10);self.sock.connect(sock)
        def call(name,args,key=admin,status=200):
            c=Conn('localhost')
            try:
                c.request('POST','/api/call',json.dumps({'method':name,'arguments':args}),{'Authorization':'Bearer '+key,'Content-Type':'application/json'});r=c.getresponse();v=json.loads(r.read());assert r.status==status,(name,r.status,v);return v
            finally:c.close()
        s=call('session.create',{'label':'nested'})['id'];foreign=call('session.create',{'label':'foreign'})['id'];scope={'sessionId':s}
        for label in ['A','B','C']:call('block.create',{**scope,'block':{'label':label,'transport':'pipe','command':{'fileName':'/bin/cat','arguments':[]}}})
        view=lambda:call('session.view',scope)
        start=view();win=start['windows'][0]['id'];a,b,c=[x['id'] for x in start['blocks']];getpid=lambda i:call('com.superlogical.terminal.process',{**scope,'blockId':i})['child']['pid'];pids={i:getpid(i) for i in [a,b,c]};cursors={x['id']:x['lastSequence'] for x in start['blocks']}
        read=call('share.create',{**scope,'role':'observer'})['token'];methods=[m['name'] for m in call('session.help',scope,read)['methods']];assert 'session.resize_pane' in methods and all(n.startswith('session.') for n in methods)
        assert len(call('session.describe_block',{**scope,'blockId':a},read)['methods'])==11
        assert call('session.list_clients',scope,read)['clients']==[];assert len(call('session.list_blocks',scope,read)['blocks'])==3
        call('session.list_clients',{'sessionId':foreign},read,403)
        move={**scope,'blockId':c,'targetId':b,'direction':'down','ratio':0.25,'expectedRevision':start['revision']}
        call('session.move_block',move,read,403);after=call('session.move_block',move);layout=after['windows'][0]['layout'];nested=layout['second'];assert nested['orientation']=='Vertical' and nested['second']['blockId']==c and nested['ratio']==.75
        args={**scope,'blockId':c,'orientation':'vertical','ratio':.375,'expectedRevision':after['revision']}
        resized=call('session.resize_pane',args);assert resized['layout']['second']['ratio']==.625 and resized['layout']['ratio']==layout['ratio']
        call('session.resize_pane',args,admin,409);call('session.resize_pane',{**args,'expectedRevision':view()['revision'],'ratio':2},admin,400)
        floated=call('block.place',{**scope,'blockId':c,'windowId':win,'kind':'floating'});bounds={'x':1000,'y':1300,'width':6000,'height':4400}
        updated=call('session.resize_layer',{**scope,'blockId':c,'bounds':bounds,'expectedRevision':floated['revision']});assert next(x for x in updated['blocks'] if x['id']==c)['floatingBounds']==bounds
        call('session.swap_blocks',{**scope,'firstId':a,'secondId':c});assert next(x for x in view()['blocks'] if x['id']==a)['floatingBounds']==bounds
        assert {i:getpid(i) for i in [a,b,c]}==pids;assert {x['id']:x['lastSequence'] for x in view()['blocks']}==cursors
        script='local b=assert(rex.session.describe_block{block_id=rex.block_id}); return {id=b.id,methods=#b.methods,clients=#assert(rex.session.list_clients{}).clients}'
        out=subprocess.run(['dotnet',cli,'do','-e',script],env=dict(env,ROYALTERMINAL_MUX_TOKEN=read,ROYALTERMINAL_MUX_SESSION=s,ROYALTERMINAL_MUX_BLOCK=a),capture_output=True,text=True,check=True,timeout=10)
        assert json.loads(out.stdout)=={'id':a,'methods':11,'clients':0}
        print('Nested geometry, optimistic conflicts, floating bounds, swap, source identities and read-only introspection verified against three live children.')
    finally:p.terminate();p.wait(timeout=10)
