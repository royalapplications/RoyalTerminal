// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty PageList.resizeWithoutReflowGrowCols is the page/metadata oracle.
// WT TextBuffer::ResizeTraditional copies into replacement row storage; xterm.js
// Buffer.resize grows lines individually. Neither defines Ghostty PAGE quotas.
// Retaining hidden cells in the general screen API is a deliberate host policy;
// processor-owned native-style resizes explicitly discard them.
public sealed class ManagedSnapshotColumnResizeTests
{
    [Fact]
    public void ReservedColumnsReuseThePageAndPreserveHiddenCellsAndCow()
    {
        TerminalScreen screen = Screen(new(4, 4, 8, 0, 0, 0), [Row(CellAttributes.Bold), Row(CellAttributes.Italic)]);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[2m"u8);
        TerminalRow first = screen.GetSnapshotRows(0)![0];
        GhosttySnapshotPageAllocation page = first.SnapshotAllocation!;
        first.WrapsToNext = true;
        screen.Resize(2, 2, reflowOnResize: false);
        TerminalScreen retained = screen.CreateStateCopy();
        object storage = first.SearchStorageIdentity;

        screen.Resize(3, 2, reflowOnResize: false);

        Assert.Same(page, first.SnapshotAllocation);
        Assert.Same(storage, first.SearchStorageIdentity);
        Assert.True(first.WrapsToNext);
        Assert.Equal(4, first.PreservedColumns);
        Assert.Equal(CellAttributes.Bold, first.ReadOnlyPreservedCells[3].Attributes);
        Assert.Equal(3, Styles(screen, first)); // Two cell styles plus the unprinted dim pen.
        Assert.Equal(2, retained.Columns);
        using (GhosttySnapshotStyleTracker.RowEdit edit = screen.EditSnapshotRowStyles(first))
        {
            edit.Clear(0, 4);
            first.Clear();
        }
        Assert.Equal(2, Styles(screen, first));
        Assert.Equal(3, Styles(retained, retained.GetSnapshotRows(0)![0]));
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void GrowthSplitsSourceCapacityAndKeepsRotatedPhysicalStyleOffsets(int alignment)
    {
        GhosttySnapshotPageCapacity source = new(4, 40, 8, 0, 0, 0);
        TerminalRow[] input = new TerminalRow[40];
        for (int i = 0; i < input.Length; i++) input[i] = Row(i % 2 == 0 ? CellAttributes.Bold : CellAttributes.Italic);
        TerminalScreen screen = Screen(source, input, alignment);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        // Reconcile source physical IDs before rotating rows.
        using (screen.EditSnapshotRowStyles(rows[0])) { }
        (rows[0], rows[1]) = (rows[1], rows[0]);
        TerminalScreen retained = screen.CreateStateCopy();
        GhosttySnapshotAllocation layout = new(alignment);
        Assert.True(layout.TryAdjustColumns(source, 128, out GhosttySnapshotPageCapacity expected));
        Assert.True(expected.Rows < rows.Count);

        screen.Resize(128, 2, reflowOnResize: false);

        for (int i = 0; i < rows.Count; i++)
        {
            Assert.Equal(expected, rows[i].SnapshotAllocation!.Capacity);
            Assert.Equal(i % expected.Rows, rows[i].SnapshotAllocationRow);
            CellAttributes attributes = (i < 2 ? 1 - i : i) % 2 == 0 ? CellAttributes.Bold : CellAttributes.Italic;
            Assert.Equal(attributes, rows[i].ReadOnlyCells[0].Attributes);
            if (i > 0 && i % expected.Rows != 0) Assert.Same(rows[i - 1].SnapshotAllocation, rows[i].SnapshotAllocation);
            if (i > 0 && i % expected.Rows == 0) Assert.NotSame(rows[i - 1].SnapshotAllocation, rows[i].SnapshotAllocation);
        }
        Assert.Equal(2, Styles(screen, rows[0]));
        Assert.Equal(source, retained.GetSnapshotRows(0)![0].SnapshotAllocation!.Capacity);
        Assert.Equal(4, retained.Columns);
    }

    [Fact]
    public void BackfillFailureReleasesOnlyItsPartialRowAndPreservesEarlierCopies()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(BackfillSnapshot());
        TerminalScreen screen = terminal.Screen;
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        TerminalScreenAnchor anchor = screen.CreateAnchor(1, 0);
        TerminalScreen retained = screen.CreateStateCopy();

        terminal.Processor.ResizeScreen(8, 3, 0, 0, reflowOnResize: false);

        Assert.Same(rows[0].SnapshotAllocation, rows[1].SnapshotAllocation);
        Assert.NotSame(rows[0].SnapshotAllocation, rows[2].SnapshotAllocation);
        Assert.Equal((ushort)4, rows[0].SnapshotAllocation!.Capacity.Styles);
        Assert.Equal(1, Styles(screen, rows[0])); // Failed italic prefix was released.
        Assert.Equal(2, Styles(screen, rows[2]));
        Assert.Equal(0, rows[2].SnapshotAllocationRow);
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition position));
        Assert.Equal(new TerminalGridPosition(0, 1), position);
        screen.ReleaseAnchor(anchor);
        Assert.NotSame(retained.GetSnapshotRows(0)![0].SnapshotAllocation, rows[0].SnapshotAllocation);
        using (GhosttySnapshotStyleTracker.RowEdit edit = screen.EditSnapshotRowStyles(rows[0]))
        {
            edit.Clear(0, 8);
            rows[0].Clear();
        }
        Assert.Equal(0, Styles(screen, rows[0]));
        Assert.Equal(2, Styles(screen, rows[2]));
    }

    [Fact]
    public void BackfillIntoAReusedPageRespectsPhysicalHolesAndForksItsTable()
    {
        TerminalScreen screen = Screen(new(8, 4, 8, 0, 0, 0), [Row(CellAttributes.Bold), Row(CellAttributes.Italic)]);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        rows[0].SnapshotAllocationRow = 1; // Retired prefix slot zero is not reusable.
        rows[1].SnapshotAllocation = new(new(4, 1, 8, 0, 0, 0));
        rows[1].SnapshotAllocationRow = 0;
        using (screen.EditSnapshotRowStyles(rows[0])) { }
        TerminalScreen retained = screen.CreateStateCopy();
        GhosttySnapshotPageAllocation first = rows[0].SnapshotAllocation!;

        screen.Resize(8, 2, reflowOnResize: false);

        Assert.Same(first, rows[0].SnapshotAllocation);
        Assert.Same(first, rows[1].SnapshotAllocation);
        Assert.Equal(1, rows[0].SnapshotAllocationRow);
        Assert.Equal(2, rows[1].SnapshotAllocationRow);
        Assert.Equal(2, Styles(screen, rows[0]));
        Assert.Equal(1, Styles(retained, retained.GetSnapshotRows(0)![0]));
        Assert.Equal(CellAttributes.Bold, rows[0].ReadOnlyCells[0].Attributes);
        Assert.Equal(CellAttributes.Italic, rows[1].ReadOnlyCells[0].Attributes);
    }

    [Fact]
    public void InterleavedSourcePagesKeepLogicalOrderAndIndependentSourceSlots()
    {
        TerminalScreen screen = Screen(new(4, 3, 8, 0, 0, 0),
            [Row(CellAttributes.Bold), Row(CellAttributes.Italic), Row(CellAttributes.Dim)]);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        rows[1].SnapshotAllocation = new(new(4, 1, 8, 0, 0, 0));
        rows[1].SnapshotAllocationRow = 0;

        screen.Resize(8, 2, reflowOnResize: false);

        Assert.Equal(CellAttributes.Bold, rows[0].ReadOnlyCells[0].Attributes);
        Assert.Equal(CellAttributes.Italic, rows[1].ReadOnlyCells[0].Attributes);
        Assert.Equal(CellAttributes.Dim, rows[2].ReadOnlyCells[0].Attributes);
        Assert.Same(rows[0].SnapshotAllocation, rows[1].SnapshotAllocation);
        Assert.Same(rows[0].SnapshotAllocation, rows[2].SnapshotAllocation);
        Assert.Equal(3, Styles(screen, rows[0]));
        using (GhosttySnapshotStyleTracker.RowEdit edit = screen.EditSnapshotRowStyles(rows[1]))
        {
            edit.Clear(0, 8);
            rows[1].Clear();
        }
        Assert.Equal(2, Styles(screen, rows[0]));
    }

    [Fact]
    public void SpacerHeadForcesCloneButPreservesSemanticAndHiddenCellMetadata()
    {
        TerminalRow source = Row(CellAttributes.Bold);
        source[1].IsWideSpacerHead = true;
        source[1].Width = 0;
        source[1].IsProtected = true;
        source.WrapsToNext = source.IsWrapContinuation = true;
        source.SemanticPrompt = TerminalSemanticPrompt.Prompt;
        TerminalScreen screen = Screen(new(4, 2, 4, 0, 0, 0), [source, new(4)]);
        screen.Resize(2, 2, reflowOnResize: false);
        TerminalScreen retained = screen.CreateStateCopy();
        GhosttySnapshotPageAllocation before = source.SnapshotAllocation!;

        screen.Resize(3, 2, reflowOnResize: false);

        Assert.NotSame(before, source.SnapshotAllocation);
        Assert.False(source.ReadOnlyCells[1].IsWideSpacerHead);
        Assert.Equal(1, source.ReadOnlyCells[1].Width);
        Assert.True(source.ReadOnlyCells[1].IsProtected);
        Assert.False(source.WrapsToNext);
        Assert.False(source.IsWrapContinuation);
        Assert.Equal(TerminalSemanticPrompt.Prompt, source.SemanticPrompt);
        Assert.Equal(CellAttributes.Bold, source.ReadOnlyPreservedCells[3].Attributes);
        Assert.Equal(1, Styles(screen, source));
        Assert.True(retained.GetSnapshotRows(0)![0].ReadOnlyCells[1].IsWideSpacerHead);
    }

    [Fact]
    public void HugeColumnFallbackUsesOnlyOccupiedRows()
    {
        TerminalScreen screen = Screen(new(4, 300, 0, 0, 0, 0), [new(4), new(4)]);
        screen.Resize(ushort.MaxValue, 2, reflowOnResize: false);
        GhosttySnapshotPageCapacity capacity = screen.GetSnapshotRows(0)![0].SnapshotAllocation!.Capacity;
        Assert.Equal(ushort.MaxValue, capacity.Columns);
        Assert.Equal((ushort)2, capacity.Rows);
    }

    [Fact]
    public void UntrackedGrowthDoesNotCreateSnapshotPages()
    {
        TerminalScreen screen = new(4, 2);
        screen.Resize(8, 2, reflowOnResize: false);
        foreach (TerminalRow row in screen.GetSnapshotRows(0)!) Assert.Null(row.SnapshotAllocation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeBackfillRollbackKeepsSavedCursorAndContinuation(bool alternate)
    {
        RequireNative();
        using GhosttyTerminal native = GhosttySnapshot.Decode(BackfillSnapshot());
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(BackfillSnapshot());
        if (alternate)
        {
            // Exercise the alternate no-reflow path with normal host allocation.
            native.Write("\u001b[?47h\u001b[1mAAAA\u001b[2;1H\u001b[0mMMMM\u001b[3;1H\u001b[3mI\u001b[2mD\u001b[0m"u8);
            managed.Processor.Process("\u001b[?47h\u001b[1mAAAA\u001b[2;1H\u001b[0mMMMM\u001b[3;1H\u001b[3mI\u001b[2mD\u001b[0m"u8);
        }
        byte[] save = "\u001b[?7l\u001b[2;1H\u001b7"u8.ToArray();
        native.Write(save); managed.Processor.Process(save);
        foreach (ushort columns in new ushort[] { 8, 3, 9 })
        {
            native.Resize(columns, 3);
            managed.Processor.ResizeScreen(columns, 3, 0, 0, reflowOnResize: false);
            native.Write("\u001b8Z"u8); managed.Processor.Process("\u001b8Z"u8);
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotScreen expected = reader.ReadReady().Screens[alternate ? 1 : 0];
            TerminalRowBuffer rows = managed.Screen.GetSnapshotRows(alternate ? 1 : 0)!;
            int index = 0;
            foreach (GhosttySnapshotPage page in expected.Pages)
            {
                if (!alternate && columns == 8 && index == 0)
                {
                    Assert.Equal(2, page.Grid.Rows);
                    Assert.Equal(1, page.StyleCount);
                }
                foreach (TerminalRow reference in GhosttySnapshotLivePage.Decode(page, new TerminalScreen(columns, 3)))
                {
                    TerminalRow actual = rows[index++];
                    GhosttySnapshotPageCapacity capacity = actual.SnapshotAllocation!.Capacity;
                    // PAGE export stores active dimensions, not reserved grid
                    // capacity. Only its metadata capacities expose allocation.
                    Assert.Equal(page.Capacity.Styles, capacity.Styles);
                    Assert.Equal(page.Capacity.HyperlinkBytes, capacity.HyperlinkBytes);
                    Assert.Equal(page.Capacity.GraphemeBytes, capacity.GraphemeBytes);
                    Assert.Equal(page.Capacity.StringBytes, capacity.StringBytes);
                    for (int column = 0; column < columns; column++)
                    {
                        TerminalCell expectedCell = reference.ReadOnlyCells[column], actualCell = actual.ReadOnlyCells[column];
                        Assert.Equal(expectedCell.Codepoint, actualCell.Codepoint);
                        Assert.Equal(GhosttySnapshotLivePage.EncodeStyle(in expectedCell), GhosttySnapshotLivePage.EncodeStyle(in actualCell));
                    }
                }
            }
            Assert.Equal(rows.Count, index);
            Assert.Equal('Z', rows[1].ReadOnlyCells[0].Codepoint);
        }
    }

    private static TerminalScreen Screen(GhosttySnapshotPageCapacity capacity, TerminalRow[] rows, int alignment = 4096)
    {
        TerminalScreen owner = new(4, 2);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(4, 2, 10000, owner.Theme);
        GhosttySnapshotPageAllocation page = new(capacity);
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i].SnapshotAllocation = page;
            rows[i].SnapshotAllocationRow = i;
        }
        screen.InstallSnapshotRows(rows, null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment };
        return screen;
    }

    private static int Styles(TerminalScreen screen, TerminalRow member)
    {
        List<TerminalRow> group = [];
        foreach (TerminalRow row in screen.GetSnapshotRows(screen.AlternateBufferActive ? 1 : 0)!)
            if (ReferenceEquals(row.SnapshotAllocation, member.SnapshotAllocation)) group.Add(row);
        Assert.True(screen.TryGetSnapshotStyleUsage(member.SnapshotAllocation!, group, out int count));
        return count;
    }

    private static TerminalRow Row(CellAttributes attributes)
    {
        TerminalRow row = new(4);
        for (int i = 0; i < 4; i++) { row[i].Codepoint = 'A' + i; row[i].Attributes = attributes; }
        return row;
    }

    private static byte[] BackfillSnapshot()
    {
        TerminalRow failed = new(4);
        failed[0].Codepoint = 'I'; failed[0].Attributes = CellAttributes.Italic;
        failed[1].Codepoint = 'D'; failed[1].Attributes = CellAttributes.Dim;
        TerminalRow[][] pages = [[Row(CellAttributes.Bold)], [Row(CellAttributes.None), failed]];
        TerminalScreen screen = new(4, 3);
        using BasicVtProcessor processor = new(screen);
        using GhosttySnapshotRecordReader reader = new(processor.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Screen) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 2);
            if (tag == GhosttySnapshotRecordTag.Page)
            {
                foreach (TerminalRow[] rows in pages)
                {
                    using MemoryStream output = new();
                    GhosttySnapshotLivePage.Capture(rows, screen, 4 * rows.Length).WritePayloadTo(output);
                    byte[] page = output.ToArray();
                    // Four requested buckets hold two distinct non-default styles.
                    BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(8), 4);
                    records.Add(new(tag, page));
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
