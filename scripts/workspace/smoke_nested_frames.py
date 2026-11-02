"""Real local broker, zero external services, explicit timeouts and no ambient workspace credentials."""
import json, os, secrets, subprocess, time

def verify(tmp, server, cli_dll):
    socket=tmp/'local.sock'
    env={k:v for k,v in os.environ.items() if not k.startswith('ROYALTERMINAL_')}
    env.update(ROYALTERMINAL_MUX_TOKEN=secrets.token_hex(32),ROYALTERMINAL_MUX_SOCKET=str(socket))
    process=subprocess.Popen(['dotnet',str(server),'--Kestrel:Endpoints:Local:Url=http://unix:'+str(socket)],env=env,stdout=subprocess.DEVNULL)
    try:
        for _ in range(200):
            if socket.exists():break
            if process.poll() is not None:raise RuntimeError('broker stopped')
            time.sleep(.05)
        def cli(*args,reader=None,reject=False):
            config=env if reader is None else {**env,'ROYALTERMINAL_MUX_TOKEN':reader}
            p=subprocess.run(['dotnet',str(cli_dll),*map(str,args)],env=config,text=True,capture_output=True,timeout=20)
            if reject:assert p.returncode!=0,args[0];return None
            assert p.returncode==0,(args[0],p.stderr[-1000:]);return json.loads(p.stdout)
        def api(method,reader=None,reject=False,**kw):return cli('api','call',method,json.dumps(kw),reader=reader,reject=reject)
        s=api('session.create',label='Float fixture');sid=s['id'];w=s['windows'][0]['id'];env['ROYALTERMINAL_MUX_SESSION']=sid
        def view():return api('session.view',sessionId=sid)
        def lua(source):return cli('do','-e',source)
        leaf="rex.layout.block{flavor='com.superlogical.terminal.shell',options={command={'/bin/cat'}}}"
        receipt=lua("local p="+leaf+";return assert(rex.session.new_layer{window_id='"+w+"',bounds={x=1000,y=1000,width=8000,height=8000},layout=rex.layout.horizontal(.5,p,rex.layout.vertical(.25,p,p))})")
        a,b,c=receipt['block_ids'];group=receipt['layer_id'];assert group not in (a,b,c)
        before=view();frame=before['windows'][0];assert frame.get('layout') is None and len(frame['floatingLayers'])==1 and frame['floatingOrder']==[a,b,c]
        assert [x['floatingBounds']['height'] for x in before['blocks']]==[8000,2000,6000]
        dimensions=[x['dimensions'] for x in before['blocks']]
        def set_zoom(val):return lua("return assert(rex.session.zoom_block{block_id='"+c+"',zoom="+val+"})")
        set_zoom('true');set_zoom('true');after=view();assert after['revision']==before['revision']+1 and after['windows'][0]['zoomedBlockId']==c
        assert dimensions==[x['dimensions'] for x in after['blocks']] and not any(x.get('controllerId') for x in after['blocks'])
        token=api('share.create',sessionId=sid,role='observer')['token'];api('session.zoom_block',reader=token,reject=True,sessionId=sid,blockId=b,zoom=True)
        # Persist the nested tree and zoom, then restore to independent fresh PTYs with new IDs.
        archive=tmp/'layout.rtmux';subprocess.run(['dotnet',str(cli_dll),'archive','save',sid,str(archive)],env=env,check=True,capture_output=True,timeout=20)
        result=cli('restore-layout',archive,'Recovered','--fresh-shells');copy=api('session.view',sessionId=result['sessionId']);cw=copy['windows'][0];assert cw['zoomedBlockId']==result['blocks'][c] and cw['floatingLayers'][0]['id']==result['layers'][group] and cw['floatingLayers'][0]['layout']['second']['second']['blockId']==result['blocks'][c]
        set_zoom('false');set_zoom('false');assert view()['revision']==after['revision']+1
        api('session.resize_pane',sessionId=sid,blockId=b,orientation='vertical',ratio=.5);assert view()['blocks'][1]['floatingBounds']['height']==4000
        lua("return assert(rex.session.new_split{block_id='"+a+"',layout="+leaf+"})");assert len(view()['windows'][0]['floatingOrder'])==4
        lua("return assert(rex.session.close_layer{layer_id='"+group+"'})");assert not view()['blocks']
        print('PASS: real nested PTYs, inner divider, Lua explicit zoom/unzoom, read-only denial, fresh-shell archive remap and group close.')
    finally:
        process.terminate()
        try:process.wait(timeout=8)
        except subprocess.TimeoutExpired:process.kill();process.wait()
