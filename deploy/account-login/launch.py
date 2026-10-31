#!/usr/bin/python3 -I
"""Account-side launcher; invoked by systemd only after switching to the target identity."""
import os
from pathlib import Path
import pwd
import secrets
import socket
import stat
import sys
import tempfile
import time

APP = '/opt/royalterminal-account/server/RoyalTerminal.Multiplexing.Server.dll'
DOTNET = '/usr/bin/dotnet'

def private_dir(path, uid):
    st = os.lstat(path)
    if not stat.S_ISDIR(st.st_mode) or st.st_uid != uid or stat.S_IMODE(st.st_mode) != 0o700:
        raise ValueError('insecure account directory: '+str(path))

def credential(path, uid):
    # Link after fsync, so a failed write never replaces an existing usable token.
    if not path.exists():
        fd, temp = tempfile.mkstemp(dir=path.parent)
        try:
            with os.fdopen(fd,'wb') as f:
                f.write((secrets.token_hex(32)+'\n').encode('ascii')); f.flush(); os.fsync(f.fileno())
            try: os.link(temp,path)
            except FileExistsError: pass
        finally: os.unlink(temp)
    fd=os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC)
    with os.fdopen(fd,'rb') as f:
        st=os.fstat(f.fileno())
        if not stat.S_ISREG(st.st_mode) or st.st_uid!=uid or stat.S_IMODE(st.st_mode)!=0o600:
            raise ValueError('insecure credential file')
        data=f.read(66)
    if len(data)!=65 or data[-1:]!=b'\n' or any(c not in b'0123456789abcdef' for c in data[:-1]):
        raise ValueError('invalid account credential')
    return data[:-1].decode('ascii')

def main():
    if len(sys.argv)!=3 or sys.argv[1] not in ('start','ready'): raise ValueError('bad arguments')
    op,name=sys.argv[1:]; uid=os.geteuid(); user=pwd.getpwuid(uid)
    if uid<1000 or uid==65534 or user.pw_name!=name or not name or '/' in name: raise ValueError('unexpected service identity')
    runtime=Path('/run')/('royalterminal-account-'+name); state=Path('/var/lib')/('royalterminal-account-'+name)
    private_dir(runtime, uid); private_dir(state, uid); sock=str(runtime/'host.sock')
    if op=='ready':
        for _ in range(120):
            try:
                with socket.socket(socket.AF_UNIX) as s:
                    s.settimeout(.5);s.connect(sock);s.sendall(b'GET /healthz HTTP/1.0\r\nHost: localhost\r\n\r\n')
                    with s.makefile('rb') as stream:
                        if stream.readline(192).split()[1:2]==[b'200']: return
            except OSError: pass
            time.sleep(.125)
        raise ValueError('account backend did not become ready')
    os.umask(0o077); os.chdir(user.pw_dir)
    env=dict(os.environ)
    for key in list(env):
        if key.startswith(('ROYALTERMINAL_MUX_', 'ASPNETCORE_', 'Kestrel__', 'LISTEN_')): del env[key]
    env.update(HOME=user.pw_dir,USER=name,LOGNAME=name,SHELL=user.pw_shell,ROYALTERMINAL_MUX_TOKEN=credential(state/'token',uid),ROYALTERMINAL_MUX_CONFIG=str(state/'automation.json'))
    os.execve(DOTNET,[DOTNET,APP,'--Kestrel:Endpoints:Account:Url=http://unix:'+sock],env)

if __name__=='__main__':
    try: main()
    except (OSError,ValueError,KeyError) as error: sys.exit('royalterminal account launcher: '+str(error))
