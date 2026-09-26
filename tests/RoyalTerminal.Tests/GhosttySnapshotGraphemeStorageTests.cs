// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Page.appendGrapheme / setGraphemes / clearGrapheme define the allocation
// lifecycle. WT ROW stores text in a row buffer; xterm.js uses combined strings.
// Those visible-text implementations are not oracles for native PAGE pressure.
public sealed class GhosttySnapshotGraphemeStorageTests
{
    [Fact]
    public void AppendRetainsItsOldSliceUntilAReplacementCanBeAllocated()
    {
        GhosttySnapshotGraphemeStorage storage = new(1024);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Set(0, 4));
        for (int i = 1; i <= 3; i++) Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Set(i, 64));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Set(4, 56));
        Assert.Equal(1008UL, storage.AllocatedBytes);
        GhosttySnapshotBitmap.Slice original = Slice(storage, 0);

        Assert.Equal(GhosttySnapshotGraphemeAddResult.AllocatorFull, storage.Append(0));
        Assert.Equal(4, storage.SuffixLength(0));
        Assert.Equal(original, Slice(storage, 0));
        Assert.Equal(1008UL, storage.AllocatedBytes);

        storage.Clear(1);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Append(0));
        Assert.Equal(5, storage.SuffixLength(0));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(1, 2), Slice(storage, 0));
        Assert.Equal(768UL, storage.AllocatedBytes);
        Assert.Equal(4, storage.Count);
    }

    [Fact]
    public void MapFailureFreesItsTemporaryBitmapAllocation()
    {
        GhosttySnapshotGraphemeStorage storage = new(1); // One cell, rounded-up bitmap word.
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Set(0, 4));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.MapFull, storage.Set(1, 8));
        Assert.Equal(1, storage.Count);
        Assert.Equal(16UL, storage.AllocatedBytes);
        Assert.False(storage.TryGetAllocation(1, out _));
        storage.Clear(0);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Set(1, 64));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(0, 16), Slice(storage, 1));
    }

    [Fact]
    public void InChunkAppendsAndBoundedSuffixesDoNotReplaceSlices()
    {
        GhosttySnapshotGraphemeStorage storage = new(1024);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Append(3));
        GhosttySnapshotBitmap.Slice first = Slice(storage, 3);
        for (int length = 2; length <= 4; length++)
        {
            Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Append(3));
            Assert.Equal(length, storage.SuffixLength(3));
            Assert.Equal(first, Slice(storage, 3));
        }
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.AppendToLength(3, 64));
        GhosttySnapshotBitmap.Slice full = Slice(storage, 3);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Append(3));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.AppendToLength(3, int.MaxValue));
        Assert.Equal(64, storage.SuffixLength(3));
        Assert.Equal(full, Slice(storage, 3));
        Assert.Equal(256UL, storage.AllocatedBytes);
    }

    [Fact]
    public void CopyPreservesFragmentationAndSearchStateWithIndependentOwnership()
    {
        GhosttySnapshotGraphemeStorage source = new(2048);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Set(0, 64));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Set(1, 64));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Set(2, 64));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Set(3, 48));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Set(4, 8));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Set(5, 8));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Set(6, 8));
        source.Clear(5); // Two free chunks on either side of the word boundary.
        GhosttySnapshotGraphemeStorage copy = source.Copy();
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Set(7, 16));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, copy.Set(7, 16));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(66, 4), Slice(copy, 7));
        Assert.Equal(Slice(source, 7), Slice(copy, 7));
        copy.Clear(0);
        Assert.Equal(64, source.SuffixLength(0));
        Assert.Equal(0, copy.SuffixLength(0));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, copy.Set(8, 64));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(0, 16), Slice(copy, 8));
        Assert.Equal(0, source.SuffixLength(8));
    }

    [Fact]
    public void CloneAllocatesOnceWhileAppendNeedsReplacementScratch()
    {
        GhosttySnapshotGraphemeStorage storage = new(1024);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.AppendToLength(0, 64));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.AppendToLength(1, 64));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.AppendToLength(2, 8));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.AllocatorFull, storage.AppendToLength(3, 61));
        // Earlier append/replacement cycles fragmented the first-fit bitmap.
        // Total free bytes are insufficient evidence of a contiguous slice.
        Assert.Equal(36, storage.SuffixLength(3));
        storage.Clear(3);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Set(3, 61));
        Assert.Equal(61, storage.SuffixLength(3));
        Assert.Equal(800UL, storage.AllocatedBytes);
    }

    [Fact]
    public void SamePageMovesOnlyTransferSliceOwnership()
    {
        GhosttySnapshotGraphemeStorage storage = new(1024);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Set(0, 5));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Set(1, 9));
        GhosttySnapshotBitmap.Slice left = Slice(storage, 0), right = Slice(storage, 1);
        storage.Swap(0, 1);
        Assert.Equal(right, Slice(storage, 0));
        Assert.Equal(left, Slice(storage, 1));
        storage.Swap(0, 9);
        Assert.Equal(right, Slice(storage, 9));
        Assert.False(storage.TryGetAllocation(0, out _));
        Assert.Equal(80UL, storage.AllocatedBytes);
        storage.ClearCells(0, 4);
        Assert.Equal(1, storage.Count);
        storage.RetainRows(new HashSet<int> { 0 }, 4);
        Assert.Equal(0, storage.Count);
        Assert.Equal(0UL, storage.AllocatedBytes);
    }

    [Fact]
    public void RebuildRepacksInPhysicalCellOrderWithoutModifyingTheSource()
    {
        GhosttySnapshotGraphemeStorage source = new(1024);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.AppendToLength(9, 8));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.AppendToLength(0, 5));
        GhosttySnapshotBitmap.Slice old = Slice(source, 9);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Rebuild(1024, out GhosttySnapshotGraphemeStorage? rebuilt));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(0, 2), Slice(rebuilt!, 0));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(2, 2), Slice(rebuilt!, 9));
        Assert.Equal(old, Slice(source, 9));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.MapFull, source.Rebuild(1, out GhosttySnapshotGraphemeStorage? failed));
        Assert.Null(failed);
        Assert.Equal(2, source.Count);
        Assert.Equal(64UL, source.AllocatedBytes);
    }

    [Fact]
    public void CrossPageCloneOwnsItsSliceAndRequiresAnEmptyDestination()
    {
        GhosttySnapshotGraphemeStorage source = new(1024), destination = new(1024);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.AppendToLength(3, 5));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, destination.CopyCellFrom(7, source, 3));
        Assert.Equal(5, destination.SuffixLength(7));
        source.Clear(3);
        Assert.Equal(5, destination.SuffixLength(7));
        Assert.Throws<InvalidOperationException>(() => destination.CopyCellFrom(7, source, 3));
        destination.Clear(7);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, destination.CopyCellFrom(7, source, 3));
        Assert.Equal(0, destination.Count);
    }

    [Fact]
    public void HugeHintsOnlyMaterializeUsedBitmapWordsAndMapEntries()
    {
        _ = new GhosttySnapshotGraphemeStorage(uint.MaxValue);
        long start = GC.GetAllocatedBytesForCurrentThread();
        GhosttySnapshotGraphemeStorage storage = new(uint.MaxValue);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.AppendToLength(0, 64));
        GhosttySnapshotGraphemeStorage copy = storage.Copy();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(64, copy.SuffixLength(0));
        Assert.InRange(allocated, 1, 64 * 1024);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(1U)]
    [InlineData(17U)]
    [InlineData(1024U)]
    [InlineData(1025U)]
    public void BatchedRestoreVisitsTheSameAllocationBoundariesAsScalarAppends(uint capacity)
    {
        GhosttySnapshotGraphemeStorage scalar = new(capacity), batched = new(capacity);
        Random random = new(0x47524150);
        for (int operation = 0; operation < 200; operation++)
        {
            int cell = random.Next(8);
            scalar.Clear(cell); batched.Clear(cell);
            int length = random.Next(1, 81);
            GhosttySnapshotGraphemeAddResult expected = GhosttySnapshotGraphemeAddResult.Success;
            for (int index = 0; index < length; index++)
            {
                expected = scalar.Append(cell);
                if (expected != GhosttySnapshotGraphemeAddResult.Success) break;
            }
            Assert.Equal(expected, batched.AppendToLength(cell, length));
            Assert.Equal(scalar.AllocatedBytes, batched.AllocatedBytes);
            Assert.Equal(scalar.Count, batched.Count);
            for (int index = 0; index < 8; index++)
            {
                Assert.Equal(scalar.SuffixLength(index), batched.SuffixLength(index));
                Assert.Equal(scalar.TryGetAllocation(index, out GhosttySnapshotBitmap.Slice a),
                    batched.TryGetAllocation(index, out GhosttySnapshotBitmap.Slice b));
                Assert.Equal(a, b);
            }
            if (expected != GhosttySnapshotGraphemeAddResult.Success)
            {
                scalar.Clear(cell); batched.Clear(cell);
            }
        }
    }

    private static GhosttySnapshotBitmap.Slice Slice(GhosttySnapshotGraphemeStorage storage, int cell)
    {
        Assert.True(storage.TryGetAllocation(cell, out GhosttySnapshotBitmap.Slice slice));
        return slice;
    }
}
