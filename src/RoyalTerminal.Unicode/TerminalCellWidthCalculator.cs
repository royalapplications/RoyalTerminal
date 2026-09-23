// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Deterministic terminal cell width calculation backed by Unicode trie tables.

using System.Runtime.CompilerServices;
using System.Text;

namespace RoyalTerminal.Unicode;

public static class TerminalCellWidthCalculator
{
    private const int VariationSelector15 = 0xFE0E;
    private const int VariationSelector16 = 0xFE0F;

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

        return (Unicode18Data.Get((uint)codepoint) >> 10) & 3;
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

        uint previous = codepoints[0];
        if (previous > 0x10FFFF)
        {
            width = 1;
            return 1;
        }
        width = GetCodepointWidth((int)previous);
        GraphemeBreakState state = default;
        int consumed = 1;
        for (; consumed < codepoints.Length; consumed++)
        {
            uint current = codepoints[consumed];
            if (current > 0x10FFFF) break;
            GraphemeBreakState before = state;
            if (state.IsBreak(previous, current, terminal: true)) break;
            if (!ApplyWidthEffect(previous, current, ref width)) state = before;
            else previous = current;
        }
        return consumed;
    }

    public static bool IsSingleGrapheme(ReadOnlySpan<char> text)
    {
        GraphemeEnumerator enumerator = new(text);
        return enumerator.MoveNext(out _) && !enumerator.MoveNext(out _);
    }

    /// <summary>
    /// Tests whether a scalar continues a terminal grapheme and calculates its
    /// new width using Ghostty's emoji-modifier and variation-selector policy.
    /// </summary>
    /// <param name="grapheme">The current nonempty UTF-16 cell content.</param>
    /// <param name="codepoint">The scalar to append.</param>
    /// <param name="width">The resulting width if the scalar continues the cell.</param>
    /// <param name="ignored">True when an invalid variation selector must not be stored.</param>
    /// <returns>True if the scalar continues the existing grapheme.</returns>
    public static bool TryGetAppendedGraphemeWidth(ReadOnlySpan<char> grapheme, int codepoint, out int width, out bool ignored)
        => TryGetAppendedGraphemeWidth(grapheme, codepoint, GetCellWidth(grapheme), out width, out ignored);

    /// <summary>
    /// Tests a scalar against existing cell content while preserving its stored
    /// width. Existing content may contain boundaries from mode 2027 being off.
    /// </summary>
    /// <param name="grapheme">Existing cell content.</param>
    /// <param name="codepoint">The scalar to append.</param>
    /// <param name="currentWidth">The cell's current width.</param>
    /// <param name="width">The cell width after the append.</param>
    /// <param name="ignored">Whether the new scalar is an invalid variation selector.</param>
    /// <returns>Whether the scalar belongs to the current cell.</returns>
    public static bool TryGetAppendedGraphemeWidth(ReadOnlySpan<char> grapheme, int codepoint, int currentWidth, out int width, out bool ignored)
    {
        width = currentWidth;
        ignored = false;
        if (grapheme.IsEmpty || !Rune.IsValid(codepoint)) return false;
        Codepoint first = Codepoint.ReadAt(grapheme, 0, out int offset);
        uint previous = first.Value;
        GraphemeBreakState state = default;
        while (offset < grapheme.Length)
        {
            Codepoint current = Codepoint.ReadAt(grapheme, offset, out int length);
            // Existing cells created with mode 2027 off can contain boundaries.
            // Feed them through the state machine just as Ghostty's printer does.
            _ = state.IsBreak(previous, current.Value, terminal: true);
            previous = current.Value;
            offset += length;
        }
        if (state.IsBreak(previous, (uint)codepoint, terminal: true)) return false;
        ignored = !ApplyWidthEffect(previous, (uint)codepoint, ref width);
        return true;
    }

    private static bool ApplyWidthEffect(uint previous, uint current, ref int width)
    {
        if (current is VariationSelector15 or VariationSelector16)
        {
            if (!IsEmojiVariationSequenceBase(previous)) return false;
            width = current == VariationSelector16 ? 2 : 1;
        }
        else if (!IsZeroWidthInGrapheme(new Codepoint(current))) width = 2;
        return true;
    }

    private static bool IsZeroWidthInGrapheme(Codepoint codepoint)
        => (Unicode18Data.Get(codepoint.Value) & (1 << 12)) != 0;

    private static bool IsEmojiVariationSequenceBase(uint codepoint)
        => (Unicode18Data.Get(codepoint) & (1 << 13)) != 0;
}
