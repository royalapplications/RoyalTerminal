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

// PageList.resizeCols / ReflowCursor.computeAndMemoizeCap are the quota oracle.
// Windows Terminal and xterm.js retain their own row-based reflow storage rules;
// neither defines the logical native PAGE budget of an incremental restore.
public sealed class ManagedSnapshotReflowAllocationTests
{
    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void BlankReflowRetainsFirstPageCapacityWithoutMutatingPublishedRows(int alignment)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        GhosttySnapshotPageCapacity capacity = new(80, 4, 128, 192, 8192, 1_000_000);
        TerminalScreen screen = CreateScreen(80, 4, alignment, (capacity, 4));
        ulong original = Measure(screen, layout);
        TerminalRow originalRow = screen.GetSnapshotRows(0)![0];
        object originalStorage = originalRow.SearchStorageIdentity;
        GhosttySnapshotPageAllocation originalPage = originalRow.SnapshotAllocation!;
        TerminalScreen staging = screen.CreateStateCopy();

        staging.Resize(40, 4);
        Assert.True(layout.TryAdjustColumns(capacity, 40, out GhosttySnapshotPageCapacity adjusted));
        Assert.Equal(adjusted, staging.GetSnapshotRows(0)![0].SnapshotAllocation!.Capacity);
        Assert.Equal(original, Measure(staging, layout));
        Assert.Equal(4, staging.TotalRows);
        Assert.Same(originalPage, originalRow.SnapshotAllocation);
        Assert.Same(originalStorage, originalRow.SearchStorageIdentity);
        Assert.Equal(80, screen.Columns);
        Assert.Equal(original, Measure(screen, layout));

        staging.Resize(80, 4);
        Assert.Equal(original, Measure(staging, layout));
        screen.AdoptStateFrom(staging);
        Assert.Equal(original, Measure(screen, layout));
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void NewReflowPagesInheritTheSourceBeingConsumed(int alignment)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        GhosttySnapshotPageCapacity first = new(80, 2, 0, 0, 0, 0);
        GhosttySnapshotPageCapacity later = new(80, 198, 0, 0, 0, 1_000_000);
        TerminalScreen screen = CreateScreen(80, 4, alignment, (first, 2), (later, 198));
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        for (int i = 0; i < rows.Count; i++) rows[i][0].Codepoint = 'A';
        Assert.True(layout.TryAdjustColumns(first, 40, out GhosttySnapshotPageCapacity firstAdjusted));
        Assert.True(layout.TryAdjustColumns(later, 40, out GhosttySnapshotPageCapacity laterAdjusted));
        Assert.True(firstAdjusted.Rows < rows.Count);

        screen.Resize(40, 4);
        Assert.Equal(200, rows.Count);
        Assert.Equal(firstAdjusted, rows[0].SnapshotAllocation!.Capacity);
        Assert.Same(rows[0].SnapshotAllocation, rows[firstAdjusted.Rows - 1].SnapshotAllocation);
        Assert.NotSame(rows[0].SnapshotAllocation, rows[firstAdjusted.Rows].SnapshotAllocation);
        Assert.Equal(laterAdjusted, rows[firstAdjusted.Rows].SnapshotAllocation!.Capacity);
        Assert.Equal(layout.AllocatedBytes(firstAdjusted) + layout.AllocatedBytes(laterAdjusted), Measure(screen, layout));
        foreach (TerminalRow row in rows) Assert.Equal('A', row.ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void DeferredBlankPagesUseTheNextNonblankSourceCapacity(int alignment)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        GhosttySnapshotPageCapacity first = new(80, 1, 0, 0, 0, 0);
        GhosttySnapshotPageCapacity blanks = new(80, 100, 0, 0, 0, 500_000);
        GhosttySnapshotPageCapacity last = new(80, 1, 0, 0, 0, 1_000_000);
        TerminalScreen screen = CreateScreen(80, 4, alignment, (first, 1), (blanks, 100), (last, 1));
        screen.GetSnapshotRows(0)![101][0].Codepoint = 'Z';
        Assert.True(layout.TryAdjustColumns(first, 40, out GhosttySnapshotPageCapacity firstAdjusted));
        Assert.True(layout.TryAdjustColumns(last, 40, out GhosttySnapshotPageCapacity lastAdjusted));

        screen.Resize(40, 4);
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        Assert.Equal(102, rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.Equal(i < firstAdjusted.Rows ? firstAdjusted : lastAdjusted, rows[i].SnapshotAllocation!.Capacity);
            Assert.Equal(i < 101 ? 0 : 'Z', rows[i].ReadOnlyCells[0].Codepoint);
        }
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void WrappedLogicalLineRetainsSourcePageBoundaries(int alignment)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        GhosttySnapshotPageCapacity first = new(8, 1, 0, 0, 0, 0);
        GhosttySnapshotPageCapacity later = new(8, 999, 0, 0, 0, 1_000_000);
        TerminalScreen screen = CreateScreen(8, 4, alignment, (first, 1), (later, 999));
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].WrapsToNext = i < rows.Count - 1;
            for (int column = 0; column < 8; column++) rows[i][column].Codepoint = 'a';
        }
        Assert.True(layout.TryAdjustColumns(first, 4, out GhosttySnapshotPageCapacity firstAdjusted));
        Assert.True(layout.TryAdjustColumns(later, 4, out GhosttySnapshotPageCapacity laterAdjusted));

        screen.Resize(4, 4);
        Assert.Equal(2000, rows.Count);
        Assert.Equal(firstAdjusted, rows[0].SnapshotAllocation!.Capacity);
        Assert.Equal(laterAdjusted, rows[firstAdjusted.Rows].SnapshotAllocation!.Capacity);
        Assert.True(rows[firstAdjusted.Rows].IsWrapContinuation);
        Assert.False(rows[rows.Count - 1].WrapsToNext);
        foreach (TerminalRow row in rows)
            foreach (ref readonly TerminalCell cell in row.ReadOnlyCells) Assert.Equal('a', cell.Codepoint);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void LargeColumnFallbackUsesLiveSourceRowsNotUnusedCapacity(int alignment)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        GhosttySnapshotPageCapacity capacity = new(1, 300, 0, 0, 0, 0);
        TerminalScreen screen = CreateScreen(1, 4, alignment, (capacity, 220));
        TerminalRowBuffer source = screen.GetSnapshotRows(0)!;
        GhosttySnapshotPageAllocation original = source[0].SnapshotAllocation!;
        Assert.False(layout.TryAdjustColumns(capacity, 65535, out _));
        GhosttySnapshotReflowAllocation reflow = new(source, 65535, layout);
        // Exercise the allocation walker without allocating 220 huge CLR rows.
        List<TerminalRow> destination = [];
        for (int i = 0; i < 221; i++)
        {
            TerminalRow row = new(1);
            reflow.Append(row, original);
            destination.Add(row);
        }
        Assert.Equal((ushort)220, destination[0].SnapshotAllocation!.Capacity.Rows);
        Assert.Same(destination[0].SnapshotAllocation, destination[219].SnapshotAllocation);
        Assert.Equal((ushort)215, destination[220].SnapshotAllocation!.Capacity.Rows);
        Assert.NotSame(destination[0].SnapshotAllocation, destination[220].SnapshotAllocation);
    }

    [Fact]
    public void OrdinaryUntrackedReflowDoesNotCreateSnapshotAllocationMetadata()
    {
        TerminalScreen screen = new(80, 4);
        screen.GetSnapshotRows(0)![0][0].Codepoint = 'A';
        screen.Resize(40, 4);
        foreach (TerminalRow row in screen.GetSnapshotRows(0)!) Assert.Null(row.SnapshotAllocation);
    }

    [Fact]
    public void ResizeRoundTripRetainsNativeHistoryAdmissionBoundary()
    {
        RequireNative();
        int alignment = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096;
        GhosttySnapshotAllocation layout = new(alignment);
        using BasicVtProcessor source = new(new TerminalScreen(80, 4, 5000));
        for (int i = 0; i < 1200; i++) source.Process("\r\n"u8);
        List<SnapshotTestRecord> records = ReadInflatedRecords(source.GetBinarySnapshot());
        using GhosttySnapshotStateReader reader = new(SnapshotTestRecords.Encode(records), new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        ulong boundary = 0;
        foreach (GhosttySnapshotPage page in ready.Screens[0].Pages) boundary += layout.AllocatedBytes(page.Capacity);
        boundary += layout.AllocatedBytes(reader.ReadNextHistoryPage()!.Value.Page.Capacity);

        foreach (bool fits in new[] { false, true })
        {
            BinaryPrimitives.WriteUInt64LittleEndian(records[0].Payload.AsSpan(87), fits ? boundary : boundary - 1);
            byte[] bytes = SnapshotTestRecords.Encode(records);
            using GhosttySnapshotDecoder nativeDecoder = new(bytes);
            using GhosttyTerminal native = nativeDecoder.Ready();
            using ManagedTerminalSnapshotDecoder managedDecoder = new(bytes);
            using ManagedTerminalSnapshot managed = managedDecoder.Ready();
            foreach (ushort columns in new ushort[] { 40, 80 })
            {
                native.Resize(columns, 4);
                managed.Processor.ResizeScreen(columns, 4, 0, 0, reflowOnResize: true);
            }
            Assert.True(nativeDecoder.Next());
            ManagedTerminalSnapshotProgress progress = managedDecoder.Next()!.Value;
            Assert.Equal(nativeDecoder.GetProgressRows(), (nuint)progress.RowsApplied);
            Assert.Equal(fits, progress.RowsApplied > 0);
        }
    }

    private static TerminalScreen CreateScreen(int columns, int viewportRows, int alignment,
        params (GhosttySnapshotPageCapacity Capacity, int Rows)[] pages)
    {
        TerminalScreen owner = new(columns, viewportRows);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(columns, viewportRows, 10000, owner.Theme);
        List<TerminalRow> rows = [];
        foreach ((GhosttySnapshotPageCapacity capacity, int count) in pages)
        {
            GhosttySnapshotPageAllocation page = new(capacity);
            for (int i = 0; i < count; i++)
                rows.Add(new(columns) { SnapshotAllocation = page, SnapshotAllocationRow = i, SnapshotAllocationUnmodified = true });
        }
        screen.InstallSnapshotRows(rows.ToArray(), null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment };
        return screen;
    }

    private static ulong Measure(TerminalScreen screen, GhosttySnapshotAllocation layout)
        => GhosttySnapshotLiveAllocation.Measure(screen, screen.GetSnapshotRows(0)!, layout);

    private static List<SnapshotTestRecord> ReadInflatedRecords(byte[] source)
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
            if (tag == GhosttySnapshotRecordTag.Finish) return records;
        }
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
