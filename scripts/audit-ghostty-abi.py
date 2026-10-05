#!/usr/bin/env python3
"""Audit libghostty-vt's entire ABI manifest against the C# declarations.

Loads the given native library, never .NET reflection. The generated C# checks
use explicitly named enum members/fields so renamed or removed bindings fail
at compile time. Run after every Ghostty pin update; --check detects stale
generated tests without changing files.
"""

from __future__ import annotations

import argparse
import ctypes
import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
TYPE_NAMES = {
    "GhosttyBuildInfo": "GhosttyBuildInfoData",
    "GhosttyKey": "GhosttyVtKey",
    "GhosttyKeyAction": "GhosttyVtKeyAction",
    "GhosttyMouseButton": "GhosttyMouseButtonId",
    "GhosttyPaste": "GhosttyPasteRequest",
    "GhosttySelection": "GhosttySelectionRange",
}
FIELD_NAMES = {("GhosttyTerminalSelectionFormatOptions", "emit"): "Format"}
ALIASES = {"GhosttyColorPaletteIndex": "byte", "GhosttyKittyKeyFlags": "GhosttyVtNative.GhosttyKittyKeyFlags", "GhosttyMode": "GhosttyVtNative.GhosttyMode", "GhosttyMods": "GhosttyVtNative.GhosttyVtMods", "GhosttyRow": "ulong", "GhosttyStyleId": "ushort", "GhosttyCell": "ulong"}
WORDS = {"ctx": "context", "ptr": "pointer", "cap": "capacity", "len": "length", "buf": "buffer", "fg": "foreground", "bg": "background", "cols": "columns", "col": "column", "ref": "reference", "ns": "nanoseconds", "prev": "previous", "esc": "escape", "mem": "memory", "dnd": "draganddrop"}


def lookup(members, key):
    direct = members.get(normalized(key))
    if direct is not None:
        return direct
    expanded = "".join(WORDS.get(word.lower(), word.lower()) for word in key.split("_"))
    return members.get(expanded)


def normalized(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.lower())


def bodies(source: str, pattern: str):
    for match in re.finditer(pattern, source):
        start = source.index("{", match.end())
        position, depth = start + 1, 1
        while depth:
            depth += (source[position] == "{") - (source[position] == "}")
            position += 1
        yield match, source[start + 1:position - 1]


def declarations():
    enums, structs = {}, {}
    for path in sorted((ROOT / "src/RoyalTerminal.GhosttySharp/Native").glob("GhosttyVtNative*.cs")):
        source = re.sub(r"/\*.*?\*/|//[^\n]*", "", path.read_text(), flags=re.S)
        for match, body in bodies(source, r"public enum (\w+)(?:\s*:\s*(\w+))?\s*"):
            members = {}
            for item in body.split(","):
                item = item.strip()
                if item:
                    name = item.split("=")[0].strip()
                    members[normalized(name)] = name
            enums[match[1]] = members
        for match, body in bodies(source, r"public (?:(?:unsafe|readonly)\s+)*struct (\w+)\s*"):
            fields = {}
            for field in re.finditer(
                r"(?:public|internal)\s+(?:readonly\s+)?(?P<fixed>fixed\s+)?(?P<type>[\w*]+|delegate\*\s+unmanaged\[Cdecl\]<[^>]+>)\s+(?P<name>\w+)\s*(?:\[(?P<count>[^]]+)\])?\s*(?P<end>;|\{\s*get;\s*\})", body
            ):
                fields[normalized(field["name"])] = dict(field.groupdict())
            structs[match[1]] = fields
    desktop = (ROOT / "src/RoyalTerminal.GhosttySharp/Native/Enums.cs").read_text()
    for match, body in bodies(desktop, r"public enum (GhosttyColorScheme)\s*"):
        body = re.sub(r"//[^\n]*", "", body)
        enums[match[1]] = {normalized(item.split("=")[0].strip()): item.split("=")[0].strip() for item in body.split(",") if item.strip()}
    return enums, structs


def manifest(library: str):
    lib = ctypes.CDLL(str(Path(library).resolve()))
    lib.ghostty_type_json.restype = ctypes.c_char_p
    return json.loads(lib.ghostty_type_json())


def callback_inventory(abi):
    headers = "\n".join(path.read_text() for path in sorted((ROOT / "external/ghostty/include/ghostty/vt").rglob("*.h")))
    headers = re.sub(r"/\*.*?\*/|//[^\n]*", "", headers, flags=re.S)
    source = "\n".join(path.read_text() for path in sorted((ROOT / "src/RoyalTerminal.GhosttySharp/Native").glob("GhosttyVtNative*.cs")))
    source = re.sub(r"/\*.*?\*/|//[^\n]*", "", source, flags=re.S)
    callbacks = {}
    for match in re.finditer(r"((?:\s*\[[^]]+\])*)\s*public\s+(?:unsafe\s+)?delegate\s+(\w+)\s+(Ghostty\w+)\s*\((.*?)\);", source, re.S):
        callbacks[match[3]] = (match[2], match[4], match[1])

    def canonical(value, managed=False, attrs=""):
        value = re.sub(r"\[[^]]+\]", "", value)
        value = re.sub(r"\bconst\b|\s", "", value)
        if "*" in value or value in ("nint", "IntPtr") or abi["types"].get(value, {}).get("kind") == "opaque":
            return "pointer"
        aliases = {"size_t": "usize", "uintptr_t": "usize", "nuint": "usize", "uint8_t": "u8", "byte": "u8", "uint32_t": "u32", "uint": "u32", "bool": "u8"}
        if value == "bool" and managed and "UnmanagedType.U1" not in attrs:
            return "invalid-bool-marshalling"
        return aliases.get(value, value)

    def parameters(parameters, managed=False):
        result = []
        for item in parameters.split(","):
            item = item.strip()
            if not item or item == "void":
                continue
            cleaned = re.sub(r"\[[^]]+\]", "", item).strip()
            type_name = re.sub(r"\b\w+\s*$", "", cleaned).strip()
            result.append(canonical(type_name, managed, item))
        return result

    rows, findings = [], []
    header_types = set(re.findall(r"}\s*(Ghostty\w+)\s*;", headers))
    header_types.update(re.findall(r"typedef\s+(?:const\s+)?(?:struct\s+)?[\w*]+\s*\**\s*(Ghostty\w+)\s*;", headers))
    missing_manifest_types = header_types - set(abi["types"])
    if missing_manifest_types:
        findings.append(f"header types absent from ABI manifest: {sorted(missing_manifest_types)}")
    for match in re.finditer(r"typedef\s+(\w+)\s*\(\*(Ghostty\w+Fn)\)\s*\((.*?)\);", headers, re.S):
        native_name = match[2]
        managed_name = native_name[:-2] + "Callback"
        expected = [canonical(match[1])] + parameters(match[3])
        callback = callbacks.get(managed_name)
        if callback is None:
            findings.append(f"missing callback {native_name}")
            continue
        result, args, attrs = callback
        actual = [canonical(result, True, attrs)] + parameters(args, True)
        if expected != actual or "CallingConvention.Cdecl" not in attrs:
            findings.append(f"callback ABI mismatch {native_name}: expected {expected}, actual {actual}, attributes {attrs}")
        rows.append((native_name, managed_name, expected))

    for field in ("alloc", "resize", "remap", "free"):
        native = re.search(r"(void\*?|bool)\s*\(\*" + field + r"\)\((.*?)\);", headers)
        managed = re.search(r"delegate\* unmanaged\[Cdecl\]<([^>]+)> " + field.title() + ";", source)
        if native is None or managed is None:
            findings.append(f"missing allocator callback {field}")
            continue
        expected = [canonical(native[1])] + parameters(native[2])
        managed_parts = [canonical(value) for value in managed[1].split(",")]
        actual = [managed_parts[-1]] + managed_parts[:-1]
        if expected != actual:
            findings.append(f"allocator callback ABI mismatch {field}: {expected}, {actual}")
        rows.append(("GhosttyAllocatorVtable." + field, "GhosttyAllocatorVtable." + field.title(), expected))
    return rows, findings


def flags_checks(enums):
    headers = "\n".join(path.read_text() for path in sorted((ROOT / "external/ghostty/include/ghostty/vt/key").glob("*.h")))
    macros = dict(re.findall(r"^#define (GHOSTTY_(?:MODS|KITTY_KEY)_\w+) (.+)$", headers, re.M))
    checks, findings = [], []
    for name, expression in macros.items():
        prefix, managed = ("GHOSTTY_MODS_", "GhosttyVtMods") if name.startswith("GHOSTTY_MODS_") else ("GHOSTTY_KITTY_KEY_", "GhosttyKittyKeyFlags")
        member = lookup(enums[managed], name[len(prefix):])
        if member is None:
            findings.append(f"missing flag {name}")
            continue
        for dependency in re.findall(r"GHOSTTY_\w+", expression):
            expression = expression.replace(dependency, macros[dependency])
        if re.search(r"[^0-9()|<>&~+\- \t]", expression):
            findings.append(f"unsupported flag expression {name} = {expression}")
            continue
        checks.append(f'        Assert.Equal((long)({expression}), (long)GhosttyVtNative.{managed}.{member});')
    return checks, findings


def mode_checks():
    header = (ROOT / "external/ghostty/include/ghostty/vt/modes.h").read_text()
    source = (ROOT / "src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.Core.cs").read_text()
    members = {normalized(name[4:]): name for name in re.findall(r"public static GhosttyMode (Mode\w+) =>", source)}
    renamed = {"KAM": "KeyboardAction", "SRM": "SendReceive", "REVERSE_WRAP_EXT": "ReverseWrapExtended", "SYNC_OUTPUT": "SynchronizedOutput"}
    checks, findings = [], []
    for name, number, ansi in re.findall(r"^#define GHOSTTY_MODE_(\w+)\s+\(ghostty_mode_new\((\d+), (true|false)\)\)", header, re.M):
        member = lookup(members, renamed.get(name, name))
        if member is None:
            findings.append(f"missing mode {name}")
            continue
        packed = int(number) | (0x8000 if ansi == "true" else 0)
        checks += [f'        Assert.Equal((ushort){packed}, GhosttyVtNative.{member}.Value);',
                   f'        Assert.Equal((ushort){number}, GhosttyVtNative.ModeValue(GhosttyVtNative.{member}));',
                   f'        Assert.{"True" if ansi == "true" else "False"}(GhosttyVtNative.ModeIsAnsi(GhosttyVtNative.{member}));']
    return checks, findings


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("library")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    abi = manifest(args.library)
    enums, structs = declarations()
    findings = []
    counts = {}
    type_rows = []
    enum_checks, struct_checks, primitive_checks = [], [], []
    for name, descriptor in sorted(abi["types"].items()):
        kind = descriptor["kind"]
        counts[kind] = counts.get(kind, 0) + 1
        managed = TYPE_NAMES.get(name, name)
        type_rows.append((name, kind, managed if kind in ("struct", "union", "enum") else ("nint" if kind == "opaque" else ALIASES.get(name, "MISSING"))))
        if kind == "enum":
            members = enums.get(managed)
            if members is None:
                findings.append(f"missing enum {name} -> {managed}")
                continue
            qualified = "global::RoyalTerminal.GhosttySharp.Native.GhosttyColorScheme" if name == "GhosttyColorScheme" else "GhosttyVtNative." + managed
            checks = [f'        JsonElement type = Type("{name}", "enum");', f'        CheckSizeAndAlignment<{qualified}>(type);', f'        JsonElement values = type.GetProperty("values");', f'        Assert.Equal({len(descriptor["values"])}, values.EnumerateObject().Count());']
            for key in descriptor["values"]:
                if key.endswith("MAX_VALUE"):
                    checks.append(f'        Assert.Equal(int.MaxValue, values.GetProperty("{key}").GetInt64());')
                    continue
                member = lookup(members, key)
                if member is None:
                    findings.append(f"missing enum member {name}.{key} = {descriptor['values'][key]}")
                else:
                    checks.append(f'        Assert.Equal((long){qualified}.{member}, values.GetProperty("{key}").GetInt64());')
            enum_checks.append(method(name, checks))
        elif kind in ("struct", "union"):
            fields = structs.get(managed)
            if fields is None:
                findings.append(f"missing {kind} {name} -> {managed}")
                continue
            qualified = "GhosttyVtNative." + managed
            auto = any("get;" in field["end"] for field in fields.values())
            initialize = "new((nint)0x1234, (nint)0x5678)" if auto else "default"
            checks = [f'        JsonElement type = Type("{name}", "{kind}");', f'        CheckSizeAndAlignment<{qualified}>(type);', f'        {qualified} value = {initialize};', '        JsonElement fields = type.GetProperty("fields");', f'        Assert.Equal({len(descriptor["fields"])}, fields.EnumerateObject().Count());']
            for key, native_field in descriptor["fields"].items():
                member = fields.get(normalized(FIELD_NAMES.get((name, key), ""))) or lookup(fields, key)
                if key == "_padding" and member is None:
                    checks.append(f'        Assert.Equal(0, fields.GetProperty("{key}").GetProperty("offset").GetInt32());')
                    continue
                if member is None:
                    findings.append(f"missing field {name}.{key} ({list(fields)})")
                    continue
                field_type = member["type"]
                if field_type.startswith("Ghostty"):
                    field_type = "GhosttyVtNative." + field_type
                if field_type.startswith("delegate*"):
                    field_type = "nint"
                if "get;" in member["end"]:
                    checks.append(f'        Assert.Equal(value.{member["name"]}, *(nint*)((byte*)&value + fields.GetProperty("{key}").GetProperty("offset").GetInt32()));')
                else:
                    pointer = f'value.{member["name"]}' if member["fixed"] else f'&value.{member["name"]}'
                    checks.append(f'        CheckField(fields, "{key}", (byte*)({pointer}) - (byte*)&value, sizeof({field_type})' + (f' * ({member["count"]})' if member["fixed"] else '') + ');')
            struct_checks.append(method(name, checks))
        elif kind in ("opaque", "alias", "packed"):
            managed_type = "nint" if kind == "opaque" else ALIASES.get(name)
            if managed_type is None:
                findings.append(f"unmapped primitive {name}")
            else:
                primitive_checks.append(f'        CheckSizeAndAlignment<{managed_type}>(Type("{name}", "{kind}"));')
    print(f"Manifest: {abi['commit']}; {abi['abi']}; {len(abi['types'])} types; {counts}")
    callbacks, callback_findings = callback_inventory(abi)
    findings += callback_findings
    flags, flag_findings = flags_checks(enums)
    findings += flag_findings
    modes, mode_findings = mode_checks()
    findings += mode_findings
    if findings:
        print("\n".join(findings))
        return 1
    output = ROOT / "tests/RoyalTerminal.IntegrationTests/GhosttyAbiManifestTests.Generated.cs"
    generated = HEADER + "\n".join(enum_checks + struct_checks) + method("HeaderModifierAndKittyFlagConstants", flags) + method("HeaderModeConstants", modes) + method("PrimitiveAliasesAndOpaqueHandles", primitive_checks) + FOOTER.replace("TYPE_COUNT", str(len(abi["types"])))
    if args.check:
        if not output.exists() or output.read_text() != generated:
            print(f"Generated tests are stale: {output}")
            return 1
    else:
        output.write_text(generated)
    inventory = "# Ghostty ABI binding inventory\n\nGenerated by `scripts/audit-ghostty-abi.py` from the pinned native\n`ghostty_type_json` manifest and all public VT headers. No .NET reflection.\nRegenerate with a freshly built native package; `--check` rejects stale output.\n\n"
    inventory += f"Types: **{len(abi['types'])}** ({', '.join(f'{v} {k}' for k, v in sorted(counts.items()))}).\nCallback signatures: **{len(callbacks)}** (including four allocator vtable entries).\n\n"
    inventory += f"Header-only constants: **{len(flags)}** modifier/Kitty-key flags and **{len(modes) // 3}** terminal modes (packed value, mode number, ANSI flag).\n\n"
    inventory += "All enum members are named compile-time C# references compared with the runtime\nmanifest's numeric values; enum-width sentinels are checked as `int.MaxValue`.\nStruct/union size, alignment, field offsets and field sizes are checked. Reserved\nunion padding is represented by explicit struct size (and its native zero offset).\nRead/write callback auto-properties are checked using distinct constructor\nmarkers at their native offsets. Opaque handles are `nint`; aliases/packed cells\nare checked at their managed scalar width. The runtime type/member counts make\nupstream additions fail validation until reviewed. Callback parameters/returns,\nCdecl, one-byte bool marshalling and allocator function-pointer signatures are\nsource-audited; raw typed pointers represented by `nint` remain pointer-width\nABI equivalents, not claims about ownership. Runtime ownership tests are separate.\n\n"
    inventory += "## Public types\n\n| Native type | Kind | Managed type |\n|---|---|---|\n"
    inventory += "".join(f"| `{name}` | {kind} | `{managed}` |\n" for name, kind, managed in type_rows)
    inventory += "\n## Callback signatures\n\n| Native callback | Managed callback | ABI return; arguments |\n|---|---|---|\n"
    inventory += "".join(f"| `{name}` | `{managed}` | `{signature[0]}; {', '.join(signature[1:])}` |\n" for name, managed, signature in callbacks)
    inventory_path = ROOT / "docs/specs/ghostty-abi-inventory-2026.md"
    if args.check:
        if not inventory_path.exists() or inventory_path.read_text() != inventory:
            print(f"Generated inventory is stale: {inventory_path}")
            return 1
    else:
        inventory_path.write_text(inventory)
    print(f"Validated source coverage and {'checked' if args.check else 'generated'} all {len(abi['types'])} ABI type checks and {len(callbacks)} callback signatures.")
    return 0


def method(name, lines):
    lines = [re.sub(r'Assert.Equal\(1, (\w+).EnumerateObject\(\).Count\(\)\);', r'Assert.Single(\1.EnumerateObject());', line) for line in lines]
    return '    [GhosttyNativeFact]\n    public unsafe void ' + name + '()\n    {\n' + '\n'.join(lines) + '\n    }\n'


HEADER = '''// <auto-generated />
// Generated by scripts/audit-ghostty-abi.py. Do not edit by hand.
// Every binding name/field is checked at compile time; no reflection is used.
#nullable enable
using System.Text.Json;
using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttyAbiManifestTests : IDisposable
{
    private JsonDocument? _manifest;

'''
FOOTER = '''
    private JsonElement Type(string name, string kind)
    {
        _manifest ??= JsonDocument.Parse(GhosttyVtHelpers.GetTypeMetadataJson());
        JsonElement types = _manifest.RootElement.GetProperty("types");
        Assert.Equal(TYPE_COUNT, types.EnumerateObject().Count());
        JsonElement type = types.GetProperty(name);
        Assert.Equal(kind, type.GetProperty("kind").GetString());
        return type;
    }

    private static unsafe void CheckSizeAndAlignment<T>(JsonElement type) where T : unmanaged
    {
        AlignmentProbe<T> probe = default;
        Assert.Equal(sizeof(T), type.GetProperty("size").GetInt32());
        Assert.Equal((byte*)&probe.Value - (byte*)&probe, type.GetProperty("align").GetInt32());
    }

    private static void CheckField(JsonElement fields, string name, long offset, int size)
    {
        JsonElement field = fields.GetProperty(name);
        Assert.Equal(offset, field.GetProperty("offset").GetInt32());
        Assert.Equal(size, field.GetProperty("size").GetInt32());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AlignmentProbe<T> where T : unmanaged
    {
        internal byte Prefix;
        internal T Value;
    }

    public void Dispose() => _manifest?.Dispose();
}
'''


if __name__ == "__main__":
    raise SystemExit(main())
