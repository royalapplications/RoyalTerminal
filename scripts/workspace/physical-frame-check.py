#!/usr/bin/env python3
"""Operator-assisted hardware acceptance. Never scheduled by CI. No APK installation or device modifications."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import time
import urllib.parse
import urllib.request

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args): return None

def cmd(*argv):
    return subprocess.run(argv,capture_output=True,check=True,timeout=25).stdout

def hardware(platform,serial):
    if platform=='android':
        if serial.startswith('emulator-') or cmd('adb','-s',serial,'get-state').strip()!=b'device': raise ValueError('Select a connected physical Android handset.')
        prop=lambda key:cmd('adb','-s',serial,'shell','getprop',key).decode().strip()
        if any(prop(k)=='1' for k in ('ro.kernel.qemu','ro.boot.qemu')): raise ValueError('Emulator rejected.')
        if prop('ro.product.model').lower().startswith(('sdk_','sdk gphone')): raise ValueError('Emulator product rejected.')
        def capture(path):
            image=cmd('adb','-s',serial,'exec-out','screencap','-p')
            if not image.startswith(b'\x89PNG\r\n\x1a\n'):raise ValueError('No PNG screenshot was returned.')
            p=path.with_suffix('.png');p.write_bytes(image);return p
        return prop('ro.product.model'),prop('ro.build.version.release'),capture
    if serial not in cmd('idevice_id','-l').decode().splitlines(): raise ValueError('Pair this physical iPhone; simulator UDIDs are not accepted.')
    field=lambda k:cmd('ideviceinfo','-u',serial,'-k',k).decode().strip()
    model=field('ProductType')
    if not model.startswith(('iPhone','iPad')):raise ValueError('Expected a physical iPhone or iPad.')
    def capture(path):
        p=path.with_suffix('.tiff');cmd('idevicescreenshot','-u',serial,str(p));raw=p.read_bytes()
        if raw.startswith(b'\x89PNG'):
            renamed=p.with_suffix('.png');p.rename(renamed);return renamed
        if raw[:4] not in (b'II*\0',b'MM\0*'):raise ValueError('No usable screenshot was returned.')
        return p
    return model,field('ProductVersion'),capture

def run(a):
    if not sys.stdin.isatty() or not sys.stdout.isatty():raise ValueError('Run at a private interactive terminal, not in CI or a recorded build log.')
    u=urllib.parse.urlsplit(a.url)
    if u.scheme!='https' or not u.hostname or u.username or u.password or u.query or u.fragment:raise ValueError('Use a trusted HTTPS base URL.')
    if re.fullmatch('[0-9a-fA-F]{40}',a.build) is None:raise ValueError('--build must be the source commit of the installed client.')
    admin=os.environ['ROYALTERMINAL_MUX_TOKEN'];model,version,capture=hardware(a.platform,a.serial)
    a.out.mkdir(mode=0o700,parents=True,exist_ok=False);http=urllib.request.build_opener(NoRedirect());sid=None;share=None
    def api(method,**args):
        req=urllib.request.Request(a.url.rstrip('/')+'/api/call',data=json.dumps({'method':method,'arguments':args}).encode(),headers={'Authorization':'Bearer '+admin,'Content-Type':'application/json'})
        with http.open(req,timeout=20) as r:return json.load(r)
    markers=[secrets.token_hex(4).upper() for _ in range(3)];screens=[]
    try:
        s=api('session.create',label='Phone acceptance '+secrets.token_hex(3));sid=s['id'];wid=s['windows'][0]['id']
        # Constant executable; stdout carries a private, per-run challenge. The test host must be Unix.
        def leaf(i):return {'flavor':'com.superlogical.terminal.shell','label':['Origin','Upper','Lower'][i],'options':{'command':['/bin/sh','-c','printf "Code: %s\\n" "$1"; exec /bin/cat','fixture',markers[i]]}}
        layer=api('session.new_layer',sessionId=sid,windowId=wid,layout={'orientation':'horizontal','ratio':.5,'first':leaf(0),'second':{'orientation':'vertical','ratio':.5,'first':leaf(1),'second':leaf(2)}});origin,_,target=layer['blockIds']
        def view():return api('session.view',sessionId=sid)
        before=view();dims={b['id']:b['dimensions'] for b in before['blocks']};grant=api('share.create',sessionId=sid,role='observer',lifetimeMinutes=15);share=grant['id']
        print('On the physical device, open the installed native workspace app and connect to',a.url,'\nTemporary read-only token:',grant['token']);print('Select Origin, tap Observe, then Follow. Do not take control.')
        def evidence(name,index,bid):
            answer=input('Read the code displayed on the device ('+name+'): ').strip().upper()
            if not secrets.compare_digest(answer,markers[index]):raise RuntimeError('Wrong terminal was displayed; run did not pass.')
            observed=view();by_id={b['id']:b for b in observed['blocks']}
            if by_id[bid]['attachmentCount']<1 or any(b.get('controllerId') is not None or b['dimensions']!=dims[b['id']] for b in by_id.values()):raise RuntimeError('Expected read-only attachment and unchanged PTY geometry.')
            p=capture(a.out/name);screens.append({'phase':name,'file':p.name,'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'revision':observed['revision']})
        evidence('original',0,origin)
        for value,index,bid,phase in [(True,2,target,'zoomed'),(False,0,origin,'restored')]:
            api('session.zoom_block',sessionId=sid,blockId=target,zoom=value);first=view()['revision'];api('session.zoom_block',sessionId=sid,blockId=target,zoom=value)
            if view()['revision']!=first:raise RuntimeError('Repeated explicit zoom advanced the layout revision.')
            evidence(phase,index,bid)
        print('Tap Detach on the handset.');deadline=time.monotonic()+45
        while any(b['attachmentCount'] for b in view()['blocks']):
            if time.monotonic()>=deadline:raise RuntimeError('Device did not detach.')
            time.sleep(.35)
    finally:
        if sid:
            try:
                if share:api('share.revoke',sessionId=sid,shareId=share)
            finally:api('session.close',sessionId=sid)
    (a.out/'result.json').write_text(json.dumps({'result':'passed','kind':'operator-assisted physical handset','platform':a.platform,'model':model,'os':version,'operatorSuppliedClientCommit':a.build,'utc':dt.datetime.now(dt.timezone.utc).isoformat(),'screens':screens},indent=2)+'\n')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('platform',choices=['android','ios']);p.add_argument('--serial',required=True);p.add_argument('--url',required=True);p.add_argument('--build',required=True);p.add_argument('--out',type=Path,required=True)
    try:run(p.parse_args())
    except (OSError,RuntimeError,ValueError,KeyError,subprocess.SubprocessError) as err:p.exit(1,str(err)+'\n')
