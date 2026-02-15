// Licensed under the MIT License.
// Deterministic terminal cell width calculation backed by Unicode trie tables.

using System.Runtime.CompilerServices;
using System.Text;

namespace RoyalTerminal.Unicode;

public static class TerminalCellWidthCalculator
{
    private const int VariationSelector16 = 0xFE0F;
    private const int KeycapEnclosingCodepoint = 0x20E3;
    private const int EmojiModifierStart = 0x1F3FB;
    private const int EmojiModifierEnd = 0x1F3FF;
    private const int TagStart = 0xE0020;
    private const int TagEnd = 0xE007F;

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

        if (IsControl(first.Value))
        {
            return 0;
        }

        bool hasEmojiPresentationSignal = false;
        bool hasExtendedPictographic = first.GraphemeBreakClass == GraphemeBreakClass.ExtendedPictographic;
        bool hasRegionalIndicator = first.GraphemeBreakClass == GraphemeBreakClass.RegionalIndicator;

        for (int index = firstLength; index < grapheme.Length;)
        {
            Codepoint current = Codepoint.ReadAt(grapheme, index, out int consumed);
            if (consumed <= 0)
            {
                break;
            }

            uint value = current.Value;
            if (value == VariationSelector16 ||
                value == KeycapEnclosingCodepoint ||
                IsEmojiModifier(value) ||
                IsTagCodepoint(value))
            {
                hasEmojiPresentationSignal = true;
            }

            if (current.GraphemeBreakClass == GraphemeBreakClass.ExtendedPictographic)
            {
                hasExtendedPictographic = true;
            }

            if (current.GraphemeBreakClass == GraphemeBreakClass.RegionalIndicator)
            {
                hasRegionalIndicator = true;
            }

            index += consumed;
        }

        if (hasRegionalIndicator || hasExtendedPictographic || hasEmojiPresentationSignal)
        {
            return 2;
        }

        return first.EastAsianWidthClass is EastAsianWidthClass.Fullwidth or EastAsianWidthClass.Wide
            ? 2
            : 1;
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

    private static bool IsTagCodepoint(uint codepoint)
    {
        return codepoint >= TagStart && codepoint <= TagEnd;
    }
}
