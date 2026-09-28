// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty PageList.setMaxBytes/setMaxLines, grow and Limits.enforce define
// whole-page pruning and active-boundary protection. WT's circular TextBuffer
// and xterm.js Buffer.resize trim individual rows. Retain that independent host
// hard cap, but apply Ghostty's allocation policy at its native checkpoints.
public sealed class ManagedSnapshotQuotaEvictionTests
{
    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void LoweringBytesEvictsWholeHistoryAndAdjustsAnchorsOnce(int alignment)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        TerminalScreen screen = Screen(2, Pages(new(80, 2, 0, 0, 0, 0), 2, 2, 2, 2, 2));
        TerminalRow kept = screen.GetSnapshotRows(0)![6];
        TerminalScreenAnchor removed = screen.CreateAnchor(0, 1), retained = screen.CreateAnchor(6, 2);
        screen.ScrollOffset = 4;
        TerminalScreen publication = screen.CreateStateCopy();

        screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment, MaximumBytes = layout.StandardPageBytes };

        Assert.Equal(4, screen.TotalRows); // Effective minimum: two pooled pages.
        Assert.Same(kept, screen.GetSnapshotRows(0)![0]);
        Assert.False(screen.TryResolveAnchor(removed, out _));
        Assert.True(screen.TryResolveAnchor(retained, out TerminalGridPosition position));
        Assert.Equal(new(2, 0), position);
        Assert.Equal(2, screen.ScrollOffset);
        Assert.Equal(10, publication.TotalRows);
        Assert.True(publication.TryResolveAnchor(removed, out _));
        Assert.True(publication.TryResolveAnchor(retained, out position));
        Assert.Equal(new(2, 6), position);
        screen.SnapshotScrollbackQuota = null;
        Assert.Equal(4, screen.TotalRows); // Removed history is not resurrected.
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void LoweringLinesUsesMinimumAndWholePageUndershoot(int alignment)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        GhosttySnapshotPageCapacity capacity = layout.InitialCapacity(80);
        int pageRows = capacity.Rows;
        TerminalScreen screen = Screen(1, Pages(capacity, pageRows, pageRows, pageRows, pageRows, 1));
        TerminalRow retained = screen.GetSnapshotRows(0)![pageRows * 3];
        screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment, MaximumRows = (ulong)(pageRows + pageRows / 2) };
        Assert.Equal(pageRows + 1, screen.TotalRows);
        Assert.Same(retained, screen.GetSnapshotRows(0)![0]);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment, MaximumRows = 0 };
        Assert.Equal(pageRows + 1, screen.TotalRows); // One standard page still fits.
        screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment };
        screen.AddRow();
        Assert.Equal(pageRows + 2, screen.TotalRows);
    }

    [Fact]
    public void ZeroBytesPreservesOversizedActiveBoundaryButDisablesScrolling()
    {
        TerminalScreen screen = Screen(2, Pages(new(80, 8, 0, 0, 0, 2_000_000), 5));
        TerminalRow original = screen.GetSnapshotRows(0)![0];
        screen.ScrollOffset = 3;
        screen.SnapshotScrollbackQuota = new() { MaximumBytes = 0 };
        Assert.Equal(5, screen.TotalRows);
        Assert.Same(original, screen.GetSnapshotRows(0)![0]);
        Assert.Equal(0, screen.ScrollOffset);
        Assert.Equal(0, screen.MaxScrollOffset);
        screen.ScrollOffset = 3;
        Assert.Equal(0, screen.ScrollOffset);
        screen.SnapshotScrollbackQuota = new();
        screen.ScrollOffset = 3;
        Assert.Equal(3, screen.ScrollOffset);
    }

    [Fact]
    public void TailRowGrowthEnforcesLinesWithoutAllocatingANewPage()
    {
        GhosttySnapshotAllocation layout = new(4096);
        GhosttySnapshotPageCapacity capacity = layout.InitialCapacity(80);
        TerminalScreen screen = Screen(1, Pages(capacity, capacity.Rows, 1));
        TerminalRow firstActive = screen.GetViewportRow(0);
        GhosttySnapshotPageAllocation page = firstActive.SnapshotAllocation!;
        TerminalScreenAnchor historical = screen.CreateAnchor(0, 0), active = screen.CreateAnchor(capacity.Rows, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumRows = 0 };

        TerminalRow tail = screen.AddRow();

        Assert.Equal(2, screen.TotalRows);
        Assert.Same(firstActive, screen.GetSnapshotRows(0)![0]);
        Assert.Same(page, tail.SnapshotAllocation);
        Assert.False(screen.TryResolveAnchor(historical, out _));
        Assert.True(screen.TryResolveAnchor(active, out TerminalGridPosition position));
        Assert.Equal(new(0, 0), position);
    }

    [Fact]
    public void OverLimitActiveBoundaryWaitsUntilEntirePageBecomesHistory()
    {
        GhosttySnapshotAllocation layout = new(4096);
        int minimum = layout.InitialRows(80);
        GhosttySnapshotPageCapacity capacity = new(80, (ushort)(minimum + 2), 0, 0, 0, 0);
        TerminalScreen screen = Screen(1, Pages(capacity, minimum + 1));
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumRows = 0 };
        GhosttySnapshotPageAllocation original = screen.GetViewportRow(0).SnapshotAllocation!;
        screen.AddRow();
        Assert.Equal(minimum + 2, screen.TotalRows);
        Assert.Same(original, screen.GetViewportRow(0).SnapshotAllocation);
        TerminalRow tail = screen.AddRow();
        Assert.Equal(1, screen.TotalRows);
        Assert.NotSame(original, tail.SnapshotAllocation);
    }

    [Fact]
    public void PageGrowthRecyclesOnlyOnePageAndTailReuseDoesNotEnforceBytes()
    {
        GhosttySnapshotAllocation layout = new(4096);
        TerminalScreen screen = Screen(1, Pages(new(80, 2, 0, 0, 0, 0), 2, 2, 2));
        GhosttySnapshotScrollbackQuota quota = new() { PageAlignment = 4096, MaximumBytes = layout.StandardPageBytes * 3 };
        screen.SnapshotScrollbackQuota = quota;
        // Model an existing page enlarged by metadata, which is not itself a
        // byte-enforcement checkpoint. Keep that oversized page behind the first.
        GhosttySnapshotPageAllocation enlarged = new(new(80, 2, 0, 0, 0, 5_000_000));
        for (int index = 2; index < 4; index++) screen.GetSnapshotRows(0)![index].SnapshotAllocation = enlarged;
        TerminalRow retained = screen.GetSnapshotRows(0)![2];
        screen.AddRow();
        Assert.Equal(5, screen.TotalRows);
        Assert.Same(retained, screen.GetSnapshotRows(0)![0]);
        screen.SynchronizeSnapshotScrollbackQuota(quota); // Copying host policy is not another setter.
        screen.AddRow();
        Assert.Equal(6, screen.TotalRows);
        Assert.Same(retained, screen.GetSnapshotRows(0)![0]);
        screen.SnapshotScrollbackQuota = quota; // Explicit same-value setter enforces immediately.
        Assert.Equal(4, screen.TotalRows);
        Assert.NotSame(retained, screen.GetSnapshotRows(0)![0]);
    }

    [Fact]
    public void ByteGrowthCannotRecycleTheSoleOldPage()
    {
        TerminalScreen screen = Screen(1, Pages(new(80, 2, 0, 0, 0, 2_000_000), 2));
        GhosttySnapshotScrollbackQuota quota = new() { MaximumBytes = 0 };
        screen.SnapshotScrollbackQuota = quota;
        TerminalRow original = screen.GetSnapshotRows(0)![0];
        screen.AddRow();
        Assert.Equal(3, screen.TotalRows);
        Assert.Same(original, screen.GetSnapshotRows(0)![0]);
        screen.SnapshotScrollbackQuota = quota;
        Assert.Equal(1, screen.TotalRows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeLimitPrunesBothBuffersWithoutSwitchingActiveScreen(bool alternate)
    {
        GhosttySnapshotAllocation layout = new(4096);
        TerminalScreen screen = Screen(2, Pages(new(80, 2, 0, 0, 0, 0), 2, 2, 2, 2, 2),
            Pages(new(80, 2, 0, 0, 0, 0), 2, 2, 2), alternate);
        TerminalRow primary = screen.GetSnapshotRows(0)![6], secondary = screen.GetSnapshotRows(1)![2];
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumBytes = layout.StandardPageBytes * 2 };
        Assert.Equal(alternate, screen.AlternateBufferActive);
        Assert.Equal(4, screen.GetSnapshotRows(0)!.Count);
        Assert.Equal(4, screen.GetSnapshotRows(1)!.Count);
        Assert.Same(primary, screen.GetSnapshotRows(0)![0]);
        Assert.Same(secondary, screen.GetSnapshotRows(1)![0]);
    }

    [Fact]
    public void HostQuotaChangeReachesHeldLiveInputBeforePublication()
    {
        GhosttySnapshotAllocation layout = new(4096);
        TerminalScreen screen = Screen(2, Pages(new(80, 2, 0, 0, 0, 0), 2, 2, 2, 2, 2));
        using BasicVtProcessor processor = new(screen);
        TerminalScreen retained = screen.CreateStateCopy();
        processor.Process("\u001b[?2026hX"u8);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumBytes = layout.StandardPageBytes * 2 };
        processor.Process("Y"u8);
        Assert.Equal(4, screen.TotalRows);
        Assert.Equal(0, screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal(4, screen.TotalRows);
        Assert.Equal('X', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal('Y', screen.GetViewportRow(0).ReadOnlyCells[1].Codepoint);
        Assert.Equal(10, retained.TotalRows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeightShrinkEnforcesLinesAndRemapsOwnedAndCallerAnchors(bool reflow)
    {
        GhosttySnapshotAllocation layout = new(4096);
        int minimum = layout.InitialRows(1024);
        TerminalScreen screen = Screen(100, Pages(new(1024, 20, 0, 0, 0, 0), 20, 20, 20, 20, 20));
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumRows = 0 };
        TerminalScreenAnchor anchor = screen.CreateAnchor(90, 2);
        TerminalGridPosition[] positions = [new(2, 90)];
        int expected = 100;
        while (expected - 1 > minimum) expected -= 20;
        TerminalRow kept = screen.GetSnapshotRows(0)![100 - expected];

        TerminalGridPosition cursor = screen.Resize(1024, 1, reflow, new(3, 99), positions);

        Assert.Equal(expected, screen.TotalRows);
        Assert.Same(kept, screen.GetSnapshotRows(0)![0]);
        Assert.Equal(new(3, 0), cursor);
        Assert.Equal(new(2, 90 - (100 - expected)), positions[0]);
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition tracked));
        Assert.Equal(positions[0], tracked);
    }

    [Fact]
    public void WiderColumnsRecalculateLineMinimumBeforeEviction()
    {
        GhosttySnapshotAllocation layout = new(4096);
        int total = layout.InitialRows(40) + 2;
        int[] counts = new int[(total + 19) / 20];
        Array.Fill(counts, 20);
        counts[^1] = total - (counts.Length - 1) * 20;
        TerminalScreen screen = Screen(2, Pages(new(40, 20, 0, 0, 0, 0), counts));
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumRows = 0 };
        Assert.Equal(total, screen.TotalRows);
        TerminalScreenAnchor anchor = screen.CreateAnchor(total - 1, 3);
        TerminalScreen retained = screen.CreateStateCopy();
        screen.Resize(160, 2, false, new(3, 1), Span<TerminalGridPosition>.Empty);
        Assert.True(screen.TotalRows - 2 <= layout.InitialRows(160));
        Assert.True(screen.TotalRows < total);
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition position));
        Assert.Equal(new(3, screen.TotalRows - 1), position);
        Assert.Equal(total, retained.TotalRows);
        Assert.Equal(40, retained.Columns);
    }

    [Fact]
    public void FailedProcessorResizeDoesNotPublishQuotaEviction()
    {
        TerminalScreen screen = Screen(100, Pages(new(1024, 20, 0, 0, 0, 0), 20, 20, 20, 20, 20));
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumRows = 0 };
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[100;4H"u8);
        TerminalRow original = screen.GetSnapshotRows(0)![0];
        TerminalScreenAnchor anchor = screen.CreateAnchor(90, 2);
        processor.ResizeCheckpoint = phase =>
        {
            if (phase == ManagedResizeCheckpoint.PrimaryCursor) throw new OutOfMemoryException("After staged quota eviction");
        };
        Assert.Throws<OutOfMemoryException>(() => processor.ResizeScreen(1024, 1, 0, 0, false));
        Assert.Equal(100, screen.TotalRows);
        Assert.Same(original, screen.GetSnapshotRows(0)![0]);
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition position));
        Assert.Equal(new(2, 90), position);
        processor.ResizeCheckpoint = null;
        processor.ResizeScreen(1024, 1, 0, 0, false);
        Assert.True(screen.TotalRows < 100);
        Assert.True(screen.TryResolveAnchor(anchor, out position));
        Assert.Equal(new(2, 90 - (100 - screen.TotalRows)), position);
    }

    [Fact]
    public void InterleavedAllocationPrefixIsNeverPartiallyEvicted()
    {
        GhosttySnapshotAllocation layout = new(4096);
        TerminalRow[] grouped = Pages(new(80, 2, 0, 0, 0, 0), 2, 2, 1);
        TerminalScreen screen = Screen(1, [grouped[0], grouped[2], grouped[1], grouped[3], grouped[4]]);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumBytes = layout.StandardPageBytes * 2 };
        Assert.Equal(1, screen.TotalRows); // Both interleaved historical pages retire together.
        Assert.Same(grouped[4], screen.GetViewportRow(0));
    }

    [Fact]
    public void ByteGrowthDoesNotRecycleMultipleInterleavedOldPages()
    {
        GhosttySnapshotAllocation layout = new(4096);
        TerminalRow[] grouped = Pages(new(80, 2, 0, 0, 0, 0), 2, 2, 2);
        TerminalScreen screen = Screen(1, [grouped[0], grouped[2], grouped[1], grouped[3], grouped[4], grouped[5]]);
        GhosttySnapshotScrollbackQuota quota = new() { PageAlignment = 4096, MaximumBytes = layout.StandardPageBytes * 3 };
        screen.SnapshotScrollbackQuota = quota;
        screen.AddRow();
        Assert.Equal(7, screen.TotalRows);
        Assert.Same(grouped[0], screen.GetSnapshotRows(0)![0]);
        screen.SnapshotScrollbackQuota = quota;
        Assert.Equal(3, screen.TotalRows);
        Assert.Same(grouped[4], screen.GetSnapshotRows(0)![0]);
    }

    [Fact]
    public void InterleavedPageReachingActiveAreaProtectsItsWholePrefix()
    {
        GhosttySnapshotAllocation layout = new(4096);
        TerminalRow[] grouped = Pages(new(80, 2, 0, 0, 0, 0), 2, 2, 2);
        TerminalScreen screen = Screen(1, [grouped[0], grouped[2], grouped[4], grouped[1], grouped[3], grouped[5]]);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumBytes = layout.StandardPageBytes * 2 };
        Assert.Equal(6, screen.TotalRows);
        Assert.Same(grouped[0], screen.GetSnapshotRows(0)![0]);
    }

    [Fact]
    public void UnrepresentableHistoricalAllocationCannotFitMaximumUnsignedByteBudget()
    {
        TerminalRow[] rows = Pages(new(80, 1, 0, 0, 0, 0), 1, 1);
        rows[0].SnapshotAllocation = new(new(80, 1, 0, 0, 0, 0), metadataOverflow: true);
        TerminalScreen screen = Screen(1, rows);
        screen.SnapshotScrollbackQuota = new() { MaximumBytes = ulong.MaxValue };
        Assert.Equal(1, screen.TotalRows);
        Assert.Same(rows[1], screen.GetViewportRow(0));
    }

    [Fact]
    public void QuotaRetiresOldRasterPlacementAndShiftsSurvivorOnlyOnce()
    {
        GhosttySnapshotAllocation layout = new(4096);
        TerminalScreen screen = Screen(2, Pages(new(80, 2, 0, 0, 0, 0), 2, 2, 2, 2, 2));
        screen.ReplaceRasterImage(new(1, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 0, 255]),
            new(1, TerminalRasterImageLayer.AboveText, 0, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1));
        screen.ReplaceRasterImage(new(2, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 0, 255]),
            new(2, TerminalRasterImageLayer.AboveText, 0, 7, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1));
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumBytes = layout.StandardPageBytes * 2 };
        ReadOnlySpan<TerminalRasterImagePlacement> placements = screen.GetRasterImagePlacements();
        Assert.Equal(1, placements.Length);
        Assert.Equal(2, placements[0].ImageId);
        Assert.Equal(1, placements[0].AnchorRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeLimitsAndSubsequentStreamingMatchNativePageEviction(bool byteLimit)
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
            Assert.Skip("Native VT library is unavailable.");
        }
        int alignment = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096;
        GhosttySnapshotAllocation layout = new(alignment);
        int pageRows = layout.InitialRows(80);
        byte[] snapshot;
        using (GhosttyTerminal source = new(80, 1))
        {
            source.SetScrollbackMaxBytes(null);
            source.SetScrollbackMaxLines(null);
            for (int index = 0; index < pageRows * 4; index++) source.Write("A\r\n"u8);
            snapshot = GhosttySnapshot.Encode(source);
        }
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        ulong? maximumBytes = byteLimit ? layout.StandardPageBytes * 2 : null;
        ulong? maximumRows = byteLimit ? null : (ulong)(pageRows + pageRows / 2);
        native.SetScrollbackMaxBytes(maximumBytes is { } bytes ? (nuint)bytes : null);
        native.SetScrollbackMaxLines(maximumRows is { } rows ? (nuint)rows : null);
        managed.Screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment, MaximumBytes = maximumBytes, MaximumRows = maximumRows };
        Assert.Equal(native.GetTotalRows(), (nuint)managed.Screen.TotalRows);
        for (int index = 0; index < pageRows * 2 + 1; index++)
        {
            native.Write("B\r\n"u8);
            managed.Processor.Process("B\r\n"u8);
            Assert.Equal(native.GetTotalRows(), (nuint)managed.Screen.TotalRows);
        }
        native.SetScrollbackMaxBytes(null);
        native.SetScrollbackMaxLines(null);
        managed.Screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment };
        int before = managed.Screen.TotalRows;
        for (int index = 0; index < pageRows; index++)
        {
            native.Write("C\r\n"u8);
            managed.Processor.Process("C\r\n"u8);
        }
        Assert.Equal(before + pageRows, managed.Screen.TotalRows);
        Assert.Equal(native.GetTotalRows(), (nuint)managed.Screen.TotalRows);
    }

    [Fact]
    public void IndependentHardRowCapStillTrimsPartialPages()
    {
        TerminalScreen screen = new(80, 2, scrollbackLimit: 3) { SnapshotScrollbackQuota = new() { MaximumRows = 0 } };
        for (int index = 0; index < 10; index++) screen.AddRow();
        Assert.Equal(5, screen.TotalRows);
    }

    private static TerminalRow[] Pages(GhosttySnapshotPageCapacity capacity, params int[] counts)
    {
        List<TerminalRow> rows = [];
        foreach (int count in counts)
        {
            GhosttySnapshotPageAllocation page = new(capacity);
            for (int index = 0; index < count; index++)
                rows.Add(new(capacity.Columns) { SnapshotAllocation = page, SnapshotAllocationRow = index, SnapshotAllocationUnmodified = true });
        }
        return rows.ToArray();
    }

    private static TerminalScreen Screen(int viewport, TerminalRow[] primary, TerminalRow[]? alternate = null, bool activeAlternate = false)
    {
        int columns = primary[0].Columns;
        TerminalScreen owner = new(columns, viewport);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(columns, viewport, 100_000, owner.Theme);
        screen.InstallSnapshotRows(primary, alternate, activeAlternate ? 1 : 0);
        return screen;
    }
}
