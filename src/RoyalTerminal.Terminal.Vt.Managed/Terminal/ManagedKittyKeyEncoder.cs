// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Text;
using System.Text;

namespace RoyalTerminal.Terminal;

// Ghostty input/key_encode.zig Kitty path and input/kitty.zig registry. All five
// progressive flags are independent; in particular report-events alone is not
// equivalent to report-all, and special finals use a different event encoding.
internal static class ManagedKittyKeyEncoder
{
    internal static bool TryEncode(in TerminalKeyEncodingRequest request, int flags, out byte[] sequence)
    {
        sequence = [];
        bool reportAll = (flags & 8) != 0, reportEvents = (flags & 2) != 0;
        bool release = request.Action == TerminalInputAction.Release;
        if (release && (!reportEvents || !reportAll && request.KeyId is "Return" or "Back" or "Tab")) return false;
        Entry entry = Lookup(request.KeyId);
        int baseCode = PhysicalCodepoint(request.KeyId);
        if (entry.Code == 0) entry = new((int)(request.UnshiftedCodepoint != 0 ? request.UnshiftedCodepoint : (uint)baseCode), 'u');
        string text = request.Text ?? string.Empty;
        if (request.IsComposing)
        {
            if (!entry.Modifier) return false;
        }
        else
        {
            if (text.Length != 0 && request.KeyId is "Return" or "Back" && !(text.Length == 1 && IsControl(text[0])))
            {
                if (request.KeyId == "Back") return false;
                sequence = Encoding.UTF8.GetBytes(text);
                return sequence.Length != 0;
            }
            TerminalModifiers binding = request.Modifiers;
            if (text.Length != 0) binding &= ~request.ConsumedModifiers;
            binding &= TerminalModifiers.Shift | TerminalModifiers.Control | TerminalModifiers.Alt | TerminalModifiers.Meta;
            if (!reportAll && binding == 0)
            {
                int legacy = request.KeyId switch { "Return" => 13, "Back" => 127, "Tab" => 9, _ => 0 };
                if (legacy != 0) { sequence = [(byte)legacy]; return true; }
                if (!release && text.Length != 0 && !ContainsControl(text))
                { sequence = Encoding.UTF8.GetBytes(text); return true; }
            }
        }
        if (entry.Code == 0)
        {
            if (release || text.Length == 0) return false;
            sequence = Encoding.UTF8.GetBytes(text);
            return true;
        }
        if (entry.Modifier && !reportAll) return false;

        TerminalModifiers mods = request.Modifiers;
        int modifier = 1 + ((mods & TerminalModifiers.Shift) != 0 ? 1 : 0) +
            ((mods & TerminalModifiers.Alt) != 0 ? 2 : 0) + ((mods & TerminalModifiers.Control) != 0 ? 4 : 0) +
            ((mods & TerminalModifiers.Meta) != 0 ? 8 : 0) + ((mods & TerminalModifiers.CapsLock) != 0 ? 64 : 0) +
            ((mods & TerminalModifiers.NumLock) != 0 ? 128 : 0);
        int eventType = !reportEvents ? 0 : request.Action switch { TerminalInputAction.Release => 3, TerminalInputAction.Repeat => 2, _ => 1 };
        int capacity = checked(128 + text.Length * 8);
        byte[]? rented = null;
        Span<byte> buffer = capacity <= 512 ? stackalloc byte[512] : (rented = ArrayPool<byte>.Shared.Rent(capacity));
        try
        {
            Writer writer = new(buffer);
            writer.Add(27); writer.Add((byte)'[');
            if (entry.Final is not ('u' or '~'))
            {
                if (eventType != 0 || modifier > 1)
                {
                    writer.Add((byte)'1'); writer.Add((byte)';'); writer.Number(modifier);
                    if (eventType != 0) { writer.Add((byte)':'); writer.Number(eventType); }
                }
            }
            else
            {
                writer.Number(entry.Code);
                if ((flags & 4) != 0 && !IsControl(entry.Code))
                {
                    StringRuneEnumerator runes = text.EnumerateRunes();
                    bool hasFirst = runes.MoveNext();
                    int first = hasFirst ? runes.Current.Value : 0;
                    bool hasSecond = hasFirst && runes.MoveNext();
                    bool shifted = hasFirst && first != entry.Code && (mods & TerminalModifiers.Shift) != 0;
                    if (shifted) { writer.Add((byte)':'); writer.Number(first); }
                    if (baseCode != 0 && baseCode != entry.Code && (!hasFirst || first != baseCode && !hasSecond))
                    {
                        if (!shifted) writer.Add((byte)':');
                        writer.Add((byte)':'); writer.Number(baseCode);
                    }
                }
                bool prior = eventType > 1 || modifier > 1;
                if (prior)
                {
                    writer.Add((byte)';'); writer.Number(modifier);
                    if (eventType > 1) { writer.Add((byte)':'); writer.Number(eventType); }
                }
                TerminalModifiers prevent = TerminalModifiers.Control | TerminalModifiers.Meta;
                if (!OperatingSystem.IsMacOS()) prevent |= TerminalModifiers.Alt;
                if ((flags & 16) != 0 && eventType != 3 && (mods & prevent) == 0)
                {
                    bool first = true;
                    foreach (Rune rune in text.EnumerateRunes())
                    {
                        if (IsControl(rune.Value)) continue;
                        if (first)
                        {
                            if (!prior) writer.Add((byte)';');
                            writer.Add((byte)';'); first = false;
                        }
                        else writer.Add((byte)':');
                        writer.Number(rune.Value);
                    }
                }
            }
            writer.Add((byte)entry.Final);
            sequence = buffer[..writer.Length].ToArray();
            return true;
        }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }

    private static bool ContainsControl(string text)
    {
        foreach (Rune rune in text.EnumerateRunes()) if (IsControl(rune.Value)) return true;
        return false;
    }

    private static bool IsControl(int value) => value < 32 || value == 127;

    private static int PhysicalCodepoint(string? key)
    {
        if (key is { Length: 1 } && key[0] is >= 'A' and <= 'Z') return key[0] + 32;
        if (key is { Length: 2 } && key[0] == 'D' && key[1] is >= '0' and <= '9') return key[1];
        if (key is { Length: 7 } && key.StartsWith("NumPad", StringComparison.Ordinal) && key[6] is >= '0' and <= '9') return key[6];
        return key switch
        {
            "Space" => 32, "OemMinus" => '-', "OemPlus" => '=', "OemOpenBrackets" => '[', "OemCloseBrackets" => ']',
            "OemBackslash" or "OemPipe" => '\\', "OemSemicolon" => ';', "OemQuotes" => '\'', "OemTilde" => '`',
            "OemComma" => ',', "OemPeriod" => '.', "Oem2" => '/',
            "Decimal" => '.', "Divide" => '/', "Multiply" => '*', "Subtract" => '-', "Add" => '+', _ => 0,
        };
    }

    private static Entry Lookup(string? key)
    {
        if (key is { Length: > 1 } && key[0] == 'F' && int.TryParse(key.AsSpan(1), out int function) && function is >= 1 and <= 25)
        {
            if (function >= 13) return new(57376 + function - 13, 'u');
            return function switch
            {
                1 => new(1, 'P'), 2 => new(1, 'Q'), 3 => new(13, '~'), 4 => new(1, 'S'),
                5 => new(15, '~'), 6 => new(17, '~'), 7 => new(18, '~'), 8 => new(19, '~'),
                9 => new(20, '~'), 10 => new(21, '~'), 11 => new(23, '~'), _ => new(24, '~'),
            };
        }
        if (key is { Length: 7 } && key.StartsWith("NumPad", StringComparison.Ordinal) && key[6] is >= '0' and <= '9')
            return new(57399 + key[6] - '0', 'u');
        return key switch
        {
            "Escape" => new(27, 'u'), "Return" => new(13, 'u'), "Tab" => new(9, 'u'), "Back" => new(127, 'u'),
            "Insert" => new(2, '~'), "Delete" => new(3, '~'), "Left" => new(1, 'D'), "Right" => new(1, 'C'),
            "Up" => new(1, 'A'), "Down" => new(1, 'B'), "PageUp" => new(5, '~'), "PageDown" => new(6, '~'),
            "Home" => new(1, 'H'), "End" => new(1, 'F'), "CapsLock" => new(57358, 'u', true),
            "Scroll" => new(57359, 'u'), "NumLock" => new(57360, 'u', true), "PrintScreen" => new(57361, 'u'), "Pause" => new(57362, 'u'),
            "Decimal" => new(57409, 'u'), "Divide" => new(57410, 'u'), "Multiply" => new(57411, 'u'),
            "Subtract" => new(57412, 'u'), "Add" => new(57413, 'u'), "Separator" => new(57416, 'u'),
            "LeftShift" => new(57441, 'u', true), "RightShift" => new(57447, 'u', true),
            "LeftCtrl" => new(57442, 'u', true), "RightCtrl" => new(57448, 'u', true),
            "LeftAlt" => new(57443, 'u', true), "RightAlt" => new(57449, 'u', true),
            "LWin" => new(57444, 'u', true), "RWin" => new(57450, 'u', true), _ => default,
        };
    }

    private readonly record struct Entry(int Code, char Final, bool Modifier = false);

    private ref struct Writer(Span<byte> buffer)
    {
        private readonly Span<byte> _buffer = buffer;
        internal int Length { get; private set; }
        internal void Add(byte value) => _buffer[Length++] = value;
        internal void Number(int value)
        {
            if (!Utf8Formatter.TryFormat(value, _buffer[Length..], out int written)) throw new InvalidOperationException("Key encoding capacity exceeded.");
            Length += written;
        }
    }
}
