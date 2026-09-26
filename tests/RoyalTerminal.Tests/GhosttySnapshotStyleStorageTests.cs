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
}
