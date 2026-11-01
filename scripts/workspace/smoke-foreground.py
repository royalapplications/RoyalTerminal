#!/usr/bin/env python3
"""Real Unix PTY job control with startup/exit barriers, including exec of a launcher under the same PID."""
import http.client
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import tempfile
import time

SAMPLES = Path(__file__).resolve().parents[2] / 'samples'
CLI = SAMPLES / 'RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll'
HOST = SAMPLES / 'RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll'
FIXTURE = '''
import os, signal, sys, time
from pathlib import Path
p=Path(sys.argv[1])
def wait(name):
    while not (p/name).exists(): time.sleep(.02)
assert os.tcgetpgrp(0)==os.getpgrp()==os.getpid()
signal.signal(signal.SIGTTOU, signal.SIG_IGN)
(p/'root').write_text(str(os.getpid()))
wait('fork')
child=os.fork()
if child==0:
    os.setpgid(0,0); wait('finish-child'); os._exit(0)
os.setpgid(child,child); os.tcsetpgrp(0,child); (p/'leader').write_text(str(child))
wait('reclaim'); os.tcsetpgrp(0,os.getpgrp()); (p/'finish-child').touch(); os.waitpid(child,0);wait('done')
'''

def wait_file(p, watcher=None):
    stop=time.monotonic()+10
    while time.monotonic()<stop:
        if p.is_file() and p.stat().st_size:return p.read_text()
        if watcher is not None and watcher.poll() is not None:raise AssertionError(watcher.communicate()[1])
        time.sleep(.02)
    raise TimeoutError(str(p))


def run(tmp):
    sock=str(tmp/'broker.sock');token=secrets.token_hex(32)
    env={k:v for k,v in os.environ.items() if not k.startswith('ROYALTERMINAL_')}
    env.update(ROYALTERMINAL_MUX_TOKEN=token,ROYALTERMINAL_MUX_SOCKET=sock)
    class Local(http.client.HTTPConnection):
        def connect(self):
            self.sock=socket.socket(socket.AF_UNIX);self.sock.settimeout(6);self.sock.connect(sock)
    def call(name,args,key=token,status=200):
        c=Local('localhost')
        try:
            c.request('POST','/api/call',json.dumps({'method':name,'arguments':args}),{'Authorization':'Bearer '+key,'Content-Type':'application/json'})
            response=c.getresponse();body=json.loads(response.read());assert response.status==status,(name,response.status,body);return body
        finally:c.close()
    broker=subprocess.Popen(['dotnet',HOST,'--Kestrel:Endpoints:Private:Url=http://unix:'+sock],env=env,stdout=subprocess.DEVNULL)
    observer=None
    try:
        for _ in range(200):
            if Path(sock).exists():break
            if broker.poll() is not None:raise RuntimeError('Broker exited')
            time.sleep(.025)
        s=call('session.create',{'label':'job-control'})['id'];session={'sessionId':s}
        b=call('block.create',dict(session,block={'label':'tty','transport':'pty','command':{'fileName':sys.executable,'arguments':['-c',FIXTURE,str(tmp)]}}))['id']
        scope=dict(session,blockId=b);ro=call('share.create',dict(session,role='observer'))['token'];root=int(wait_file(tmp/'root'))
        foreign=call('session.create',{'label':'not-authorized'})['id'];call('terminal.events',dict(scope,sessionId=foreign),ro,403)
        def status():return call('com.superlogical.terminal.process',scope,ro)['foreground']
        current=status();assert current and current['pid']==root,current
        cursor=0
        def next_job(pid,name=None):
            nonlocal cursor
            deadline=time.monotonic()+8
            while time.monotonic()<deadline:
                page=call('terminal.events',dict(scope,after=cursor,timeoutMs=500),ro);cursor=page['nextSequence']
                for event in page['events']:
                    if event['name']!='terminal.process_changed':continue
                    p=event['process'];fg=p['foreground'];assert not fg or set(fg)=={'name','pid'},fg
                    if fg and fg['pid']==pid and (name is None or fg['name']==name):return p
            raise AssertionError(('foreground not observed',pid,call('terminal.events',dict(scope,after=0),ro)))
        # Darwin can observe the spawn launcher before exec. Its generation is real; do not assume the fixture is generation 1.
        first=next_job(root,current['name']);assert first['generation']>=1 and first['ancestors']==[]
        lua='''rex.on('terminal.process_changed',function(ctx,ev)
          if ev.foreground and ev.foreground~=rex.json.null and ev.foreground.pid~=rex.args.root then
            assert(ctx.block_id==rex.args.block and ev.generation>rex.args.base and ev.ancestors[1].pid==rex.args.root)
            print('LUA_JOB_OK');rex.stop(0)
          end
        end)
        local f=assert(io.open(rex.args.ready,'w'));f:write('ready');f:close()
        rex.after(8,function()rex.stop(19)end)'''
        observer=subprocess.Popen(['dotnet',CLI,'do','-e',lua,'--args',json.dumps({'root':root,'base':first['generation'],'block':b,'ready':str(tmp/'registered')})],env=dict(env,ROYALTERMINAL_MUX_TOKEN=ro,ROYALTERMINAL_MUX_SESSION=s,ROYALTERMINAL_MUX_BLOCK=b),stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
        wait_file(tmp/'registered',observer);(tmp/'fork').touch();leader=int(wait_file(tmp/'leader'))
        second=next_job(leader);assert second['generation']>first['generation'] and [a['pid'] for a in second['ancestors']]==[root],second
        assert status()==second['foreground']
        _,err=observer.communicate(timeout=10);assert observer.returncode==0 and 'LUA_JOB_OK' in err,err
        (tmp/'reclaim').touch();third=next_job(root,current['name']);assert third['generation']>second['generation'] and not third['ancestors'] and status()==current
        (tmp/'done').touch();stop=time.monotonic()+6
        while time.monotonic()<stop:
            page=call('terminal.events',dict(scope,after=0),ro)
            if page['ended']:break
            time.sleep(.05)
        assert page['ended'] and page['events'][-1]['name']=='terminal.child_exited'
        jobs=[e['process'] for e in page['events'] if e['name']=='terminal.process_changed']
        assert [j['generation'] for j in jobs]==list(range(1,len(jobs)+1)),jobs
        assert [j for j in jobs if j['generation']>=first['generation'] and j['foreground'] is not None]==[first,second,third],jobs
        mixed=call('session.events',dict(session,after=0),ro)['events']; events=[e for e in mixed if e['type']=='block_event' and e['detail']['name']=='terminal.process_changed'];assert [e['detail']['process'] for e in events]==jobs
        out=subprocess.run(['dotnet',CLI,'events','-s',s,'--json','--after','0','--count',str(len(mixed))],env=dict(env,ROYALTERMINAL_MUX_TOKEN=ro),timeout=12,capture_output=True,text=True,check=True)
        nested=[json.loads(line)['payload']['event'] for line in out.stdout.splitlines()];formatted=[e['payload']['payload'] for e in nested if e['type']=='block_event' and e['payload']['name']=='process_changed'];assert formatted==jobs
        print(f'Verified owner {root}, foreground child {leader}, owner restored; generations {first["generation"]}, {second["generation"]}, {third["generation"]}; scoped native query, HTTP events, Lua callback, NDJSON and clean exit.')
    finally:
        for name in ('fork','reclaim','finish-child','done'):(tmp/name).touch(exist_ok=True)
        if observer is not None and observer.poll() is None:observer.kill();observer.wait(timeout=5)
        broker.terminate();broker.wait(timeout=15)

if __name__=='__main__':
    with tempfile.TemporaryDirectory() as td:run(Path(td))
