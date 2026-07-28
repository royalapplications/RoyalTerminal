// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Deterministic terminal cell width calculation backed by Unicode trie tables.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace RoyalTerminal.Unicode;

public static class TerminalCellWidthCalculator
{
    private const int VariationSelector15 = 0xFE0E;
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
        if (!Rune.IsValid(codepoint))
        {
            return 1;
        }

        Codepoint value = new((uint)codepoint);
        if (IsControl(value.Value) ||
            value.GraphemeBreakClass is GraphemeBreakClass.Control
                or GraphemeBreakClass.CR
                or GraphemeBreakClass.LF
                or GraphemeBreakClass.Extend
                or GraphemeBreakClass.ZWJ)
        {
            return 0;
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

        if (IsControl(first.Value))
        {
            return 0;
        }

        bool hasTextPresentationSignal = false;
        bool hasEmojiPresentationSignal = false;
        bool hasRegionalIndicator = first.GraphemeBreakClass == GraphemeBreakClass.RegionalIndicator;

        for (int index = firstLength; index < grapheme.Length;)
        {
            Codepoint current = Codepoint.ReadAt(grapheme, index, out int consumed);
            if (consumed <= 0)
            {
                break;
            }

            uint value = current.Value;
            if (value == VariationSelector15)
            {
                hasTextPresentationSignal = true;
            }

            if (value == VariationSelector16 ||
                value == KeycapEnclosingCodepoint ||
                IsEmojiModifier(value) ||
                IsTagCodepoint(value))
            {
                hasEmojiPresentationSignal = true;
            }

            if (current.GraphemeBreakClass == GraphemeBreakClass.RegionalIndicator)
            {
                hasRegionalIndicator = true;
            }

            index += consumed;
        }

        if (hasTextPresentationSignal && !hasEmojiPresentationSignal)
        {
            return 1;
        }

        if (hasRegionalIndicator || hasEmojiPresentationSignal)
        {
            return 2;
        }

        return first.EastAsianWidthClass is EastAsianWidthClass.Fullwidth or EastAsianWidthClass.Wide
            ? 2
            : 1;
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
            if (codepoint > int.MaxValue || !Rune.IsValid((int)codepoint))
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
                Rune rune = new(checked((int)codepoints[index]));
                written += rune.EncodeToUtf16(utf16[written..]);
            }

            GraphemeEnumerator enumerator = new(utf16[..written]);
            if (!enumerator.MoveNext(out Grapheme grapheme))
            {
                width = 0;
                return 0;
            }

            width = GetCellWidth(utf16.Slice(grapheme.Offset, grapheme.Length));
            int consumed = 0;
            int consumedUtf16 = 0;
            while (consumed < validCount && consumedUtf16 < grapheme.Length)
            {
                consumedUtf16 += codepoints[consumed] <= char.MaxValue ? 1 : 2;
                consumed++;
            }

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

    private static bool IsTagCodepoint(uint codepoint)
    {
        return codepoint >= TagStart && codepoint <= TagEnd;
    }
}
