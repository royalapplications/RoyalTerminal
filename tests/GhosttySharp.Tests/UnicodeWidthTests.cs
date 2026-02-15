// Licensed under the MIT License.

using GhosttySharp.Unicode;
using Xunit;

namespace GhosttySharp.Tests;

public class UnicodeWidthTests
{
    [Theory]
    [InlineData("A", 1)]
    [InlineData("e\u0301", 1)]
    [InlineData("a\u200D", 1)]
    [InlineData("中", 2)]
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
