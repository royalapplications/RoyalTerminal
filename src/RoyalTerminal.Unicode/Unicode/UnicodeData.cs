// Licensed under the MIT License.
// Ported/adapted from Avalonia Unicode text formatting data accessors.

using System.Runtime.CompilerServices;

namespace RoyalTerminal.Unicode;

internal static class UnicodeData
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GraphemeBreakClass GetGraphemeClusterBreak(uint codepoint)
    {
        return (GraphemeBreakClass)GraphemeBreakTrie.Trie.Get(codepoint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EastAsianWidthClass GetEastAsianWidthClass(uint codepoint)
    {
        return (EastAsianWidthClass)EastAsianWidthTrie.Trie.Get(codepoint);
    }
}
