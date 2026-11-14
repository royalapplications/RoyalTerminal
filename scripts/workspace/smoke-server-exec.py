#!/usr/bin/env python3
"""Run the operator's actual init.lua in two separate .NET hosts; never use a user's shell or configuration."""
import contextlib
import http.client
import json
import os
import secrets
import shutil
import socket
import subprocess
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / 'samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll'
CONFIG = r'''
local during_load, reason = rex.exec({'/usr/bin/touch', '__MARKER__'})
assert(during_load == nil and reason)
rex.action{name='probe', run=function()
  local r,e=rex.exec({'/bin/true'});return {permitted=r~=nil,reason=e}
end}
rex.action{name='streams', run=function()
  local arg='a b;$(not-a-shell)'
  local script=[[import sys,os; assert not os.getenv('ROYALTERMINAL_MUX_TOKEN') and not os.getenv('WORKSPACE_CANARY'); sys.stdout.buffer.write(sys.stdin.buffer.read()); sys.stderr.write(sys.argv[1]+os.environ['EXPLICIT']); sys.exit(37)]]
  local pieces={};local h=assert(rex.exec.start({'/usr/bin/python3','-c',script,arg},{stdin=true,stdout=function(v) pieces[#pieces+1]=v end,env={EXPLICIT='!'}}))
  assert(h:running());local pending,why=h:wait(0);assert(pending==nil and why=='timeout');local data=string.char(0,255,65)
  assert(h:write(data));assert(h:close_stdin());local r=assert(h:wait(3));assert(r.stdout==data and table.concat(pieces)==data and r.stderr==arg..'!' and not r.ok)
  assert(not rex.exec({'relative'}));assert(not rex.exec({'/bin/true'},{env={ROYALTERMINAL_MUX_TOKEN='forbidden'}}))
  local timeout=assert(rex.exec({'/bin/sleep','5'},{timeout=.1}));assert(timeout.timed_out)
  return {code=r.code,bytes=#r.stdout}
end}
rex.action{name='owned',run=function()return assert(rex.exec.start({'/bin/sleep','120'})).pid end}
rex.action{name='failed',run=function(ctx,args)
  local h=assert(rex.exec.start({'/bin/sleep','120'}));assert(rex.exec({'/usr/bin/python3','-c','import pathlib,sys;pathlib.Path(sys.argv[1]).write_text(sys.argv[2])',args.file,tostring(h.pid)}).ok);error('intentional')
end}
rex.action{name='detach',run=function()return assert(rex.exec.start({'/bin/sleep','120'},{detach=true,stdout=false,stderr=false})).pid end}
'''

def alive(pid):
    try: os.kill(pid, 0); return True
    except ProcessLookupError: return False

def until(predicate):
    end=time.monotonic()+8
    while time.monotonic()<end:
        if predicate(): return
        time.sleep(.025)
    raise AssertionError('Process lifetime assertion timed out')

@contextlib.contextmanager
def broker(root, enabled):
    root.mkdir(); sock=str(root/'host.sock'); config=root/'init.lua';marker=root/'MUST-NOT-EXIST'
    config.write_text(CONFIG.replace('__MARKER__',str(marker)))
    token=secrets.token_hex(32);env={k:v for k,v in os.environ.items() if not k.startswith('ROYALTERMINAL_')}
    env.update(ROYALTERMINAL_MUX_TOKEN=token,ROYALTERMINAL_MUX_LUA_INIT=str(config),ROYALTERMINAL_MUX_LUA_EXECUTABLE=shutil.which('lua5.1'),WORKSPACE_CANARY='never inherit this')
    if enabled:env['ROYALTERMINAL_MUX_LUA_EXEC']='1'
    host=subprocess.Popen(['dotnet',HOST,'--Kestrel:Endpoints:Local:Url=http://unix:'+sock],env=env,stdout=subprocess.DEVNULL)
    class Unix(http.client.HTTPConnection):
        def connect(self):
            self.sock=socket.socket(socket.AF_UNIX);self.sock.settimeout(10);self.sock.connect(sock)
    def api(method,args=None,expected=200):
        c=Unix('localhost')
        try:
            c.request('POST','/api/call',json.dumps({'method':method,'arguments':args or {}}),{'Authorization':'Bearer '+token,'Content-Type':'application/json'});r=c.getresponse();v=json.loads(r.read());assert r.status==expected,(r.status,v);return v
        finally:c.close()
    try:
        until(lambda:os.path.exists(sock));api('lua.check');assert not marker.exists();yield api
    finally:
        host.terminate();host.wait(timeout=12)

with tempfile.TemporaryDirectory() as tmp:
    root=Path(tmp)
    with broker(root/'off',False) as api:
        assert api('lua.invoke',{'name':'probe'})['permitted'] is False
    with broker(root/'on',True) as api:
        assert api('lua.invoke',{'name':'probe'})['permitted'] is True
        assert api('lua.invoke',{'name':'streams'})=={'code':37,'bytes':3}
        owned=api('lua.invoke',{'name':'owned'});until(lambda:not alive(owned))
        pidfile=root/'failed.pid';api('lua.invoke',{'name':'failed','arguments':{'file':str(pidfile)}},422);failed=int(pidfile.read_text());until(lambda:not alive(failed))
        detached=api('lua.invoke',{'name':'detach'});api('lua.reload');assert alive(detached)
        api('process.start',{},404)
    until(lambda:not alive(detached))
print('Verified opt-in and validation boundaries; exact binary I/O, nonzero exit and timeout; cleanup on success/failure; detached child survives the action and reload but not broker shutdown.')
