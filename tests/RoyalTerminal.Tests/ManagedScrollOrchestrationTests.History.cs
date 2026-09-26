// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Runtime.InteropServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Screen.cursorScrollAboveRotate advances the cursor before rotating
// page-local suffixes tail-first and cloning their boundary rows. WT pans its
// viewport then scrolls the stationary suffix; xterm.js BufferService.scroll
// inserts a BufferLine. Neither defines Ghostty's PAGE allocation contract.
public sealed partial class ManagedScrollOrchestrationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void HistoryRotationKeepsAllocationsContiguousAndRecyclesOnlyPageLocalStorage(int pageRows)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(ContentRows(), pageRows));
        Process(terminal.Processor, "\u001b[1;2r\u001b[2;4H");
        TerminalRow[] before = ViewportRows(terminal.Screen);
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        TerminalScreenAnchor moving = terminal.Screen.CreateAnchor(1, 3);
        TerminalScreenAnchor stationary = terminal.Screen.CreateAnchor(3, 3);

        terminal.Processor.Process("\n"u8);

        Assert.Equal(5, terminal.Screen.TotalRows);
        Assert.Equal((3, 1), (terminal.Processor.CursorCol, terminal.Processor.CursorRow));
        Assert.Same(before[1], terminal.Screen.GetViewportRow(0));
        TerminalRow blank = terminal.Screen.GetViewportRow(1);
        Assert.Same(before[pageRows == 1 ? 2 : 3], blank);
        Assert.Equal(pageRows == 1 ? 0 : 3 % pageRows, blank.SnapshotAllocationRow);
        Assert.False(blank.WrapsToNext);
        Assert.False(blank.IsWrapContinuation);
        Assert.Equal(TerminalSemanticPrompt.None, blank.SemanticPrompt);
        AssertRow(retained.GetViewportRow(2), terminal.Screen.GetViewportRow(2));
        AssertRow(retained.GetViewportRow(3), terminal.Screen.GetViewportRow(3));
        if (pageRows > 1)
        {
            Assert.Same(before[2], terminal.Screen.GetViewportRow(2));
            Assert.Same(retained.GetViewportRow(2).SearchStorageIdentity,
                terminal.Screen.GetViewportRow(2).SearchStorageIdentity);
        }
        Assert.True(terminal.Screen.TryResolveAnchor(moving, out TerminalGridPosition moved));
        Assert.Equal(new TerminalGridPosition(3, 1), moved);
        Assert.True(terminal.Screen.TryResolveAnchor(stationary, out TerminalGridPosition fixedPosition));
        Assert.Equal(new TerminalGridPosition(3, 4), fixedPosition);
        AssertContiguousPages(terminal.Screen);
        for (int row = 0; row < 4; row++) Assert.Equal('A' + row, retained.GetViewportRow(row).ReadOnlyCells[3].Codepoint);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void UntrackedHistoryRotationPreservesStationaryAnchorsAndRasterAcrossHostPruning(int history)
    {
        TerminalScreen screen = new(8, 4, history);
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[3;1HC\u001b[4;1HD\u001b[1;2r\u001b[2;1H");
        TerminalRow stationary = screen.GetViewportRow(3);
        TerminalScreenAnchor anchor = screen.CreateAnchor(3, 0);
        screen.ReplaceRasterImage(new(1, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 0, 255]),
            new(1, TerminalRasterImageLayer.AboveText, 7, 3, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1));
        TerminalScreen retained = screen.CreateStateCopy();

        processor.Process("\n\n\n\n\n"u8);

        Assert.False(screen.TracksSnapshotMetadata);
        Assert.Same(stationary, screen.GetViewportRow(3));
        Assert.Same(retained.GetViewportRow(3).SearchStorageIdentity, stationary.SearchStorageIdentity);
        Assert.Equal('C', screen.GetViewportRow(2).ReadOnlyCells[0].Codepoint);
        Assert.Equal('D', stationary.ReadOnlyCells[0].Codepoint);
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition position));
        Assert.Equal(new TerminalGridPosition(0, screen.TotalRows - 1), position);
        Assert.Equal(screen.TotalRows - 1, Assert.Single(screen.GetRasterImagePlacements().ToArray()).AnchorRow);
        Assert.Equal(Math.Min(9, 4 + history), screen.TotalRows);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void HeldHistoryRotationPublishesPageBoundariesAndRowsTogether(int pageRows)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(ContentRows(), pageRows));
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        Process(terminal.Processor, "\u001b[?2026h\u001b[1;2r\u001b[2;4H\n\n");
        Assert.Equal(4, terminal.Screen.TotalRows);
        for (int row = 0; row < 4; row++) AssertRow(retained.GetViewportRow(row), terminal.Screen.GetViewportRow(row));

        Process(terminal.Processor, "\u001b[?2026l");

        Assert.Equal(6, terminal.Screen.TotalRows);
        AssertContiguousPages(terminal.Screen);
        AssertRow(retained.GetViewportRow(2), terminal.Screen.GetViewportRow(2));
        AssertRow(retained.GetViewportRow(3), terminal.Screen.GetViewportRow(3));
        for (int row = 0; row < 4; row++) Assert.Equal('A' + row, retained.GetViewportRow(row).ReadOnlyCells[3].Codepoint);
    }

    [Theory]
    [InlineData(1, 2, "\n")]
    [InlineData(2, 2, "\n")]
    [InlineData(4, 2, "\n")]
    [InlineData(1, 3, "\n")]
    [InlineData(2, 3, "\n")]
    [InlineData(4, 3, "\n")]
    [InlineData(1, 2, "\u001bD")]
    [InlineData(2, 2, "\u001bD")]
    [InlineData(4, 2, "\u001bD")]
    [InlineData(1, 3, "\u001bD")]
    [InlineData(2, 3, "\u001bD")]
    [InlineData(4, 3, "\u001bD")]
    [InlineData(1, 2, "\u001b[2S")]
    [InlineData(2, 2, "\u001b[2S")]
    [InlineData(4, 2, "\u001b[2S")]
    [InlineData(1, 3, "\u001b[2S")]
    [InlineData(2, 3, "\u001b[2S")]
    [InlineData(4, 3, "\u001b[2S")]
    public void TopOriginHistoryRotationMatchesNativeCursorCapacityAndContinuation(int pageRows, int bottom, string command)
    {
        RequireNative();
        byte[] snapshot = Snapshot(ContentRows(), pageRows);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        string setup = $"\u001b[1;{bottom}r\u001b[{bottom};4H\u001b[44m\u001b]8;;u\a";
        // The first iteration allocates a fresh tail; later iterations reuse
        // it. Suffix clones can grow their own pages without moving the pen to
        // the tail, and subsequent writes exercise the retained physical slots.
        foreach (string input in new[] { setup, command, command, command,
            "X\u0301\u001b]8;;\a", "\u001b[0m\u001b[3;5H\u0301", command })
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            native.Write(bytes);
            managed.Processor.Process(bytes);
            AssertNative(native, managed, Convert.ToHexString(bytes));
            AssertContiguousPages(managed.Screen);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void PageEntryStyleGrowthRestoresIncomingHyperlinkOnceBeforeMigration(bool explicitId, bool provisioned, bool upward)
    {
        RequireNative();
        byte[] snapshot = Snapshot(ContentRows(), 2);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        int source = upward ? 3 : 2, destination = upward ? 2 : 3;
        string allocate = provisioned ? $"\u001b[{destination};4H\u001b]8;id=seed;seed\a\u001b]8;;\a" : "";
        string setup = allocate + $"\u001b[{source};4H\u001b[44m\u001b]8;{(explicitId ? "id=active" : "")};u\a";
        byte[] bytes = Encoding.UTF8.GetBytes(setup);
        native.Write(bytes);
        managed.Processor.Process(bytes);
        AssertNative(native, managed);
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        GhosttySnapshotPageAllocation oldDestination = retained.GetViewportRow(destination - 1).SnapshotAllocation!;

        bytes = Encoding.UTF8.GetBytes($"\u001b[{destination};4H");
        native.Write(bytes);
        managed.Processor.Process(bytes);

        AssertNative(native, managed);
        GhosttySnapshotScreenState cursor = Cursor(managed.Processor);
        Assert.Equal(provisioned, cursor.TryGetHyperlink(out _));
        Assert.Equal(explicitId ? 0U : provisioned ? 2U : 1U, cursor.HyperlinkImplicitCounter);
        Assert.NotSame(oldDestination, managed.Screen.GetViewportRow(destination - 1).SnapshotAllocation);
        Assert.Same(oldDestination, retained.GetViewportRow(destination - 1).SnapshotAllocation);
        native.Write("X"u8);
        managed.Processor.Process("X"u8);
        AssertNative(native, managed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryRotationAndWholePageByteEvictionMatchNative(bool limitAfterFirstRotation)
    {
        RequireNative();
        byte[] snapshot = Snapshot(ContentRows(), 1);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        int alignment = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096;
        GhosttySnapshotAllocation layout = new(alignment);
        string setup = "\u001b[1;2r\u001b[2;4H\u001b[44m\u001b]8;;u\a" + (limitAfterFirstRotation ? "\n" : "");
        native.Write(Encoding.UTF8.GetBytes(setup));
        Process(managed.Processor, setup);
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        TerminalScreenAnchor stationary = managed.Screen.CreateAnchor(managed.Screen.TotalRows - 1, 3);
        ulong budget = layout.StandardPageBytes * 2;
        native.SetScrollbackMaxBytes((nuint)budget);
        managed.Screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment, MaximumBytes = budget };

        for (int iteration = 0; iteration < 6; iteration++)
        {
            native.Write("\n"u8);
            managed.Processor.Process("\n"u8);
            Assert.Equal(native.GetTotalRows(), (nuint)managed.Screen.TotalRows);
            AssertNative(native, managed);
            AssertContiguousPages(managed.Screen);
            Assert.True(managed.Screen.TryResolveAnchor(stationary, out TerminalGridPosition position));
            Assert.Equal(new TerminalGridPosition(3, managed.Screen.TotalRows - 1), position);
            Assert.Equal('D', managed.Screen.GetViewportRow(3).ReadOnlyCells[3].Codepoint);
        }
        Assert.Equal(limitAfterFirstRotation ? 5 : 4, retained.TotalRows);
        Assert.Equal('D', retained.GetViewportRow(3).ReadOnlyCells[3].Codepoint);
    }

    private static void AssertContiguousPages(TerminalScreen screen)
    {
        HashSet<GhosttySnapshotPageAllocation> completed = [];
        GhosttySnapshotPageAllocation? previous = null;
        foreach (TerminalRow row in screen.GetSnapshotRows(0)!)
        {
            GhosttySnapshotPageAllocation page = Assert.IsType<GhosttySnapshotPageAllocation>(row.SnapshotAllocation);
            if (ReferenceEquals(page, previous)) continue;
            Assert.DoesNotContain(page, completed);
            if (previous is not null) completed.Add(previous);
            previous = page;
        }
    }
}
