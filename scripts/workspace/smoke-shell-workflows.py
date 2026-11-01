#!/usr/bin/env python3
"""Use the shipped CLI and an isolated real broker; never connect to the developer's workspace."""
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
SAMPLES = ROOT / 'samples'
CLI = SAMPLES / 'RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll'
HOST = SAMPLES / 'RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll'


def main():
    with tempfile.TemporaryDirectory() as td:
        sock = str(Path(td) / 'host.sock')
        env = {k: v for k, v in os.environ.items() if not k.startswith('ROYALTERMINAL_')}
        env.update(ROYALTERMINAL_MUX_TOKEN=secrets.token_hex(32), ROYALTERMINAL_MUX_SOCKET=sock)
        host = subprocess.Popen(['dotnet', HOST, '--Kestrel:Endpoints:Local:Url=http://unix:' + sock], env=env, stdout=subprocess.DEVNULL)
        try:
            for _ in range(200):
                if Path(sock).exists(): break
                if host.poll() is not None: raise RuntimeError('Broker failed to start')
                time.sleep(.05)
            def cmd(*args, code=0, stdin=b'', extra=None):
                r = subprocess.run(['dotnet', CLI, *args], env={**env, **(extra or {})}, input=stdin, capture_output=True, timeout=20)
                assert r.returncode == code, (args, r.returncode, code, r.stdout[-2000:], r.stderr[-2000:])
                return r.stdout
            def api(method, **args): return json.loads(cmd('api', 'call', method, json.dumps(args)))
            def tree(window): return next(w for w in api('session.view',sessionId=s)['windows'] if w['id'] == window)['layout']
            def capture(b): return cmd('capture','-s',s,'-b',b,'--trim','--unwrap')
            created=json.loads(cmd('new','Shell acceptance','--label','anchor','--json','--','/bin/sleep','120'))
            s,w,anchor=created['session_id'],created['window_id'],created['block_ids'][0]
            assert tree(w)['blockId']==anchor
            # A command in a split receives literal argv including empty values; do not invoke a shell implicitly.
            arg='two words;$(touch SHOULD_NEVER_EXIST)'
            program='import sys; print("ARGS="+repr(sys.argv[1:]),flush=True); sys.exit(37)'
            r=json.loads(cmd('split','-s',s,'-b',anchor,'--label','exited','--split=below','--ratio','30','--focus=false','--keep-open','--json','--','/usr/bin/python3','-c',program,arg,''))
            child=r['block_ids'][0];layout=tree(w)
            assert r['anchor_block_id']==anchor and r['window_id']==w and layout['orientation']=='Vertical' and abs(layout['ratio']-.7)<1e-9
            assert layout['first']['blockId']==anchor and layout['second']['blockId']==child
            assert json.loads(cmd('wait','-s',s,'-b',child,'--json','--for','4s',code=37))['exit_code']==37
            assert arg.encode() in capture(child)
            assert json.loads(cmd('capture','-s',s,'-b',child,'--format','html','--json'))['format']=='html'
            # A different window and positional syntax use distinct contracts. No hidden focus mutation.
            run=json.loads(cmd('run','-s',s,'--label','logs','--json','--','/bin/sleep','120'))
            assert run['window_id']!=w and tree(run['window_id'])['blockId']==run['block_ids'][0]
            cmd('split','-s',s,'--json',code=3)
            cmd('capture','-s',s,'-w',run['window_id'],'-b',anchor,code=3)
            cmd('run','-s',s,'--wait','--for','5s','--','/bin/sh','-c','exit 23',code=23)
            # Resolve the shell's own block when invoked without -b, and verify before/after symmetry on a nested target.
            ctx={'ROYALTERMINAL_MUX_SESSION':s,'ROYALTERMINAL_MUX_BLOCK':anchor}
            r2=json.loads(cmd('split','--split=left','--ratio=25','--json','--','/bin/sleep','120',extra=ctx))
            local=tree(w)['first'];assert local['first']['blockId']==r2['block_ids'][0] and local['second']['blockId']==anchor and local['ratio']==.25
            sleep=run['block_ids'][0];cmd('wait','-s',s,'-b',sleep,'--for','75ms',code=1)
            assert next(b for b in api('session.view',sessionId=s)['blocks'] if b['id']==sleep)['state']=='Running'
            # Oversized stdin is rejected before the single write. A read-only token may capture, never write.
            pipe=api('block.create',sessionId=s,block={'label':'binary','transport':'pipe','command':{'fileName':'/usr/bin/python3','arguments':['-u','-c','import sys; print(sys.stdin.buffer.read(4).hex(),flush=True); sys.exit(31)']}})['id']
            observer=api('share.create',sessionId=s,role='observer')['token'];ro={'ROYALTERMINAL_MUX_TOKEN':observer}
            cmd('send','-s',s,'-b',pipe,stdin=b'x'*65537,code=2)
            cmd('send','-s',s,'-b',pipe,'X',extra=ro,code=1)
            cmd('send','-s',s,'-b',pipe,stdin=b'\0\xff\r\n');cmd('wait','-s',s,'-b',pipe,'--for','5s',code=31)
            assert b'00ff0d0a' in cmd('capture','-s',s,'-b',pipe,extra=ro)
            assert json.loads(cmd('capture',s,pipe))['format']=='text'
            print('Verified real PTY argv/exit, both split sides, new-window isolation, immutable targeting, wait deadlines, binary stdin and observer rights.')
        finally:
            host.terminate(); host.wait(timeout=15)


if __name__=='__main__': main()
