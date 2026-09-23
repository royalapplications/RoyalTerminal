#!/usr/bin/env python3
"""Generate managed Unicode properties from Ghostty's pinned uucode UCD."""

from __future__ import annotations

import argparse
import array
import hashlib
import pathlib
import re
import tarfile


ROOT = pathlib.Path(__file__).resolve().parents[1]
COUNT = 0x110000
GCB = {name: value for value, name in enumerate([
    "Other", "Control", "CR", "E_Base", "E_Base_GAZ", "E_Modifier", "Extend",
    "Glue_After_Zwj", "L", "LF", "LV", "LVT", "Prepend", "Regional_Indicator",
    "SpacingMark", "T", "V", "ZWJ", "Extended_Pictographic",
])}
EAW = {name: value for value, name in enumerate(["A", "F", "H", "N", "Na", "W"])}
INCB = {"None": 0, "Linker": 1, "Consonant": 2, "Extend": 3}


def records(text: str):
    for line in text.splitlines():
        value = line.split("#", 1)[0].strip()
        if not value:
            continue
        fields = [field.strip() for field in value.split(";")]
        bounds = fields[0].split("..")
        yield int(bounds[0], 16), int(bounds[-1], 16) + 1, fields[1:]


def assign(values, start, end, value):
    values[start:end] = array.array("B", [value]) * (end - start)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--uucode", type=pathlib.Path, help="Pinned uucode source directory or cached .tar.gz")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    zon = (ROOT / "external/ghostty/build.zig.zon").read_text()
    package = re.search(r'\.uucode = .*?\.hash = "([^"]+)"', zon, re.S).group(1)
    source = args.uucode or pathlib.Path.home() / ".cache/zig/p" / (package + ".tar.gz")
    digest = hashlib.sha256()

    def read(path):
        if source.is_dir():
            raw = (source / "ucd" / path).read_bytes()
        else:
            with tarfile.open(source) as archive:
                raw = archive.extractfile(package + "/ucd/" + path).read()
        digest.update(path.encode() + b"\0" + raw)
        return raw.decode("utf-8")

    grapheme_text = read("auxiliary/GraphemeBreakProperty.txt")
    if "GraphemeBreakProperty-18.0.0.txt" not in grapheme_text:
        raise RuntimeError("Review generation rules before changing the Unicode major version")
    gcb = array.array("B", [GCB["Other"]]) * COUNT
    for start, end, fields in records(grapheme_text):
        assign(gcb, start, end, GCB[fields[0]])
    eaw_text = read("extracted/DerivedEastAsianWidth.txt")
    eaw = array.array("B", [EAW["N"]]) * COUNT
    aliases = {"Neutral": "N", "Wide": "W"}
    for value in re.findall(r"# @missing: ([^\n]+)", eaw_text):
        for start, end, fields in records(value):
            assign(eaw, start, end, EAW[aliases.get(fields[0], fields[0])])
    for start, end, fields in records(eaw_text):
        assign(eaw, start, end, EAW[fields[0]])
    category_text = read("extracted/DerivedGeneralCategory.txt")
    category = array.array("B", [0]) * COUNT
    categories = {"Cc": 1, "Cs": 1, "Zl": 1, "Zp": 1, "Mn": 2, "Me": 2}
    for start, end, fields in records(category_text):
        assign(category, start, end, categories.get(fields[0], 0))
    derived_text = read("DerivedCoreProperties.txt")
    incb = array.array("B", [0]) * COUNT
    ignorable = array.array("B", [0]) * COUNT
    for start, end, fields in records(derived_text):
        if fields[0] == "InCB":
            assign(incb, start, end, INCB[fields[1]])
        elif fields[0] == "Default_Ignorable_Code_Point":
            assign(ignorable, start, end, 1)
    emoji_text = read("emoji/emoji-data.txt")
    modifier = array.array("B", [0]) * COUNT
    modifier_base = array.array("B", [0]) * COUNT
    for start, end, fields in records(emoji_text):
        if fields[0] == "Extended_Pictographic":
            assign(gcb, start, end, GCB["Extended_Pictographic"])
        elif fields[0] == "Emoji_Modifier":
            assign(modifier, start, end, 1)
        elif fields[0] == "Emoji_Modifier_Base":
            assign(modifier_base, start, end, 1)
    emoji_vs_text = read("emoji/emoji-variation-sequences.txt")
    emoji_vs = {int(line.split()[0], 16) for line in emoji_vs_text.splitlines()
                if line and not line.startswith("#") and ";" in line}

    values = []
    for cp in range(COUNT):
        # uucode components.zig Wcwidth and Ghostty uucode_config.zig WidthComponent.
        if category[cp] == 1:
            width = 0
        elif cp == 0xAD:
            width = 1
        elif ignorable[cp]:
            width = 0
        elif cp in (0x2E3A, 0x2E3B) or eaw[cp] in (EAW["W"], EAW["F"]) or gcb[cp] == GCB["Regional_Indicator"]:
            width = 2
        else:
            width = 1
        zero = width == 0 or modifier[cp] or category[cp] == 2 or gcb[cp] in (GCB["V"], GCB["T"], GCB["Prepend"])
        if zero and not modifier[cp] and gcb[cp] != GCB["Prepend"]:
            width = 0
        elif cp == 0x20E3:
            width = 2
        values.append(gcb[cp] | eaw[cp] << 5 | incb[cp] << 8 | width << 10 | int(bool(zero)) << 12 |
                      int(cp in emoji_vs) << 13 | modifier[cp] << 14 | modifier_base[cp] << 15)

    blocks = {}
    indexes = []
    data = []
    for offset in range(0, COUNT, 256):
        block = tuple(values[offset:offset + 256])
        if block not in blocks:
            blocks[block] = len(blocks)
            data.extend(block)
        indexes.append(blocks[block])

    def table(name, entries):
        # ReadOnlySpan literal helpers allocate RuntimeFieldHandle wrappers on
        # every lookup in .NET Debug builds. Private generated arrays are built
        # once, never mutated or exposed, and retain allocation-free lookups.
        lines = [f"    private static readonly ushort[] {name} =", "    ["]
        for offset in range(0, len(entries), 16):
            lines.append("        " + ", ".join(f"0x{value:04X}" for value in entries[offset:offset + 16]) + ",")
        return "\n".join(lines + ["    ];"])

    generated = "\n".join([
        "// Copyright (c) Royal Apps. All rights reserved.",
        "// Licensed under the MIT license. See LICENSE file in the project root for details.",
        "// <auto-generated />", "// Unicode 18.0.0, Ghostty pinned " + package,
        "// UCD © 2026 Unicode, Inc.; https://www.unicode.org/license.txt",
        "// Input SHA-256: " + digest.hexdigest(),
        "namespace RoyalTerminal.Unicode;", "", "internal static class Unicode18Data", "{",
        "    internal static ushort Get(uint codepoint) => codepoint <= 0x10FFFF",
        "        ? Values[(Blocks[(int)(codepoint >> 8)] << 8) | (int)(codepoint & 255)] : (ushort)0;", "",
        table("Blocks", indexes), "", table("Values", data), "}", "",
    ])
    outputs = {ROOT / "src/RoyalTerminal.Unicode/Unicode/Unicode18Data.Generated.cs": generated}
    fixtures = ROOT / "tests/RoyalTerminal.Tests/Fixtures/Unicode18"
    for name, contents in {
        "GraphemeBreakProperty.txt": grapheme_text,
        "DerivedEastAsianWidth.txt": eaw_text,
        "emoji-data.txt": emoji_text,
        "IndicConjunctBreak.txt": "\n".join(derived_text.splitlines()[:14]) + "\n" +
            "\n".join(line for line in derived_text.splitlines() if re.search(r";\s*InCB\s*;", line)) + "\n",
        "GraphemeBreakTest.txt": read("auxiliary/GraphemeBreakTest.txt"),
    }.items():
        outputs[fixtures / name] = contents
    for path, contents in outputs.items():
        if args.check:
            if not path.exists() or path.read_text() != contents:
                raise RuntimeError(f"Generated file is stale: {path}")
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(contents)
    print(f"Unicode18: {len(blocks)} unique blocks, {len(indexes) * 2 + len(data) * 2} lookup bytes")


if __name__ == "__main__":
    main()
