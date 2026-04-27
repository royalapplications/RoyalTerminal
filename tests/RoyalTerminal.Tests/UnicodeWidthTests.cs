// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Unicode;
using Xunit;

namespace RoyalTerminal.Tests;

public class UnicodeWidthTests
{
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
    [InlineData("\U0001F642\uFE0E", 1)] // 🙂︎
    [InlineData("\U0001F1E8\U0001F1E6", 2)] // 🇨🇦
    [InlineData("#\uFE0F\u20E3", 2)] // #️⃣
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466", 2)] // family
    public void CellWidthCalculator_ReturnsExpectedWidths(string grapheme, int expectedWidth)
    {
        int width = TerminalCellWidthCalculator.GetCellWidth(grapheme);
        Assert.Equal(expectedWidth, width);
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
