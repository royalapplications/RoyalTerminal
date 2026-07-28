// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Unicode;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public class UnicodeWidthTests
{
    [Fact]
    public void ManagedCodepointWidths_MatchPinnedGhosttyForAllUnicodeCodepoints()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        List<(int Start, int End, byte Native)> ranges = [];
        int mismatchCount = 0;
        for (int codepoint = 0; codepoint <= 0x10FFFF; codepoint++)
        {
            int managed = TerminalCellWidthCalculator.GetCodepointWidth(codepoint);
            byte native = GhosttyUnicode.GetCodepointWidth((uint)codepoint);
            if (managed == native)
            {
                continue;
            }

            mismatchCount++;
            if (ranges.Count > 0 &&
                ranges[^1].End + 1 == codepoint &&
                ranges[^1].Native == native)
            {
                (int start, _, byte expected) = ranges[^1];
                ranges[^1] = (start, codepoint, expected);
            }
            else
            {
                ranges.Add((codepoint, codepoint, native));
            }
        }

        Assert.True(
            mismatchCount == 0,
            $"{mismatchCount} mismatches in {ranges.Count} ranges: {string.Join(", ", ranges.Select(static range => range.Start == range.End ? $"0x{range.Start:X}: {range.Native}" : $"0x{range.Start:X}-0x{range.End:X}: {range.Native}"))}");
    }

    [Fact]
    public void ManagedGraphemeWidths_MatchPinnedGhosttyEdgeCases()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        uint[][] cases =
        [
            [],
            [(uint)'A'],
            [(uint)'A', (uint)'B'],
            [0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467],
            [0x23, 0xFE0F, 0x20E3],
            [0x31, 0x20E3],
            [0x1F44B, 0x1F3FF],
            [(uint)'A', 0xFE0F],
            [0x23, 0xFE0E],
            [0x231A, 0xFE0E],
            [0x231A, 0xFE0E, 0xFE0F],
            [0x1F3F4, 0x200D, 0x2620, 0xFE0F],
            [0x1F1E6, 0x1F1E7, 0x1F1E8],
            [0x0915, 0x094D, 0x0937, (uint)'A'],
            [0x0301, 0x0302],
            [0xD800, 0x0301],
            [0x11_0000, 0x0301],
            [(uint)'A', 0x11_0000],
        ];

        foreach (uint[] codepoints in cases)
        {
            int managedConsumed =
                TerminalCellWidthCalculator.GetFirstGraphemeWidth(codepoints, out int managedWidth);
            nuint nativeConsumed = GhosttyUnicode.GetGraphemeWidth(codepoints, out byte nativeWidth);

            Assert.Equal(nativeConsumed, (nuint)managedConsumed);
            Assert.Equal(nativeWidth, (byte)managedWidth);
        }
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("e\u0301", 1)]
    [InlineData("a\u200D", 1)]
    [InlineData("中", 2)]
    [InlineData("\u2610", 1)] // ☐
    [InlineData("\u2611", 1)] // ☑
    [InlineData("\u25CB", 1)] // ○
    [InlineData("\u25EF", 1)] // ◯
    [InlineData("\u25CF", 1)] // ●
    [InlineData("\u25C9", 1)] // ◉
    [InlineData("\u2B24", 1)] // ⬤
    [InlineData("\u2665", 1)] // ♥
    [InlineData("\u2611\uFE0F", 2)] // ☑️
    [InlineData("\u2665\uFE0F", 2)] // ♥️
    [InlineData("\U0001F5F9\uFE0E", 1)] // 🗹︎
    [InlineData("\U0001F837\uFE0E", 1)] // 🠷︎
    [InlineData("\U0001F834\uFE0E", 1)] // 🠴︎
    [InlineData("\U0001F642\uFE0E", 2)] // Invalid VS15 base; selector is ignored.
    [InlineData("A\uFE0F", 1)] // Invalid VS16 base; selector is ignored.
    [InlineData("中\uFE0E", 2)] // Invalid VS15 base; selector is ignored.
    [InlineData("\U0001F1E8\U0001F1E6", 2)] // 🇨🇦
    [InlineData("#\uFE0F\u20E3", 2)] // #️⃣
    [InlineData("1\u20E3", 1)] // Keycap combining mark without VS16.
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466", 2)] // family
    public void CellWidthCalculator_ReturnsExpectedWidths(string grapheme, int expectedWidth)
    {
        int width = TerminalCellWidthCalculator.GetCellWidth(grapheme);
        Assert.Equal(expectedWidth, width);
    }

    [Theory]
    [InlineData('A', 1)]
    [InlineData(0x0000, 0)]
    [InlineData(0x0301, 0)]
    [InlineData(0x200B, 0)]
    [InlineData(0x200D, 0)]
    [InlineData(0xFE0F, 0)]
    [InlineData(0xD800, 0)]
    [InlineData(0x4E00, 2)]
    [InlineData(0x1F600, 2)]
    [InlineData(0x1F1E6, 2)]
    [InlineData(0x2E3B, 2)]
    public void CodepointWidthCalculator_ReturnsTerminalWidths(int codepoint, int expectedWidth)
    {
        Assert.Equal(expectedWidth, TerminalCellWidthCalculator.GetCodepointWidth(codepoint));
    }

    [Fact]
    public void FirstGraphemeWidth_ConsumesOneCluster()
    {
        uint[] codepoints = [0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467, (uint)'A'];

        int consumed = TerminalCellWidthCalculator.GetFirstGraphemeWidth(
            codepoints,
            out int width);

        Assert.Equal(5, consumed);
        Assert.Equal(2, width);
    }

    [Fact]
    public void FirstGraphemeWidth_ConsumesUnicode17IndicConjunct()
    {
        uint[] codepoints = [0x0915, 0x094D, 0x0937, (uint)'A'];

        int consumed = TerminalCellWidthCalculator.GetFirstGraphemeWidth(
            codepoints,
            out int width);

        Assert.Equal(3, consumed);
        Assert.Equal(2, width);
    }

    [Fact]
    public void CellWidthCalculator_IsSingleGrapheme_RecognizesRegionalIndicatorPair()
    {
        Assert.True(TerminalCellWidthCalculator.IsSingleGrapheme("\U0001F1E8\U0001F1E6"));
    }

    [Fact]
    public void CellWidthCalculator_IsSingleGrapheme_RejectsRegionalIndicatorTriplet()
    {
        Assert.False(TerminalCellWidthCalculator.IsSingleGrapheme("\U0001F1E8\U0001F1E6\U0001F1FA"));
    }
}
