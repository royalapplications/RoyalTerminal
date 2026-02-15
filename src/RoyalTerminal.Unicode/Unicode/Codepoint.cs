// Licensed under the MIT License.
// Ported/adapted from Avalonia Codepoint utilities.

using System.Runtime.CompilerServices;

namespace GhosttySharp.Unicode;

public readonly record struct Codepoint
{
    private readonly uint _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Codepoint(uint value)
    {
        _value = value;
    }

    public uint Value => _value;

    public static Codepoint ReplacementCodepoint
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new('\uFFFD');
    }

    public GraphemeBreakClass GraphemeBreakClass => UnicodeData.GetGraphemeClusterBreak(_value);

    public EastAsianWidthClass EastAsianWidthClass => UnicodeData.GetEastAsianWidthClass(_value);

    public static implicit operator int(Codepoint codepoint)
    {
        return (int)codepoint._value;
    }

    public static implicit operator uint(Codepoint codepoint)
    {
        return codepoint._value;
    }

#if NET6_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public static Codepoint ReadAt(ReadOnlySpan<char> text, int index, out int count)
    {
        count = 1;

        if ((uint)index >= (uint)text.Length)
        {
            return ReplacementCodepoint;
        }

        uint code = text[index];

        if (IsInRangeInclusive(code, 0xD800U, 0xDFFFU))
        {
            uint hi;
            uint low;

            if (code <= 0xDBFF)
            {
                if ((uint)(index + 1) < (uint)text.Length)
                {
                    hi = code;
                    low = text[index + 1];

                    if (IsInRangeInclusive(low, 0xDC00U, 0xDFFFU))
                    {
                        count = 2;
                        return new Codepoint((hi << 10) + low - ((0xD800U << 10) + 0xDC00U - (1 << 16)));
                    }
                }
            }
            else
            {
                if (index > 0)
                {
                    low = code;
                    hi = text[index - 1];

                    if (IsInRangeInclusive(hi, 0xD800U, 0xDBFFU))
                    {
                        count = 2;
                        return new Codepoint((hi << 10) + low - ((0xD800U << 10) + 0xDC00U - (1 << 16)));
                    }
                }
            }

            return ReplacementCodepoint;
        }

        return new Codepoint(code);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInRangeInclusive(uint value, uint lowerBound, uint upperBound)
    {
        return value - lowerBound <= upperBound - lowerBound;
    }
}
