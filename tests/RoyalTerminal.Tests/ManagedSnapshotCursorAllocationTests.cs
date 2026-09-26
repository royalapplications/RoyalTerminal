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

// Ghostty Screen.cursorChangePin migrates the cursor reference between pages;
// Terminal.restoreCursor and Screen.cursorCopy apply style before position.
// WT/xterm.js cursor operations have no Ghostty PAGE allocator contract to reuse.
public sealed class ManagedSnapshotCursorAllocationTests
{
    [Theory]
    [InlineData("\u001b[2;1H")]
    [InlineData("\u001b[1B")]
    [InlineData("\u001b[2d")]
    [InlineData("\n")]
    [InlineData("\u001bD")]
    [InlineData("\u001bE")]
    [InlineData("12345")]
    public void UnchangedPenAllocatesOnEnteringAnotherPage(string movement)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(initial: "\u001b[1m"));
        Assert.Equal(new ushort[] { 16, 0 }, Capacities(terminal.Screen));
        terminal.Processor.Process(Encoding.UTF8.GetBytes(movement));
        Assert.Equal(new ushort[] { 16, 16 }, Capacities(terminal.Screen));
    }

    [Fact]
    public void DefaultPenCrossingDoesNotAllocateAndReverseIndexMigratesABoldPen()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot());
        terminal.Processor.Process("\u001b[2;1H"u8);
        Assert.Equal(new ushort[] { 0, 0 }, Capacities(terminal.Screen));
        terminal.Processor.Process("\u001b[1m\u001bM"u8);
        Assert.Equal(new ushort[] { 16, 16 }, Capacities(terminal.Screen));
    }

    [Fact]
    public void RestoreAppliesSavedStyleOnDepartureBeforeMovingToDestination()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot());
        terminal.Processor.Process("\u001b[1m\u001b7\u001b[0m\u001b[2;1H"u8);
        Assert.Equal(new ushort[] { 16, 0 }, Capacities(terminal.Screen));
        terminal.Processor.Process("\u001b8"u8);
        Assert.Equal(new ushort[] { 16, 16 }, Capacities(terminal.Screen));
        Assert.Equal(0, terminal.Processor.CursorRow);
    }

    [Fact]
    public void RestoreWithoutSavedCursorReleasesOldStyleBeforeMoving()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(initial: "\u001b[2;1H\u001b[1m"));
        Assert.Equal(new ushort[] { 0, 16 }, Capacities(terminal.Screen));
        terminal.Processor.Process("\u001b8"u8);
        Assert.Equal(new ushort[] { 0, 16 }, Capacities(terminal.Screen));
        Assert.Equal(0, terminal.Processor.CursorRow);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(1047)]
    [InlineData(1049)]
    public void AlternateEntryCopiesThePenAtItsDormantCursorBeforeMoving(int mode)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(withAlternate: true));
        terminal.Processor.Process(Encoding.UTF8.GetBytes($"\u001b[1m\u001b[2;1H\u001b[?{mode}h"));
        Assert.True(terminal.Screen.AlternateBufferActive);
        Assert.Equal(new ushort[] { 16, 16 }, Capacities(terminal.Screen));
        Assert.Equal(1, terminal.Processor.CursorRow);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(1047)]
    public void PrimaryEntryCopiesThePenAtItsDormantCursorBeforeMoving(int mode)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(withAlternate: true));
        terminal.Processor.Process(Encoding.UTF8.GetBytes($"\u001b[?{mode}h\u001b[2;1H\u001b[1m\u001b[?{mode}l"));
        Assert.False(terminal.Screen.AlternateBufferActive);
        Assert.Equal(new ushort[] { 16, 16 }, Capacities(terminal.Screen));
        Assert.Equal(1, terminal.Processor.CursorRow);
    }

    [Fact]
    public void Mode1049ReturnDoesNotAllocateTheDepartingAlternatePenOnPrimary()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(withAlternate: true));
        terminal.Processor.Process("\u001b[?1049h\u001b[2;1H\u001b[1m\u001b[?1049l"u8);
        Assert.False(terminal.Screen.AlternateBufferActive);
        Assert.Equal(new ushort[] { 0, 0 }, Capacities(terminal.Screen));
        Assert.Equal(0, terminal.Processor.CursorRow);
        terminal.Processor.Process("X"u8);
        Assert.Equal(CellAttributes.None, terminal.Screen.GetViewportRow(0).ReadOnlyCells[0].Attributes);
    }

    [Fact]
    public void Mode1049RestoreStartsAtTheDormantPrimaryPositionNotTheAlternatePosition()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(withAlternate: true));
        terminal.Processor.Process("\u001b[1m\u001b7\u001b[0m\u001b[2;1H\u001b[?47h\u001b[1;1H\u001b[3m"u8);
        Assert.Equal(new ushort[] { 16, 0 }, Capacities(terminal.Screen, 0));
        terminal.Processor.Process("\u001b[?1049l"u8);
        Assert.Equal(new ushort[] { 16, 16 }, Capacities(terminal.Screen));
        Assert.Equal(0, terminal.Processor.CursorRow);
    }

    [Fact]
    public void CompoundModeChangesAccountForMovementBeforeSwitchingScreens()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(
            Snapshot(initial: "\u001b[2;1H\u001b[1m", withAlternate: true));
        terminal.Processor.Process("\u001b[?6;47h"u8);
        Assert.Equal(new ushort[] { 16, 16 }, Capacities(terminal.Screen, 0));
        Assert.Equal(new ushort[] { 16, 0 }, Capacities(terminal.Screen, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SixelCursorAdvanceMigratesThePenOnlyOutsideDisplayMode(bool displayMode)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(initial: "\u001b[1m"));
        terminal.Processor.SixelGraphicsEnabled = true;
        terminal.Processor.NotifyResize(4, 2, 40, 24);
        if (displayMode) terminal.Processor.Process("\u001b[?80h"u8);
        terminal.Processor.Process("\u001bPq#1;2;100;0;0#1@-@\u001b\\"u8);
        Assert.True(terminal.Screen.HasRasterGraphics);
        Assert.Equal(displayMode ? 0 : 1, terminal.Processor.CursorRow);
        Assert.Equal(new ushort[] { 16, displayMode ? (ushort)0 : (ushort)16 }, Capacities(terminal.Screen));
    }

    [Fact]
    public void MigratedCursorDoesNotLeaveAnExtraReferenceOnTheOldPage()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(styledFirstPage: true, initial: "\u001b[1m"));
        terminal.Processor.Process("\u001b[2;1H\u001b[0m\u001b[1;1H\u001b[1X\u001b[2m"u8);
        // Only italic remains in the old grid. A leaked bold cursor reference
        // would force growth instead of same-capacity dead-entry rehash.
        Assert.Equal(new ushort[] { 4, 16 }, Capacities(terminal.Screen));
    }

    [Fact]
    public void CursorMigrationAndHeldPublicationHaveIndependentAllocationOwnership()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(initial: "\u001b[1m"));
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        terminal.Processor.Process("\u001b[?2026h\u001b[2;1H"u8);
        Assert.Equal(new ushort[] { 16, 0 }, Capacities(terminal.Screen));
        terminal.Processor.Process("\u001b[?2026l"u8);
        Assert.Equal(new ushort[] { 16, 16 }, Capacities(terminal.Screen));
        Assert.Equal(new ushort[] { 16, 0 }, Capacities(retained));
    }

    [Fact]
    public void DiscardingAnAlternateCursorLeavesItsCowOwnerIntact()
    {
        GhosttySnapshotPageTracker tracker = new();
        TerminalRow row = new(4) { SnapshotAllocation = new(new(4, 1, 16, 0, 0, 0)) };
        TerminalRowBuffer rows = new(1);
        rows.Add(row);
        GhosttySnapshotStyle bold = new(default, default, default, 1);
        uint counter = 0;
        Assert.True(tracker.ChangeCursor(rows, 1, row, default, bold, new(4096), new(4, 1), ref counter));
        GhosttySnapshotPageTracker retained = tracker.Copy();
        tracker.DiscardCursor(1);
        Assert.False(tracker.IsCurrent(1, row, bold));
        Assert.True(retained.IsCurrent(1, row, bold));
    }

    [Fact]
    public void HostClearVisibleHistoryRebindsTheCursorWithoutChangingRetainedRows()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(initial: "\u001b[2;1H\u001b[1m"));
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        terminal.Processor.ClearVisibleHistory();
        Assert.Equal(0, terminal.Processor.CursorRow);
        Assert.True(terminal.Screen.SnapshotCursorStyleIsCurrent(0, 0, new(default, default, default, 1)));
        Assert.Equal(new ushort[] { 0, 16 }, Capacities(retained));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetRebindsTheDefaultPenAndDiscardsTheAlternateCursor(bool newSession)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(withAlternate: true));
        terminal.Processor.Process("\u001b[?47h\u001b[1m\u001b[2;1H"u8);
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        if (newSession) terminal.Processor.PrepareForNewSession(preserveScrollback: false);
        else terminal.Processor.Reset();
        Assert.False(terminal.Screen.AlternateBufferActive);
        Assert.Null(terminal.Screen.GetSnapshotRows(1));
        Assert.True(terminal.Screen.SnapshotCursorStyleIsCurrent(0, 0, default));
        Assert.Equal(new ushort[] { 16, 16 }, Capacities(retained, 1));
    }

    [Fact]
    public void AppendedRowsConsumeTailSlotsBeforeCreatingAnotherPage()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(initial: "\u001b[1m"));
        terminal.Processor.Process("\u001b[2;1H\n"u8);
        TerminalRowBuffer rows = terminal.Screen.GetSnapshotRows(0)!;
        GhosttySnapshotPageAllocation tail = rows[rows.Count - 1].SnapshotAllocation!;
        Assert.Equal(0, rows[rows.Count - 1].SnapshotAllocationRow);
        int room = tail.Capacity.Rows;
        for (int i = 1; i < room; i++) terminal.Processor.Process("\n"u8);
        Assert.Same(tail, rows[rows.Count - 1].SnapshotAllocation);
        Assert.Equal(room - 1, rows[rows.Count - 1].SnapshotAllocationRow);
        terminal.Processor.Process("\n"u8);
        Assert.NotSame(tail, rows[rows.Count - 1].SnapshotAllocation);
        Assert.Equal(0, rows[rows.Count - 1].SnapshotAllocationRow);
    }

    [Fact]
    public void TailSlotWatermarksIncludeCheckpointAssignedAndRotatedRows()
    {
        TerminalScreen screen = new(80, 4, 5000) { SnapshotScrollbackQuota = new() };
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1m"u8);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        screen.AddRow(); screen.AddRow();
        _ = GhosttySnapshotLiveAllocation.Measure(screen, rows, new(4096));
        TerminalRow first = rows[0]; rows[0] = rows[rows.Count - 1]; rows[rows.Count - 1] = first;
        processor.Process("\u001b[4;1H\n"u8);
        Assert.Equal(6, rows[rows.Count - 1].SnapshotAllocationRow);
    }

    [Fact]
    public void CursorTransitionsAndRestoreOrderingMatchNativeCapacities()
    {
        RequireNative();
        (bool Styled, string Initial, string[] Writes)[] cases =
        [
            (false, "\u001b[1m", ["\u001b[2;1H", "\u001bM", "12345", "\u001b[1;1H"]),
            (false, "", ["\u001b[1m\u001b7\u001b[0m\u001b[2;1H", "\u001b8"]),
            (false, "\u001b[2;1H\u001b[1m", ["\u001b8"]),
            (true, "\u001b[1m", ["\u001b[2;1H", "\u001b[0m\u001b[1;1H\u001b[1X\u001b[2m"]),
        ];
        foreach ((bool styled, string initial, string[] writes) in cases)
        {
            byte[] snapshot = Snapshot(styled, initial);
            using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
            using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
            foreach (string write in writes)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(write);
                native.Write(bytes); managed.Processor.Process(bytes);
                using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
                GhosttySnapshotScreen screen = reader.ReadReady().Screens[0];
                Assert.Equal(2, screen.Pages.Count);
                Assert.Equal(screen.Pages[0].Capacity.Styles, Capacities(managed.Screen)[0]);
                Assert.Equal(screen.Pages[1].Capacity.Styles, Capacities(managed.Screen)[1]);
                Assert.Equal(screen.State.CursorY, managed.Processor.CursorRow);
            }
        }
    }

    [Fact]
    public void ScreenSwitchOrderingMatchesNativeCapacitiesOnBothScreens()
    {
        RequireNative();
        string[] inputs =
        [
            "\u001b[1m\u001b[2;1H\u001b[?47h",
            "\u001b[1m\u001b[2;1H\u001b[?1047h",
            "\u001b[1m\u001b[2;1H\u001b[?1049h",
            "\u001b[?47h\u001b[2;1H\u001b[1m\u001b[?47l",
            "\u001b[?1047h\u001b[2;1H\u001b[1m\u001b[?1047l",
            "\u001b[?1049h\u001b[2;1H\u001b[1m\u001b[?1049l",
            "\u001b[1m\u001b7\u001b[0m\u001b[2;1H\u001b[?47h\u001b[1;1H\u001b[3m\u001b[?1049l",
            "\u001b[2;1H\u001b[1m\u001b[?6;47h",
        ];
        foreach (string input in inputs)
        {
            byte[] snapshot = Snapshot(withAlternate: true);
            using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
            using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            native.Write(bytes); managed.Processor.Process(bytes);
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotReadyState ready = reader.ReadReady();
            foreach (GhosttySnapshotScreen screen in ready.Screens)
            {
                Assert.Equal(2, screen.Pages.Count);
                Assert.Equal(new[] { screen.Pages[0].Capacity.Styles, screen.Pages[1].Capacity.Styles },
                    Capacities(managed.Screen, screen.State.Key));
            }
        }
    }

    private static ushort[] Capacities(TerminalScreen screen, int? key = null)
    {
        TerminalRowBuffer rows = screen.GetSnapshotRows(key ?? (screen.AlternateBufferActive ? 1 : 0))!;
        return [rows[rows.Count - 2].SnapshotAllocation!.Capacity.Styles, rows[rows.Count - 1].SnapshotAllocation!.Capacity.Styles];
    }

    private static byte[] Snapshot(bool styledFirstPage = false, string initial = "", bool withAlternate = false)
    {
        TerminalScreen sourceScreen = new(4, 2, 20000);
        using BasicVtProcessor source = new(sourceScreen);
        if (withAlternate) source.Process("\u001b[?47h\u001b[?47l"u8);
        source.Process(Encoding.UTF8.GetBytes(initial));
        TerminalRow first = new(4), second = new(4);
        if (styledFirstPage)
        {
            first[0].Codepoint = 'A'; first[0].Attributes = CellAttributes.Bold;
            first[1].Codepoint = 'B'; first[1].Attributes = CellAttributes.Italic;
        }
        byte[] Page(TerminalRow row)
        {
            using MemoryStream output = new();
            GhosttySnapshotLivePage.Capture([row], sourceScreen, 4).WritePayloadTo(output);
            return output.ToArray();
        }
        using GhosttySnapshotRecordReader reader = new(source.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Screen) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 2);
            if (tag == GhosttySnapshotRecordTag.Page)
            {
                records.Add(new(tag, Page(first))); records.Add(new(tag, Page(second)));
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
