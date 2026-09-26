// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Screen.setAttribute/manualStyleUpdate allocates each intermediate pen,
// not just the final SGR state or the style of the next printed cell. WT ROW and
// xterm.js BufferLine don't define this PAGE-capacity/history-admission contract.
public sealed class ManagedSnapshotStyleMutationTests
{
    private static GhosttySnapshotStyle Bold => new(default, default, default, 1);
    private static GhosttySnapshotStyle Italic => new(default, default, default, 2);

    [Theory]
    [InlineData("\u001b[1;0m\u001b[2K")]
    [InlineData("\u001b[38:2::12:34:56;0m")]
    [InlineData("\u001b[4:3;0m")]
    public void IntermediatePenGrowthSurvivesResetAndEraseWithoutAnAdmissionCheckpoint(string input)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot());
        Assert.Equal(0, Capacity(terminal.Screen).Styles);
        terminal.Processor.Process(Encoding.UTF8.GetBytes(input));
        Assert.Equal(16, Capacity(terminal.Screen).Styles);
        _ = GhosttySnapshotLiveAllocation.Measure(terminal.Screen, terminal.Screen.GetSnapshotRows(0)!, new(4096));
        Assert.Equal(16, Capacity(terminal.Screen).Styles);
    }

    [Fact]
    public void PartialCsiDoesNotAllocateUntilItsFinalAndNoOpStylesDoNotReplaceThePage()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot());
        GhosttySnapshotPageAllocation original = Page(terminal.Screen);
        terminal.Processor.Process("\u001b[1;"u8);
        Assert.Same(original, Page(terminal.Screen));
        terminal.Processor.Process("0m"u8);
        Assert.Equal(16, Capacity(terminal.Screen).Styles);
        GhosttySnapshotPageAllocation grown = Page(terminal.Screen);
        terminal.Processor.Process("\u001b[0;39;49;999m"u8);
        Assert.Same(grown, Page(terminal.Screen));
    }

    [Fact]
    public void FullCellStyleSetGrowsAtSgrBeforePrintingAndKeepsItsCapacityAfterErase()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(4, [1, 2, 0, 0]));
        terminal.Processor.Process("\u001b[2;0m\u001b[2K"u8);
        Assert.Equal(8, Capacity(terminal.Screen).Styles);
        foreach (ref readonly TerminalCell cell in terminal.Screen.GetViewportRow(0).ReadOnlyCells)
            Assert.Equal(CellAttributes.None, cell.Attributes);
        terminal.Processor.Process("\u001b[1;0m"u8);
        Assert.Equal(8, Capacity(terminal.Screen).Styles);
    }

    [Fact]
    public void DeadInteriorEntryRehashesWithoutGrowingAndLiveWritesOwnTheirReferences()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(4, [2, 0, 0, 0]));
        GhosttySnapshotPageAllocation initial = Page(terminal.Screen);
        terminal.Processor.Process("\u001b[2m"u8);
        Assert.Equal(4, Capacity(terminal.Screen).Styles);
        Assert.NotSame(initial, Page(terminal.Screen));
        terminal.Processor.Process("\u001b[1;2HX\u001b[0;1m"u8);
        Assert.Equal(8, Capacity(terminal.Screen).Styles);
    }

    [Fact]
    public void StyleOnlyMutationsForkAllocationStateWithoutCopyingCellArrays()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot());
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        object cells = retained.GetViewportRow(0).SearchStorageIdentity;
        terminal.Processor.Process("\u001b[1;0m"u8);
        Assert.Equal(0, Capacity(retained).Styles);
        Assert.Equal(16, Capacity(terminal.Screen).Styles);
        Assert.Same(cells, terminal.Screen.GetViewportRow(0).SearchStorageIdentity);
        using BasicVtProcessor sibling = new(retained);
        sibling.Process("\u001b[3;0m"u8);
        Assert.Equal(16, Capacity(retained).Styles);
        Assert.NotSame(Page(retained), Page(terminal.Screen));
    }

    [Fact]
    public void SynchronizedOutputKeepsAllocationChangesPrivateUntilPublication()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot());
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        terminal.Processor.Process("\u001b[?2026h\u001b[1;0m"u8);
        Assert.Equal(0, Capacity(terminal.Screen).Styles);
        terminal.Processor.Process("\u001b[?2026l"u8);
        Assert.Equal(16, Capacity(terminal.Screen).Styles);
        Assert.Equal(0, Capacity(retained).Styles);
    }

    [Fact]
    public void OrdinaryNonSnapshotTerminalDoesNotCreateAnAllocationTracker()
    {
        TerminalScreen screen = new(4, 1);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1mA\u001b[0m\u001b[2K"u8);
        Assert.Null(screen.GetViewportRow(0).SnapshotAllocation);
        Assert.False(screen.TracksSnapshotMetadata);
    }

    [Fact]
    public void UnrepresentableStyleAccountingRejectsHistoryUntilARepresentableMutationCheckpoint()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot());
        TerminalRow row = terminal.Screen.GetViewportRow(0);
        row.SnapshotAllocation = new(row.SnapshotAllocation!.Capacity, metadataOverflow: true);
        row.SnapshotAllocationUnmodified = true;
        GhosttySnapshotAllocation layout = new(4096);
        Assert.Equal(ulong.MaxValue, GhosttySnapshotLiveAllocation.Measure(terminal.Screen, terminal.Screen.GetSnapshotRows(0)!, layout));
        row.Clear();
        Assert.Equal(layout.StandardPageBytes, GhosttySnapshotLiveAllocation.Measure(terminal.Screen, terminal.Screen.GetSnapshotRows(0)!, layout));
        Assert.False(row.SnapshotAllocation.MetadataOverflow);
    }

    [Fact]
    public void ScreenCursorStyleIsAllocatedEvenWhenTheRestoredPageHasNoStyledCells()
    {
        using BasicVtProcessor source = new(new TerminalScreen(4, 1));
        source.Process("\u001b[1m"u8);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(source.GetBinarySnapshot());
        Assert.Equal(16, Capacity(restored.Screen).Styles);
        restored.Processor.Process("\u001b[0m"u8);
        Assert.Equal(16, Capacity(restored.Screen).Styles);
    }

    [Fact]
    public void CursorAllocationAndTransientStyledWritesMatchNativeCapacityHints()
    {
        RequireNative();
        (byte[] Snapshot, string[] Writes)[] cases =
        [
            (Snapshot(), ["\u001b[1;0m", "\u001b[38:2::12:34:56;0m", "\u001b[4:3;0m"]),
            (Snapshot(4, [1, 2, 0, 0]), ["\u001b[2;0m\u001b[2K", "\u001b[1mX\u001b[0m"]),
            (Snapshot(4, [2, 0, 0, 0]), ["\u001b[2m", "\u001b[1;2HX\u001b[0;1m", "\u001b[0m\u001b[2K"]),
            (Snapshot(4, [1, 0, 0, 0]), ["\u001b[2m", "\u001b[1;2HX\u001b[0;3m"]),
        ];
        foreach ((byte[] snapshot, string[] writes) in cases)
        {
            using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
            using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
            foreach (string write in writes)
            {
                byte[] input = Encoding.UTF8.GetBytes(write);
                native.Write(input); managed.Processor.Process(input);
                using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
                GhosttySnapshotPage actual = Assert.Single(reader.ReadReady().Screens[0].Pages);
                Assert.Equal(actual.Capacity.Styles, Capacity(managed.Screen).Styles);
                for (int i = 0; i < 4; i++)
                {
                    actual.TryGetStyle((ushort)(actual.Grid.Cells[i] >> 26), out GhosttySnapshotStyle style);
                    Assert.Equal(style, GhosttySnapshotLivePage.EncodeStyle(managed.Screen.GetViewportRow(0).ReadOnlyCells[i]));
                }
            }
        }
    }

    private static GhosttySnapshotPageAllocation Page(TerminalScreen screen) => screen.GetViewportRow(0).SnapshotAllocation!;
    private static GhosttySnapshotPageCapacity Capacity(TerminalScreen screen) => Page(screen).Capacity;

    private static byte[] Snapshot(ushort capacity = 0, ushort[]? styles = null)
    {
        using MemoryStream page = new();
        using (BinaryWriter writer = new(page, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)4); writer.Write((ushort)1);
            writer.Write(styles is null ? (ushort)0 : (ushort)2); writer.Write((ushort)0);
            writer.Write(capacity); writer.Write((ushort)0); writer.Write(0U); writer.Write(0U);
            if (styles is not null)
            {
                Span<byte> encoded = stackalloc byte[16];
                writer.Write((ushort)1); Bold.Write(encoded); page.Write(encoded);
                writer.Write((ushort)2); Italic.Write(encoded); page.Write(encoded);
            }
            writer.Write((byte)0x30); writer.Write((ushort)4);
            for (int i = 0; i < 4; i++) writer.Write(((ulong)'A' << 2) | ((ulong)(styles?[i] ?? 0) << 26));
            writer.Write(0U);
        }
        using BasicVtProcessor source = new(new TerminalScreen(4, 1));
        using GhosttySnapshotRecordReader reader = new(source.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            records.Add(new(tag, tag == GhosttySnapshotRecordTag.Page ? page.ToArray() : payload.ToArray()));
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
