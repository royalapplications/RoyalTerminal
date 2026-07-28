// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Deterministic terminal cell width calculation backed by Unicode trie tables.

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace RoyalTerminal.Unicode;

public static class TerminalCellWidthCalculator
{
    private const int VariationSelector15 = 0xFE0E;
    private const int VariationSelector16 = 0xFE0F;
    private const int EmojiModifierStart = 0x1F3FB;
    private const int EmojiModifierEnd = 0x1F3FF;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCellWidth(int codepoint)
    {
        if (!Rune.IsValid(codepoint))
        {
            return 1;
        }

        Span<char> buffer = stackalloc char[2];
        int len = new Rune(codepoint).EncodeToUtf16(buffer);
        return GetCellWidth(buffer[..len]);
    }

    /// <summary>
    /// Gets the standalone terminal-cell width of one Unicode codepoint.
    /// </summary>
    /// <remarks>
    /// This mirrors terminal wcwidth-style behavior. Use
    /// <see cref="GetFirstGraphemeWidth"/> when cluster-level presentation
    /// selectors and emoji sequences must be considered.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCodepointWidth(int codepoint)
    {
        if (codepoint is >= 0xD800 and <= 0xDFFF)
        {
            return 0;
        }

        if (!Rune.IsValid(codepoint))
        {
            return 1;
        }

        int overrideWidth = GetGhosttyUnicode17WidthOverride(codepoint);
        if (overrideWidth >= 0)
        {
            return overrideWidth;
        }

        Codepoint value = new((uint)codepoint);
        if (IsControl(value.Value) ||
            IsDefaultIgnorable(value.Value) ||
            value.GraphemeBreakClass is GraphemeBreakClass.Control
                or GraphemeBreakClass.CR
                or GraphemeBreakClass.LF
                or GraphemeBreakClass.Extend
                or GraphemeBreakClass.V
                or GraphemeBreakClass.T
                or GraphemeBreakClass.ZWJ)
        {
            return 0;
        }

        if (value.GraphemeBreakClass == GraphemeBreakClass.RegionalIndicator ||
            value.Value is 0x2E3A or 0x2E3B)
        {
            return 2;
        }

        return value.EastAsianWidthClass is EastAsianWidthClass.Fullwidth or EastAsianWidthClass.Wide
            ? 2
            : 1;
    }

    public static int GetCellWidth(ReadOnlySpan<char> grapheme)
    {
        if (grapheme.IsEmpty)
        {
            return 0;
        }

        Codepoint first = Codepoint.ReadAt(grapheme, 0, out int firstLength);
        if (firstLength <= 0)
        {
            return 1;
        }

        int width = GetCodepointWidth(checked((int)first.Value));
        uint previous = first.Value;

        for (int index = firstLength; index < grapheme.Length;)
        {
            Codepoint current = Codepoint.ReadAt(grapheme, index, out int consumed);
            if (consumed <= 0)
            {
                break;
            }

            uint value = current.Value;
            if (value is VariationSelector15 or VariationSelector16)
            {
                if (IsEmojiVariationSequenceBase(previous))
                {
                    width = value == VariationSelector16 ? 2 : 1;
                    previous = value;
                }

                index += consumed;
                continue;
            }

            if (!IsZeroWidthInGrapheme(current))
            {
                width = 2;
            }

            previous = value;
            index += consumed;
        }

        return width;
    }

    /// <summary>
    /// Measures the first grapheme cluster in a codepoint sequence.
    /// </summary>
    /// <param name="codepoints">The available Unicode codepoints.</param>
    /// <param name="width">The first cluster's terminal-cell width.</param>
    /// <returns>The number of codepoints consumed by the first cluster.</returns>
    public static int GetFirstGraphemeWidth(ReadOnlySpan<uint> codepoints, out int width)
    {
        if (codepoints.IsEmpty)
        {
            width = 0;
            return 0;
        }

        int validCount = 0;
        int utf16Length = 0;
        while (validCount < codepoints.Length)
        {
            uint codepoint = codepoints[validCount];
            if (codepoint > 0x10FFFF)
            {
                if (validCount == 0)
                {
                    width = 1;
                    return 1;
                }

                break;
            }

            utf16Length += codepoint <= char.MaxValue ? 1 : 2;
            validCount++;
        }

        char[]? rented = null;
        Span<char> utf16 = utf16Length <= 128
            ? stackalloc char[utf16Length]
            : (rented = ArrayPool<char>.Shared.Rent(utf16Length));

        try
        {
            int written = 0;
            for (int index = 0; index < validCount; index++)
            {
                uint codepoint = codepoints[index];
                if (codepoint is >= 0xD800 and <= 0xDFFF)
                {
                    utf16[written++] = (char)codepoint;
                }
                else
                {
                    Rune rune = new(checked((int)codepoint));
                    written += rune.EncodeToUtf16(utf16[written..]);
                }
            }

            GraphemeEnumerator enumerator = new(utf16[..written]);
            if (!enumerator.MoveNext(out Grapheme grapheme))
            {
                width = 0;
                return 0;
            }

            int consumed = 0;
            int consumedUtf16 = 0;
            while (consumed < validCount && consumedUtf16 < grapheme.Length)
            {
                consumedUtf16 += codepoints[consumed] <= char.MaxValue ? 1 : 2;
                consumed++;
            }

            width = GetCodepointSequenceWidth(codepoints[..consumed]);
            return consumed;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    public static bool IsSingleGrapheme(ReadOnlySpan<char> text)
    {
        GraphemeEnumerator enumerator = new(text);
        return enumerator.MoveNext(out _) && !enumerator.MoveNext(out _);
    }

    private static bool IsControl(uint codepoint)
    {
        return (codepoint <= 0x1F) || (codepoint >= 0x7F && codepoint <= 0x9F);
    }

    private static bool IsEmojiModifier(uint codepoint)
    {
        return codepoint >= EmojiModifierStart && codepoint <= EmojiModifierEnd;
    }

    private static int GetCodepointSequenceWidth(ReadOnlySpan<uint> codepoints)
    {
        int width = GetCodepointWidth(checked((int)codepoints[0]));
        uint previous = codepoints[0];
        for (int index = 1; index < codepoints.Length; index++)
        {
            uint value = codepoints[index];
            if (value is VariationSelector15 or VariationSelector16)
            {
                if (IsEmojiVariationSequenceBase(previous))
                {
                    width = value == VariationSelector16 ? 2 : 1;
                    previous = value;
                }

                continue;
            }

            if (!IsZeroWidthInGrapheme(new Codepoint(value)))
            {
                width = 2;
            }

            previous = value;
        }

        return width;
    }

    private static bool IsZeroWidthInGrapheme(Codepoint codepoint)
    {
        if (GetCodepointWidth(checked((int)codepoint.Value)) == 0 ||
            IsEmojiModifier(codepoint.Value) ||
            codepoint.GraphemeBreakClass is GraphemeBreakClass.V
                or GraphemeBreakClass.T
                or GraphemeBreakClass.Prepend)
        {
            return true;
        }

        UnicodeCategory category =
            Rune.GetUnicodeCategory(new Rune(checked((int)codepoint.Value)));
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format;
    }

    private static bool IsDefaultIgnorable(uint codepoint)
    {
        if (codepoint == 0x00AD)
        {
            return false;
        }

        return codepoint is
            0x034F or
            0x061C or
            >= 0x115F and <= 0x1160 or
            >= 0x17B4 and <= 0x17B5 or
            >= 0x180B and <= 0x180F or
            >= 0x200B and <= 0x200F or
            >= 0x202A and <= 0x202E or
            >= 0x2060 and <= 0x206F or
            0x3164 or
            >= 0xFE00 and <= 0xFE0F or
            0xFEFF or
            0xFFA0 or
            >= 0xFFF0 and <= 0xFFF8 or
            >= 0x1BCA0 and <= 0x1BCA3 or
            >= 0x1D173 and <= 0x1D17A or
            >= 0xE0000 and <= 0xE0FFF;
    }

    private static bool IsEmojiVariationSequenceBase(uint codepoint)
    {
        ReadOnlySpan<uint> bases =
        [
            0x23, 0x2A, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37,
            0x38, 0x39, 0xA9, 0xAE, 0x203C, 0x2049, 0x2122, 0x2139, 0x2194, 0x2195,
            0x2196, 0x2197, 0x2198, 0x2199, 0x21A9, 0x21AA, 0x231A, 0x231B, 0x2328, 0x23CF,
            0x23E9, 0x23EA, 0x23EB, 0x23EC, 0x23ED, 0x23EE, 0x23EF, 0x23F0, 0x23F1, 0x23F2,
            0x23F3, 0x23F8, 0x23F9, 0x23FA, 0x24C2, 0x25AA, 0x25AB, 0x25B6, 0x25C0, 0x25FB,
            0x25FC, 0x25FD, 0x25FE, 0x2600, 0x2601, 0x2602, 0x2603, 0x2604, 0x260E, 0x2611,
            0x2614, 0x2615, 0x2618, 0x261D, 0x2620, 0x2622, 0x2623, 0x2626, 0x262A, 0x262E,
            0x262F, 0x2638, 0x2639, 0x263A, 0x2640, 0x2642, 0x2648, 0x2649, 0x264A, 0x264B,
            0x264C, 0x264D, 0x264E, 0x264F, 0x2650, 0x2651, 0x2652, 0x2653, 0x265F, 0x2660,
            0x2663, 0x2665, 0x2666, 0x2668, 0x267B, 0x267E, 0x267F, 0x2692, 0x2693, 0x2694,
            0x2695, 0x2696, 0x2697, 0x2699, 0x269B, 0x269C, 0x26A0, 0x26A1, 0x26A7, 0x26AA,
            0x26AB, 0x26B0, 0x26B1, 0x26BD, 0x26BE, 0x26C4, 0x26C5, 0x26C8, 0x26CE, 0x26CF,
            0x26D1, 0x26D3, 0x26D4, 0x26E9, 0x26EA, 0x26F0, 0x26F1, 0x26F2, 0x26F3, 0x26F4,
            0x26F5, 0x26F7, 0x26F8, 0x26F9, 0x26FA, 0x26FD, 0x2702, 0x2705, 0x2708, 0x2709,
            0x270A, 0x270B, 0x270C, 0x270D, 0x270F, 0x2712, 0x2714, 0x2716, 0x271D, 0x2721,
            0x2728, 0x2733, 0x2734, 0x2744, 0x2747, 0x274C, 0x274E, 0x2753, 0x2754, 0x2755,
            0x2757, 0x2763, 0x2764, 0x2795, 0x2796, 0x2797, 0x27A1, 0x27B0, 0x27BF, 0x2934,
            0x2935, 0x2B05, 0x2B06, 0x2B07, 0x2B1B, 0x2B1C, 0x2B50, 0x2B55, 0x3030, 0x303D,
            0x3297, 0x3299, 0x1F004, 0x1F170, 0x1F171, 0x1F17E, 0x1F17F, 0x1F202, 0x1F21A, 0x1F22F,
            0x1F237, 0x1F30D, 0x1F30E, 0x1F30F, 0x1F315, 0x1F31C, 0x1F321, 0x1F324, 0x1F325, 0x1F326,
            0x1F327, 0x1F328, 0x1F329, 0x1F32A, 0x1F32B, 0x1F32C, 0x1F336, 0x1F378, 0x1F37D, 0x1F393,
            0x1F396, 0x1F397, 0x1F399, 0x1F39A, 0x1F39B, 0x1F39E, 0x1F39F, 0x1F3A7, 0x1F3AC, 0x1F3AD,
            0x1F3AE, 0x1F3C2, 0x1F3C4, 0x1F3C6, 0x1F3CA, 0x1F3CB, 0x1F3CC, 0x1F3CD, 0x1F3CE, 0x1F3D4,
            0x1F3D5, 0x1F3D6, 0x1F3D7, 0x1F3D8, 0x1F3D9, 0x1F3DA, 0x1F3DB, 0x1F3DC, 0x1F3DD, 0x1F3DE,
            0x1F3DF, 0x1F3E0, 0x1F3ED, 0x1F3F3, 0x1F3F5, 0x1F3F7, 0x1F408, 0x1F415, 0x1F41F, 0x1F426,
            0x1F43F, 0x1F441, 0x1F442, 0x1F446, 0x1F447, 0x1F448, 0x1F449, 0x1F44D, 0x1F44E, 0x1F453,
            0x1F46A, 0x1F47D, 0x1F4A3, 0x1F4B0, 0x1F4B3, 0x1F4BB, 0x1F4BF, 0x1F4CB, 0x1F4DA, 0x1F4DF,
            0x1F4E4, 0x1F4E5, 0x1F4E6, 0x1F4EA, 0x1F4EB, 0x1F4EC, 0x1F4ED, 0x1F4F7, 0x1F4F9, 0x1F4FA,
            0x1F4FB, 0x1F4FD, 0x1F508, 0x1F50D, 0x1F512, 0x1F513, 0x1F549, 0x1F54A, 0x1F550, 0x1F551,
            0x1F552, 0x1F553, 0x1F554, 0x1F555, 0x1F556, 0x1F557, 0x1F558, 0x1F559, 0x1F55A, 0x1F55B,
            0x1F55C, 0x1F55D, 0x1F55E, 0x1F55F, 0x1F560, 0x1F561, 0x1F562, 0x1F563, 0x1F564, 0x1F565,
            0x1F566, 0x1F567, 0x1F56F, 0x1F570, 0x1F573, 0x1F574, 0x1F575, 0x1F576, 0x1F577, 0x1F578,
            0x1F579, 0x1F587, 0x1F58A, 0x1F58B, 0x1F58C, 0x1F58D, 0x1F590, 0x1F5A5, 0x1F5A8, 0x1F5B1,
            0x1F5B2, 0x1F5BC, 0x1F5C2, 0x1F5C3, 0x1F5C4, 0x1F5D1, 0x1F5D2, 0x1F5D3, 0x1F5DC, 0x1F5DD,
            0x1F5DE, 0x1F5E1, 0x1F5E3, 0x1F5E8, 0x1F5EF, 0x1F5F3, 0x1F5FA, 0x1F610, 0x1F687, 0x1F68D,
            0x1F691, 0x1F694, 0x1F698, 0x1F6AD, 0x1F6B2, 0x1F6B9, 0x1F6BA, 0x1F6BC, 0x1F6CB, 0x1F6CD,
            0x1F6CE, 0x1F6CF, 0x1F6E0, 0x1F6E1, 0x1F6E2, 0x1F6E3, 0x1F6E4, 0x1F6E5, 0x1F6E9, 0x1F6F0,
            0x1F6F3,
        ];

        return bases.BinarySearch(codepoint) >= 0;
    }

    private static int GetGhosttyUnicode17WidthOverride(int codepoint)
    {
        // RoyalTerminal's compact tries predate Ghostty's Unicode 17 table.
        // Keep the public managed width API aligned with the pinned libghostty
        // table until the full tries are regenerated.
        return codepoint switch
        {
            0x0897 or
            >= 0x1ACF and <= 0x1ADD or
            >= 0x1AE0 and <= 0x1AEB or
            >= 0x10D69 and <= 0x10D6D or
            >= 0x10EFA and <= 0x10EFC or
            >= 0x113BB and <= 0x113C0 or
            0x113CE or
            0x113D0 or
            0x113D2 or
            >= 0x113E1 and <= 0x113E2 or
            0x11B60 or
            >= 0x11B62 and <= 0x11B64 or
            0x11B66 or
            0x11F5A or
            >= 0x1611E and <= 0x16129 or
            >= 0x1612D and <= 0x1612F or
            0x16D63 or
            >= 0x16D67 and <= 0x16D6A or
            >= 0x1E5EE and <= 0x1E5EF or
            0x1E6E3 or
            0x1E6E6 or
            >= 0x1E6EE and <= 0x1E6EF or
            0x1E6F5 => 0,

            0x00AD or
            0x09BE or
            0x09D7 or
            0x0B3E or
            0x0B57 or
            0x0BBE or
            0x0BD7 or
            0x0CC2 or
            >= 0x0CD5 and <= 0x0CD6 or
            0x0D3E or
            0x0D57 or
            0x0DCF or
            0x0DDF or
            0x1B35 or
            >= 0xFF9E and <= 0xFF9F or
            >= 0xFFF9 and <= 0xFFFB or
            0x1133E or
            0x11357 or
            0x114B0 or
            0x114BD or
            0x115AF or
            0x1171E or
            0x11930 or
            >= 0x13430 and <= 0x1343F or
            0x1D165 or
            >= 0x1D16E and <= 0x1D172 => 1,

            >= 0x2630 and <= 0x2637 or
            >= 0x268A and <= 0x268F or
            >= 0x302E and <= 0x302F or
            >= 0x31E4 and <= 0x31E5 or
            >= 0x4DC0 and <= 0x4DFF or
            >= 0x16FF2 and <= 0x16FF6 or
            >= 0x187F8 and <= 0x187FF or
            0x18CFF or
            >= 0x18D09 and <= 0x18D1E or
            >= 0x18D80 and <= 0x18DF2 or
            >= 0x1D300 and <= 0x1D356 or
            >= 0x1D360 and <= 0x1D376 or
            >= 0x1F3FB and <= 0x1F3FF or
            0x1F6D8 or
            >= 0x1FA89 and <= 0x1FA8A or
            >= 0x1FA8E and <= 0x1FA8F or
            0x1FABE or
            0x1FAC6 or
            0x1FAC8 or
            0x1FACD or
            0x1FADC or
            0x1FADF or
            >= 0x1FAE9 and <= 0x1FAEA or
            0x1FAEF => 2,

            _ => -1,
        };
    }
}
