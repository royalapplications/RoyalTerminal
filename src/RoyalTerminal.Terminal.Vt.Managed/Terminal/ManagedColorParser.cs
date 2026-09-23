// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Ghostty RGB.parse semantics, independent of host theme parsing.</summary>
internal static partial class ManagedColorParser
{
    internal static bool TryParse(ReadOnlySpan<char> value, out uint color)
    {
        color = 0;
        value = value.Trim(" \t");
        if (value.IsEmpty) return false;
        if (value[0] == '#')
        {
            value = value[1..];
            return value.Length is 3 or 6 or 9 or 12 && HexTriplet(value, out color);
        }
        // Native names use ASCII case folding, not Unicode case equivalence.
        foreach (char c in value) if (c > 127) return false;
        if (NamedColors.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(value, out color)) return true;
        if (value.Length is 3 or 6) return HexTriplet(value, out color);
        bool intensity = value.StartsWith("rgbi:");
        if (!intensity && !value.StartsWith("rgb:")) return false;
        value = value[(intensity ? 5 : 4)..];
        Span<byte> channels = stackalloc byte[3];
        for (int i = 0; i < channels.Length; i++)
        {
            int slash = value.IndexOf('/');
            if ((i < 2) != (slash >= 0)) return false;
            ReadOnlySpan<char> channel = slash >= 0 ? value[..slash] : value;
            if (intensity)
            {
                if (!Fraction(channel, out double fraction)) return false;
                channels[i] = (byte)(fraction * 255);
            }
            else if (!HexChannel(channel, out channels[i])) return false;
            if (slash >= 0) value = value[(slash + 1)..];
        }
        color = Pack(channels[0], channels[1], channels[2]);
        return true;
    }

    private static bool HexTriplet(ReadOnlySpan<char> value, out uint color)
    {
        color = 0;
        int width = value.Length / 3;
        if (!HexChannel(value[..width], out byte r) || !HexChannel(value.Slice(width, width), out byte g) ||
            !HexChannel(value[(width * 2)..], out byte b)) return false;
        color = Pack(r, g, b);
        return true;
    }

    private static bool HexChannel(ReadOnlySpan<char> value, out byte channel)
    {
        channel = 0;
        if (value.Length is < 1 or > 4 || !Unsigned(value, 16, 65535, out int number)) return false;
        // Ghostty scales then truncates for both # and rgb: syntax. Underscores
        // are accepted by Zig parseUnsigned; the divisor uses the original width.
        channel = (byte)(number * 255 / ((1 << (value.Length * 4)) - 1));
        return true;
    }

    internal static bool Unsigned(ReadOnlySpan<char> value, int radix, int maximum, out int result)
    {
        result = 0;
        if (value.IsEmpty || value[0] == '_' || value[^1] == '_') return false;
        foreach (char c in value)
        {
            if (c == '_') continue;
            int digit = c is >= '0' and <= '9' ? c - '0' :
                c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
            if ((uint)digit >= radix || result > (maximum - digit) / radix) return false;
            result = result * radix + digit;
        }
        return true;
    }

    // Port of terminal/fraction.zig: no exponents/NaN/locale, and only the first
    // 15 fractional digits accumulate; all remaining digits are still validated.
    private static bool Fraction(ReadOnlySpan<char> value, out double result)
    {
        result = 0;
        bool negative = !value.IsEmpty && value[0] == '-';
        if (!value.IsEmpty && value[0] is '+' or '-') value = value[1..];
        int index = 0, digits = 0;
        while (index < value.Length && value[index] != '.')
        {
            char digit = value[index++];
            if (digit is < '0' or > '9') return false;
            result = result * 10 + (digit - '0');
            digits++;
        }
        ulong fraction = 0, scale = 1;
        if (index < value.Length)
        {
            index++;
            while (index < value.Length)
            {
                char digit = value[index++];
                if (digit is < '0' or > '9') return false;
                if (scale < 1_000_000_000_000_000) { fraction = fraction * 10 + (uint)(digit - '0'); scale *= 10; }
                digits++;
            }
        }
        result += (double)fraction / scale;
        if (negative) result = -result;
        return digits > 0 && result >= 0 && result <= 1;
    }

    private static uint Pack(byte r, byte g, byte b) => 0xFF000000u | (uint)(r << 16 | g << 8 | b);
}
