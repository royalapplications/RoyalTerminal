#!/usr/bin/env python3
import http.client,json,os,pathlib,secrets,socket,subprocess,tempfile,time
root=pathlib.Path(__file__).resolve().parents[2];sample=root/'samples';f=root/'tests/workspace-lua/json-spec.lua'
cli=sample/'RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll'; server=sample/'RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll'
with tempfile.TemporaryDirectory() as d:
 path=d+'/host.sock'; key=secrets.token_hex(32); env=dict(os.environ,ROYALTERMINAL_MUX_LUA_INIT=str(f),ROYALTERMINAL_MUX_TOKEN=key)
 local=subprocess.run(['dotnet',cli,'do',f],env=dict(env,ROYALTERMINAL_MUX_SESSION='offline'),capture_output=True,text=True,check=True,timeout=15);assert json.loads(local.stdout)=='json-parity-ok'
 host=subprocess.Popen(['dotnet',server,'--Kestrel:Endpoints:Demo:Url=http://unix:'+path],env=env)
 try:
  for i in range(120):
   if os.path.exists(path):break
   if host.poll() is not None:raise SystemExit('host startup failed')
   time.sleep(.1)
  class Unix(http.client.HTTPConnection):
   def connect(self):self.sock=socket.socket(socket.AF_UNIX);self.sock.settimeout(10);self.sock.connect(path)
  conn=Unix('localhost');conn.request('POST','/api/call',json.dumps({'method':'lua.invoke','arguments':{'name':'json.contract'}}),{'Authorization':'Bearer '+key,'Content-Type':'application/json'}); r=conn.getresponse();v=r.read();assert r.status==200 and json.loads(v)=='json-parity-ok',(r.status,v);conn.close()
 finally:host.terminate();host.wait(timeout=10)
