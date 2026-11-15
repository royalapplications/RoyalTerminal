// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotPageGrowthTests
{
    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void CarriesBusyStylesThenReturnsToStandardAfterTwoUnusedPages(int alignment)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        GhosttySnapshotPageCapacity standard = layout.InitialCapacity(80), previous = standard with { Styles = 1024 };
        GhosttySnapshotPageStorage busy = new(previous), empty = new(standard);
        for (int i = 1; i <= 300; i++)
            busy.Styles.AddTableReference(new(new(2, (byte)i, (byte)(i >> 8), 0), default, default, 0));
        GhosttySnapshotPageGrowth growth = new();
        GhosttySnapshotPageCapacity next = growth.Next(previous, busy, previous.Rows, 80, layout);
        Assert.Equal(1024, next.Styles);
        Assert.True(next.Rows <= standard.Rows);
        Assert.True(layout.AllocatedBytes(next) >= layout.StandardPageBytes);
        GhosttySnapshotPageGrowth retained = growth.Copy();
        Assert.Equal(1024, growth.Next(next, empty, next.Rows, 80, layout).Styles);
        Assert.Equal(standard, growth.Next(standard, empty, standard.Rows, 80, layout));
        Assert.Equal(1024, retained.Next(next, empty, next.Rows, 80, layout).Styles);
    }

    [Fact]
    public void HyperlinkMapGrowthDoesNotIncreaseTheUniqueLinkSet()
    {
        GhosttySnapshotAllocation layout = new(4096);
        GhosttySnapshotPageCapacity original = layout.InitialCapacity(80);
        Assert.True(layout.TryIncreaseCapacity(original, GhosttySnapshotCapacityDimension.HyperlinkCells,
            102, 2, out GhosttySnapshotPageCapacity grown));
        Assert.Equal(original.HyperlinkBytes, grown.HyperlinkBytes);
        Assert.True(grown.HyperlinkCells > original.HyperlinkCells);
        Assert.True(grown.HyperlinkWireBytes > grown.HyperlinkBytes);
        GhosttySnapshotHyperlinkStorage storage = new(192, 2048, 0);
        byte[] encoded = [1, 0, 0, 0, 0, 1, 0, 0, 0, (byte)'x'];
        // A zero map can still hold cursor links; adding a cell requires map growth.
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.MapFull, storage.ObserveCell(0, encoded));
        Assert.Equal(GhosttySnapshotCapacityDimension.HyperlinkCells,
            GhosttySnapshotHyperlinkStorage.GrowthDimension(GhosttySnapshotHyperlinkAddResult.MapFull));
    }

    [Fact]
    public void PlainPageGrowthDoesNotAllocatePerPage()
    {
        GhosttySnapshotAllocation layout = new(4096);
        GhosttySnapshotPageCapacity capacity = layout.InitialCapacity(80);
        GhosttySnapshotPageStorage storage = new(capacity);
        GhosttySnapshotPageGrowth growth = new();
        for (int i = 0; i < 20; i++) growth.Next(capacity, storage, capacity.Rows, 80, layout);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) capacity = growth.Next(capacity, storage, capacity.Rows, 80, layout);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(layout.InitialCapacity(80), capacity);
    }
}
