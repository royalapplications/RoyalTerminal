// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Text;
using System.Text;

namespace RoyalTerminal.Terminal;

// Ghostty input/key_encode.zig legacy mode 2 and function_keys.zig. This
// encodes the extension only; false leaves unchanged legacy keys to the host.
internal static class ManagedModifyOtherKeysEncoder
{
    internal static bool TryEncode(in TerminalKeyEncodingRequest request, bool backarrow, out byte[] sequence)
    {
        sequence = [];
        if (request.IsComposing || request.Action == TerminalInputAction.Release) return false;
        TerminalModifiers mods = request.Modifiers & (TerminalModifiers.Shift | TerminalModifiers.Alt | TerminalModifiers.Control | TerminalModifiers.Meta);
        int codepoint = request.KeyId switch { "Back" => 127, "Tab" => 9, "Return" => 13, "Escape" => 27, _ => 0 };
        if (codepoint != 0)
        {
            // Back/Return/Escape may carry committed IME text instead of a
            // physical control key. Tab always uses its PC-style mapping.
            if (codepoint != 9 && !string.IsNullOrEmpty(request.Text) &&
                (request.Text.Length != 1 || (request.Text[0] >= 0x20 && request.Text[0] != 0x7F))) return false;
            if (codepoint == 127 && mods is TerminalModifiers.None or TerminalModifiers.Control)
            {
                sequence = [((mods == TerminalModifiers.Control) != backarrow) ? (byte)8 : (byte)127];
                return true;
            }
            if (mods == TerminalModifiers.None) return false;
        }
        else
        {
            // Keys with PC-style mappings must retain those mappings even if
            // the UI supplies text; only printable physical keys reach here.
            if (!IsPrintableKey(request.KeyId) || string.IsNullOrEmpty(request.Text) ||
                Rune.DecodeFromUtf16(request.Text, out Rune rune, out int consumed) != OperationStatus.Done ||
                consumed != request.Text.Length) return false;
            codepoint = rune.Value;
            // The C encoder's default macOS policy treats Option as text input.
            if (OperatingSystem.IsMacOS()) mods &= ~TerminalModifiers.Alt;
            if (mods == TerminalModifiers.None) return false;
            if (codepoint is not (>= 0x40 and <= 0x7F) && codepoint != 32 &&
                (mods & ~TerminalModifiers.Shift) == 0) return false;
        }

        int modifier = 1 + ((mods & TerminalModifiers.Shift) != 0 ? 1 : 0) +
            ((mods & TerminalModifiers.Alt) != 0 ? 2 : 0) +
            ((mods & TerminalModifiers.Control) != 0 ? 4 : 0) +
            ((mods & TerminalModifiers.Meta) != 0 ? 8 : 0);
        Span<byte> buffer = stackalloc byte[24];
        "\u001b[27;"u8.CopyTo(buffer);
        Utf8Formatter.TryFormat(modifier, buffer[5..], out int written);
        int offset = 5 + written;
        buffer[offset++] = (byte)';';
        Utf8Formatter.TryFormat(codepoint, buffer[offset..], out written);
        offset += written;
        buffer[offset++] = (byte)'~';
        sequence = buffer[..offset].ToArray();
        return true;
    }

    private static bool IsPrintableKey(string key) =>
        key is { Length: 1 } && key[0] is >= 'A' and <= 'Z' ||
        key is { Length: 2 } && key[0] == 'D' && key[1] is >= '0' and <= '9' ||
        key is "Space" or "OemPlus" or "OemMinus" or "OemComma" or "OemPeriod" or
            "OemOpenBrackets" or "OemCloseBrackets" or "OemBackslash" or "OemPipe" or
            "OemSemicolon" or "OemQuotes" or "OemTilde" or
            "Oem1" or "Oem2" or "Oem3" or "Oem4" or "Oem5" or "Oem6" or "Oem7" or "Oem8" or "Oem102";
}
