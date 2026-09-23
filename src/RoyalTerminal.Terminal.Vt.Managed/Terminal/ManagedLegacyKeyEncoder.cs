// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Text;
using System.Text;

namespace RoyalTerminal.Terminal;

// Ghostty input/key_encode.zig legacy() and function_keys.zig. PC-style keys
// precede modifyOtherKeys, C0, fixterms and text, in that order. The default
// native C encoder treats macOS Option as text (not configurable Alt).
internal static class ManagedLegacyKeyEncoder
{
    private const TerminalModifiers BindingMask = TerminalModifiers.Shift | TerminalModifiers.Alt |
        TerminalModifiers.Control | TerminalModifiers.Meta;

    internal static bool TryEncode(in TerminalKeyEncodingRequest request, bool cursorApplication,
        bool keypadApplication, bool ignoreKeypad, bool altEscape, bool backarrow, bool modifyOther,
        out byte[] sequence)
    {
        sequence = [];
        if (request.IsComposing || request.Action == TerminalInputAction.Release) return false;
        string text = request.Text ?? string.Empty;
        TerminalModifiers all = request.Modifiers & BindingMask;
        TerminalModifiers effective = text.Length == 0 ? all : all & ~request.ConsumedModifiers;
        int physical = ManagedKittyKeyEncoder.PhysicalCodepoint(request.KeyId);
        int unshifted = request.UnshiftedCodepoint == 0 ? physical : (int)request.UnshiftedCodepoint;
        bool ime = text.Length != 0 && !(text.Length == 1 && (text[0] < 32 || text[0] == 127));
        if (ime && request.KeyId == "Back") return false;
        if (!(ime && request.KeyId is "Return" or "Escape") &&
            TryPcKey(request.KeyId, all, cursorApplication, keypadApplication && !ignoreKeypad,
                backarrow, modifyOther, out sequence)) return true;

        bool single = TrySingleRune(text, out int scalar);
        if (modifyOther && single)
        {
            TerminalModifiers mods = OperatingSystem.IsMacOS() ? all & ~TerminalModifiers.Alt : all;
            if (mods != 0 && (scalar is >= 0x40 and <= 0x7F || scalar == 32 || (mods & ~TerminalModifiers.Shift) != 0))
            {
                sequence = Numeric("\u001b[27;"u8, Modifier(mods), scalar, (byte)'~');
                return true;
            }
        }
        if (TryControl(text, physical, unshifted, all, out byte control))
        {
            sequence = (effective & TerminalModifiers.Alt) != 0 ? [(byte)27, control] : [control];
            return true;
        }
        if (text.Length == 0)
        {
            if (!OperatingSystem.IsMacOS() && altEscape && (effective & TerminalModifiers.Alt) != 0 && unshifted != 0)
            {
                Span<byte> buffer = stackalloc byte[5];
                buffer[0] = 27;
                int length = new Rune(unshifted).EncodeToUtf8(buffer[1..]);
                sequence = buffer[..(length + 1)].ToArray();
                return true;
            }
            return false;
        }
        if ((all & TerminalModifiers.Control) != 0 && single)
        {
            TerminalModifiers mods = all & ~TerminalModifiers.Meta;
            if ((mods & TerminalModifiers.Shift) != 0 && scalar is >= 'A' and <= 'Z') scalar += 32;
            if (unshifted != scalar) mods &= ~TerminalModifiers.Shift;
            sequence = Numeric("\u001b["u8, scalar, Modifier(mods), (byte)'u');
            return true;
        }
        if (!OperatingSystem.IsMacOS() && altEscape && (effective & TerminalModifiers.Alt) != 0)
        {
            sequence = new byte[checked(1 + Encoding.UTF8.GetByteCount(text))];
            sequence[0] = 27;
            Encoding.UTF8.GetBytes(text, sequence.AsSpan(1));
            return true;
        }
        if (OperatingSystem.IsMacOS() && (all & TerminalModifiers.Meta) != 0) return false;
        sequence = Encoding.UTF8.GetBytes(text);
        return sequence.Length != 0;
    }

    private static bool TryPcKey(string key, TerminalModifiers mods, bool cursorApplication,
        bool keypadApplication, bool backarrow, bool modifyOther, out byte[] sequence)
    {
        sequence = [];
        int modifier = Modifier(mods);
        char cursor = key switch { "Up" => 'A', "Down" => 'B', "Right" => 'C', "Left" => 'D', "Home" => 'H', "End" => 'F', _ => '\0' };
        if (cursor != 0)
        {
            sequence = mods == 0 ? [(byte)27, (byte)(cursorApplication ? 'O' : '['), (byte)cursor]
                : Numeric("\u001b["u8, 1, modifier, (byte)cursor);
            return true;
        }
        int tilde = key switch { "Insert" => 2, "Delete" => 3, "PageUp" => 5, "PageDown" => 6, "Apps" => 29, _ => 0 };
        if (key.Length > 1 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out int function) && function is >= 1 and <= 25)
        {
            if (function <= 4 && mods == 0) { sequence = [(byte)27, (byte)'O', (byte)('P' + function - 1)]; return true; }
            if (function is 1 or 2 or 4) { sequence = Numeric("\u001b["u8, 1, modifier, (byte)('P' + function - 1)); return true; }
            tilde = function switch
            {
                3 => 13, 5 => 15, 6 => 17, 7 => 18, 8 => 19, 9 => 20, 10 => 21, 11 => 23, 12 => 24,
                13 => 25, 14 => 26, 15 => 28, 16 => 29, 17 => 31, 18 => 32, 19 => 33, 20 => 34,
                21 => 42, 22 => 43, 23 => 44, 24 => 45, _ => 46,
            };
        }
        if (tilde != 0)
        {
            sequence = Numeric("\u001b["u8, tilde, mods == 0 ? null : modifier, (byte)'~');
            return true;
        }
        char keypad = key switch { "Decimal" => 'n', "Divide" => 'o', "Multiply" => 'j', "Subtract" => 'm', "Add" => 'k', _ => '\0' };
        if (key.Length == 7 && key.StartsWith("NumPad", StringComparison.Ordinal) && key[6] is >= '0' and <= '9')
            keypad = (char)('p' + key[6] - '0');
        if (keypad != 0)
        {
            sequence = !keypadApplication ? [(byte)ManagedKittyKeyEncoder.PhysicalCodepoint(key)]
                : mods == 0 ? [(byte)27, (byte)'O', (byte)keypad]
                : Numeric("\u001bO"u8, modifier, null, (byte)keypad);
            return true;
        }
        int code = key switch { "Back" => 127, "Tab" => 9, "Return" => 13, "Escape" => 27, _ => 0 };
        if (code == 0) return false;
        if (key == "Back")
        {
            if (modifyOther && mods is not (0 or TerminalModifiers.Control))
                sequence = Numeric("\u001b[27;"u8, modifier, code, (byte)'~');
            else if (mods is 0 or TerminalModifiers.Control)
                sequence = [((mods == TerminalModifiers.Control) != backarrow) ? (byte)8 : (byte)127];
            // Upstream has no normal-mode Ctrl+Alt+Shift entry: the final
            // any-modifier default applies, including its DECBKM override.
            else if (mods == (TerminalModifiers.Control | TerminalModifiers.Alt | TerminalModifiers.Shift))
                sequence = [backarrow ? (byte)8 : (byte)127];
            else
            {
                byte value = (mods & TerminalModifiers.Control) != 0 ? (byte)8 : (byte)127;
                sequence = (mods & TerminalModifiers.Alt) != 0 ? [(byte)27, value] : [value];
            }
        }
        else if (mods == 0) sequence = [(byte)code];
        else if (!modifyOther && mods == TerminalModifiers.Alt) sequence = [(byte)27, (byte)code];
        else if (!modifyOther && key == "Tab" && mods == TerminalModifiers.Shift) sequence = "\u001b[Z"u8.ToArray();
        else sequence = Numeric("\u001b[27;"u8, modifier, code, (byte)'~');
        return true;
    }

    private static bool TryControl(string text, int physical, int unshifted, TerminalModifiers mods, out byte value)
    {
        value = 0;
        if ((mods & TerminalModifiers.Control) == 0) return false;
        mods &= ~TerminalModifiers.Alt;
        int character;
        if (text.Length == 1 && text[0] < 128) character = text[0];
        else
        {
            if (physical is <= 0 or > 255 || mods != TerminalModifiers.Control) return false;
            character = physical;
        }
        if ((mods & TerminalModifiers.Shift) != 0 && character is not (>= 'A' and <= 'Z') && character != '@')
            mods &= ~TerminalModifiers.Shift;
        if (character is >= 'A' and <= 'Z' && unshifted is > 0 and <= 255) character = unshifted;
        if (mods != TerminalModifiers.Control) return false;
        int result = character switch
        {
            ' ' or '2' or '@' => 0, '/' or '7' or '_' => 31, '3' => 27, '4' or '\\' => 28,
            '5' or ']' => 29, '6' or '^' or '~' => 30, '8' or '?' => 127,
            '0' or '1' or '9' => character,
            >= 'a' and <= 'z' when character is not ('i' or 'm') => character - 'a' + 1,
            _ => -1,
        };
        if (result < 0) return false;
        value = (byte)result;
        return true;
    }

    private static bool TrySingleRune(string text, out int scalar)
    {
        // EnumerateRunes uses the same replacement policy as native SetText's
        // UTF-16 -> UTF-8 conversion, including isolated surrogate input.
        StringRuneEnumerator runes = text.EnumerateRunes();
        scalar = runes.MoveNext() ? runes.Current.Value : -1;
        return scalar >= 0 && !runes.MoveNext();
    }

    private static int Modifier(TerminalModifiers mods) => 1 +
        ((mods & TerminalModifiers.Shift) != 0 ? 1 : 0) + ((mods & TerminalModifiers.Alt) != 0 ? 2 : 0) +
        ((mods & TerminalModifiers.Control) != 0 ? 4 : 0) + ((mods & TerminalModifiers.Meta) != 0 ? 8 : 0);

    private static byte[] Numeric(ReadOnlySpan<byte> prefix, int first, int? second, byte final)
    {
        Span<byte> buffer = stackalloc byte[32];
        prefix.CopyTo(buffer);
        Utf8Formatter.TryFormat(first, buffer[prefix.Length..], out int written);
        int length = prefix.Length + written;
        if (second.HasValue)
        {
            buffer[length++] = (byte)';';
            Utf8Formatter.TryFormat(second.Value, buffer[length..], out written);
            length += written;
        }
        buffer[length++] = final;
        return buffer[..length].ToArray();
    }
}
