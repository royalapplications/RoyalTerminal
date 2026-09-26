// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Follows Ghostty snapshot/page.zig table-reference release, Screen.manualStyleUpdate
// and Page.cloneFrom. Windows Terminal ROW attributes and xterm.js BufferLine's
// packed attributes have no equivalent PAGE refcount/dead-slot snapshot contract.
public sealed class GhosttySnapshotStyleStorageTests
{
    private static GhosttySnapshotStyle Bold => new(default, default, default, 1);
    private static GhosttySnapshotStyle Italic => new(default, default, default, 2);
    private static GhosttySnapshotStyle Faint => new(default, default, default, 4);

    [Fact]
    public void CursorOnlyChangesReuseDeadTailWithoutAccumulatingStyles()
    {
        GhosttySnapshotStyleStorage storage = new(4);
        foreach (GhosttySnapshotStyle value in new[] { Bold, Italic, Faint, Bold })
        {
            Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.ChangeCursor(value));
            Assert.Equal(value, storage.Cursor);
            Assert.Equal(1, storage.Count);
            Assert.Equal(0, storage.CellCount);
        }
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.ChangeCursor(default));
        Assert.Equal(0, storage.Count);
    }

    [Fact]
    public void CellsKeepPreviousPensLiveAndFailedCursorChangeFallsBackToDefault()
    {
        GhosttySnapshotStyleStorage storage = new(4);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.ChangeCursor(Bold));
        storage.WriteCursorToCell(0); storage.WriteCursorToCell(0);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.ChangeCursor(Italic));
        storage.WriteCursorToCell(1);
        Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory, storage.ChangeCursor(Faint));
        Assert.Equal(default, storage.Cursor);
        Assert.Equal(Bold, storage.CellStyle(0));
        Assert.Equal(Italic, storage.CellStyle(1));
        Assert.Equal(2, storage.Count);
        storage.ClearCell(0);
        Assert.Equal(GhosttySnapshotSetAddResult.NeedsRehash, storage.ChangeCursor(Faint));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.Rebuild(4, out GhosttySnapshotStyleStorage? rebuilt));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, rebuilt!.ChangeCursor(Faint));
        Assert.Equal(Italic, rebuilt.CellStyle(1));
        Assert.Equal(2, rebuilt.Count);
    }

    [Fact]
    public void DuplicateTableValuesSurrenderAllTemporaryReferencesAfterAttachingCells()
    {
        GhosttySnapshotStyleStorage storage = new(8);
        int first = storage.AddTableReference(Bold), alias = storage.AddTableReference(Bold);
        Assert.Equal(first, alias);
        storage.AttachDecodedCell(0, first); storage.AttachDecodedCell(2, alias);
        storage.ReleaseTableReference(first); storage.ReleaseTableReference(alias);
        Assert.Equal(1, storage.Count);
        storage.ClearCell(0);
        Assert.Equal(Bold, storage.CellStyle(2));
        Assert.Equal(1, storage.Count);
        storage.ClearCell(2);
        Assert.Equal(0, storage.Count);
    }

    [Fact]
    public void CopyOnWriteForkRetainsIndependentCursorCellAndDeadSlotReferences()
    {
        GhosttySnapshotStyleStorage source = new(4);
        source.ChangeCursor(Bold); source.WriteCursorToCell(0);
        source.ChangeCursor(Italic); source.WriteCursorToCell(1);
        GhosttySnapshotStyleStorage copy = source.Copy();
        copy.ClearCell(0);
        Assert.Equal(GhosttySnapshotSetAddResult.NeedsRehash, copy.ChangeCursor(Faint));
        Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory, source.ChangeCursor(Faint));
        Assert.Equal(Bold, source.CellStyle(0));
        Assert.Equal(default, copy.CellStyle(0));
        Assert.Equal(2, source.Count);
        Assert.Equal(1, copy.Count);
    }

    [Fact]
    public void RebuildCopiesCellsInPhysicalOrderAndLeavesTheCursorForItsOwner()
    {
        GhosttySnapshotStyleStorage source = new(4);
        source.ChangeCursor(Bold); source.WriteCursorToCell(100);
        source.ChangeCursor(Italic); source.WriteCursorToCell(0);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, source.Rebuild(8, out GhosttySnapshotStyleStorage? rebuilt));
        Assert.Equal(default, rebuilt!.Cursor);
        Assert.Equal(Italic, rebuilt.CellStyle(0));
        Assert.Equal(Bold, rebuilt.CellStyle(100));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, rebuilt.ChangeCursor(Faint));
        Assert.Equal(3, rebuilt.Count);
        rebuilt.WriteCursorToCell(0);
        Assert.Equal(2, rebuilt.Count); // Rebuild retained no hidden cursor reference to italic.
        Assert.Equal(Italic, source.Cursor);
        Assert.Equal(Italic, source.CellStyle(0));
    }

    [Fact]
    public void FailedRebuildCannotChangeTheSourceOrExposePartialReplacement()
    {
        GhosttySnapshotStyleStorage source = new(8);
        source.ChangeCursor(Bold); source.WriteCursorToCell(0);
        source.ChangeCursor(Italic); source.WriteCursorToCell(1);
        source.ChangeCursor(Faint); source.WriteCursorToCell(2);
        Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory, source.Rebuild(4, out GhosttySnapshotStyleStorage? rebuilt));
        Assert.Null(rebuilt);
        Assert.Equal(3, source.Count);
        Assert.Equal(Bold, source.CellStyle(0));
        Assert.Equal(Italic, source.CellStyle(1));
        Assert.Equal(Faint, source.CellStyle(2));
        Assert.Equal(Faint, source.Cursor);
    }

    [Fact]
    public void DenseCellReferencesUseCompactChunksRatherThanPerCellDictionaryEntries()
    {
        GhosttySnapshotStyleStorage warm = new(4);
        int warmId = warm.AddTableReference(Bold);
        warm.AttachDecodedCell(0, warmId);

        GhosttySnapshotStyleStorage storage = new(4);
        int id = storage.AddTableReference(Bold);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 65536; i++) storage.AttachDecodedCell(i, id);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        storage.ReleaseTableReference(id);
        Assert.Equal(65536, storage.CellCount);
        Assert.InRange(allocated, 128 * 1024, 256 * 1024);
        GhosttySnapshotStyleStorage copy = storage.Copy();
        for (int i = 0; i < 65536; i++) copy.ClearCell(i);
        Assert.Equal(0, copy.Count);
        Assert.Equal(0, copy.CellCount);
        Assert.Equal(1, storage.Count);
        Assert.Equal(Bold, storage.CellStyle(65535));
    }

    [Fact]
    public void SparseHighIndicesDoNotAllocateTheirAbsentPrefix()
    {
        GhosttySnapshotStyleStorage storage = new(ushort.MaxValue);
        int id = storage.AddTableReference(Bold);
        long before = GC.GetAllocatedBytesForCurrentThread();
        storage.AttachDecodedCell(int.MaxValue, id);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        storage.ReleaseTableReference(id);
        Assert.InRange(allocated, 1, 4096);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.Rebuild(4, out GhosttySnapshotStyleStorage? rebuilt));
        Assert.Equal(Bold, rebuilt!.CellStyle(int.MaxValue));
        rebuilt.ClearCell(int.MaxValue);
        Assert.Equal(0, rebuilt.Count);
    }

    [Fact]
    public void PrunedRowsReleaseTheirReferencesButRetainTheCursorAndOtherRows()
    {
        GhosttySnapshotStyleStorage storage = new(8);
        storage.ChangeCursor(Bold); storage.WriteCursorToCell(0); storage.WriteCursorToCell(400);
        storage.ChangeCursor(Italic); storage.WriteCursorToCell(600);
        storage.RetainRows(new HashSet<int> { 2 }, 200);
        Assert.Equal(1, storage.CellCount);
        Assert.Equal(Bold, storage.CellStyle(400));
        Assert.Equal(Italic, storage.Cursor);
        Assert.Equal(2, storage.Count);
        storage.RetainRows(new HashSet<int>(), 200);
        Assert.Equal(0, storage.CellCount);
        Assert.Equal(1, storage.Count);
    }

    [Fact]
    public void InlineBackgroundObservationDoesNotInventAStyleUntilTextReplacesIt()
    {
        GhosttySnapshotStyleStorage storage = new(4);
        GhosttySnapshotStyle native = Bold with { Background = new(1, 1, 0, 0) };
        GhosttySnapshotStyle visible = native with { Background = new(1, 2, 0, 0) };
        int id = storage.AddTableReference(native);
        storage.AttachDecodedCell(0, id); storage.ReleaseTableReference(id);
        storage.ObserveInlineBackground(0, visible.Background);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.ObserveCell(0, visible, empty: true));
        Assert.Equal(native, storage.CellStyle(0));
        GhosttySnapshotStyleStorage copy = storage.Copy();
        Assert.Equal(GhosttySnapshotSetAddResult.Success, copy.Rebuild(4, out GhosttySnapshotStyleStorage? rebuilt));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, rebuilt!.ObserveCell(0, visible, empty: true));
        Assert.Equal(native, rebuilt.CellStyle(0));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, rebuilt.ObserveCell(0, visible, empty: false));
        Assert.Equal(visible, rebuilt.CellStyle(0));
        Assert.Equal(native, storage.CellStyle(0));
    }

    [Fact]
    public void GroupedClearCrossesChunkBoundariesWithoutDroppingTheCursorOrOtherCells()
    {
        GhosttySnapshotStyleStorage storage = new(4);
        storage.ChangeCursor(Bold);
        for (int i = 250; i < 270; i++) storage.WriteCursorToCell(i);
        storage.ChangeCursor(Italic); storage.WriteCursorToCell(270);
        GhosttySnapshotStyleStorage retained = storage.Copy();
        storage.ClearCells(251, 20);
        Assert.Equal(1, storage.CellCount);
        Assert.Equal(Bold, storage.CellStyle(250));
        Assert.Equal(Italic, storage.Cursor);
        Assert.Equal(2, storage.Count);
        storage.ClearCells(0, 251);
        Assert.Equal(0, storage.CellCount);
        Assert.Equal(1, storage.Count);
        Assert.Equal(21, retained.CellCount);
    }

    [Fact]
    public void SwappingCellOwnershipPreservesReferencesAndInlineBackgroundObservations()
    {
        GhosttySnapshotStyleStorage storage = new(4);
        storage.ChangeCursor(Bold); storage.WriteCursorToCell(0);
        storage.ChangeCursor(Italic); storage.WriteCursorToCell(256);
        storage.ObserveInlineBackground(0, new(1, 2, 0, 0));
        storage.SwapCells(0, 256);
        Assert.Equal(Italic, storage.CellStyle(0));
        Assert.Equal(Bold, storage.CellStyle(256));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.ObserveCell(256,
            Bold with { Background = new(1, 2, 0, 0) }, empty: true));
        Assert.Equal(Bold, storage.CellStyle(256));
        storage.SwapCells(256, 512);
        Assert.Equal(default, storage.CellStyle(256));
        Assert.Equal(2, storage.CellCount);
        storage.ClearCells(0, 513);
        Assert.Equal(0, storage.CellCount);
        Assert.Equal(1, storage.Count); // Only italic's cursor reference remains.
    }

    [Fact]
    public void CrossPageCopiesReusePreferredDeadIdsAndRetainInlineStyleIdentity()
    {
        GhosttySnapshotStyleStorage source = new(4), destination = new(4);
        source.ChangeCursor(Faint); source.WriteCursorToCell(0);
        source.ObserveInlineBackground(0, new(1, 2, 0, 0));
        destination.ChangeCursor(Bold); destination.WriteCursorToCell(0);
        destination.ChangeCursor(Italic); destination.WriteCursorToCell(1);
        destination.ChangeCursor(default);
        destination.ClearCells(0, 2);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, destination.CopyCellFrom(0, source, 0));
        Assert.Equal(Faint, destination.CellStyle(0));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, destination.ObserveCell(0,
            Faint with { Background = new(1, 2, 0, 0) }, empty: true));
        Assert.Equal(Faint, destination.CellStyle(0));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, destination.CopyCellFrom(1, destination, 0));
        destination.ClearCell(0);
        Assert.Equal(Faint, destination.CellStyle(1));
        destination.ClearCell(1);
        Assert.Equal(0, destination.Count);
        Assert.Equal(Faint, source.CellStyle(0));
    }

    [Fact]
    public void ReplacingTheLastStyledCellReusesItsEmptyChunk()
    {
        GhosttySnapshotStyleStorage storage = new(4);
        storage.ChangeCursor(Bold); storage.WriteCursorToCell(0);
        storage.ClearCell(0); storage.WriteCursorToCell(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            storage.ClearCell(0); storage.WriteCursorToCell(0);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(1, storage.CellCount);
        Assert.Equal(Bold, storage.CellStyle(0));
    }

    [Fact]
    public void BackgroundOnlyEraseDoesNotAllocateAStyleInAnEmptySet()
    {
        GhosttySnapshotStyleStorage storage = new(0);
        GhosttySnapshotStyle visible = new(default, new(1, 4, 0, 0), default, 0);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.ObserveCell(0, visible, empty: true));
        Assert.Equal(0, storage.Count);
        Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory, storage.ObserveCell(0, visible, empty: false));
    }

    [Fact]
    public void ReflowRunCopyRetainsCountsAndInlineObservationsAcrossChunks()
    {
        GhosttySnapshotStyleStorage source = new(4), destination = new(4);
        source.ChangeCursor(Bold);
        for (int i = 0; i < 600; i++) source.WriteCursorToCell(i);
        source.ObserveInlineBackground(300, new(1, 5, 0, 0));
        GhosttySnapshotStyleStorage.CopyCache cache = default;
        Assert.Equal(GhosttySnapshotSetAddResult.Success, destination.CopyCellsFrom(250, source, 0, 300, ref cache, out int first));
        Assert.Equal(300, first);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, destination.CopyCellsFrom(550, source, 300, 300, ref cache, out int second));
        Assert.Equal(300, second);
        Assert.Equal(600, destination.CellCount);
        Assert.Equal(1, destination.Count);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, destination.ObserveCell(550,
            Bold with { Background = new(1, 5, 0, 0) }, empty: true));
        Assert.Equal(Bold, destination.CellStyle(550));
        destination.ClearCells(250, 599);
        Assert.Equal(1, destination.Count);
        destination.ClearCell(849);
        Assert.Equal(0, destination.Count);
        Assert.Equal(600, source.CellCount);
    }

    [Fact]
    public void ReflowCopyFailureLeavesOnlyTheCompletedPrefixForRebuildAndRetry()
    {
        GhosttySnapshotStyleStorage source = new(8), destination = new(4);
        source.ChangeCursor(Bold); source.WriteCursorToCell(0); source.WriteCursorToCell(1);
        source.ChangeCursor(Italic); source.WriteCursorToCell(2);
        source.ChangeCursor(Faint); source.WriteCursorToCell(3); source.WriteCursorToCell(4);
        GhosttySnapshotStyleStorage.CopyCache cache = default;
        Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory, destination.CopyCellsFrom(0, source, 0, 5, ref cache, out int copied));
        Assert.Equal(3, copied);
        Assert.Equal(3, destination.CellCount);
        Assert.Equal(default, destination.CellStyle(3));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, destination.Rebuild(8, out GhosttySnapshotStyleStorage? rebuilt));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, rebuilt!.CopyCellsFrom(3, source, 3, 2, ref cache, out int remainder));
        Assert.Equal(2, remainder);
        Assert.Equal(5, rebuilt.CellCount);
        for (int i = 0; i < 5; i++) Assert.Equal(source.CellStyle(i), rebuilt.CellStyle(i));
        rebuilt.ClearCells(0, 5);
        Assert.Equal(0, rebuilt.Count);
    }

    [Fact]
    public void ReflowStyleCacheDoesNotReuseIdsAcrossSourceOrDestinationTables()
    {
        GhosttySnapshotStyleStorage first = new(4), second = new(4), destination = new(4), nextPage = new(4);
        first.ChangeCursor(Bold); first.WriteCursorToCell(0);
        second.ChangeCursor(Italic); second.WriteCursorToCell(0);
        GhosttySnapshotStyleStorage.CopyCache cache = default;
        Assert.Equal(GhosttySnapshotSetAddResult.Success, destination.CopyCellsFrom(0, first, 0, 1, ref cache, out _));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, destination.CopyCellsFrom(1, second, 0, 1, ref cache, out _));
        Assert.Equal(Bold, destination.CellStyle(0));
        Assert.Equal(Italic, destination.CellStyle(1));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, nextPage.CopyCellsFrom(0, second, 0, 1, ref cache, out _));
        Assert.Equal(Italic, nextPage.CellStyle(0));
        Assert.Equal(1, nextPage.Count);
    }
}
