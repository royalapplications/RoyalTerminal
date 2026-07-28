// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Unicode 17.0 Indic_Conjunct_Break data used by UAX #29 rule GB9c.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace RoyalTerminal.Unicode;

internal enum IndicConjunctBreakClass
{
    None,
    Linker,
    Consonant,
    Extend,
}

internal static class IndicConjunctBreakData
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IndicConjunctBreakClass Get(Codepoint codepoint)
    {
        uint value = codepoint.Value;
        if (IsLinker(value))
        {
            return IndicConjunctBreakClass.Linker;
        }

        if (IsConsonant(value))
        {
            return IndicConjunctBreakClass.Consonant;
        }

        // Include the non-mark members and Unicode 17 additions that may not
        // yet have a mark category in the current .NET Unicode tables.
        if (value is 0x200D or
            >= 0x1ACF and <= 0x1ADD or
            >= 0x1AE0 and <= 0x1AEB or
            >= 0xFF9E and <= 0xFF9F or
            >= 0x10EFA and <= 0x10EFB or
            0x11B60 or
            >= 0x11B62 and <= 0x11B64 or
            0x11B66 or
            >= 0x1F3FB and <= 0x1F3FF or
            0x1E6E3 or
            0x1E6E6 or
            >= 0x1E6EE and <= 0x1E6EF or
            0x1E6F5 or
            >= 0xE0020 and <= 0xE007F)
        {
            return IndicConjunctBreakClass.Extend;
        }

        UnicodeCategory category =
            Rune.GetUnicodeCategory(new Rune(checked((int)value)));
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark
            ? IndicConjunctBreakClass.Extend
            : IndicConjunctBreakClass.None;
    }

    private static bool IsLinker(uint value)
    {
        return value is
            0x094D or 0x09CD or 0x0ACD or 0x0B4D or 0x0C4D or 0x0D4D or
            0x1039 or 0x17D2 or 0x1A60 or 0x1B44 or 0x1BAB or 0xA9C0 or
            0xAAF6 or 0x10A3F or 0x11133 or 0x113D0 or 0x1193E or 0x11A47 or
            0x11A99 or 0x11F42;
    }

    private static bool IsConsonant(uint value)
    {
        return value is
            >= 0x0915 and <= 0x0939 or
            >= 0x0958 and <= 0x095F or
            >= 0x0978 and <= 0x097F or
            >= 0x0995 and <= 0x09A8 or
            >= 0x09AA and <= 0x09B0 or
            0x09B2 or
            >= 0x09B6 and <= 0x09B9 or
            >= 0x09DC and <= 0x09DD or
            0x09DF or
            >= 0x09F0 and <= 0x09F1 or
            >= 0x0A95 and <= 0x0AA8 or
            >= 0x0AAA and <= 0x0AB0 or
            >= 0x0AB2 and <= 0x0AB3 or
            >= 0x0AB5 and <= 0x0AB9 or
            0x0AF9 or
            >= 0x0B15 and <= 0x0B28 or
            >= 0x0B2A and <= 0x0B30 or
            >= 0x0B32 and <= 0x0B33 or
            >= 0x0B35 and <= 0x0B39 or
            >= 0x0B5C and <= 0x0B5D or
            0x0B5F or
            0x0B71 or
            >= 0x0C15 and <= 0x0C28 or
            >= 0x0C2A and <= 0x0C39 or
            >= 0x0C58 and <= 0x0C5A or
            >= 0x0D15 and <= 0x0D3A or
            >= 0x1000 and <= 0x102A or
            0x103F or
            >= 0x1050 and <= 0x1055 or
            >= 0x105A and <= 0x105D or
            0x1061 or
            >= 0x1065 and <= 0x1066 or
            >= 0x106E and <= 0x1070 or
            >= 0x1075 and <= 0x1081 or
            0x108E or
            >= 0x1780 and <= 0x17B3 or
            >= 0x1A20 and <= 0x1A54 or
            >= 0x1B0B and <= 0x1B0C or
            >= 0x1B13 and <= 0x1B33 or
            >= 0x1B45 and <= 0x1B4C or
            >= 0x1B83 and <= 0x1BA0 or
            >= 0x1BAE and <= 0x1BAF or
            >= 0x1BBB and <= 0x1BBD or
            >= 0xA989 and <= 0xA98B or
            >= 0xA98F and <= 0xA9B2 or
            >= 0xA9E0 and <= 0xA9E4 or
            >= 0xA9E7 and <= 0xA9EF or
            >= 0xA9FA and <= 0xA9FE or
            >= 0xAA60 and <= 0xAA6F or
            >= 0xAA71 and <= 0xAA73 or
            0xAA7A or
            >= 0xAA7E and <= 0xAA7F or
            >= 0xAAE0 and <= 0xAAEA or
            >= 0xABC0 and <= 0xABDA or
            0x10A00 or
            >= 0x10A10 and <= 0x10A13 or
            >= 0x10A15 and <= 0x10A17 or
            >= 0x10A19 and <= 0x10A35 or
            >= 0x11103 and <= 0x11126 or
            0x11144 or
            0x11147 or
            >= 0x11380 and <= 0x11389 or
            0x1138B or
            0x1138E or
            >= 0x11390 and <= 0x113B5 or
            >= 0x11900 and <= 0x11906 or
            0x11909 or
            >= 0x1190C and <= 0x11913 or
            >= 0x11915 and <= 0x11916 or
            >= 0x11918 and <= 0x1192F or
            0x11A00 or
            >= 0x11A0B and <= 0x11A32 or
            0x11A50 or
            >= 0x11A5C and <= 0x11A83 or
            >= 0x11F04 and <= 0x11F10 or
            >= 0x11F12 and <= 0x11F33;
    }
}
