#!/usr/bin/python3 -I
"""Root-side one-shot provisioning/admission. Never parses network traffic or launches a shell."""
import json
import os
import pwd
import re
import stat
import subprocess
import sys

CONFIG = '/etc/royalterminal-login/accounts.json'
ENTRY = '/run/royalterminal-login'
NAME = re.compile(r'[a-z_][a-z0-9_-]{0,30}\Z', re.ASCII)

def unique(pairs):
    out = {}
    for key, value in pairs:
        if key in out:
            raise ValueError('duplicate policy key')
        out[key] = value
    return out

def protected_directory(path):
    for part in (path, os.path.dirname(path), '/'):
        st = os.lstat(part)
        if not stat.S_ISDIR(st.st_mode) or st.st_uid != 0 or st.st_mode & 0o022:
            raise ValueError('directory is not root-controlled: ' + part)

def read_policy():
    protected_directory(os.path.dirname(CONFIG))
    fd = os.open(CONFIG, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC)
    with os.fdopen(fd, 'rb') as f:
        st = os.fstat(f.fileno())
        if not stat.S_ISREG(st.st_mode) or st.st_uid != 0 or st.st_mode & 0o022:
            raise ValueError('accounts file is not a root-controlled regular file')
        data = f.read(65537)
    if len(data) > 65536:
        raise ValueError('accounts file exceeds 64 KiB')
    config = json.loads(data, object_pairs_hook=unique)
    if not isinstance(config, dict) or set(config) != {'version', 'accounts'} or type(config['version']) is not int or config['version'] != 1:
        raise ValueError('unsupported accounts schema')
    accounts = config['accounts']
    if not isinstance(accounts, dict) or len(accounts) > 128:
        raise ValueError('invalid accounts map')
    for account, ids in accounts.items():
        if not NAME.fullmatch(account) or not isinstance(ids, list) or len(ids) > 64 or any(type(v) is not int or not 1 <= v < 2**32-1 for v in ids) or len(set(ids)) != len(ids):
            raise ValueError('invalid account or principal list')
    return accounts

def main():
    if os.geteuid() != 0 or len(sys.argv) not in (2, 3):
        raise ValueError('requires root; usage: access.py check | acl ACCOUNT | admit ACCOUNT')
    accounts = read_policy()
    if sys.argv[1:] == ['check']:
        return
    if len(sys.argv) != 3 or sys.argv[1] not in ('acl', 'admit'):
        raise ValueError('invalid operation')
    operation, name = sys.argv[1:]
    if not NAME.fullmatch(name):
        raise ValueError('invalid target')
    # ACL application always replaces the complete list. Applying a removed target explicitly revokes old ACL entries.
    principals = accounts.get(name, [])
    if operation == 'acl':
        protected_directory(ENTRY)
        path = f'{ENTRY}/{name}.sock'
        st = os.lstat(path)
        if not stat.S_ISSOCK(st.st_mode) or st.st_uid != 0 or st.st_gid != 0:
            raise ValueError('not the expected root-owned entry socket')
        acl = ['u::rw-', 'g::---', 'o::---', 'm::rw-' if principals else 'm::---']
        acl.extend(f'u:{uid}:rw-' for uid in principals)
        subprocess.run(['/usr/bin/setfacl', '--set=' + ','.join(acl), '--', path], check=True, env={'LC_ALL':'C'})
        return
    if not principals:
        raise ValueError('target has no enabled principals')
    target = pwd.getpwnam(name)
    if target.pw_uid < 1000 or target.pw_uid == 65534 or target.pw_name != name or not target.pw_shell.startswith('/') or os.path.basename(target.pw_shell) in ('false', 'nologin'):
        raise ValueError('system accounts and non-login shells are refused')
    status = subprocess.check_output(['/usr/bin/passwd','--status',name], env={'LC_ALL':'C'}, text=True, timeout=5).split()
    if len(status)<2 or status[0]!=name or status[1]!='P':
        raise ValueError('locked or passwordless target refused by this profile')

if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        sys.exit('royalterminal account policy: ' + str(error))
