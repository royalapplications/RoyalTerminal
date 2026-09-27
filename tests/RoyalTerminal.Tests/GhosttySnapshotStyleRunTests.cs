// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty's reflow copyRun uses one style mapping and a grouped reference
// increment. Page.clonePartialRowFrom's scalar preferred-ID insertion is the
// independent oracle here; WT ROW and xterm.js BufferLine have no PAGE IDs.
public sealed class GhosttySnapshotStyleRunTests
{
    private static GhosttySnapshotStyle Bold => new(default, default, default, 1);
    private static GhosttySnapshotStyle Italic => new(default, default, default, 2);
    private static GhosttySnapshotStyle Dim => new(default, default, default, 4);

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 255, false)]
    [InlineData(250, 511, false)]
    [InlineData(511, 3, false)]
    [InlineData(0, 0, true)]
    [InlineData(1, 255, true)]
    [InlineData(250, 511, true)]
    [InlineData(511, 3, true)]
    public void ChunkRunsMatchScalarIdsReferencesAndInlineObservations(int start, int target, bool alternating)
    {
        const int count = 1201;
        GhosttySnapshotStyleStorage source = new(8), expected = new(8), actual = new(8);
        source.ChangeCursor(Dim); // Cursor-only ID must not be copied.
        for (int i = start + count; i >= Math.Max(0, start - 1); i--)
        {
            if (i is >= 768 and < 1024) continue; // Entire default chunk.
            Assert.Equal(GhosttySnapshotSetAddResult.Success,
                source.ChangeCell(i, (alternating ? i % 2 : i / 73 % 2) == 0 ? Bold : Italic));
        }
        int observed = start + 7;
        GhosttySnapshotStyle native = source.CellStyle(observed);
        GhosttySnapshotColor background = new(1, 5, 0, 0);
        source.ObserveInlineBackground(observed, background);
        int originalCount = source.CellCount;
        for (int i = 0; i < count; i++)
            Assert.Equal(GhosttySnapshotSetAddResult.Success, expected.CopyCellFrom(target + i, source, start + i));
        GhosttySnapshotStyleStorage.CopyCache cache = default;

        Assert.Equal(GhosttySnapshotSetAddResult.Success,
            actual.CopyCellsFrom(target, source, start, count, ref cache, out int copied));

        Assert.Equal(count, copied);
        Assert.Equal(expected.CellCount, actual.CellCount);
        Assert.Equal(default, actual.Cursor);
        Assert.Equal(default, actual.CellStyle(target + count));
        if (target > 0) Assert.Equal(default, actual.CellStyle(target - 1));
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(expected.CellId(target + i), actual.CellId(target + i));
            Assert.Equal(expected.CellStyle(target + i), actual.CellStyle(target + i));
        }
        Assert.Equal(GhosttySnapshotSetAddResult.Success,
            actual.ObserveCell(target + 7, native with { Background = background }, empty: true));
        Assert.Equal(native, actual.CellStyle(target + 7));
        for (int i = 0; i < count; i++)
        {
            expected.ClearCell(target + i); actual.ClearCell(target + i);
            Assert.Equal(expected.Count, actual.Count);
            Assert.Equal(expected.CellCount, actual.CellCount);
        }
        Assert.Equal(0, actual.Count);
        Assert.Equal(originalCount, source.CellCount);
        Assert.Equal(Dim, source.Cursor);
    }

    [Theory]
    [InlineData(260)]
    [InlineData(520)]
    [InlineData(600)]
    public void RebuildGroupsRunsWithoutChangingLogicalRemapOrder(int columns)
    {
        GhosttySnapshotStyleStorage source = new(8), expected = new(8);
        TerminalRow[] rows = [new(520) { SnapshotAllocationRow = 2 }, new(520) { SnapshotAllocationRow = 0 }];
        source.ChangeCursor(Dim);
        for (int i = 1559; i >= 0; i--)
            if (i % 151 != 0) source.ChangeCell(i, i % 137 < 80 ? Bold : Italic);
        source.ObserveInlineBackground(1043, new(1, 7, 0, 0));
        GhosttySnapshotStyle observed = source.CellStyle(1043);
        for (int row = 0; row < rows.Length; row++)
        for (int column = 0; column < Math.Min(columns, 520); column++)
            Assert.Equal(GhosttySnapshotSetAddResult.Success,
                expected.CopyCellFrom(row * columns + column, source, rows[row].SnapshotAllocationRow * 520 + column));

        Assert.Equal(GhosttySnapshotSetAddResult.Success,
            source.Rebuild(8, out GhosttySnapshotStyleStorage? actual, new(520, columns, rows)));

        Assert.Equal(default, actual!.Cursor);
        Assert.Equal(expected.CellCount, actual.CellCount);
        for (int cell = 0; cell < columns * rows.Length; cell++)
        {
            Assert.Equal(expected.CellId(cell), actual.CellId(cell));
            Assert.Equal(expected.CellStyle(cell), actual.CellStyle(cell));
        }
        Assert.Equal(GhosttySnapshotSetAddResult.Success,
            actual.ObserveCell(3, observed with { Background = new(1, 7, 0, 0) }, empty: true));
        Assert.Equal(observed, actual.CellStyle(3));
        actual.ClearCells(0, columns * rows.Length);
        Assert.Equal(0, actual.Count);
        Assert.Equal(Dim, source.Cursor);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(257)]
    public void SparseRebuildUsesOccupiedChunksAndRecoversAfterRejectedCopies(int chunks)
    {
        GhosttySnapshotStyleStorage source = new(8), expected = new(4);
        // Insert in reverse order with large holes; sorting must use occupied
        // chunk count, not the physical address or declared page dimensions.
        for (int i = chunks - 1; i >= 0; i--) source.ChangeCell(i * 4096 + 255, i % 2 == 0 ? Bold : Italic);
        for (int i = 0; i < chunks; i++)
            Assert.Equal(GhosttySnapshotSetAddResult.Success, expected.CopyCellFrom(i * 4096 + 255, source, i * 4096 + 255));
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory, source.Rebuild(0, out GhosttySnapshotStyleStorage? rejected));
            Assert.Null(rejected);
            Assert.Equal(chunks, source.CellCount);
        }

        Assert.Equal(GhosttySnapshotSetAddResult.Success, source.Rebuild(4, out GhosttySnapshotStyleStorage? actual));

        Assert.Equal(chunks, actual!.CellCount);
        for (int i = 0; i < chunks; i++)
        {
            Assert.Equal(expected.CellId(i * 4096 + 255), actual.CellId(i * 4096 + 255));
            Assert.Equal(expected.CellStyle(i * 4096 + 255), actual.CellStyle(i * 4096 + 255));
            actual.ClearCell(i * 4096 + 255);
        }
        Assert.Equal(0, actual.Count);
        Assert.Equal(chunks, source.CellCount);
    }

    [Fact]
    public void RebuildIncludesTheLastRepresentableCellWithoutOverflowingItsExclusiveEnd()
    {
        GhosttySnapshotStyleStorage source = new(4);
        source.ChangeCell(int.MaxValue, Bold);

        Assert.Equal(GhosttySnapshotSetAddResult.Success, source.Rebuild(4, out GhosttySnapshotStyleStorage? actual));

        Assert.Equal(Bold, actual!.CellStyle(int.MaxValue));
        Assert.Equal(1, actual.CellCount);
        actual.ClearCell(int.MaxValue);
        Assert.Equal(0, actual.Count);
        Assert.Equal(1, source.CellCount);
    }

    [Fact]
    public void FailedRunReportsTheExactPrefixAcrossDefaultAndStyledChunks()
    {
        GhosttySnapshotStyleStorage source = new(8), destination = new(4);
        for (int i = 0; i < 256; i++)
        {
            source.ChangeCell(i, Bold);
            source.ChangeCell(512 + i, Italic);
            source.ChangeCell(768 + i, Dim);
        }
        GhosttySnapshotStyleStorage.CopyCache cache = default;

        Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory,
            destination.CopyCellsFrom(255, source, 0, 1024, ref cache, out int copied));

        Assert.Equal(768, copied);
        Assert.Equal(512, destination.CellCount);
        Assert.Equal(default, destination.CellStyle(255 + copied));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, destination.Rebuild(8, out GhosttySnapshotStyleStorage? grown));
        Assert.Equal(GhosttySnapshotSetAddResult.Success,
            grown!.CopyCellsFrom(255 + copied, source, copied, 1024 - copied, ref cache, out int rest));
        Assert.Equal(256, rest);
        grown.ClearCells(255, 1024);
        Assert.Equal(0, grown.Count);
        Assert.Equal(768, source.CellCount);
    }
}
