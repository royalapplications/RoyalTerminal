// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Page.resetRow releases cell metadata before reuse, independently of
// the cursor pin. PageList.grow reuses reset tail capacity. WT's
// TextBuffer::IncrementCircularBuffer resets recycled rows and xterm.js
// Buffer.resize drops blank bottom rows, but neither exposes Ghostty PAGE refs.
// Keep RoyalTerminal's explicit hard row cap (not native whole-page eviction).
// These tests check allocator effects immediately, before an SGR/checkpoint can
// hide a missing host hook by reconciling the surviving rows afterwards.
public sealed class ManagedSnapshotRowLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearScrollbackReleasesOnlyHistoricalCellsAndForksRestoredSeeds(bool initialize)
    {
        TerminalScreen screen = CreateScreen([Row(CellAttributes.Bold), Row(CellAttributes.Italic), Row()], 2, initialize);
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalRow survivor = screen.GetSnapshotRows(0)![1];
        object storage = survivor.SearchStorageIdentity;

        screen.ClearScrollback();
        Assert.Equal(2, screen.TotalRows);
        Assert.Same(survivor, screen.GetSnapshotRows(0)![0]);
        Assert.Same(storage, survivor.SearchStorageIdentity);
        WriteStyle(screen, survivor, 1, CellAttributes.Dim);
        Assert.Equal(4, Capacity(survivor));
        WriteStyle(retained, retained.GetSnapshotRows(0)![1], 1, CellAttributes.Dim);
        Assert.Equal(8, Capacity(retained.GetSnapshotRows(0)![1]));
        Assert.Equal(CellAttributes.Bold, retained.GetSnapshotRows(0)![0].ReadOnlyCells[0].Attributes);
    }

    [Fact]
    public void HardRowLimitReleasesTheRemovedPrefixBeforeAnotherCellWrite()
    {
        TerminalScreen screen = CreateScreen([Row(CellAttributes.Bold), Row(CellAttributes.Italic), Row()], 2);
        screen.ScrollbackLimit = 0;
        TerminalRow survivor = screen.GetSnapshotRows(0)![0];
        WriteStyle(screen, survivor, 1, CellAttributes.Dim);
        Assert.Equal(2, screen.TotalRows);
        Assert.Equal(4, Capacity(survivor));
    }

    [Fact]
    public void RecyclingReleasesTheOldSlotWithoutReusingPrefixHoles()
    {
        TerminalScreen screen = CreateScreen([Row(CellAttributes.Bold), Row(CellAttributes.Italic), Row()], 2);
        screen.ScrollbackLimit = 1;
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        TerminalRow oldHead = rows[0], survivor = rows[1];
        GhosttySnapshotPageAllocation page = survivor.SnapshotAllocation!;
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalRow recycled = screen.AddRow();
        Assert.Same(oldHead, recycled);
        Assert.Null(recycled.SnapshotAllocation);
        Assert.Equal(0, recycled.ReadOnlyCells[0].Codepoint);
        WriteStyle(screen, survivor, 1, CellAttributes.Dim);
        Assert.Equal(4, Capacity(survivor));
        using (screen.EditSnapshotRowMetadata(recycled)) { }
        Assert.NotSame(page, recycled.SnapshotAllocation);
        Assert.Equal(0, recycled.SnapshotAllocationRow);
        Assert.Equal(CellAttributes.Bold, retained.GetSnapshotRows(0)![0].ReadOnlyCells[0].Attributes);
    }

    [Fact]
    public void RetiredTailSlotsAreReusableWithoutChangingThePublishedWatermark()
    {
        TerminalScreen screen = CreateScreen([Row(), Row(), Row(), Row()], 2);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        GhosttySnapshotPageAllocation page = rows[0].SnapshotAllocation!;
        rows[3].IsTransientResizeRow = true;
        TerminalScreen retained = screen.CreateStateCopy();
        Assert.Equal(1, screen.DiscardTransientResizeRows());
        TerminalRow appended = screen.AddRow();
        using (screen.EditSnapshotRowMetadata(appended)) { }
        Assert.Same(page, appended.SnapshotAllocation);
        Assert.Equal(3, appended.SnapshotAllocationRow);

        TerminalRow retainedAppend = retained.AddRow();
        using (retained.EditSnapshotRowMetadata(retainedAppend)) { }
        Assert.NotSame(page, retainedAppend.SnapshotAllocation);
        Assert.Equal(0, retainedAppend.SnapshotAllocationRow);
    }

    [Fact]
    public void RetiringARotatedLogicalTailDoesNotReclaimAnOccupiedPhysicalTail()
    {
        TerminalScreen screen = CreateScreen([Row(), Row(), Row(), Row()], 2);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        GhosttySnapshotPageAllocation page = rows[0].SnapshotAllocation!;
        TerminalRow first = rows[0]; rows[0] = rows[3]; rows[3] = first;
        rows[3].IsTransientResizeRow = true;
        Assert.Equal(1, screen.DiscardTransientResizeRows());
        TerminalRow appended = screen.AddRow();
        using (screen.EditSnapshotRowMetadata(appended)) { }
        Assert.NotSame(page, appended.SnapshotAllocation);
        Assert.Equal(3, rows[0].SnapshotAllocationRow);
    }

    [Fact]
    public void CheckpointAssignedSlotsStayOccupiedAfterAnotherTailRowIsRetired()
    {
        TerminalScreen screen = CreateScreen([Row(), Row()], 2, capacityRows: 6);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        GhosttySnapshotPageAllocation page = rows[0].SnapshotAllocation!;
        screen.AddRow(); screen.AddRow(); screen.AddRow();
        _ = GhosttySnapshotLiveAllocation.Measure(screen, rows, new(4096));
        rows[4].IsTransientResizeRow = true;
        Assert.Equal(1, screen.DiscardTransientResizeRows());
        TerminalRow appended = screen.AddRow();
        using (screen.EditSnapshotRowMetadata(appended)) { }
        Assert.Same(page, appended.SnapshotAllocation);
        Assert.Equal(4, appended.SnapshotAllocationRow);
        Assert.Equal(3, rows[3].SnapshotAllocationRow);
    }

    [Fact]
    public void HeightShrinkReleasesBlankStyledTailAndReusesItsSlot()
    {
        TerminalScreen screen = CreateScreen([Row(CellAttributes.Bold), Row(), Row(CellAttributes.Italic, text: false)], 3);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        screen.Resize(4, 2, trackedViewportPosition: new(0, 0));
        Assert.Equal(2, rows.Count);
        WriteStyle(screen, rows[0], 1, CellAttributes.Dim);
        Assert.Equal(4, Capacity(rows[0]));
        TerminalRow appended = screen.AddRow();
        using (screen.EditSnapshotRowMetadata(appended)) { }
        Assert.Same(rows[0].SnapshotAllocation, appended.SnapshotAllocation);
        Assert.Equal(2, appended.SnapshotAllocationRow);
    }

    [Fact]
    public void HiddenColumnRemovalReleasesStylesWithoutTouchingRetainedReaders()
    {
        TerminalRow contents = Row(CellAttributes.Italic);
        contents[3].Codepoint = 'B'; contents[3].Attributes = CellAttributes.Bold;
        TerminalScreen screen = CreateScreen([contents], 1);
        TerminalScreen retained = screen.CreateStateCopy();
        screen.GetViewportRow(0).Resize(2);
        screen.DiscardHiddenCells();
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(2, row.PreservedColumns);
        WriteStyle(screen, row, 1, CellAttributes.Dim);
        Assert.Equal(4, Capacity(row));
        Assert.Equal(CellAttributes.Bold, retained.GetViewportRow(0).ReadOnlyCells[3].Attributes);
        WriteStyle(retained, retained.GetViewportRow(0), 1, CellAttributes.Dim);
        Assert.Equal(8, Capacity(retained.GetViewportRow(0)));
    }

    [Fact]
    public void MovingTheViewportIntoHistoryClearsStylesOnBlankViewportRows()
    {
        TerminalScreen screen = CreateScreen([Row(CellAttributes.Bold, text: false), Row(CellAttributes.Italic, text: false)], 2);
        TerminalScreen retained = screen.CreateStateCopy();
        screen.MoveViewportToScrollbackAndClear();
        Assert.Equal(2, screen.TotalRows);
        TerminalRow row = screen.GetViewportRow(0);
        WriteStyle(screen, row, 1, CellAttributes.Dim);
        Assert.Equal(4, Capacity(row));
        Assert.Equal(CellAttributes.Bold, retained.GetViewportRow(0).ReadOnlyCells[0].Attributes);
    }

    [Fact]
    public void ClearingAnExistingAlternateBufferReleasesCellButNotCursorReferences()
    {
        TerminalScreen screen = new(4, 2) { SnapshotScrollbackQuota = new() };
        screen.SwitchToAlternateBuffer(clear: true);
        InstallPage(screen, [Row(CellAttributes.Bold), Row(CellAttributes.Italic)]);
        GhosttySnapshotStyle italic = new(default, default, default, 2);
        screen.SnapshotStyleChanged(1, 1, default, italic);
        TerminalScreen retained = screen.CreateStateCopy();
        screen.SwitchToAlternateBuffer(clear: true);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(1, 1, italic));
        WriteStyle(screen, screen.GetViewportRow(0), 0, CellAttributes.Dim);
        Assert.Equal(4, Capacity(screen.GetViewportRow(0)));
        Assert.Equal(CellAttributes.Bold, retained.GetViewportRow(0).ReadOnlyCells[0].Attributes);
        Assert.True(retained.SnapshotCursorStyleIsCurrent(1, 1, italic));
    }

    [Fact]
    public void RowRetirementDoesNotReleaseTheCursorPinEvenWhenItsRowDisappears()
    {
        TerminalScreen screen = CreateScreen([Row(CellAttributes.Bold), Row(CellAttributes.Italic), Row()], 2);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        GhosttySnapshotPageTracker tracker = new();
        GhosttySnapshotStyle bold = new(default, default, default, 1);
        tracker.ChangeCursor(rows, 0, rows[0], default, bold, new(4096));
        tracker.RetireRows(rows, 0, 1);
        rows.RemoveFirst();
        Assert.True(tracker.IsCurrent(0, rows[0], bold));
        using (GhosttySnapshotPageTracker.RowEdit edit = tracker.EditRow(rows, rows[0], new(4096)))
            edit.Write(1, new(default, default, default, 4));
        // Italic cells + the independent bold cursor still fill the two usable
        // entries. Dim must grow despite retiring all bold cells.
        Assert.Equal(8, Capacity(rows[0]));
    }

    [Fact]
    public void RasterReplacementReleasesTheErasedWideCellRun()
    {
        TerminalRow contents = Row(CellAttributes.Bold);
        contents[0].Width = 2;
        contents[1].Width = 0; contents[1].Attributes = CellAttributes.Bold;
        contents[2].Codepoint = 'I'; contents[2].Attributes = CellAttributes.Italic;
        TerminalScreen screen = CreateScreen([contents], 1);
        TerminalScreen retained = screen.CreateStateCopy();
        // The image covers only the spacer; text erasure expands left to the
        // wide head and releases both references before another style is added.
        screen.ReplaceRasterImage(new TerminalRasterImageSource(1, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 0, 255]),
            new TerminalRasterImagePlacement(1, TerminalRasterImageLayer.AboveText, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1));
        Assert.Equal(0, screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        WriteStyle(screen, screen.GetViewportRow(0), 3, CellAttributes.Dim);
        Assert.Equal(4, Capacity(screen.GetViewportRow(0)));
        Assert.Equal(2, retained.GetViewportRow(0).ReadOnlyCells[0].Width);
        WriteStyle(retained, retained.GetViewportRow(0), 3, CellAttributes.Dim);
        Assert.Equal(8, Capacity(retained.GetViewportRow(0)));
    }

    [Fact]
    public void HeightShrinkAndTailRegrowthMatchNativeStyleCapacity()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
            Assert.Skip("Native VT library is unavailable.");
        }
        using BasicVtProcessor source = new(CreateScreen([Row(CellAttributes.Bold), Row(), Row(CellAttributes.Italic, text: false)], 3));
        byte[] snapshot = source.GetBinarySnapshot();
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        native.Resize(4, 2);
        managed.Processor.ResizeScreen(4, 2, 0, 0, reflowOnResize: false);
        byte[] input = "\u001b[2mZ\u001b[0m\u001b[2;1H\n"u8.ToArray();
        native.Write(input); managed.Processor.Process(input);
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotScreen expected = reader.ReadReady().Screens[0];
        Assert.Single(expected.Pages);
        TerminalRow[] expectedRows = GhosttySnapshotLivePage.Decode(expected.Pages[0], new TerminalScreen(4, 2));
        TerminalRowBuffer observed = managed.Screen.GetSnapshotRows(0)!;
        Assert.Equal(expectedRows.Length, observed.Count);
        for (int row = 0; row < observed.Count; row++)
        {
            Assert.Equal(expected.Pages[0].Capacity.Styles, Capacity(observed[row]));
            Assert.Equal(row, observed[row].SnapshotAllocationRow);
            for (int column = 0; column < 4; column++)
            {
                TerminalCell actual = observed[row].ReadOnlyCells[column];
                TerminalCell reference = expectedRows[row].ReadOnlyCells[column];
                Assert.Equal(reference.Codepoint, actual.Codepoint);
                Assert.Equal(GhosttySnapshotLivePage.EncodeStyle(in reference), GhosttySnapshotLivePage.EncodeStyle(in actual));
            }
        }
    }

    [Fact]
    public void OrdinaryHostClearsDoNotOptIntoSnapshotAccounting()
    {
        TerminalScreen screen = new(4, 2, 1);
        screen.AddRow(); screen.AddRow(); screen.ClearScrollback();
        screen.MoveViewportToScrollbackAndClear();
        screen.GetViewportRow(0).Resize(2);
        screen.DiscardHiddenCells();
        screen.SwitchToAlternateBuffer(clear: true);
        for (int key = 0; key < 2; key++)
        {
            TerminalRowBuffer rows = screen.GetSnapshotRows(key)!;
            for (int i = 0; i < rows.Count; i++) Assert.Null(rows[i].SnapshotAllocation);
        }
    }

    private static ushort Capacity(TerminalRow row) => row.SnapshotAllocation!.Capacity.Styles;

    private static TerminalRow Row(CellAttributes attributes = CellAttributes.None, bool text = true)
    {
        TerminalRow row = new(4);
        row[0].Attributes = attributes;
        if (text && attributes != CellAttributes.None) row[0].Codepoint = 'A';
        return row;
    }

    private static TerminalScreen CreateScreen(TerminalRow[] rows, int viewportRows, bool initialize = true, int capacityRows = 0)
    {
        TerminalScreen screen = new(4, viewportRows, 64) { SnapshotScrollbackQuota = new() };
        InstallPage(screen, rows, capacityRows);
        if (initialize) screen.SnapshotStyleChanged(0, 0, default, default);
        return screen;
    }

    private static void InstallPage(TerminalScreen screen, TerminalRow[] contents, int capacityRows = 0)
    {
        GhosttySnapshotPage page = GhosttySnapshotLivePage.Capture(contents, screen, checked(4 * contents.Length));
        TerminalRow[] decoded = GhosttySnapshotLivePage.Decode(page, screen);
        GhosttySnapshotPageAllocation allocation = decoded[0].SnapshotAllocation!;
        if (capacityRows != 0)
            allocation = new(allocation.Capacity with { Rows = checked((ushort)capacityRows) }, allocation.CopyRestoredStyles());
        TerminalRowBuffer rows = screen.GetSnapshotRows(screen.AlternateBufferActive ? 1 : 0)!;
        rows.Clear();
        foreach (TerminalRow row in decoded)
        {
            row.SnapshotAllocation = allocation;
            rows.Add(row);
        }
    }

    private static void WriteStyle(TerminalScreen screen, TerminalRow row, int column, CellAttributes attributes)
    {
        using GhosttySnapshotPageTracker.RowEdit edit = screen.EditSnapshotRowMetadata(row);
        TerminalCell cell = TerminalCell.Empty(screen.DefaultForeground, screen.DefaultBackground);
        cell.Codepoint = 'Z'; cell.Attributes = attributes;
        edit.Write(column, GhosttySnapshotLivePage.EncodeStyle(in cell));
        row[column] = cell;
    }
}
