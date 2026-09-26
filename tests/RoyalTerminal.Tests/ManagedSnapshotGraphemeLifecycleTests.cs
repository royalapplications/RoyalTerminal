// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Screen.appendGrapheme grows the page and retries while preserving the
// old suffix. Page.cloneFrom rebuilds both styles and graphemes. WT ROW and
// xterm.js combined strings are text references, not native allocation oracles.
public sealed class ManagedSnapshotGraphemeLifecycleTests
{
    [Fact]
    public void AppendGrowsAtMutationTimeAndEraseReleasesWithoutACheckpoint()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 0, 0, 0));
        using BasicVtProcessor processor = new(screen);
        Process(processor, "A\u0301");
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(8192U, row.SnapshotAllocation!.Capacity.GraphemeBytes);
        Assert.Equal((1UL, 16UL), Usage(screen, row));
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, new string('\u0301', 4));
        Assert.Equal((1UL, 32UL), Usage(screen, row));
        Assert.Equal((1UL, 16UL), Usage(retained, retained.GetViewportRow(0)));
        Process(processor, "\u001b[1;1HX");
        Assert.Equal((0UL, 0UL), Usage(screen, row));
        Assert.Equal(8192U, row.SnapshotAllocation!.Capacity.GraphemeBytes);
        Assert.Equal("A\u0301", retained.GetViewportRow(0).ReadOnlyCells[0].Grapheme);
    }

    [Fact]
    public void StyleGrowthRebuildsGraphemesAndPreservesTheRetainedOwner()
    {
        TerminalScreen screen = Screen(new(8, 2, 4, 0, 1024, 0));
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[1mA" + new string('\u0301', 5) + "\u001b[0;3mB");
        TerminalRow row = screen.GetViewportRow(0);
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, "\u001b[0;2m"); // A third style, held only by the cursor.
        Assert.True(row.SnapshotAllocation!.Capacity.Styles > 4);
        Assert.Equal((ushort)4, retained.GetViewportRow(0).SnapshotAllocation!.Capacity.Styles);
        Assert.Equal((1UL, 32UL), Usage(screen, row));
        Assert.Equal((1UL, 32UL), Usage(retained, retained.GetViewportRow(0)));
        Process(processor, "\u001b[1;1H\u001b[X");
        Assert.Equal((0UL, 0UL), Usage(screen, row));
        Assert.Equal((1UL, 32UL), Usage(retained, retained.GetViewportRow(0)));
    }

    [Fact]
    public void IgnoredSuffixAtTheLimitDoesNotDetachPublishedCellStorage()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 0, 1024, 0));
        using BasicVtProcessor processor = new(screen);
        Process(processor, "A" + new string('\u0301', 64));
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalRow row = screen.GetViewportRow(0);
        object identity = row.SearchStorageIdentity;
        ulong revision = row.SnapshotMetadataRevision;
        Process(processor, "\u0301");
        Assert.Same(identity, row.SearchStorageIdentity);
        Assert.Same(identity, retained.GetViewportRow(0).SearchStorageIdentity);
        Assert.Equal(revision, row.SnapshotMetadataRevision);
        Assert.Equal((1UL, 256UL), Usage(screen, row));
    }

    [Fact]
    public void CharacterShiftsMoveOwnershipAndOnlyVacatedCellsAreReleased()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 0, 1024, 0));
        using BasicVtProcessor processor = new(screen);
        Process(processor, "A\u0301B\u0302\u001b[1;1H\u001b[@");
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal((2UL, 32UL), Usage(screen, row));
        Assert.Equal("A\u0301", row.ReadOnlyCells[1].Grapheme);
        Assert.Equal("B\u0302", row.ReadOnlyCells[2].Grapheme);
        Process(processor, "\u001b[P");
        Assert.Equal((2UL, 32UL), Usage(screen, row));
        Assert.Equal("A\u0301", row.ReadOnlyCells[0].Grapheme);
        Process(processor, "\u001b[P");
        Assert.Equal((1UL, 16UL), Usage(screen, row));
        Assert.Equal("B\u0302", row.ReadOnlyCells[0].Grapheme);
    }

    [Fact]
    public void CrossPageRowCopyGrowsItsDestinationAndClearReleasesTheSource()
    {
        TerminalScreen screen = Screen(new(8, 1, 8, 0, 1024, 0));
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        rows[1].SnapshotAllocation = new(new(8, 1, 8, 0, 0, 0));
        rows[1].SnapshotAllocationRow = 0;
        using BasicVtProcessor processor = new(screen);
        Process(processor, "A" + new string('\u0301', 5) + "\u001b[1;1H");
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, "\u001bM");
        Assert.Equal((0UL, 0UL), Usage(screen, rows[0]));
        Assert.Equal((1UL, 32UL), Usage(screen, rows[1]));
        Assert.Equal(8192U, rows[1].SnapshotAllocation!.Capacity.GraphemeBytes);
        Assert.Equal("A" + new string('\u0301', 5), rows[1].ReadOnlyCells[0].Grapheme);
        Assert.Equal((1UL, 32UL), Usage(retained, retained.GetViewportRow(0)));
        Assert.Equal(0U, retained.GetViewportRow(1).SnapshotAllocation!.Capacity.GraphemeBytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResizeCopiesGraphemesWithoutInventingReplacementScratch(bool reflow)
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 0, 17, 0));
        using BasicVtProcessor processor = new(screen);
        Process(processor, "A" + new string('\u0301', 5) + "\u001b[2;1HB" + new string('\u0302', 5));
        TerminalScreen retained = screen.CreateStateCopy();
        processor.ResizeScreen(16, 2, 0, 0, reflowOnResize: reflow);
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(17U, row.SnapshotAllocation!.Capacity.GraphemeBytes);
        Assert.Equal((2UL, 64UL), Usage(screen, row));
        Assert.Equal((2UL, 64UL), Usage(retained, retained.GetViewportRow(0)));
        Process(processor, "\u001b[1;1H\u001b[2K");
        Assert.Equal((1UL, 32UL), Usage(screen, row));
        Assert.Equal((2UL, 64UL), Usage(retained, retained.GetViewportRow(0)));
    }

    [Fact]
    public void SyntheticWideTailDoesNotAcquireTheHeadsGraphemeAllocation()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 0, 1024, 0));
        TerminalRow row = screen.GetViewportRow(0);
        row[0].Codepoint = 0x754C; row[0].Width = 2; row[0].Grapheme = "\u754c\u0301";
        // A host row can lack a normalized tail. Reflow synthesizes its payload.
        row[1].Codepoint = 'X'; row[1].Width = 1;
        screen.Resize(4, 2);
        TerminalRow result = screen.GetSnapshotRows(0)![0];
        Assert.Equal((1UL, 16UL), Usage(screen, result));
        Assert.Null(result.ReadOnlyCells[1].Grapheme);
    }

    [Fact]
    public void NonReflowBackfillStopsAtGraphemeMapPressureWithoutGrowingPreviousPage()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 0, 1, 0));
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        rows[1].SnapshotAllocation = new(new(8, 1, 8, 0, 17, 0));
        rows[1].SnapshotAllocationRow = 0;
        using BasicVtProcessor processor = new(screen);
        Process(processor, "A\u0301\u001b[2;1HB\u0302");
        processor.ResizeScreen(16, 2, 0, 0, reflowOnResize: false);
        Assert.NotSame(rows[0].SnapshotAllocation, rows[1].SnapshotAllocation);
        Assert.Equal(1U, rows[0].SnapshotAllocation!.Capacity.GraphemeBytes);
        Assert.Equal(17U, rows[1].SnapshotAllocation!.Capacity.GraphemeBytes);
        Assert.Equal((1UL, 16UL), Usage(screen, rows[0]));
        Assert.Equal((1UL, 16UL), Usage(screen, rows[1]));
    }

    [Fact]
    public void HostHistoryRetirementReleasesGraphemesWhileKeepingCowReaders()
    {
        TerminalScreen screen = Screen(new(8, 8, 8, 0, 1024, 0));
        using BasicVtProcessor processor = new(screen);
        Process(processor, "A\u0301\r\nB\u0302\r\nC");
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalRow current = screen.GetViewportRow(0);
        screen.ScrollbackLimit = 0;
        Assert.Equal((1UL, 16UL), Usage(screen, current));
        Assert.Equal((2UL, 32UL), Usage(retained, retained.GetSnapshotRows(0)![0]));
    }

    [Fact]
    public void FragmentedRestoreGrowthAndRepeatedMutationsMatchNativeCapacities()
    {
        RequireNative();
        byte[] snapshot = FragmentedSnapshot();
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        string[] inputs =
        [
            "\u001b[1;5H" + new string('\u0301', 57),
            "\u001b[1;1HX",
            "\u001b[1;2H\u001b[@\u001b[P",
            "\u001b[2;1H\u001bM",
            "\u001b[1;1H\u001b[1mQ\u0302\u001b[0;3mR\u001b[0;2m",
        ];
        foreach (string input in inputs)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            native.Write(bytes); managed.Processor.Process(bytes);
            Compare(native, managed);
        }
        foreach (ushort columns in new ushort[] { 4, 12, 8 })
        {
            native.Resize(columns, 2);
            managed.Processor.ResizeScreen(columns, 2, 0, 0);
            Compare(native, managed);
        }
        Assert.Equal(1024U, retained.GetSnapshotRows(0)![0].SnapshotAllocation!.Capacity.GraphemeBytes);
    }

    private static void Compare(GhosttyTerminal native, ManagedTerminalSnapshot managed)
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotScreen expected = reader.ReadReady().Screens[0];
        TerminalRowBuffer rows = managed.Screen.GetSnapshotRows(0)!;
        int count = 0;
        foreach (GhosttySnapshotPage page in expected.Pages) count += page.Grid.Rows;
        int index = rows.Count - count;
        Assert.True(index >= 0);
        foreach (GhosttySnapshotPage page in expected.Pages)
        foreach (TerminalRow reference in GhosttySnapshotLivePage.Decode(page, new TerminalScreen(managed.Screen.Columns, 2)))
        {
            TerminalRow actual = rows[index++];
            Assert.Equal(page.Capacity.Styles, actual.SnapshotAllocation!.Capacity.Styles);
            Assert.Equal(page.Capacity.GraphemeBytes, actual.SnapshotAllocation!.Capacity.GraphemeBytes);
            for (int column = 0; column < reference.Columns; column++)
            {
                Assert.Equal(reference.ReadOnlyCells[column].Codepoint, actual.ReadOnlyCells[column].Codepoint);
                Assert.Equal(reference.ReadOnlyCells[column].Grapheme, actual.ReadOnlyCells[column].Grapheme);
            }
        }
    }

    private static TerminalScreen Screen(GhosttySnapshotPageCapacity capacity)
    {
        TerminalScreen owner = new(8, 2);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(8, 2, 10000, owner.Theme);
        GhosttySnapshotPageAllocation page = new(capacity);
        screen.InstallSnapshotRows([new(8) { SnapshotAllocation = page }, new(8) { SnapshotAllocation = page, SnapshotAllocationRow = 1 }], null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        return screen;
    }

    private static (ulong Cells, ulong Bytes) Usage(TerminalScreen screen, TerminalRow member)
    {
        List<TerminalRow> group = [];
        foreach (TerminalRow row in screen.GetSnapshotRows(0)!)
            if (ReferenceEquals(row.SnapshotAllocation, member.SnapshotAllocation)) group.Add(row);
        Assert.True(screen.TryGetSnapshotGraphemeUsage(member.SnapshotAllocation!, group, out ulong cells, out ulong bytes));
        return (cells, bytes);
    }

    private static byte[] FragmentedSnapshot()
    {
        using BasicVtProcessor source = new(new TerminalScreen(8, 2));
        Process(source, "A" + new string('\u0301', 64) + "B" + new string('\u0302', 64) + "C" + new string('\u0303', 8) + "D" + new string('\u0304', 4));
        using GhosttySnapshotRecordReader reader = new(source.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Page) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1024);
            records.Add(new(tag, bytes));
            if (tag == GhosttySnapshotRecordTag.Finish) return SnapshotTestRecords.Encode(records);
        }
    }

    private static void Process(BasicVtProcessor processor, string input) => processor.Process(Encoding.UTF8.GetBytes(input));

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
