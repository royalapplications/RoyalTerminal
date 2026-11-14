#!/usr/bin/env python3
"""Inventory the whole PR and syntax-check scripts/configuration; never execute inventoried files."""
from __future__ import annotations
import argparse
import ast
import collections
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


def git(*args: str) -> bytes:
    return subprocess.check_output(['git', *args])


def verify(path: Path) -> str | None:
    suffix = path.suffix.lower()
    if path.is_symlink():
        return None
    if suffix == '.py':
        compile(path.read_bytes(), str(path), 'exec', flags=ast.PyCF_ONLY_AST)
        return 'python'
    if suffix in {'.csproj', '.props', '.targets', '.axaml', '.xml', '.plist', '.slnx', '.svg'}:
        ET.parse(path)
        return 'xml'
    if suffix in {'.json', '.webmanifest'}:
        json.loads(path.read_text(encoding='utf-8-sig'))
        return 'json'
    cmd = {'.js': ['node', '--check'], '.mjs': ['node', '--check'], '.cjs': ['node', '--check'],
           '.lua': ['luac5.1', '-p'], '.sh': ['bash', '-n'], '.go': ['gofmt', '-e']}.get(suffix)
    if cmd:
        subprocess.run([*cmd, str(path)], check=True, stdout=subprocess.DEVNULL, timeout=30)
        return cmd[0]
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', required=True); parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args(); root = Path(os.fsdecode(git('rev-parse', '--show-toplevel')).strip())
    os.chdir(root)
    # Include deletions and renames in inventory; do not feed missing paths or symlinks to parsers.
    files = [os.fsdecode(item) for item in git('diff', '--name-only', '--no-renames', '-z', args.base, 'HEAD', '--').split(b'\0') if item]
    counts: collections.Counter[str] = collections.Counter(); rows = []; errors = []
    for name in files:
        path = root / name
        try:
            check = (verify(path) or 'inventory only') if path.is_file() else 'deleted or nonregular'
        except (OSError, ValueError, SyntaxError, ET.ParseError, subprocess.SubprocessError) as exc:
            check='failed'; errors.append(f'{name}: {exc}')
        counts[check] += 1; rows.append({'path': name, 'check': check})
    result={'base':args.base,'checkout':git('rev-parse','HEAD').decode().strip(),'files':len(rows),'by_check':dict(counts),'entries':rows,'errors':errors}
    args.out.parent.mkdir(parents=True,exist_ok=True);args.out.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print(f'{len(rows)} paths in PR:', dict(counts));print('\n'.join(errors),file=sys.stderr)
    return int(bool(errors))


if __name__ == '__main__':
    raise SystemExit(main())
