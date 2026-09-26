// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Page.cloneFrom visits logical rows and gives the replacement fresh,
// sequential cell blocks. WT ROW and xterm.js BufferLine have no per-PAGE
// metadata allocator contract. Keep the host's preserved hidden columns, but
// follow Ghostty's copy order, ownership and one-attempt cursor restoration.
public sealed class GhosttySnapshotPageRemapTests
{
    private static GhosttySnapshotPageCapacity Capacity => new(8, 8, 64, 1024, 2048, 4096);

    [Theory]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    public void LogicalRowsRepackAllAllocatorsAndPreserveSourceOwnership(int columns, bool restoreCursor)
    {
        GhosttySnapshotPageStorage source = Source();
        TerminalRow[] rows = Rows();
        GhosttySnapshotPageCapacity capacity = Capacity with { Columns = (ushort)columns };
        GhosttySnapshotPageRemap remap = new(8, columns, rows);
        GhosttySnapshotPageStorage expected = new(capacity);
        // Independent row/cell traversal is also Page.clonePartialRowFrom's
        // grapheme, hyperlink, style ordering; no rebuild helper is the oracle.
        for (int row = 0; row < rows.Length; row++)
        for (int column = 0; column < Math.Min(columns, rows[row].PreservedColumns); column++)
        {
            int old = rows[row].SnapshotAllocationRow * 8 + column, target = row * columns + column;
            Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, expected.Graphemes.CopyCellFrom(target, source.Graphemes, old));
            Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, expected.Hyperlinks.CopyCellFrom(target, source.Hyperlinks, old));
            Assert.Equal(GhosttySnapshotSetAddResult.Success, expected.Styles.CopyCellFrom(target, source.Styles, old));
        }
        if (restoreCursor)
        {
            Assert.Equal(GhosttySnapshotSetAddResult.Success, expected.Styles.ChangeCursor(source.Styles.Cursor));
            Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.Hyperlinks.CopyCursorTo(expected.Hyperlinks));
        }

        Assert.True(source.Rebuild(capacity, restoreCursor, out GhosttySnapshotPageStorage? rebuilt, remap));

        AssertStorage(expected, rebuilt!, columns * rows.Length);
        Assert.Equal(new GhosttySnapshotBitmap.Slice(0, 3), Slice(rebuilt!.Graphemes, 0));
        Assert.Equal(1, rebuilt.Styles.CellId(0));
        Assert.Equal(1, rebuilt.Hyperlinks.CellId(0));
        Assert.Equal(new[] { 2, 0, 4 }, rows.Select(row => row.SnapshotAllocationRow));
        Assert.Equal(6, source.Styles.CellCount); // Retired row and clipped cells still belong to source.
        Assert.Equal(6, source.Graphemes.Count);
        Assert.Equal(6, source.Hyperlinks.CellCount);
        Assert.Equal(9, source.Graphemes.SuffixLength(16));
        rebuilt.Graphemes.Clear(0);
        rebuilt.Styles.ClearCell(0);
        rebuilt.Hyperlinks.Clear(0);
        Assert.Equal(9, source.Graphemes.SuffixLength(16));
        Assert.NotEqual(0, source.Styles.CellId(16));
        Assert.NotEqual(0, source.Hyperlinks.CellId(16));
    }

    [Theory]
    [InlineData("style")]
    [InlineData("grapheme")]
    [InlineData("hyperlink")]
    [InlineData("strings")]
    public void FailedMappedCloneDoesNotPublishAnAddressTranslation(string dimension)
    {
        GhosttySnapshotPageStorage source = Source(), retained = source.Copy();
        TerminalRow[] rows = Rows();
        GhosttySnapshotPageCapacity capacity = dimension switch
        {
            "style" => Capacity with { Styles = 0 },
            "grapheme" => Capacity with { GraphemeBytes = 0 },
            "hyperlink" => Capacity with { HyperlinkBytes = 0 },
            _ => Capacity with { StringBytes = 0 },
        };

        Assert.False(source.Rebuild(capacity, true, out GhosttySnapshotPageStorage? rebuilt, new(8, 8, rows)));

        Assert.Null(rebuilt);
        AssertStorage(retained, source, 40);
        Assert.Equal(new[] { 2, 0, 4 }, rows.Select(row => row.SnapshotAllocationRow));
    }

    [Fact]
    public void StyleRemappingPreservesInlineBackgroundObservationsAcrossChunkBoundaries()
    {
        GhosttySnapshotStyleStorage source = new(16);
        GhosttySnapshotStyle bold = new(default, default, default, 1);
        GhosttySnapshotColor background = new(1, 3, 0, 0);
        foreach (int column in new[] { 0, 1, 255, 256 })
        {
            Assert.Equal(GhosttySnapshotSetAddResult.Success, source.ChangeCell(257 + column, bold));
            source.ObserveInlineBackground(257 + column, background);
        }
        TerminalRow[] rows = [new(257) { SnapshotAllocationRow = 1 }, new(257) { SnapshotAllocationRow = 0 }];

        Assert.Equal(GhosttySnapshotSetAddResult.Success, source.Rebuild(16, out GhosttySnapshotStyleStorage? rebuilt, new(257, 260, rows)));

        foreach (int column in new[] { 0, 1, 255, 256 })
        {
            Assert.Equal(bold, rebuilt!.CellStyle(column));
            Assert.Equal(GhosttySnapshotSetAddResult.Success, rebuilt.ObserveCell(column, bold with { Background = background }, empty: true));
            Assert.Equal(bold, rebuilt.CellStyle(column)); // Observation did not invent an allocated background style.
            Assert.Equal(bold, source.CellStyle(257 + column));
        }
        Assert.Equal(4, rebuilt!.CellCount);
        Assert.Equal(1, rebuilt.Count);
    }

    [Fact]
    public void HugePageHintsRemapOnlyRetainedRowsAndOccupiedMetadata()
    {
        GhosttySnapshotPageStorage source = new(Capacity);
        int old = 5000 * ushort.MaxValue + 3;
        Assert.Equal(GhosttySnapshotSetAddResult.Success, source.Styles.ChangeCell(old, new(default, default, default, 1)));
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Graphemes.Set(old, 8));
        TerminalRow[] rows = [new(8) { SnapshotAllocationRow = 5000 }, new(8) { SnapshotAllocationRow = 2 }];
        GhosttySnapshotPageCapacity capacity = Capacity with { Columns = ushort.MaxValue, Rows = 6000 };
        Assert.True(source.Rebuild(capacity, false, out _, new(ushort.MaxValue, ushort.MaxValue, rows)));
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(source.Rebuild(capacity, false, out GhosttySnapshotPageStorage? rebuilt,
            new(ushort.MaxValue, ushort.MaxValue, rows)));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 1, 128 * 1024);
        Assert.Equal(1, rebuilt!.Styles.CellCount);
        Assert.Equal(source.Styles.CellStyle(old), rebuilt.Styles.CellStyle(3));
        Assert.Equal(8, rebuilt.Graphemes.SuffixLength(3));
        Assert.Equal(0, rebuilt.Styles.CellId(old));
    }

    private static TerminalRow[] Rows() =>
        [new(6) { SnapshotAllocationRow = 2 }, new(4) { SnapshotAllocationRow = 0 }, new(6) { SnapshotAllocationRow = 4 }];

    private static GhosttySnapshotPageStorage Source()
    {
        GhosttySnapshotPageStorage source = new(Capacity);
        int[] cells = [0, 8, 16, 20, 22, 32];
        for (int i = 0; i < cells.Length; i++)
        {
            GhosttySnapshotStyle style = new(new(1, (byte)(i + 1), 0, 0), default, default, 1);
            Assert.Equal(GhosttySnapshotSetAddResult.Success, source.Styles.ChangeCell(cells[i], style));
            Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, source.Graphemes.Set(cells[i], i * 4 + 1));
            TerminalHyperlink link = new(Encoding.UTF8.GetBytes(new string('u', i * 32 + 1)), "id"u8, 0);
            Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.Hyperlinks.ObserveCell(cells[i], link.SnapshotEncoding));
        }
        Assert.Equal(GhosttySnapshotSetAddResult.Success, source.Styles.ChangeCursor(new(default, default, default, 2)));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success,
            source.Hyperlinks.StartCursor(new TerminalHyperlink("cursor"u8, default, 1).SnapshotEncoding));
        return source;
    }

    private static GhosttySnapshotBitmap.Slice Slice(GhosttySnapshotGraphemeStorage storage, int index)
    {
        Assert.True(storage.TryGetAllocation(index, out GhosttySnapshotBitmap.Slice slice));
        return slice;
    }

    private static void AssertStorage(GhosttySnapshotPageStorage expected, GhosttySnapshotPageStorage actual, int length)
    {
        Assert.Equal(expected.Styles.CellCount, actual.Styles.CellCount);
        Assert.Equal(expected.Styles.Count, actual.Styles.Count);
        Assert.Equal(expected.Styles.Cursor, actual.Styles.Cursor);
        Assert.Equal(expected.Graphemes.Count, actual.Graphemes.Count);
        Assert.Equal(expected.Graphemes.AllocatedBytes, actual.Graphemes.AllocatedBytes);
        Assert.Equal(expected.Hyperlinks.CellCount, actual.Hyperlinks.CellCount);
        Assert.Equal(expected.Hyperlinks.Count, actual.Hyperlinks.Count);
        Assert.Equal(expected.Hyperlinks.StringBytes, actual.Hyperlinks.StringBytes);
        Assert.Equal(expected.Hyperlinks.CursorId, actual.Hyperlinks.CursorId);
        Assert.True(expected.Hyperlinks.CursorEncoding.SequenceEqual(actual.Hyperlinks.CursorEncoding));
        for (int index = 0; index < length; index++)
        {
            Assert.Equal(expected.Styles.CellId(index), actual.Styles.CellId(index));
            Assert.Equal(expected.Styles.CellStyle(index), actual.Styles.CellStyle(index));
            Assert.Equal(expected.Graphemes.SuffixLength(index), actual.Graphemes.SuffixLength(index));
            Assert.Equal(expected.Graphemes.TryGetAllocation(index, out GhosttySnapshotBitmap.Slice a),
                actual.Graphemes.TryGetAllocation(index, out GhosttySnapshotBitmap.Slice b));
            Assert.Equal(a, b);
            int id = expected.Hyperlinks.CellId(index);
            Assert.Equal(id, actual.Hyperlinks.CellId(index));
            if (id == 0) continue;
            Assert.Equal(expected.Hyperlinks.ReferenceCount(id), actual.Hyperlinks.ReferenceCount(id));
            Assert.True(expected.Hyperlinks.TryGetAllocation(id, out GhosttySnapshotBitmap.Slice aId, out GhosttySnapshotBitmap.Slice aUri));
            Assert.True(actual.Hyperlinks.TryGetAllocation(id, out GhosttySnapshotBitmap.Slice bId, out GhosttySnapshotBitmap.Slice bUri));
            Assert.Equal((aId, aUri), (bId, bUri));
        }
    }
}
