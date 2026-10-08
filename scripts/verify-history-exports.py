#!/usr/bin/env python3
"""Check history exports in PE, ELF and Mach-O libraries using LLVM."""
import argparse
import pathlib
import re
import shutil
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--tool")
parser.add_argument("libraries", nargs="+")
args = parser.parse_args()
required = ("ghostty_royal_history_info", "ghostty_royal_history_row_ref",
            "ghostty_royal_history_grapheme_fits")
tool = args.tool or next((shutil.which(name) for name in
    ["llvm-readobj", *[f"llvm-readobj-{v}" for v in range(22, 14, -1)]] if shutil.which(name)), None)
if tool is None:
    tool = next((str(p) for p in map(pathlib.Path, [
        "C:/Program Files/LLVM/bin/llvm-readobj.exe", "/opt/homebrew/opt/llvm/bin/llvm-readobj",
        "/usr/local/opt/llvm/bin/llvm-readobj",
        # The pinned macos-15 runners provide keg-only LLVM 18.
        "/opt/homebrew/opt/llvm@18/bin/llvm-readobj",
        "/usr/local/opt/llvm@18/bin/llvm-readobj"]) if p.is_file()), None)
if tool is None:
    raise SystemExit("llvm-readobj is required to verify native history exports")
libraries = []
for argument in args.libraries:
    path = pathlib.Path(argument)
    matches = ([p for p in path.rglob("*ghostty-vt*") if p.is_file() and
        (p.suffix in (".dll", ".dylib") or ".so" in p.name)] if path.is_dir() else [path])
    if not matches:
        raise SystemExit(f"No native VT library found in {path}")
    libraries.extend(matches)
for library in libraries:
    result = subprocess.run([tool, "--coff-exports", "--dyn-symbols", "--symbols", str(library)],
                            check=True, capture_output=True, text=True)
    for name in required:
        # The exports are additive and must be definitions, not import references.
        blocks = re.findall(r"(?:Symbol|Export) \{([^}]+)\}", result.stdout)
        present = any(re.search(r"Name: _?" + name + r"(?:\s|$)", block)
                      and not re.search(r"^\s*(?:Section|Type): (?:Undefined|Undef)", block, re.MULTILINE)
                      and ("RVA:" in block or "Binding: Global" in block or
                           ("Extern" in block and "Type: Section" in block)) for block in blocks)
        if not present:
            raise SystemExit(f"Missing history export {name} in {library}")
    print(f"History exports verified: {library}")
