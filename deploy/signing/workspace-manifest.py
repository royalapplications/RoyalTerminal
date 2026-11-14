#!/usr/bin/env python3
"""Sign/verify a workspace artifact without trusting a filename or public key inside the archive."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile


def fail(message: str) -> None:
    raise ValueError(message)


def manifest(path: Path, revision: str) -> bytes:
    if not re.fullmatch('[0-9a-f]{40}', revision) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,127}', path.name):
        fail('Supply a full lowercase Git revision and a portable archive basename.')
    if path.is_symlink() or not path.is_file():
        fail('The archive must be a regular file, not a link.')
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        count = 0
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
            count += len(block)
    record = dict(format='royalterminal-workspace-v1', revision=revision, archive=path.name, sha256=digest.hexdigest(), bytes=count)
    return (json.dumps(record, sort_keys=True, separators=(',', ':')) + '\n').encode('ascii')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['sign', 'verify'])
    parser.add_argument('archive', type=Path); parser.add_argument('revision'); parser.add_argument('key', type=Path)
    parser.add_argument('signature', type=Path)
    args = parser.parse_args()
    payload = manifest(args.archive, args.revision)
    with tempfile.TemporaryDirectory() as root:
        message = Path(root, 'manifest'); message.write_bytes(payload)
        common = ['openssl', 'pkeyutl', '-rawin', '-inkey', str(args.key), '-in', str(message)]
        if args.operation == 'sign':
            if os.name != 'nt' and args.key.stat().st_mode & 0o077: fail('Restrict the signing key to its owner.')
            sig = subprocess.check_output([*common, '-sign'])
            if len(sig) != 64: fail('Expected a raw Ed25519 signature.')
            with args.signature.open('xb') as dest: dest.write(sig)
        else:
            if args.signature.is_symlink() or args.signature.stat().st_size != 64: fail('Invalid detached signature.')
            subprocess.run([*common, '-verify', '-pubin', '-sigfile', str(args.signature)], check=True)
    print(payload.decode('ascii').rstrip())


if __name__ == '__main__':
    try: main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error: raise SystemExit(str(error))
