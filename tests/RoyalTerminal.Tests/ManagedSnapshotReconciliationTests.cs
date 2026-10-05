// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty cursorChangePin keeps same-page state in place; crossing pages migrates
// references, not whole terminal rows. WT ROW and xterm.js BufferLine keep their
// own row payloads. Our reconciliation is needed for host/COW writes, but clean
// revisions must not reconstruct allocator history or force metadata rescans.
public sealed class ManagedSnapshotReconciliationTests
{
    private static GhosttySnapshotStyle Bold => new(default, default, default, 1);
    private static GhosttySnapshotStyle Italic => new(default, default, default, 2);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmStyleChangesReuseScratchWithoutCellCopiesOrReconciliation(bool separatePages)
    {
        Fixture fixture = new(separatePages);
        object firstCells = fixture.Rows[0].SearchStorageIdentity, secondCells = fixture.Rows[1].SearchStorageIdentity;
        for (int i = 0; i < 128; i++) fixture.Cycle();
        int reconciliations = 0;
        fixture.Screen.MutationCheckpoint = phase => { if (phase == SnapshotMutationCheckpoint.MetadataReconciliation) reconciliations++; };

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool accepted = true;
        for (int i = 0; i < 128; i++) accepted &= fixture.Cycle();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(accepted);
        Assert.Equal(0, allocated);
        Assert.Equal(0, reconciliations);
        Assert.Same(firstCells, fixture.Rows[0].SearchStorageIdentity);
        Assert.Same(secondCells, fixture.Rows[1].SearchStorageIdentity);
    }

    [Fact]
    public void DirtyNonCursorRowCannotBeSkipped()
    {
        Fixture fixture = new();
        Assert.True(fixture.Set(0, default, Bold));
        int reconciliations = 0;
        fixture.Screen.MutationCheckpoint = phase => { if (phase == SnapshotMutationCheckpoint.MetadataReconciliation) reconciliations++; };
        fixture.Rows[1][0].Attributes = CellAttributes.Bold | CellAttributes.Italic;

        Assert.True(fixture.Set(0, Bold, default));

        Assert.Equal(1, reconciliations);
        Assert.Equal(2, fixture.StyleCount());
        Assert.True(fixture.Set(0, default, Bold));
        Assert.Equal(1, reconciliations);
    }

    [Fact]
    public void RemovedPhysicalSlotStillReleasesItsMetadata()
    {
        Fixture fixture = new();
        Assert.True(fixture.Set(0, default, default));
        Assert.Equal(2, fixture.StyleCount());
        fixture.Rows.RemoveRange(1, 1); // Simulate a host mutation not yet observed by the tracker.
        int reconciliations = 0;
        fixture.Screen.MutationCheckpoint = phase => { if (phase == SnapshotMutationCheckpoint.MetadataReconciliation) reconciliations++; };

        Assert.True(fixture.Set(0, default, Bold));

        Assert.Equal(1, reconciliations);
        Assert.Equal(1, fixture.StyleCount());
        Assert.True(fixture.Set(0, Bold, default));
        Assert.Equal(1, reconciliations);
    }

    [Fact]
    public void SameRowCountWithDifferentPhysicalSlotsMustReconcile()
    {
        Fixture fixture = new();
        Assert.True(fixture.Set(0, default, default));
        GhosttySnapshotPageAllocation page = fixture.Rows[0].SnapshotAllocation!;
        fixture.Rows[1] = new(8) { SnapshotAllocation = page, SnapshotAllocationRow = 7 };

        Assert.True(fixture.Set(0, default, Bold));

        Assert.Equal(1, fixture.StyleCount());
        fixture.Rows[1][0].Attributes = CellAttributes.Italic;
        Assert.True(fixture.Set(0, Bold, default));
        Assert.Equal(2, fixture.StyleCount());
    }

    [Fact]
    public void InterleavedPagesDoNotHideDirtyRowsOfEitherOwner()
    {
        Fixture fixture = new(separatePages: true);
        GhosttySnapshotPageAllocation first = fixture.Rows[0].SnapshotAllocation!;
        TerminalRow tail = new(8) { SnapshotAllocation = first, SnapshotAllocationRow = 3 };
        fixture.Rows.Add(tail);
        Assert.True(fixture.Set(0, default, Bold));
        tail[0].Attributes = CellAttributes.Italic;

        Assert.True(fixture.Set(1, Bold, default)); // Departing-page synchronization must see the nonadjacent tail.

        Assert.True(fixture.Tracker.TryGetStyleUsage(first, [fixture.Rows[0], tail], out int styles));
        Assert.Equal(2, styles);
        Assert.True(fixture.Set(0, default, Bold));
        Assert.False(first.MetadataOverflow);
    }

    [Fact]
    public void FreshRestoredSeedTrimsMetadataOutsideInstalledRowsEvenWithMatchingRevisions()
    {
        GhosttySnapshotPageCapacity capacity = new(8, 8, 16, 192, 1024, 2048);
        GhosttySnapshotStyleStorage styles = new(capacity.Styles);
        GhosttySnapshotGraphemeStorage graphemes = new(capacity.GraphemeBytes);
        GhosttySnapshotHyperlinkStorage hyperlinks = new(capacity.HyperlinkBytes, capacity.StringBytes);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, styles.ChangeCell(8, Bold));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, graphemes.Set(8, 1));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, hyperlinks.ObserveCell(8, new TerminalHyperlink("u"u8, default, 0).SnapshotEncoding));
        GhosttySnapshotPageAllocation page = new(capacity, styles, restoredGraphemes: graphemes, restoredHyperlinks: hyperlinks);
        TerminalRow row = new(8) { SnapshotAllocation = page, SnapshotAllocationUnmodified = true };
        TerminalRowBuffer rows = new(1);
        rows.Add(row);
        GhosttySnapshotPageTracker tracker = new();
        TerminalScreen screen = new(8, 1);
        uint counter = 0;

        Assert.True(tracker.ChangeCursor(rows, 0, row, default, default, new(4096), screen, ref counter));

        Assert.True(tracker.TryGetStyleUsage(page, [row], out int styleCount));
        Assert.Equal(0, styleCount);
        Assert.True(tracker.TryGetGraphemeUsage(page, [row], out ulong graphemeCells, out _));
        Assert.Equal(0UL, graphemeCells);
        Assert.True(tracker.TryGetHyperlinkUsage(page, [row], out ulong links, out ulong linkCells, out _));
        Assert.Equal(0UL, links);
        Assert.Equal(0UL, linkCells);
        // The immutable seed belongs to other potential owners.
        Assert.Equal(1, styles.CellCount);
    }

    [Fact]
    public void CowOwnersReconcileIndependently()
    {
        Fixture fixture = new();
        Assert.True(fixture.Set(0, default, Bold));
        GhosttySnapshotPageTracker sibling = fixture.Tracker.Copy();
        TerminalRowBuffer copiedRows = new(2);
        copiedRows.Add(fixture.Rows[0].CreateStateCopy());
        copiedRows.Add(fixture.Rows[1].CreateStateCopy());
        fixture.Rows[1][0].Attributes = CellAttributes.None;
        Assert.True(fixture.Set(0, Bold, default));
        Assert.Equal(1, fixture.StyleCount());
        uint counter = 0;

        Assert.True(sibling.ChangeCursor(copiedRows, 0, copiedRows[0], Bold, default, new(4096), fixture.Screen, ref counter));

        Assert.True(sibling.TryGetStyleUsage(copiedRows[0].SnapshotAllocation!, [copiedRows[0], copiedRows[1]], out int siblingStyles));
        Assert.Equal(2, siblingStyles);
        Assert.Equal(CellAttributes.Italic, copiedRows[1].ReadOnlyCells[0].Attributes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedScratchDoesNotRetainRowOwnersAfterSuccessOrException(bool fail)
    {
        (GhosttySnapshotPageTracker tracker, WeakReference<TerminalRow> row) = CaptureWeakRow(fail);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.False(row.TryGetTarget(out _));
        GC.KeepAlive(tracker);
    }

    [Fact]
    public void FailedPreparationReturnsBothScratchCollectionsForRetry()
    {
        Fixture fixture = new();
        OutOfMemoryException failure = new("Before reconciliation");
        fixture.Screen.MutationCheckpoint = phase => { if (phase == SnapshotMutationCheckpoint.MetadataReconciliation) throw failure; };
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => fixture.Set(0, default, Bold)));
        fixture.Screen.MutationCheckpoint = null;

        for (int i = 0; i < 128; i++) Assert.True(fixture.Cycle());
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool accepted = fixture.Cycle();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(accepted);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    public void StyleRetirementCanRemoveWholeAndPartialChunksWithoutKeyCopies(int columns)
    {
        GhosttySnapshotStyleStorage storage = new(16);
        for (int i = 0; i < columns * 4; i++)
            Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.ChangeCell(i, (i / columns & 1) == 0 ? Bold : Italic));
        HashSet<int> retained = [1, 3];
        // Warm the same code on independent storage before measuring removal.
        GhosttySnapshotStyleStorage warm = storage.Copy();
        warm.RetainRows(retained, columns);

        long before = GC.GetAllocatedBytesForCurrentThread();
        storage.RetainRows(retained, columns);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(columns * 2, storage.CellCount);
        for (int i = 0; i < columns * 4; i++)
            Assert.Equal((i / columns & 1) == 0 ? default : Italic, storage.CellStyle(i));
        storage.RetainRows(new HashSet<int>(), columns);
        Assert.Equal(0, storage.CellCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (GhosttySnapshotPageTracker, WeakReference<TerminalRow>) CaptureWeakRow(bool fail)
    {
        Fixture fixture = new();
        if (fail) fixture.Screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.MetadataReconciliation) throw new OutOfMemoryException("Scratch cleanup");
        };
        try { fixture.Set(0, default, Bold); }
        catch (OutOfMemoryException) when (fail) { }
        return (fixture.Tracker, new(fixture.Rows[0]));
    }

    private sealed class Fixture
    {
        internal TerminalScreen Screen { get; } = new(8, 2);
        internal TerminalRowBuffer Rows { get; } = new(2);
        internal GhosttySnapshotPageTracker Tracker { get; } = new();
        private readonly GhosttySnapshotAllocation _layout = new(4096);
        private uint _counter;

        internal Fixture(bool separatePages = false)
        {
            GhosttySnapshotPageCapacity capacity = new(8, 8, 16, 192, 1024, 2048);
            GhosttySnapshotPageAllocation page = new(capacity);
            TerminalRow first = new(8) { SnapshotAllocation = page };
            TerminalRow second = new(8) { SnapshotAllocation = separatePages ? new(capacity) : page, SnapshotAllocationRow = separatePages ? 0 : 1 };
            first[0].Attributes = CellAttributes.Bold;
            second[0].Attributes = CellAttributes.Italic;
            Rows.Add(first); Rows.Add(second);
        }

        internal bool Set(int row, GhosttySnapshotStyle previous, GhosttySnapshotStyle current) =>
            Tracker.ChangeCursor(Rows, 0, Rows[row], previous, current, _layout, Screen, ref _counter);

        internal bool Cycle() => Set(0, default, Bold) & Set(1, Bold, Italic) & Set(0, Italic, default);

        internal int StyleCount()
        {
            List<TerminalRow> group = [];
            GhosttySnapshotPageAllocation page = Rows[0].SnapshotAllocation!;
            foreach (TerminalRow row in Rows) if (ReferenceEquals(row.SnapshotAllocation, page)) group.Add(row);
            Assert.True(Tracker.TryGetStyleUsage(page, group, out int count));
            return count;
        }
    }
}
