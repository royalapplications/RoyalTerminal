// Licensed under the MIT License.
// RoyalTerminal.Tests — Scroll data state management tests.

using RoyalTerminal.Avalonia.Scrolling;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Tests for TerminalScrollData — scroll state management.
/// </summary>
public class TerminalScrollDataTests
{
    private static TerminalScrollData CreateScrollData(
        double cellHeight = 16,
        double viewport = 384,    // 24 rows * 16px
        double extent = 1600)     // 100 rows * 16px
    {
        return new TerminalScrollData
        {
            CellHeight = cellHeight,
            Viewport = viewport,
            Extent = extent,
        };
    }

    [Fact]
    public void Initial_OffsetIsZero()
    {
        var data = CreateScrollData();
        Assert.Equal(0, data.Offset);
    }

    [Fact]
    public void MaxOffset_CalculatedCorrectly()
    {
        var data = CreateScrollData(viewport: 384, extent: 1600);
        Assert.Equal(1600 - 384, data.MaxOffset);
    }

    [Fact]
    public void MaxOffset_WhenExtentSmallerThanViewport_IsZero()
    {
        var data = CreateScrollData(viewport: 1600, extent: 384);
        Assert.Equal(0, data.MaxOffset);
    }

    [Fact]
    public void Offset_ClampedToRange()
    {
        var data = CreateScrollData();

        data.Offset = -100;
        Assert.Equal(0, data.Offset);

        data.Offset = 999999;
        Assert.Equal(data.MaxOffset, data.Offset);
    }

    [Fact]
    public void ScrollByRows_MovesOffset()
    {
        var data = CreateScrollData(cellHeight: 16);
        data.ScrollByRows(5);
        Assert.Equal(80, data.Offset); // 5 * 16
    }

    [Fact]
    public void ScrollByRows_Negative_ScrollsUp()
    {
        var data = CreateScrollData(cellHeight: 16);
        data.Offset = 160;
        data.ScrollByRows(-3);
        Assert.Equal(112, data.Offset); // 160 - 48
    }

    [Fact]
    public void ScrollToBottom_SetsMaxOffset()
    {
        var data = CreateScrollData();
        data.ScrollToBottom();
        Assert.Equal(data.MaxOffset, data.Offset);
    }

    [Fact]
    public void ScrollToTop_SetsZero()
    {
        var data = CreateScrollData();
        data.Offset = 500;
        data.ScrollToTop();
        Assert.Equal(0, data.Offset);
    }

    [Fact]
    public void PageDown_ScrollsByViewport()
    {
        var data = CreateScrollData(viewport: 384, extent: 3200);
        data.PageDown();
        Assert.Equal(384, data.Offset);
    }

    [Fact]
    public void PageUp_ScrollsByViewport()
    {
        var data = CreateScrollData(viewport: 384, extent: 3200);
        data.Offset = 768;
        data.PageUp();
        Assert.Equal(384, data.Offset);
    }

    [Fact]
    public void IsAtBottom_WhenAtMax()
    {
        var data = CreateScrollData();
        data.ScrollToBottom();
        Assert.True(data.IsAtBottom);
    }

    [Fact]
    public void IsAtBottom_WhenNotAtMax()
    {
        var data = CreateScrollData();
        data.Offset = 0;
        Assert.False(data.IsAtBottom);
    }

    [Fact]
    public void CanScroll_WhenExtentExceedsViewport()
    {
        var data = CreateScrollData(viewport: 384, extent: 1600);
        Assert.True(data.CanScroll);
    }

    [Fact]
    public void CanScroll_WhenExtentEqualsViewport()
    {
        var data = CreateScrollData(viewport: 384, extent: 384);
        Assert.False(data.CanScroll);
    }

    [Fact]
    public void OffsetRows_Calculated()
    {
        var data = CreateScrollData(cellHeight: 16);
        data.Offset = 48;
        Assert.Equal(3, data.OffsetRows);
    }

    [Fact]
    public void ViewportRows_Calculated()
    {
        var data = CreateScrollData(cellHeight: 16, viewport: 384);
        Assert.Equal(24, data.ViewportRows);
    }

    [Fact]
    public void TotalRows_Calculated()
    {
        var data = CreateScrollData(cellHeight: 16, extent: 1600);
        Assert.Equal(100, data.TotalRows);
    }

    [Fact]
    public void UpdateExtent_AutoScrollToBottom()
    {
        var data = CreateScrollData(cellHeight: 16, viewport: 384);
        data.Extent = 384; // Start at extent == viewport (at bottom)
        data.ScrollToBottom();

        data.UpdateExtent(totalRows: 100, autoScrollToBottom: true);

        Assert.Equal(data.MaxOffset, data.Offset);
    }

    [Fact]
    public void UpdateExtent_NoAutoScroll_StaysAtPosition()
    {
        var data = CreateScrollData(cellHeight: 16, viewport: 384);
        data.Offset = 100;

        data.UpdateExtent(totalRows: 200, autoScrollToBottom: false);

        Assert.Equal(100, data.Offset);
    }

    [Fact]
    public void ViewportYToRow_ConvertsCorrectly()
    {
        var data = CreateScrollData(cellHeight: 16);
        data.Offset = 32; // 2 rows offset

        Assert.Equal(2, data.ViewportYToRow(0));
        Assert.Equal(3, data.ViewportYToRow(16));
        Assert.Equal(4, data.ViewportYToRow(32));
    }

    [Fact]
    public void ThumbSize_Proportional()
    {
        var data = CreateScrollData(viewport: 200, extent: 1000);
        Assert.Equal(0.2, data.ThumbSize, 0.01);
    }

    [Fact]
    public void ThumbSize_WhenNoExtent_IsOne()
    {
        var data = new TerminalScrollData { CellHeight = 16, Extent = 0, Viewport = 384 };
        Assert.Equal(1, data.ThumbSize);
    }

    [Fact]
    public void CellHeight_MinimumIsOne()
    {
        var data = new TerminalScrollData();
        data.CellHeight = 0;
        Assert.Equal(1, data.CellHeight);

        data.CellHeight = -5;
        Assert.Equal(1, data.CellHeight);
    }
}
