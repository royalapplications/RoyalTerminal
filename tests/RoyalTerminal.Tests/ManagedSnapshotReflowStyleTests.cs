// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty PageList.ReflowCursor copies styles with preferred source IDs and
// grows using the populated prefix, not final row density; Screen.resize adds
// the cursor after reflow/prompt clearing. WT TextBuffer::Reflow and xterm.js
// Buffer._reflow supply visible layout references, not native PAGE accounting.
public sealed class ManagedSnapshotReflowStyleTests
{
    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void GrowthUsesTheFailingCopyPrefixInsteadOfFinalRowDensity(int alignment)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(PressureSnapshot());
        terminal.Screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment };
        GhosttySnapshotAllocation layout = new(alignment);
        GhosttySnapshotPageCapacity source = terminal.Screen.GetSnapshotRows(0)![0].SnapshotAllocation!.Capacity;
        Assert.True(layout.TryAdjustColumns(source, 8, out GhosttySnapshotPageCapacity adjusted));
        Assert.True(layout.TryIncreaseCapacity(adjusted, GhosttySnapshotCapacityDimension.Styles, 2, 1, out GhosttySnapshotPageCapacity expected));
        TerminalScreen retained = terminal.Screen.CreateStateCopy();

        terminal.Processor.ResizeScreen(8, 4, 0, 0);
        TerminalRowBuffer rows = terminal.Screen.GetSnapshotRows(0)!;
        Assert.Equal(expected.Styles, rows[0].SnapshotAllocation!.Capacity.Styles);
        Assert.True(expected.Styles > 8);
        Assert.Equal(3, StyleCount(terminal.Screen, rows[0]));
        Assert.Equal(4, retained.Columns);
        Assert.Equal(source.Styles, retained.GetSnapshotRows(0)![0].SnapshotAllocation!.Capacity.Styles);
        Assert.Equal(CellAttributes.Bold, rows[0].ReadOnlyCells[0].Attributes);
        Assert.Equal(CellAttributes.Italic, rows[0].ReadOnlyCells[1].Attributes);
        Assert.Equal(CellAttributes.Dim, rows[0].ReadOnlyCells[4].Attributes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReflowKeepsPhysicalRowOffsetsAndLiveStyleReferences(bool rotate)
    {
        TerminalRow first = Row(CellAttributes.Bold), second = Row(CellAttributes.Italic);
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot([[first, second]]));
        TerminalRowBuffer before = terminal.Screen.GetSnapshotRows(0)!;
        if (rotate) { TerminalRow row = before[0]; before[0] = before[1]; before[1] = row; }
        before[0].WrapsToNext = true;
        before[1].WrapsToNext = false;
        CellAttributes survivor = before[1].ReadOnlyCells[0].Attributes;
        terminal.Processor.ResizeScreen(8, 2, 0, 0);
        TerminalRow result = terminal.Screen.GetSnapshotRows(0)![0];
        Assert.Equal(2, StyleCount(terminal.Screen, result));
        using (GhosttySnapshotPageTracker.RowEdit edit = terminal.Screen.EditSnapshotRowMetadata(result))
        {
            edit.Clear(0, 4);
            result.Cells[..4].Fill(TerminalCell.Empty());
        }
        Assert.Equal(1, StyleCount(terminal.Screen, result));
        Write(terminal.Screen, result, 0, survivor);
        Assert.Equal(1, StyleCount(terminal.Screen, result));
        Assert.Equal(survivor, result.ReadOnlyCells[4].Attributes);
    }

    [Fact]
    public void ReflowPublishesAllocatorStateWithoutChangingTheRetainedCowOwner()
    {
        TerminalRow first = Row(CellAttributes.Bold), second = Row(CellAttributes.Italic);
        first.WrapsToNext = true;
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot([[first, second]]));
        terminal.Processor.ResizeScreen(8, 2, 0, 0);
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        TerminalRow row = terminal.Screen.GetSnapshotRows(0)![0];
        using (GhosttySnapshotPageTracker.RowEdit edit = terminal.Screen.EditSnapshotRowMetadata(row))
        {
            edit.Clear(0, 8);
            row.Clear();
        }
        Assert.Equal(0, StyleCount(terminal.Screen, row));
        Assert.Equal(2, StyleCount(retained, retained.GetSnapshotRows(0)![0]));
        Write(terminal.Screen, row, 0, CellAttributes.Dim);
        Assert.Equal(1, StyleCount(terminal.Screen, row));
        Assert.Equal(CellAttributes.Bold, retained.GetSnapshotRows(0)![0].ReadOnlyCells[0].Attributes);
    }

    [Fact]
    public void NarrowingToOneColumnDoesNotCopyDiscardedWideStylesOrReferences()
    {
        TerminalRow wide = new(4);
        wide[0].Codepoint = 0x754C; wide[0].Width = 2; wide[0].Attributes = CellAttributes.Bold;
        wide[1].Width = 0; wide[1].Attributes = CellAttributes.Bold;
        wide[2].Codepoint = 'I'; wide[2].Attributes = CellAttributes.Italic;
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot([[wide, new(4)]]));
        terminal.Processor.ResizeScreen(1, 2, 0, 0);
        TerminalRow row = terminal.Screen.GetSnapshotRows(0)![0];
        Assert.Equal(1, StyleCount(terminal.Screen, row));
        Assert.Equal(0, row.ReadOnlyCells[0].Codepoint);
        Assert.Equal(CellAttributes.None, row.ReadOnlyCells[0].Attributes);
    }

    [Fact]
    public void InlineBackgroundCellsDoNotBecomeStyleEntriesAtReflowCheckpoints()
    {
        TerminalScreen screen = new(4, 2) { SnapshotScrollbackQuota = new() { PageAlignment = 4096 } };
        GhosttySnapshotPageAllocation allocation = new(new(4, 2, 0, 0, 0, 0));
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i][0].BackgroundIdentity = TerminalColorIdentity.Palette((byte)(i + 1));
            rows[i].SnapshotAllocation = allocation;
            rows[i].SnapshotAllocationRow = i;
        }
        screen.Resize(2, 2);
        Assert.Equal(0, rows[0].SnapshotAllocation!.Capacity.Styles);
        Assert.Equal(0, StyleCount(screen, rows[0]));
        Assert.Equal(TerminalColorIdentity.Palette(1), rows[0].ReadOnlyCells[0].BackgroundIdentity);
        Assert.Equal(TerminalColorIdentity.Palette(2), rows[1].ReadOnlyCells[0].BackgroundIdentity);
    }

    [Fact]
    public void ResizeRestoresAnUnprintedPenOnTheNewPage()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot([[new(4), new(4)]]));
        terminal.Processor.Process("\u001b[1m"u8);
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        terminal.Processor.ResizeScreen(8, 2, 0, 0);
        GhosttySnapshotStyle bold = new(default, default, default, 1);
        Assert.True(terminal.Screen.SnapshotCursorStyleIsCurrent(0, terminal.Processor.CursorRow, bold));
        Assert.Equal(1, StyleCount(terminal.Screen, terminal.Screen.GetViewportRow(terminal.Processor.CursorRow)));
        Assert.True(retained.SnapshotCursorStyleIsCurrent(0, 0, bold));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizingBothBuffersRetainsEachBuffersOwnPen(bool alternateVisible)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot([[new(4), new(4)]]));
        terminal.Processor.Process("\u001b[1m\u001b[?47h\u001b[0;3m"u8);
        if (!alternateVisible) terminal.Processor.Process("\u001b[?47l\u001b[0;1m"u8);
        terminal.Processor.ResizeScreen(8, 3, 0, 0);
        Assert.Equal(alternateVisible, terminal.Screen.AlternateBufferActive);
        Assert.True(terminal.Screen.SnapshotCursorStyleIsCurrent(0, 0, new(default, default, default, 1)));
        // 47 return copies italic onto primary, then the explicit SGR above
        // restores bold there without changing the dormant alternate pen.
        Assert.True(terminal.Screen.SnapshotCursorStyleIsCurrent(1, 0, new(default, default, default, 2)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReflowGrowthAndContinuationMatchNativeAcrossRepeatedResizes(bool pullScrollback)
    {
        RequireNative();
        using GhosttyTerminal native = GhosttySnapshot.Decode(PressureSnapshot());
        native.SetResizePullScrollback(pullScrollback);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(PressureSnapshot(), new()
        {
            ProcessorOptions = new() { ResizePullScrollback = pullScrollback },
        });
        int alignment = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096;
        managed.Screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment };
        foreach (ushort columns in new ushort[] { 8, 3, 9 })
        {
            native.Resize(columns, 4);
            managed.Processor.ResizeScreen(columns, 4, 0, 0);
            byte[] input = "\u001b[1;3mX\u001b[0m"u8.ToArray();
            native.Write(input); managed.Processor.Process(input);
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotScreen expected = reader.ReadReady().Screens[0];
            TerminalRowBuffer rows = managed.Screen.GetSnapshotRows(0)!;
            // READY can omit wholly historical pages; align its suffix with the
            // resident managed rows before comparing per-page capacities/cells.
            int count = 0;
            foreach (GhosttySnapshotPage page in expected.Pages) count += page.Grid.Rows;
            int index = rows.Count - count;
            Assert.True(index >= 0, $"Resize to {columns}: managed rows {rows.Count}, native READY rows {count}; pull={pullScrollback}");
            foreach (GhosttySnapshotPage page in expected.Pages)
            foreach (TerminalRow referenceRow in GhosttySnapshotLivePage.Decode(page, new TerminalScreen(columns, 4)))
            {
                TerminalRow actualRow = rows[index++];
                Assert.Equal(page.Capacity.Styles, actualRow.SnapshotAllocation!.Capacity.Styles);
                for (int column = 0; column < columns; column++)
                {
                    TerminalCell reference = referenceRow.ReadOnlyCells[column], actual = actualRow.ReadOnlyCells[column];
                    Assert.Equal(reference.Codepoint, actual.Codepoint);
                    Assert.Equal(GhosttySnapshotLivePage.EncodeStyle(in reference), GhosttySnapshotLivePage.EncodeStyle(in actual));
                }
            }
        }
    }

    private static int StyleCount(TerminalScreen screen, TerminalRow member)
    {
        TerminalRowBuffer rows = screen.GetSnapshotRows(screen.AlternateBufferActive ? 1 : 0)!;
        List<TerminalRow> group = [];
        for (int i = 0; i < rows.Count; i++)
            if (ReferenceEquals(rows[i].SnapshotAllocation, member.SnapshotAllocation)) group.Add(rows[i]);
        Assert.True(screen.TryGetSnapshotStyleUsage(member.SnapshotAllocation!, group, out int count));
        return count;
    }

    private static void Write(TerminalScreen screen, TerminalRow row, int column, CellAttributes attributes)
    {
        using GhosttySnapshotPageTracker.RowEdit edit = screen.EditSnapshotRowMetadata(row);
        TerminalCell cell = TerminalCell.Empty(); cell.Codepoint = 'Z'; cell.Attributes = attributes;
        edit.Write(column, GhosttySnapshotLivePage.EncodeStyle(in cell));
        row[column] = cell;
    }

    private static TerminalRow Row(CellAttributes attributes = CellAttributes.None)
    {
        TerminalRow row = new(4);
        for (int i = 0; i < 4; i++) { row[i].Codepoint = 'a' + i; row[i].Attributes = attributes; }
        return row;
    }

    private static byte[] PressureSnapshot()
    {
        TerminalRow first = Row(CellAttributes.Bold);
        first[1].Attributes = CellAttributes.Italic;
        first.WrapsToNext = true;
        List<TerminalRow[]> pages = [[first], [Row(CellAttributes.Dim)]];
        for (int i = 0; i < 20; i++) pages.Add([Row()]);
        return Snapshot(pages.ToArray(), viewportRows: 4);
    }

    private static byte[] Snapshot(TerminalRow[][] pages, int viewportRows = 2)
    {
        TerminalScreen screen = new(4, viewportRows);
        using BasicVtProcessor source = new(screen);
        using GhosttySnapshotRecordReader reader = new(source.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Screen) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), checked((ushort)pages.Length));
            if (tag == GhosttySnapshotRecordTag.Page)
            {
                foreach (TerminalRow[] rows in pages)
                {
                    using MemoryStream output = new();
                    GhosttySnapshotLivePage.Capture(rows, screen, checked(4 * rows.Length)).WritePayloadTo(output);
                    records.Add(new(tag, output.ToArray()));
                }
            }
            else records.Add(new(tag, bytes));
            if (tag == GhosttySnapshotRecordTag.Finish) return SnapshotTestRecords.Encode(records);
        }
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
