// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Ported/adapted from Avalonia Unicode text formatting data accessors.

using System.Runtime.CompilerServices;

namespace RoyalTerminal.Unicode;

internal static class UnicodeData
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GraphemeBreakClass GetGraphemeClusterBreak(uint codepoint)
    {
        return (GraphemeBreakClass)(Unicode18Data.Get(codepoint) & 31);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EastAsianWidthClass GetEastAsianWidthClass(uint codepoint)
    {
        return (EastAsianWidthClass)((Unicode18Data.Get(codepoint) >> 5) & 7);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IndicConjunctBreakClass GetIndicConjunctBreak(uint codepoint)
        => (IndicConjunctBreakClass)((Unicode18Data.Get(codepoint) >> 8) & 3);
}
