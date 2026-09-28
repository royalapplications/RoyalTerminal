// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty PageList.increaseCapacity uses live allocator usage, including cursor
// references and occupied slices. WT/xterm.js don't define this PAGE contract.
public sealed class ManagedSnapshotMetadataUsageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CombinedCensusMatchesAllCountersIncludingUnprintedCursor(bool array)
    {
        Fixture fixture = new();
        IReadOnlyList<TerminalRow> rows = array ? fixture.Rows.ToArray() : fixture.Rows;
        Assert.True(fixture.Tracker.TryGetMetadataUsage(fixture.Page, rows, out GhosttySnapshotMetadataUsage usage));
        Assert.Equal(new GhosttySnapshotMetadataUsage(2, 2, 48, 0, 1, 2, 64), usage);
        AssertIndependentCounters(fixture.Tracker, fixture.Page, rows, usage);
    }

    [Fact]
    public void CensusVisitsEachRowOnceWithoutUsingAnEnumerator()
    {
        Fixture fixture = new();
        IndexedRows rows = new(fixture.Rows);
        Assert.True(fixture.Tracker.TryGetMetadataUsage(fixture.Page, rows, out _));
        Assert.Equal(rows.Count, rows.Reads);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void DirtyOrForeignRowsRejectTheWholeCensusAndReturnZero(int index, bool foreign)
    {
        Fixture fixture = new();
        if (foreign) fixture.Rows[index].SnapshotAllocation = new(fixture.Page.Capacity);
        else fixture.Rows[index][0].Codepoint = 'x';
        Assert.False(fixture.Tracker.TryGetMetadataUsage(fixture.Page, fixture.Rows, out GhosttySnapshotMetadataUsage usage));
        Assert.Equal(default, usage);
        Assert.False(fixture.Tracker.TryGetStyleUsage(fixture.Page, fixture.Rows, out int styles));
        Assert.Equal(0, styles);
        Assert.False(fixture.Tracker.TryGetGraphemeUsage(fixture.Page, fixture.Rows, out ulong cells, out ulong bytes));
        Assert.Equal(0UL, cells); Assert.Equal(0UL, bytes);
        Assert.False(fixture.Tracker.TryGetHyperlinkUsage(fixture.Page, fixture.Rows, out ulong links, out cells, out bytes));
        Assert.Equal(0UL, links); Assert.Equal(0UL, cells); Assert.Equal(0UL, bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrOverflowedPagesNeverReportAuthoritativeUsage(bool overflow)
    {
        Fixture fixture = new();
        GhosttySnapshotPageAllocation page = new(fixture.Page.Capacity, metadataOverflow: overflow);
        foreach (TerminalRow row in fixture.Rows) row.SnapshotAllocation = page;
        if (overflow) fixture.Tracker.InstallReflowPage(page, new(page.Capacity), fixture.Rows);
        Assert.False(fixture.Tracker.TryGetMetadataUsage(page, fixture.Rows, out GhosttySnapshotMetadataUsage usage));
        Assert.Equal(default, usage);
    }

    [Fact]
    public void CowOwnersKeepIndependentUsageAfterAnAllocatorMutation()
    {
        Fixture fixture = new();
        GhosttySnapshotPageTracker sibling = fixture.Tracker.Copy();
        TerminalRow[] retained = [fixture.Rows[0].CreateStateCopy(), fixture.Rows[1].CreateStateCopy()];
        Assert.True(sibling.TryGetMetadataUsage(fixture.Page, retained, out GhosttySnapshotMetadataUsage before));
        TerminalRowBuffer buffer = new();
        buffer.AddRange(fixture.Rows);
        using (GhosttySnapshotPageTracker.RowEdit edit = fixture.Tracker.EditRow(buffer, fixture.Rows[0], new(4096)))
        {
            edit.Clear(0, 8);
            fixture.Rows[0].Clear(0xFFFFFFFF, 0xFF000000);
        }
        Assert.True(fixture.Tracker.TryGetMetadataUsage(fixture.Page, fixture.Rows, out GhosttySnapshotMetadataUsage current));
        Assert.Equal(new GhosttySnapshotMetadataUsage(2, 1, 16, 0, 1, 1, 64), current);
        Assert.True(sibling.TryGetMetadataUsage(fixture.Page, retained, out GhosttySnapshotMetadataUsage after));
        Assert.Equal(before, after);
    }

    [Fact]
    public void WarmCombinedAndIndividualQueriesAllocateNothing()
    {
        Fixture fixture = new();
        for (int i = 0; i < 16; i++) Query();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) Query();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        void Query()
        {
            _ = fixture.Tracker.TryGetMetadataUsage(fixture.Page, fixture.Rows, out _);
            _ = fixture.Tracker.TryGetStyleUsage(fixture.Page, fixture.Rows, out _);
            _ = fixture.Tracker.TryGetGraphemeUsage(fixture.Page, fixture.Rows, out _, out _);
            _ = fixture.Tracker.TryGetHyperlinkUsage(fixture.Page, fixture.Rows, out _, out _, out _);
        }
    }

    [Fact]
    public void HostWritesUseTheFallbackUntilReconciledAndKeepTheSamePageCharge()
    {
        TerminalScreen screen = new(8, 2) { SnapshotScrollbackQuota = new() { PageAlignment = 4096 } };
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1ma\u0301\u001b]8;id=z;u\u001b\\b"u8);
        TerminalRowBuffer buffer = screen.GetSnapshotRows(0)!;
        List<TerminalRow> rows = [buffer[0], buffer[1]];
        GhosttySnapshotPageAllocation page = rows[0].SnapshotAllocation!;
        Assert.True(screen.TryGetSnapshotMetadataUsage(page, rows, out _));
        rows[1][0].Attributes = CellAttributes.Italic;
        Assert.False(screen.TryGetSnapshotMetadataUsage(page, rows, out GhosttySnapshotMetadataUsage stale));
        Assert.Equal(default, stale);
        ulong charge = GhosttySnapshotLiveAllocation.Measure(screen, buffer, new(4096));
        processor.Process("\u001b[0m"u8);
        page = rows[0].SnapshotAllocation!;
        Assert.True(screen.TryGetSnapshotMetadataUsage(page, rows, out GhosttySnapshotMetadataUsage reconciled));
        Assert.True(reconciled.Styles >= 2);
        Assert.Equal(charge, GhosttySnapshotLiveAllocation.Measure(screen, buffer, new(4096)));
    }

    private static void AssertIndependentCounters(GhosttySnapshotPageTracker tracker, GhosttySnapshotPageAllocation page,
        IReadOnlyList<TerminalRow> rows, GhosttySnapshotMetadataUsage usage)
    {
        Assert.True(tracker.TryGetStyleUsage(page, rows, out int styles));
        Assert.True(tracker.TryGetGraphemeUsage(page, rows, out ulong graphemeCells, out ulong graphemeBytes));
        Assert.True(tracker.TryGetHyperlinkUsage(page, rows, out ulong links, out ulong linkedCells, out ulong strings));
        Assert.Equal(new GhosttySnapshotMetadataUsage((ulong)styles, graphemeCells, graphemeBytes, 0, links, linkedCells, strings), usage);
    }

    private sealed class IndexedRows(IReadOnlyList<TerminalRow> rows) : IReadOnlyList<TerminalRow>
    {
        internal int Reads { get; private set; }
        public int Count => rows.Count;
        public TerminalRow this[int index] { get { Reads++; return rows[index]; } }
        public IEnumerator<TerminalRow> GetEnumerator() => throw new InvalidOperationException("Census must use indexing.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Fixture
    {
        internal GhosttySnapshotPageAllocation Page { get; } = new(new(8, 8, 16, 192, 1024, 2048));
        internal GhosttySnapshotPageTracker Tracker { get; } = new();
        internal List<TerminalRow> Rows { get; } = [];

        internal Fixture()
        {
            GhosttySnapshotPageStorage storage = new(Page.Capacity);
            GhosttySnapshotStyle bold = new(default, default, default, 1), italic = new(default, default, default, 2);
            Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.Styles.ChangeCursor(bold));
            Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.Styles.ChangeCell(0, italic));
            Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.Styles.ChangeCell(9, italic));
            Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Graphemes.Set(0, 5));
            Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Graphemes.Set(9, 1));
            byte[] link = new TerminalHyperlink("u"u8, "id"u8, 0).SnapshotEncoding;
            Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.Hyperlinks.ObserveCell(0, link));
            Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.Hyperlinks.ObserveCell(8, link));
            for (int i = 0; i < 2; i++) Rows.Add(new(8) { SnapshotAllocation = Page, SnapshotAllocationRow = i });
            Tracker.InstallReflowPage(Page, storage, Rows);
        }
    }
}
