// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedRasterRetirementTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(64)]
    public void WarmReplacementDoesNotAllocateRetirementCollections(int count)
    {
        TerminalScreen screen = new(count + 1, 2);
        for (int i = 1; i <= count; i++) screen.ReplaceRasterImage(Source(i), Placement(i, i, 0));
        TerminalRasterImageSource source = Source(1);
        TerminalRasterImagePlacement placement = Placement(1, 1, 0);
        for (int i = 0; i < 128; i++) screen.ReplaceRasterImage(source, placement);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) screen.ReplaceRasterImage(source, placement);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(count, screen.GetRasterImagePlacements().Length);
        for (int i = 1; i <= count; i++) Assert.True(screen.TryGetRasterImageSource(i, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void ErasingAnImageDropsOnlyUnreferencedPayloads(int retainedCount)
    {
        TerminalScreen screen = new(8, 2);
        for (int i = 1; i <= retainedCount + 1; i++) screen.ReplaceRasterImage(Source(i), Placement(i, i, 0));
        TerminalScreen retained = screen.CreateStateCopy();

        screen.ClearRasterGraphicsInViewportRectangle(0, 0, 1, 1);

        Assert.False(screen.TryGetRasterImageSource(1, out _));
        Assert.Equal(retainedCount, screen.GetRasterImagePlacements().Length);
        for (int i = 2; i <= retainedCount + 1; i++) Assert.True(screen.TryGetRasterImageSource(i, out _));
        Assert.True(retained.TryGetRasterImageSource(1, out _));
        Assert.Equal(retainedCount + 1, retained.GetRasterImagePlacements().Length);
    }

    [Fact]
    public void NonRetiringShiftsKeepSourcesAndRetiringShiftsRemoveThem()
    {
        TerminalScreen screen = new(8, 4);
        screen.ReplaceRasterImage(Source(1), Placement(1, 0, 1));
        screen.ReplaceRasterImage(Source(2), Placement(2, 1, 2));
        TerminalScreen retained = screen.CreateStateCopy();

        screen.ShiftRasterGraphicsInViewportRows(0, 3, -1);

        Assert.Equal(0, screen.GetRasterImagePlacements()[0].AnchorRow);
        Assert.Equal(1, screen.GetRasterImagePlacements()[1].AnchorRow);
        Assert.True(screen.TryGetRasterImageSource(1, out _));
        Assert.True(screen.TryGetRasterImageSource(2, out _));
        screen.ShiftRasterGraphicsInViewportRows(0, 3, -1);
        Assert.False(screen.TryGetRasterImageSource(1, out _));
        Assert.True(screen.TryGetRasterImageSource(2, out _));
        Assert.Single(screen.GetRasterImagePlacements().ToArray());
        Assert.Equal(1, retained.GetRasterImagePlacements()[0].AnchorRow);
        Assert.True(retained.TryGetRasterImageSource(1, out _));
    }

    [Fact]
    public void ScrollbackEvictionDropsPayloadOnlyAfterLastVisiblePixel()
    {
        TerminalScreen screen = new(8, 2, 0);
        screen.ReplaceRasterImage(Source(1), Placement(1, 0, 1));

        screen.AddRow();

        Assert.True(screen.TryGetRasterImageSource(1, out _));
        Assert.Equal(0, screen.GetRasterImagePlacements()[0].AnchorRow);
        screen.AddRow();
        Assert.False(screen.HasRasterGraphics);
        Assert.False(screen.TryGetRasterImageSource(1, out _));
    }

    [Fact]
    public void RetirementScratchDoesNotLeakAcrossBuffersOrPublication()
    {
        TerminalScreen screen = new(8, 2);
        for (int i = 1; i <= 4; i++) screen.ReplaceRasterImage(Source(i), Placement(i, i, 0));
        screen.ReplaceRasterImage(Source(1), Placement(1, 1, 0)); // Warm multi-image scratch.
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalScreen staged = screen.CreateStateCopy();
        staged.ClearRasterGraphicsInViewportRectangle(0, 0, 2, 2);
        screen.AdoptStateFrom(staged);
        screen.SwitchToAlternateBuffer(false);
        for (int i = 11; i <= 14; i++) screen.ReplaceRasterImage(Source(i), Placement(i, i - 10, 0));
        screen.ClearRasterGraphicsInViewportRectangle(0, 0, 2, 2);
        Assert.False(screen.TryGetRasterImageSource(12, out _));
        Assert.True(screen.TryGetRasterImageSource(11, out _));
        screen.SwitchToPrimaryBuffer();
        screen.ClearRasterGraphicsInViewportRectangle(0, 0, 3, 3);

        Assert.True(screen.TryGetRasterImageSource(1, out _));
        Assert.False(screen.TryGetRasterImageSource(2, out _));
        Assert.False(screen.TryGetRasterImageSource(3, out _));
        Assert.True(screen.TryGetRasterImageSource(4, out _));
        for (int i = 1; i <= 4; i++) Assert.True(retained.TryGetRasterImageSource(i, out _));
    }

    private static TerminalRasterImageSource Source(int id) => new(id, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 0, 255]);
    private static TerminalRasterImagePlacement Placement(int id, int column, int row) =>
        new(id, TerminalRasterImageLayer.BelowText, column, row, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1);
}
