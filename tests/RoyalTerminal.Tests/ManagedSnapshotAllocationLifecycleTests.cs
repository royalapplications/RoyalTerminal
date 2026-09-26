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

// Ghostty PageList.grow retains a page until its tail capacity is exhausted;
// increaseCapacity retains the replacement allocation even when text is erased.
// Windows Terminal TextBuffer::Reflow and xterm.js Buffer.resize use their own
// row-based storage. Our CLR row recycling remains, but must not keep an old
// logical native page alive after its last historical row has been recycled.
public sealed class ManagedSnapshotAllocationLifecycleTests
{
    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void TailSlotsAreReusedAcrossAdmissionChecksWithoutDetachingCells(int alignment)
    {
        GhosttySnapshotAllocation allocation = new(alignment);
        TerminalScreen screen = new(80, 2, 5000);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        object cells = rows[0].SearchStorageIdentity;
        Assert.Equal(allocation.StandardPageBytes, Measure(screen, allocation));
        GhosttySnapshotPageAllocation page = rows[0].SnapshotAllocation!;
        Assert.Equal(allocation.InitialCapacity(80), page.Capacity);
        Assert.Same(cells, rows[0].SearchStorageIdentity);

        for (int i = rows.Count; i < page.Capacity.Rows; i++) screen.AddRow();
        Assert.Equal(allocation.StandardPageBytes, Measure(screen, allocation));
        Assert.Same(page, rows[rows.Count - 1].SnapshotAllocation);
        Assert.Equal(page.Capacity.Rows - 1, rows[rows.Count - 1].SnapshotAllocationRow);

        screen.AddRow();
        Assert.Equal(2 * allocation.StandardPageBytes, Measure(screen, allocation));
        Assert.NotSame(page, rows[rows.Count - 1].SnapshotAllocation);
        Assert.Equal(0, rows[rows.Count - 1].SnapshotAllocationRow);
        screen.AddRow();
        Assert.Equal(2 * allocation.StandardPageBytes, Measure(screen, allocation));
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void PrefixPruningDoesNotReclaimOccupiedTailSlots(int alignment)
    {
        GhosttySnapshotAllocation allocation = new(alignment);
        TerminalScreen screen = new(80, 2, 5000);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        int capacity = allocation.InitialRows(80);
        for (int i = rows.Count; i < capacity; i++) screen.AddRow();
        Assert.Equal(allocation.StandardPageBytes, Measure(screen, allocation));
        rows.RemoveFirst(capacity - 2);
        Assert.Equal(allocation.StandardPageBytes, Measure(screen, allocation));
        screen.AddRow();
        Assert.Equal(2 * allocation.StandardPageBytes, Measure(screen, allocation));
        rows.RemoveFirst(2);
        Assert.Equal(allocation.StandardPageBytes, Measure(screen, allocation));
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void GrowthCheckpointSurvivesEraseAndPublicationButNotFinalPageRemoval(int alignment)
    {
        GhosttySnapshotAllocation allocation = new(alignment);
        TerminalScreen screen = new(80, 2, 5000);
        ulong original = Measure(screen, allocation);
        TerminalScreen staging = screen.CreateStateCopy();
        staging.GetSnapshotRows(0)![1][0].Grapheme = "a" + new string('\u0301', 200_000);
        ulong grown = Measure(staging, allocation);
        Assert.True(grown > original);
        Assert.Equal(original, Measure(screen, allocation));
        TerminalScreen retained = staging.CreateStateCopy();

        staging.GetSnapshotRows(0)![1].Clear();
        Assert.Equal(grown, Measure(staging, allocation));
        Assert.NotNull(retained.GetSnapshotRows(0)![1].ReadOnlyCells[0].Grapheme);
        Assert.Equal(grown, Measure(retained, allocation));
        screen.AdoptStateFrom(staging);
        Assert.Equal(grown, Measure(screen, allocation));
        screen.GetSnapshotRows(0)!.RemoveFirst();
        Assert.Equal(grown, Measure(screen, allocation));
        screen.GetSnapshotRows(0)!.RemoveFirst();
        Assert.Equal(0UL, Measure(screen, allocation));
        Assert.Equal(grown, Measure(retained, allocation));
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void RecyclingTheLastRowReleasesItsOldPageCharge(int alignment)
    {
        GhosttySnapshotAllocation allocation = new(alignment);
        TerminalScreen screen = new(80, 2, 0);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        GhosttySnapshotPageAllocation historical = new(new(80, 2, 0, 0, 0, 1_000_000));
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].SnapshotAllocation = historical;
            rows[i].SnapshotAllocationRow = i;
            rows[i].SnapshotAllocationUnmodified = true;
        }
        ulong original = Measure(screen, allocation);
        Assert.True(original > allocation.StandardPageBytes);
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalRow recycled = rows[0];

        Assert.Same(recycled, screen.AddRow());
        Assert.Null(recycled.SnapshotAllocation);
        Assert.Equal(original + allocation.StandardPageBytes, Measure(screen, allocation));
        screen.AddRow();
        Assert.Equal(allocation.StandardPageBytes, Measure(screen, allocation));
        Assert.Equal(original, Measure(retained, allocation));
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void PhysicalRowWidthControlsNewPageCapacity(int alignment)
    {
        GhosttySnapshotAllocation allocation = new(alignment);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(80, 2, 5000, new TerminalScreen(80, 2).Theme);
        screen.InstallSnapshotRows([new(80), new(80), new(65535)], null, 0);
        GhosttySnapshotPageCapacity wide = allocation.InitialCapacity(65535);
        Assert.True(allocation.AllocatedBytes(wide) > allocation.StandardPageBytes);
        Assert.Equal(allocation.StandardPageBytes + allocation.AllocatedBytes(wide), Measure(screen, allocation));
        Assert.Equal(wide, screen.GetSnapshotRows(0)![2].SnapshotAllocation!.Capacity);
        Assert.Equal(80, screen.Columns);
    }

    [Fact]
    public void RotatedRowsDoNotAllocateTheSameTailSlotTwice()
    {
        GhosttySnapshotAllocation allocation = new(4096);
        TerminalScreen screen = new(80, 4, 5000);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        Measure(screen, allocation);
        TerminalRow first = rows[0];
        rows[0] = rows[3];
        rows[3] = first;
        screen.AddRow();
        Measure(screen, allocation);
        Assert.Equal(4, rows[4].SnapshotAllocationRow);
        Assert.Same(rows[0].SnapshotAllocation, rows[4].SnapshotAllocation);
    }

    [Fact]
    public void UnrepresentableCapacityCannotFitAnExplicitMaximumValueBudget()
    {
        GhosttySnapshotAllocation allocation = new(4096);
        TerminalScreen screen = new(1700, 1) { SnapshotScrollbackQuota = new() { MaximumBytes = ulong.MaxValue } };
        TerminalRow row = screen.GetSnapshotRows(0)![0];
        for (int i = 0; i < row.Columns; i++)
            row[i].HyperlinkId = screen.RegisterHyperlink("https://example.com"u8, default, (uint)i + 1);
        GhosttySnapshotPage history = GhosttySnapshotLivePage.Capture([new TerminalRow(1700)], screen, 1700);
        Assert.Equal(ulong.MaxValue, Measure(screen, allocation));
        Assert.False(screen.FitsSnapshotHistoryQuota(0, history));
        row.Clear();
        Assert.True(screen.FitsSnapshotHistoryQuota(0, history));
    }

    [Fact]
    public void LiveTailPageBoundariesMatchNativeHistoryAdmission()
    {
        RequireNative();
        int alignment = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096;
        GhosttySnapshotAllocation allocation = new(alignment);
        using BasicVtProcessor source = new(new TerminalScreen(80, 4, 5000));
        for (int i = 0; i < 1200; i++) source.Process("\r\n"u8);
        byte[] snapshot = WithByteLimit(source.GetBinarySnapshot(), allocation.StandardPageBytes * 3);

        foreach (int liveRows in new[] { 1, allocation.InitialRows(80), allocation.InitialRows(80) + 1 })
        {
            using GhosttySnapshotDecoder nativeDecoder = new(snapshot);
            using GhosttyTerminal native = nativeDecoder.Ready();
            using ManagedTerminalSnapshotDecoder managedDecoder = new(snapshot);
            using ManagedTerminalSnapshot managed = managedDecoder.Ready();
            for (int i = 0; i < liveRows; i++)
            {
                native.Write("\r\n"u8);
                managed.Processor.Process("\r\n"u8);
                // Intervening admission-accounting checkpoints must not change
                // which page the next appended row will occupy.
                if (i == liveRows / 2) Measure(managed.Screen, allocation);
            }
            Assert.True(nativeDecoder.Next());
            ManagedTerminalSnapshotProgress result = managedDecoder.Next()!.Value;
            Assert.Equal(nativeDecoder.GetProgressRows(), (nuint)result.RowsApplied);
            Assert.Equal(liveRows <= allocation.InitialRows(80), result.RowsApplied > 0);
        }
    }

    private static ulong Measure(TerminalScreen screen, GhosttySnapshotAllocation allocation)
        => GhosttySnapshotLiveAllocation.Measure(screen, screen.GetSnapshotRows(0)!, allocation);

    private static byte[] WithByteLimit(byte[] source, ulong maximum)
    {
        using GhosttySnapshotRecordReader reader = new(source, 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Terminal)
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(87), maximum);
            records.Add(new(tag, bytes));
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
