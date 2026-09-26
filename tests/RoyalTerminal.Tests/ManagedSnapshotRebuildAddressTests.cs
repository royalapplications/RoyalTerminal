// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Page replacement compacts logical rows even when rotation/pruning left old
// physical slots out of order. In-flight edits must resolve their address again
// after every growth/rehash; COW readers keep the original slot/allocator pair.
public sealed class ManagedSnapshotRebuildAddressTests
{
    private static GhosttySnapshotStyle Bold => new(default, default, default, 1);
    private static GhosttySnapshotPageCapacity EmptyCapacity => new(8, 6, 0, 0, 0, 0);

    [Theory]
    [InlineData("style", 0)]
    [InlineData("style", 1)]
    [InlineData("style", 2)]
    [InlineData("grapheme", 0)]
    [InlineData("grapheme", 1)]
    [InlineData("grapheme", 2)]
    [InlineData("hyperlink", 0)]
    [InlineData("hyperlink", 1)]
    [InlineData("hyperlink", 2)]
    public void LiveEditRetriesAtTheRebasedCellAndLeavesCowReadersUntouched(string dimension, int target)
    {
        Fixture fixture = new();
        GhosttySnapshotPageTracker retained = fixture.Tracker.Copy();
        TerminalRowBuffer held = CopyRows(fixture.Rows);
        TerminalRow row = fixture.Rows[target];
        GhosttySnapshotPageAllocation old = row.SnapshotAllocation!;

        using (GhosttySnapshotPageTracker.RowEdit edit = fixture.Tracker.EditRow(fixture.Rows, row, fixture.Layout, fixture.Screen))
        {
            switch (dimension)
            {
                case "style": edit.Write(1, Bold); break;
                case "grapheme": Assert.True(edit.TryAppendGrapheme(1)); break;
                default: edit.WriteHyperlink(1, fixture.Link); break;
            }
            Mutate(row, dimension, fixture.Link);
        }

        Assert.NotSame(old, row.SnapshotAllocation);
        Assert.False(row.SnapshotAllocation!.MetadataOverflow);
        AssertSlots(fixture.Rows, 0, 1, 2);
        AssertSlots(held, 5, 1, 3);
        Assert.True(fixture.Tracker.TryGetStyleUsage(row.SnapshotAllocation, fixture.List(), out _));
        GhosttySnapshotPageStorage storage = fixture.Storage();
        AssertSingleCell(storage, dimension, target * 8 + 1);
        GhosttySnapshotPageStorage published = retained.ReflowSources(held, fixture.Layout, fixture.Screen)[old];
        Assert.Equal(0, published.Styles.CellCount);
        Assert.Equal(0, published.Graphemes.Count);
        Assert.Equal(0, published.Hyperlinks.CellCount);
        Assert.Equal(CellAttributes.None, held[target].ReadOnlyCells[1].Attributes);
        Assert.Null(held[target].ReadOnlyCells[1].Grapheme);
        Assert.Equal(0, held[target].ReadOnlyCells[1].HyperlinkId);
    }

    [Theory]
    [InlineData("style", 0)]
    [InlineData("style", 1)]
    [InlineData("style", 2)]
    [InlineData("grapheme", 0)]
    [InlineData("grapheme", 1)]
    [InlineData("grapheme", 2)]
    [InlineData("hyperlink", 0)]
    [InlineData("hyperlink", 1)]
    [InlineData("hyperlink", 2)]
    public void BulkReconciliationRefreshesTheCurrentAndFollowingColumnAddresses(string dimension, int target)
    {
        Fixture fixture = new();
        TerminalRow row = fixture.Rows[target];
        Mutate(row, dimension, fixture.Link);
        // A later column in the same row must not reuse the pre-growth slot.
        row[2] = row.ReadOnlyCells[1];

        using (fixture.Tracker.EditRow(fixture.Rows, row, fixture.Layout, fixture.Screen)) { }

        AssertSlots(fixture.Rows, 0, 1, 2);
        Assert.True(fixture.Tracker.TryGetStyleUsage(row.SnapshotAllocation!, fixture.List(), out _));
        GhosttySnapshotPageStorage storage = fixture.Storage();
        int first = target * 8 + 1, second = first + 1;
        switch (dimension)
        {
            case "style":
                Assert.Equal(2, storage.Styles.CellCount);
                Assert.Equal(Bold, storage.Styles.CellStyle(first));
                Assert.Equal(Bold, storage.Styles.CellStyle(second));
                break;
            case "grapheme":
                Assert.Equal(2, storage.Graphemes.Count);
                Assert.Equal(1, storage.Graphemes.SuffixLength(first));
                Assert.Equal(1, storage.Graphemes.SuffixLength(second));
                break;
            default:
                Assert.Equal(2, storage.Hyperlinks.CellCount);
                Assert.NotEqual(0, storage.Hyperlinks.CellId(first));
                Assert.Equal(storage.Hyperlinks.CellId(first), storage.Hyperlinks.CellId(second));
                break;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RebuildReclaimsThePhysicalTailAndPreservesItsRevisionCoverage(bool checkpoint)
    {
        Fixture fixture = new();
        TerminalRow row = fixture.Rows[0];
        if (checkpoint)
        {
            GhosttySnapshotPageAllocation replacement = fixture.Tracker.AllocationReplaced(row.SnapshotAllocation!,
                new(EmptyCapacity with { GraphemeBytes = 1024 }), fixture.List());
            for (int i = 0; i < fixture.Rows.Count; i++) fixture.Rows[i].SnapshotAllocation = replacement;
            Assert.False(fixture.Tracker.TryGetStyleUsage(replacement, fixture.List(), out _));
            using (fixture.Tracker.EditRow(fixture.Rows, row, fixture.Layout, fixture.Screen)) { }
        }
        else
        {
            using GhosttySnapshotPageTracker.RowEdit edit = fixture.Tracker.EditRow(fixture.Rows, row, fixture.Layout, fixture.Screen);
            Assert.True(edit.TryAppendGrapheme(1));
            Mutate(row, "grapheme", fixture.Link);
        }
        AssertSlots(fixture.Rows, 0, 1, 2);
        TerminalRow tail = new(8);
        fixture.Rows.Add(tail);

        Assert.True(fixture.Tracker.AssignTailRow(fixture.Rows, 3, fixture.Layout));

        Assert.Same(row.SnapshotAllocation, tail.SnapshotAllocation);
        Assert.Equal(3, tail.SnapshotAllocationRow); // Old physical slot 5 must not force a new page.
        using (fixture.Tracker.EditRow(fixture.Rows, tail, fixture.Layout, fixture.Screen)) { }
        Assert.True(fixture.Tracker.TryGetStyleUsage(row.SnapshotAllocation!, fixture.List(), out _));
    }

    [Fact]
    public void CursorMapGrowthRetriesTheWriteAfterAllRowsHaveMoved()
    {
        GhosttySnapshotAllocation layout = new(4096);
        GhosttySnapshotPageCapacity capacity = new(64, 4, 0, 192, 0, 2048);
        GhosttySnapshotPageAllocation page = new(capacity);
        GhosttySnapshotPageStorage storage = new(capacity);
        GhosttySnapshotPageTracker tracker = new();
        TerminalScreen screen = new(64, 3);
        int token = screen.RegisterHyperlink("u"u8, default, 7);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.Hyperlinks.StartCursor(screen.SnapshotHyperlinkEncoding(token)!));
        TerminalRowBuffer rows = new();
        foreach (int slot in new[] { 2, 0, 3 }) rows.Add(new(64) { SnapshotAllocation = page, SnapshotAllocationRow = slot });
        for (int i = 0; i < 102; i++)
        {
            TerminalRow row = rows[i / 64];
            int column = i % 64;
            row[column].Codepoint = 'A';
            row[column].HyperlinkId = token;
            Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success,
                storage.Hyperlinks.WriteCursorToCell(row.SnapshotAllocationRow * 64 + column));
        }
        tracker.InstallReflowPage(page, storage, [rows[0], rows[1], rows[2]]);
        GhosttySnapshotPageTracker retained = tracker.Copy();
        TerminalRowBuffer held = CopyRows(rows);

        using (GhosttySnapshotPageTracker.RowEdit edit = tracker.EditRow(rows, rows[2], layout, screen))
        {
            Assert.Equal(token, edit.WriteCursorHyperlink(0, token));
            rows[2][0].HyperlinkId = token;
        }

        AssertSlots(rows, 0, 1, 2);
        GhosttySnapshotPageStorage current = tracker.ReflowSources(rows, layout, screen)[rows[0].SnapshotAllocation!];
        Assert.Equal(103, current.Hyperlinks.CellCount);
        Assert.Equal(current.Hyperlinks.CursorId, current.Hyperlinks.CellId(128));
        Assert.NotEqual(0, current.Hyperlinks.CellId(128));
        Assert.Equal(0, current.Hyperlinks.CellId(192));
        GhosttySnapshotPageStorage published = retained.ReflowSources(held, layout, screen)[page];
        Assert.Equal(102, published.Hyperlinks.CellCount);
        Assert.Equal(32UL, published.Hyperlinks.StringBytes); // URI scratch belongs only to the mutating owner.
        AssertSlots(held, 2, 0, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckpointReplacementRebasesUnknownOrTrackedRowsWithoutCopyingCellArrays(bool tracked)
    {
        Fixture fixture = new();
        GhosttySnapshotPageTracker tracker = tracked ? fixture.Tracker : new();
        TerminalRow row = fixture.Rows[0];
        TerminalRow held = row.CreateStateCopy();
        GhosttySnapshotPageAllocation replacement = tracker.AllocationReplaced(row.SnapshotAllocation!,
            new(EmptyCapacity with { Columns = 10 }), fixture.List());
        for (int i = 0; i < fixture.Rows.Count; i++) fixture.Rows[i].SnapshotAllocation = replacement;

        AssertSlots(fixture.Rows, 0, 1, 2);
        Assert.Equal(5, held.SnapshotAllocationRow);
        Assert.Same(held.SearchStorageIdentity, row.SearchStorageIdentity);
        using (tracker.EditRow(fixture.Rows, row, fixture.Layout, fixture.Screen)) { }
        Assert.True(tracker.TryGetStyleUsage(replacement, fixture.List(), out _));
        Assert.False(replacement.MetadataOverflow);
    }

    [Fact]
    public void RejectedCheckpointCloneKeepsItsOriginalSlotMapping()
    {
        Fixture fixture = new(EmptyCapacity with { Styles = 16 });
        using (GhosttySnapshotPageTracker.RowEdit edit = fixture.Tracker.EditRow(fixture.Rows, fixture.Rows[0], fixture.Layout, fixture.Screen))
        {
            edit.Write(1, Bold);
            Mutate(fixture.Rows[0], "style", fixture.Link);
        }
        GhosttySnapshotPageAllocation old = fixture.Rows[0].SnapshotAllocation!;

        GhosttySnapshotPageAllocation rejected = fixture.Tracker.AllocationReplaced(old, new(EmptyCapacity), fixture.List());

        Assert.True(rejected.MetadataOverflow);
        AssertSlots(fixture.Rows, 5, 1, 3);
        Assert.Equal(Bold, rejected.CopyRestoredStyles().CellStyle(5 * 8 + 1));
        Assert.Equal(default, rejected.CopyRestoredStyles().CellStyle(1));
    }

    private static void Mutate(TerminalRow row, string dimension, int link)
    {
        row[1].Codepoint = 'A';
        switch (dimension)
        {
            case "style": row[1].Attributes = CellAttributes.Bold; break;
            case "grapheme": row[1].Grapheme = "A\u0301"; break;
            default: row[1].HyperlinkId = link; break;
        }
    }

    private static void AssertSingleCell(GhosttySnapshotPageStorage storage, string dimension, int cell)
    {
        Assert.Equal(dimension == "style" ? 1 : 0, storage.Styles.CellCount);
        Assert.Equal(dimension == "grapheme" ? 1 : 0, storage.Graphemes.Count);
        Assert.Equal(dimension == "hyperlink" ? 1 : 0, storage.Hyperlinks.CellCount);
        if (dimension == "style") Assert.Equal(Bold, storage.Styles.CellStyle(cell));
        if (dimension == "grapheme") Assert.Equal(1, storage.Graphemes.SuffixLength(cell));
        if (dimension == "hyperlink") Assert.NotEqual(0, storage.Hyperlinks.CellId(cell));
    }

    private static void AssertSlots(TerminalRowBuffer rows, params int[] slots)
    {
        Assert.Equal(slots.Length, rows.Count);
        for (int i = 0; i < slots.Length; i++) Assert.Equal(slots[i], rows[i].SnapshotAllocationRow);
    }

    private static TerminalRowBuffer CopyRows(TerminalRowBuffer rows)
    {
        TerminalRowBuffer copy = new(rows.Count);
        for (int i = 0; i < rows.Count; i++) copy.Add(rows[i].CreateStateCopy());
        return copy;
    }

    private sealed class Fixture
    {
        internal GhosttySnapshotPageTracker Tracker { get; } = new();
        internal GhosttySnapshotAllocation Layout { get; } = new(4096);
        internal TerminalScreen Screen { get; } = new(8, 3);
        internal TerminalRowBuffer Rows { get; } = new();
        internal int Link { get; }

        internal Fixture(GhosttySnapshotPageCapacity? capacity = null)
        {
            GhosttySnapshotPageCapacity size = capacity ?? EmptyCapacity;
            GhosttySnapshotPageAllocation page = new(size);
            foreach (int slot in new[] { 5, 1, 3 }) Rows.Add(new(8) { SnapshotAllocation = page, SnapshotAllocationRow = slot });
            Tracker.InstallReflowPage(page, new(size), List());
            Link = Screen.RegisterHyperlink("u"u8, default, 9);
        }

        internal List<TerminalRow> List()
        {
            List<TerminalRow> rows = new(Rows.Count);
            for (int i = 0; i < Rows.Count; i++) rows.Add(Rows[i]);
            return rows;
        }

        internal GhosttySnapshotPageStorage Storage() => Tracker.ReflowSources(Rows, Layout, Screen)[Rows[0].SnapshotAllocation!];
    }
}
