#!/usr/bin/env python3
"""Mutation checks for the source-level native-call ABI gate (stdlib only)."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("audit", Path(__file__).with_name("audit-ghostty-abi.py"))
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class ExportSignatureTests(unittest.TestCase):
    def findings(self, signature, convention=True):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            header = root / "external/ghostty/include/ghostty/vt/test.h"
            source = root / "src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.Test.cs"
            header.parent.mkdir(parents=True)
            source.parent.mkdir(parents=True)
            header.write_text("// UTF-8 comment: \u201cVT\u201d\nGHOSTTY_API bool ghostty_test(uint16_t count, const GhosttyString* text);", encoding="utf-8")
            source.write_text('// UTF-8 comment: \u201cVT\u201d\n[LibraryImport(LibName, EntryPoint = "ghostty_test")]\n'
                              + ('[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]\n' if convention else '')
                              + signature, encoding="utf-8")
            with patch.object(audit, "ROOT", root), patch.object(audit.ctypes, "CDLL"):
                return audit.export_inventory("unused", {"types": {}})[1]

    def test_pointer_forms_and_escaped_parameter_names(self):
        self.assertEqual([], self.findings('[return: MarshalAs(UnmanagedType.U1)] public static partial bool Test(ushort count, in GhosttyString @event);'))

    def test_mutations_are_rejected(self):
        for signature in [
            'public static partial bool Test(ushort count, in GhosttyString text);',
            'public static partial void Test(ushort count, in GhosttyString text);',
            '[return: MarshalAs(UnmanagedType.U1)] public static partial bool Test(uint count, in GhosttyString text);',
            '[return: MarshalAs(UnmanagedType.U1)] public static partial bool Test(ushort count, GhosttyString text);',
            '[return: MarshalAs(UnmanagedType.U1)] public static partial bool Test(ushort count);',
        ]:
            with self.subTest(signature=signature):
                self.assertTrue(self.findings(signature))
        self.assertTrue(self.findings('[return: MarshalAs(UnmanagedType.U1)] public static partial bool Test(ushort count, nint text);', convention=False))


if __name__ == "__main__":
    unittest.main()
