// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>Ported behavior from Ghostty ImageStorage.Placement geometry and margin-clipping tests.</summary>
public sealed class ManagedKittyPlacementGeometryTests
{
    [Fact]
    public void SourceRectIntersectsImageAndOffsetsClampToCell()
    {
        ManagedKittyPlacementOptions placement = new(10, 20, uint.MaxValue, 0, 99, 99, 0, 0, 0);
        ManagedKittyPlacementGeometry result = placement.Calculate(40, 50, 8, 16);
        Assert.Equal(new ManagedKittyPlacementGeometry(10, 20, 30, 30, 7, 15, 30, 30, 5, 3), result);
    }

    [Theory]
    [InlineData(3u, 0u, 20u, 10u, 3u, 1u)]
    [InlineData(0u, 3u, 86u, 43u, 12u, 3u)]
    [InlineData(3u, 3u, 20u, 43u, 3u, 3u)]
    public void ExplicitDimensionsSubtractOffsetsBeforeAspectRatio(uint columns, uint rows, uint width, uint height, uint expectedColumns, uint expectedRows)
    {
        ManagedKittyPlacementOptions placement = new(0, 0, 0, 0, 4, 5, columns, rows, 0);
        ManagedKittyPlacementGeometry result = placement.Calculate(40, 20, 8, 16);
        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
        Assert.Equal(expectedColumns, result.Columns);
        Assert.Equal(expectedRows, result.Rows);
    }

    [Fact]
    public void OverflowSaturatesAndUnavailableMetricsDoNotDivideByZero()
    {
        ManagedKittyPlacementOptions placement = new(0, 0, 0, 0, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, 0);
        ManagedKittyPlacementGeometry result = placement.Calculate(1, 1, 8, 16);
        Assert.Equal(uint.MaxValue - 7, result.Width);
        Assert.Equal(uint.MaxValue - 15, result.Height);
        Assert.Equal(uint.MaxValue, result.Columns);
        result = placement.Calculate(1, 1, 0, 0);
        Assert.Equal(0u, result.Width);
        Assert.Equal(uint.MaxValue, result.Columns);
        Assert.Equal(0u, new ManagedKittyPlacementOptions().Calculate(1, 1, 0, 0).Columns);
    }

    [Fact]
    public void MarginClippingMaterializesSourceRectAndScalesConsistently()
    {
        ManagedKittyPlacementOptions placement = new(0, 0, 0, 0, 0, 0, 10, 10, 0);
        Assert.True(placement.TryClipTop(100, 100, 8, 16, 2, 10, out var top));
        Assert.Equal(20u, top.SourceY);
        Assert.Equal(80u, top.SourceHeight);
        Assert.Equal(8u, top.Rows);
        Assert.True(placement.TryClipBottom(100, 100, 8, 16, 2, 10, out var bottom));
        Assert.Equal(0u, bottom.SourceY);
        Assert.Equal(80u, bottom.SourceHeight);
        Assert.Equal(8u, bottom.Rows);
        Assert.False(placement.TryClipBottom(100, 0, 8, 16, 2, 10, out _));
    }

    [Fact]
    public void NativeSizeMarginClippingAccountsForPixelOffset()
    {
        ManagedKittyPlacementOptions placement = new(0, 0, 0, 0, 0, 5, 0, 0, 0);
        Assert.True(placement.TryClipBottom(20, 50, 8, 16, 1, 4, out var bottom));
        Assert.Equal(43u, bottom.SourceHeight);
        Assert.True(placement.TryClipTop(20, 50, 8, 16, 1, 4, out var top));
        Assert.Equal(16u, top.SourceY);
        Assert.Equal(34u, top.SourceHeight);
    }
}
