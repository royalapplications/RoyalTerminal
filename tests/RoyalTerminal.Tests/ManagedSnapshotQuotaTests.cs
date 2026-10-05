// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedSnapshotQuotaTests
{
    [Fact]
    public void HostQuotaChangesDuringHoldApplyImmediatelyAndSurvivePublication()
    {
        using BasicVtProcessor source = new(new TerminalScreen(80, 4, 5000));
        for (int i = 0; i < 1200; i++) source.Process("\r\n"u8);
        using ManagedTerminalSnapshotDecoder decoder = new(InflatePageCapacities(source.GetBinarySnapshot()));
        using ManagedTerminalSnapshot restored = decoder.Ready();
        restored.Processor.Process("\u001b[?2026h"u8);
        GhosttySnapshotScrollbackQuota changed = new() { MaximumBytes = 0 };
        restored.Screen.SnapshotScrollbackQuota = changed;
        Assert.Equal(0, decoder.Next()!.Value.RowsApplied);
        restored.Processor.Process("\u001b[?2026l"u8);
        Assert.Same(changed, restored.Screen.SnapshotScrollbackQuota);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void RetainedPageAccountingSurvivesCowPartialPruningAndLiveGrowth(int alignment)
    {
        TerminalScreen screen = new(80, 2);
        GhosttySnapshotPage page = GhosttySnapshotLivePage.Capture([new TerminalRow(80), new TerminalRow(80)], screen, 1000);
        TerminalRow[] rows = GhosttySnapshotLivePage.Decode(page, screen);
        TerminalScreen staged = TerminalScreen.CreateSnapshotStorage(80, 2, 10000, screen.Theme);
        staged.InstallSnapshotRows(rows, null, 0);
        GhosttySnapshotAllocation allocation = new(alignment);
        ulong initial = GhosttySnapshotLiveAllocation.Measure(staged, staged.GetSnapshotRows(0)!, allocation);
        Assert.Equal(allocation.AllocatedBytes(page.Capacity), initial);
        TerminalScreen copy = staged.CreateStateCopy();
        Assert.Equal(initial, GhosttySnapshotLiveAllocation.Measure(copy, copy.GetSnapshotRows(0)!, allocation));
        // Changes in a staging view must not mutate the published view's charge.
        copy.GetSnapshotRows(0)![1][0].Grapheme = "a" + new string('\u0301', 200000);
        Assert.True(GhosttySnapshotLiveAllocation.Measure(copy, copy.GetSnapshotRows(0)!, allocation) > initial);
        Assert.Equal(initial, GhosttySnapshotLiveAllocation.Measure(staged, staged.GetSnapshotRows(0)!, allocation));
        staged.GetSnapshotRows(0)!.RemoveFirst();
        Assert.Equal(initial, GhosttySnapshotLiveAllocation.Measure(staged, staged.GetSnapshotRows(0)!, allocation));
        staged.GetSnapshotRows(0)!.RemoveFirst();
        Assert.Equal(0UL, GhosttySnapshotLiveAllocation.Measure(staged, staged.GetSnapshotRows(0)!, allocation));
        staged.GetSnapshotRows(0)!.Add(new TerminalRow(80));
        Assert.Equal(allocation.StandardPageBytes, GhosttySnapshotLiveAllocation.Measure(staged, staged.GetSnapshotRows(0)!, allocation));
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void SnapshotQuotaRejectsWholePageAtExactByteBoundaryAndGapStaysClosed(int alignment)
    {
        using BasicVtProcessor source = new(new TerminalScreen(80, 4, 5000));
        for (int i = 0; i < 1200; i++) source.Process("\r\n"u8);
        byte[] bytes = InflatePageCapacities(source.GetBinarySnapshot());
        GhosttySnapshotAllocation allocation = new(alignment);
        using GhosttySnapshotStateReader reader = new(bytes, new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        ulong resident = 0;
        foreach (GhosttySnapshotPage page in ready.Screens[0].Pages) resident += allocation.AllocatedBytes(page.Capacity);
        GhosttySnapshotHistoryPage history = reader.ReadNextHistoryPage()!.Value;
        ulong boundary = resident + allocation.AllocatedBytes(history.Page.Capacity);
        foreach (bool fits in new[] { false, true })
        {
            using ManagedTerminalSnapshotDecoder decoder = new(bytes, new()
            {
                ScrollbackQuota = new() { PageAlignment = alignment, MaximumBytes = fits ? boundary : boundary - 1 },
            });
            using ManagedTerminalSnapshot restored = decoder.Ready();
            Assert.Equal(fits ? history.Page.Grid.Rows : 0, decoder.Next()!.Value.RowsApplied);
            if (!fits)
            {
                restored.Screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment };
                while (decoder.Next() is { } progress) Assert.Equal(0, progress.RowsApplied);
            }
        }
    }

    [Fact]
    public void SourceLimitsRoundTripAndHostCanOverrideThem()
    {
        TerminalScreen screen = new(10, 2) { SnapshotScrollbackQuota = new() { MaximumBytes = 1234567, MaximumRows = 42 } };
        using BasicVtProcessor processor = new(screen);
        byte[] bytes = processor.GetBinarySnapshot();
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(bytes);
        Assert.Equal(1234567UL, restored.Screen.SnapshotScrollbackQuota!.MaximumBytes);
        Assert.Equal(42UL, restored.Screen.SnapshotScrollbackQuota.MaximumRows);
        using ManagedTerminalSnapshot overridden = ManagedTerminalSnapshot.Restore(bytes, new() { ScrollbackQuota = new() });
        Assert.Null(overridden.Screen.SnapshotScrollbackQuota!.MaximumBytes);
        Assert.Null(overridden.Screen.SnapshotScrollbackQuota.MaximumRows);
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.SnapshotScrollbackQuota = new() { PageAlignment = 8192 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedTerminalSnapshotDecoder(bytes, new() { ScrollbackQuota = new() { PageAlignment = 1 } }));
    }

    private static byte[] InflatePageCapacities(byte[] source)
    {
        using GhosttySnapshotRecordReader reader = new(source, 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Page) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 1_000_000);
            records.Add(new(tag, bytes));
            if (tag == GhosttySnapshotRecordTag.Finish) return SnapshotTestRecords.Encode(records);
        }
    }

    [Fact]
    public void LiveOutputDuringHoldCountsAgainstAdmissionBudget()
    {
        using BasicVtProcessor source = new(new TerminalScreen(80, 4, 5000));
        for (int i = 0; i < 1200; i++) source.Process("\r\n"u8);
        byte[] bytes = source.GetBinarySnapshot();
        using GhosttySnapshotStateReader reader = new(bytes, new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        GhosttySnapshotAllocation allocation = new(4096);
        ulong budget = 0;
        foreach (GhosttySnapshotPage page in ready.Screens[0].Pages) budget += allocation.AllocatedBytes(page.Capacity);
        budget += allocation.AllocatedBytes(reader.ReadNextHistoryPage()!.Value.Page.Capacity);
        using ManagedTerminalSnapshotDecoder decoder = new(bytes, new() { ScrollbackQuota = new() { PageAlignment = 4096, MaximumBytes = budget } });
        using ManagedTerminalSnapshot restored = decoder.Ready();
        int published = restored.Screen.TotalRows;
        restored.Processor.Process("\u001b[?2026h\r\nnew live row\r\n"u8);
        Assert.Equal(published, restored.Screen.TotalRows);
        Assert.Equal(0, decoder.Next()!.Value.RowsApplied);
        restored.Processor.Process("\u001b[?2026l"u8);
        Assert.True(restored.Screen.TotalRows > published);
    }
}
