#!/usr/bin/env python3
"""Verify staged history packages, their dependencies and consumed native bytes."""
import argparse
import hashlib
import json
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("feed", type=pathlib.Path)
parser.add_argument("--version", default="0.6.2")
parser.add_argument("--consumer-cache", type=pathlib.Path)
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parent.parent
manifest = []
native = []
for package in sorted(args.feed.glob(f"*.{args.version}.nupkg")):
    with zipfile.ZipFile(package) as archive:
        spec = ET.fromstring(archive.read(next(n for n in archive.namelist() if n.endswith(".nuspec"))))
        for element in spec.iter():
            if element.tag.rsplit("}", 1)[-1] == "dependency" and element.get("id", "").startswith("RoyalApps.RoyalTerminal"):
                if element.get("version") != args.version:
                    raise SystemExit(f"Mismatched RoyalTerminal dependency in {package}: {element.attrib}")
        entry = {"package": package.name, "sha256": hashlib.sha256(package.read_bytes()).hexdigest(), "native": []}
        for name in archive.namelist():
            if name.endswith(("/ghostty-vt.dll", "/libghostty-vt.so", "/libghostty-vt.dylib")):
                data = archive.read(name)
                digest = hashlib.sha256(data).hexdigest()
                package_id = package.name.removesuffix(f".{args.version}.nupkg")
                project = package_id.removeprefix("RoyalApps.")
                if hashlib.sha256((root / "src" / project / name).read_bytes()).hexdigest() != digest:
                    raise SystemExit(f"Stale packaged native library: {package}:{name}")
                if args.consumer_cache:
                    consumed = args.consumer_cache / package_id.lower() / args.version / name
                    if hashlib.sha256(consumed.read_bytes()).hexdigest() != digest:
                        raise SystemExit(f"Consumer native mismatch: {consumed}")
                target = args.feed / "verified-native" / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
                native.append(target)
                entry["native"].append({"path": name, "sha256": digest})
        manifest.append(entry)
if len(native) != 6:
    raise SystemExit(f"Expected all six native VT runtimes; found {len(native)}")
subprocess.run([sys.executable, str(root / "scripts" / "verify-history-exports.py"), *map(str, native)], check=True)
(args.feed / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print(f"Verified {len(manifest)} staged packages and six native runtimes")
