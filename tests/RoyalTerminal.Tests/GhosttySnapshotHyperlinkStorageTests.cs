// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Native PAGE/Screen hyperlink allocation is the reference here. Windows
// Terminal TextBuffer prunes IDs after row retirement and xterm.js OscLinkService
// owns line markers; neither defines native per-page string pressure. Keep the
// host's immutable link registry separate from this native ownership model.
public sealed class GhosttySnapshotHyperlinkStorageTests
{
    [Fact]
    public void DecodeAllocatesIdFirstWhileCursorInsertionAllocatesUriFirst()
    {
        byte[] encoded = Link(64, 1);
        GhosttySnapshotHyperlinkStorage decoded = new(192, 2048), live = new(192, 2048);
        int id = decoded.AddDecodedTableReference(GhosttySnapshotHyperlink.Read(encoded, out _), encoded);
        Assert.NotEqual(0, id);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, live.StartCursor(encoded));
        Assert.Equal((new GhosttySnapshotBitmap.Slice(0, 1), new GhosttySnapshotBitmap.Slice(1, 2)), Allocation(decoded, id));
        Assert.Equal((new GhosttySnapshotBitmap.Slice(2, 1), new GhosttySnapshotBitmap.Slice(0, 2)), Allocation(live, live.CursorId));
        Assert.Equal(96UL, decoded.StringBytes);
        Assert.Equal(96UL, live.StringBytes);
    }

    [Fact]
    public void ReleasingTheCursorKeepsDeadStringsUntilInsertionCanReachTheSet()
    {
        GhosttySnapshotHyperlinkStorage storage = new(192, 2048);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.StartCursor(Link(2000)));
        storage.EndCursor();
        Assert.Equal(0, storage.Count);
        Assert.Equal(2016UL, storage.StringBytes);
        GhosttySnapshotHyperlinkStorage copy = storage.Copy();
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.StringsFull, storage.StartCursor(Link(64)));
        Assert.Equal(2016UL, storage.StringBytes);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.StartCursor(Link(32)));
        Assert.Equal(32UL, storage.StringBytes);
        Assert.Equal(new GhosttySnapshotBitmap.Slice(63, 1), Allocation(storage, storage.CursorId).Uri);
        Assert.Equal(2016UL, copy.StringBytes);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, copy.StartCursor(Link(32)));
        Assert.Equal(Allocation(storage, storage.CursorId), Allocation(copy, copy.CursorId));
    }

    [Fact]
    public void RestartingAnEqualCursorNeedsStringsButCrossPageCopiesLookupFirst()
    {
        byte[] value = Link(1984, 1);
        GhosttySnapshotHyperlinkStorage destination = new(192, 2048), source = new(192, 2048);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.StartCursor(value));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.WriteCursorToCell(0));
        int id = destination.CursorId;
        Assert.Equal(2, destination.ReferenceCount(id));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.StringsFull, destination.StartCursor(value));
        Assert.Equal(0, destination.CursorId);
        Assert.Equal(1, destination.ReferenceCount(id));
        Assert.Equal(2016UL, destination.StringBytes);

        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.StartCursor(value));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.WriteCursorToCell(4));
        source.EndCursor();
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.CopyCellFrom(1, source, 4));
        Assert.Equal(2016UL, destination.StringBytes);
        Assert.Equal(id, destination.CellId(1));
        Assert.Equal(2, destination.ReferenceCount(id));
        Assert.Equal(1, source.ReferenceCount(source.CellId(4)));
    }

    [Fact]
    public void MapPressurePrecedesStringCloningAndReplacingExistingCellsStillWorks()
    {
        GhosttySnapshotHyperlinkStorage destination = new(192, 2048), source = new(192, 2048);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.StartCursor(Link(1)));
        for (int i = 0; i < 102; i++)
            Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.WriteCursorToCell(i));
        int id = destination.CursorId;
        Assert.Equal(103, destination.ReferenceCount(id));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.MapFull, destination.WriteCursorToCell(102));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.WriteCursorToCell(0));
        Assert.Equal(103, destination.ReferenceCount(id));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.StartCursor(Link(2048)));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.WriteCursorToCell(0));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.MapFull, destination.CopyCellFrom(102, source, 0));
        Assert.Equal(32UL, destination.StringBytes);
        destination.Clear(1);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.StringsFull, destination.CopyCellFrom(102, source, 0));
        Assert.Equal(32UL, destination.StringBytes);
    }

    [Fact]
    public void OwnershipMovesAndRetirementPreserveCursorReferencesAndCowCopies()
    {
        GhosttySnapshotHyperlinkStorage storage = new(192, 2048);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.StartCursor(Link(1)));
        for (int i = 0; i < 3; i++) Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.WriteCursorToCell(i));
        int id = storage.CursorId;
        GhosttySnapshotHyperlinkStorage retained = storage.Copy();
        storage.Swap(0, 8);
        storage.ClearCells(1, 1);
        storage.RetainRows(new HashSet<int> { 2 }, 4);
        Assert.Equal(1, storage.CellCount);
        Assert.Equal(2, storage.ReferenceCount(id)); // One cell and the cursor.
        Assert.Equal(0, storage.CellId(0));
        Assert.Equal(id, storage.CellId(8));
        storage.Clear(8);
        Assert.Equal(1, storage.ReferenceCount(id));
        storage.EndCursor();
        Assert.Equal(0, storage.Count);
        Assert.Equal(32UL, storage.StringBytes);
        Assert.Equal(4, retained.ReferenceCount(id));
        Assert.Equal(3, retained.CellCount);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, retained.CopyCellFrom(8, retained, 0));
        Assert.Equal(5, retained.ReferenceCount(id));
        Assert.Throws<InvalidOperationException>(() => retained.CopyCellFrom(0, storage, 0));
    }

    [Fact]
    public void RebuildCopiesPhysicalCellOrderAndDoesNotImplicitlyRestoreTheCursor()
    {
        GhosttySnapshotHyperlinkStorage storage = new(384, 2048);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.StartCursor(Link(1, identity: 1)));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.WriteCursorToCell(9));
        int oldFirst = storage.CursorId;
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.StartCursor(Link(1, identity: 2)));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.WriteCursorToCell(1));
        int oldSecond = storage.CursorId;
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.Rebuild(384, 2048, out GhosttySnapshotHyperlinkStorage? rebuilt));
        Assert.Equal(0, rebuilt!.CursorId);
        Assert.Equal(1, rebuilt.CellId(1));
        Assert.Equal(2, rebuilt.CellId(9));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(0, 1), Allocation(rebuilt, rebuilt.CellId(1)).Uri);
        Assert.Equal(new GhosttySnapshotBitmap.Slice(1, 1), Allocation(rebuilt, rebuilt.CellId(9)).Uri);
        Assert.Equal(oldFirst, storage.CellId(9));
        Assert.Equal(oldSecond, storage.CellId(1));
        Assert.Equal(2, storage.ReferenceCount(oldSecond));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, rebuilt.StartCursor(storage.CursorEncoding));
        Assert.Equal(2, rebuilt.ReferenceCount(rebuilt.CellId(1)));
        Assert.Equal(64UL, rebuilt.StringBytes); // Temporary duplicate was freed.
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.SetFull, storage.Rebuild(48, 2048, out GhosttySnapshotHyperlinkStorage? failed));
        Assert.Null(failed);
        Assert.Equal(2, storage.Count);
    }

    [Fact]
    public void FailedCloneIdAllocationReleasesTheAlreadyAllocatedUri()
    {
        GhosttySnapshotHyperlinkStorage destination = new(384, 2048), source = new(192, 2048);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.StartCursor(Link(1984)));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.WriteCursorToCell(0));
        destination.EndCursor();
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.StartCursor(Link(32, 64)));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.WriteCursorToCell(0));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.StringsFull, destination.CopyCellFrom(1, source, 0));
        Assert.Equal(1984UL, destination.StringBytes);
        Assert.Equal(1, destination.CellCount);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.StartCursor(Link(64, identity: 2)));
        Assert.Equal(2048UL, destination.StringBytes);
    }

    [Fact]
    public void CloneSetFailureKeepsNativeOrphanPressureUntilRebuild()
    {
        GhosttySnapshotHyperlinkStorage destination = new(192, 2048), source = new(192, 2048);
        for (uint i = 1; i <= 2; i++)
        {
            Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.StartCursor(Link(1, identity: i)));
            Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.WriteCursorToCell((int)i));
        }
        destination.EndCursor();
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.StartCursor(Link(1, identity: 3)));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.WriteCursorToCell(0));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.SetFull, destination.CopyCellFrom(3, source, 0));
        Assert.Equal(96UL, destination.StringBytes);
        Assert.Equal(2, destination.CellCount);
        GhosttySnapshotHyperlinkStorage retained = destination.Copy();
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.SetFull, destination.StartCursor(source.CursorEncoding));
        Assert.Equal(96UL, destination.StringBytes); // Normal insert rolls back its own strings.
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, destination.Rebuild(384, 2048, out GhosttySnapshotHyperlinkStorage? rebuilt));
        Assert.Equal(64UL, rebuilt!.StringBytes);
        Assert.Equal(96UL, retained.StringBytes);
    }

    [Fact]
    public void MapGrowthScratchIsChargedCopiedAndThenDiscardedWithTheOldPage()
    {
        GhosttySnapshotHyperlinkStorage storage = new(192, 2048);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.StartCursor(Link(1025)));
        Assert.False(storage.TryReserveCursorUri());
        Assert.Equal(1056UL, storage.StringBytes);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.StartCursor(Link(1, identity: 2)));
        Assert.True(storage.TryReserveCursorUri());
        Assert.Equal(64UL, storage.StringBytes);
        Assert.Equal(64UL, storage.Copy().StringBytes);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.Rebuild(384, 2048, out GhosttySnapshotHyperlinkStorage? rebuilt));
        Assert.Equal(0UL, rebuilt!.StringBytes);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, rebuilt.StartCursor(storage.CursorEncoding));
        Assert.Equal(32UL, rebuilt.StringBytes);
    }

    [Fact]
    public void HugeCapacityHintsDoNotMaterializeDenseStringOrCellStorage()
    {
        _ = new GhosttySnapshotHyperlinkStorage(ushort.MaxValue, uint.MaxValue);
        byte[] encoded = Link(1);
        long start = GC.GetAllocatedBytesForCurrentThread();
        GhosttySnapshotHyperlinkStorage storage = new(ushort.MaxValue, uint.MaxValue);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.StartCursor(encoded));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.WriteCursorToCell(100_000_000));
        GhosttySnapshotHyperlinkStorage copy = storage.Copy();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(1, copy.CellCount);
        Assert.Equal(32UL, copy.StringBytes);
        Assert.InRange(allocated, 1, 64 * 1024);
    }

    private static (GhosttySnapshotBitmap.Slice Id, GhosttySnapshotBitmap.Slice Uri) Allocation(GhosttySnapshotHyperlinkStorage storage, int id)
    {
        Assert.True(storage.TryGetAllocation(id, out GhosttySnapshotBitmap.Slice explicitId, out GhosttySnapshotBitmap.Slice uri));
        return (explicitId, uri);
    }

    private static byte[] Link(int uriLength, int explicitLength = 0, uint identity = 1)
    {
        byte[] uri = new byte[uriLength], id = new byte[explicitLength];
        Array.Fill(uri, (byte)'u'); Array.Fill(id, (byte)'i');
        using MemoryStream output = new();
        new GhosttySnapshotHyperlink(explicitLength != 0, identity, id, uri).WriteTo(output);
        return output.ToArray();
    }
}
