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

public sealed class GhosttySnapshotAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void LayoutIsBoundedAllocationFreeAndRetainsPoolGranularity(int alignment)
    {
        GhosttySnapshotAllocation allocation = new(alignment);
        GhosttySnapshotPageCapacity tiny = new(1, 1, 0, 0, 0, 0);
        Assert.Equal((ulong)alignment, allocation.LayoutBytes(tiny));
        Assert.Equal(allocation.StandardPageBytes, allocation.AllocatedBytes(tiny));
        Assert.Equal(allocation.StandardPageBytes, allocation.LayoutBytes(new(215, 215, 128, 192, 8192, 2048)));
        GhosttySnapshotPageCapacity maximum = new(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, uint.MaxValue, uint.MaxValue);
        ulong largest = allocation.LayoutBytes(maximum);
        Assert.InRange(largest, (ulong)uint.MaxValue, 128UL * 1024 * 1024 * 1024);
        Assert.Equal(0UL, largest % (ulong)alignment);
        foreach (int columns in new[] { 1, 2, 80, 132, 215, 1000, 10000, 65535 })
        {
            int rows = allocation.InitialRows(columns);
            Assert.InRange(rows, 1, ushort.MaxValue);
            (ulong bytes, ulong lines) = allocation.MinimumLimits(columns, 24);
            Assert.True(bytes >= allocation.StandardPageBytes * 2);
            Assert.Equal((ulong)rows, lines);
            Assert.True(allocation.Fits(columns, 24, bytes, 24 + lines, 0, 0));
            Assert.False(allocation.Fits(columns, 24, bytes + 1, 24, 0, null));
            Assert.False(allocation.Fits(columns, 24, 0, 25 + lines, null, 0));
        }
        allocation.LayoutBytes(maximum);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) allocation.LayoutBytes(maximum);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Throws<ArgumentOutOfRangeException>(() => allocation.LayoutBytes(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocation.InitialRows(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocation.MinimumLimits(80, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GhosttySnapshotAllocation(8192));
    }

    [Theory]
    [InlineData(0U, 0U, (ushort)0, (ushort)0)]
    [InlineData(1000000U, 0U, (ushort)0, (ushort)0)]
    [InlineData(0U, 1000000U, (ushort)0, (ushort)0)]
    [InlineData(1U, 33U, (ushort)65535, (ushort)65535)]
    [InlineData(16385U, 2049U, (ushort)129, (ushort)193)]
    public void ExactHistoryByteAndLineBoundariesMatchNative(uint graphemes, uint strings, ushort styles, ushort links)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native allocation accounting oracle available: {available}");
        if (!available) return;
        GhosttySnapshotAllocation allocation = new(OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096);
        using BasicVtProcessor processor = new(new TerminalScreen(80, 4, 5000));
        for (int row = 0; row < 1200; row++) processor.Process("\r\n"u8);
        byte[] original = processor.GetBinarySnapshot();
        List<SnapshotTestRecord> records = ReadRecords(original);
        // Inflate advertised capacities independently of actual blank content.
        // Native allocates from these hints; managed must only account for them.
        foreach (SnapshotTestRecord record in records)
        {
            if (record.Tag != GhosttySnapshotRecordTag.Page) continue;
            BinaryPrimitives.WriteUInt16LittleEndian(record.Payload.AsSpan(8), styles);
            BinaryPrimitives.WriteUInt16LittleEndian(record.Payload.AsSpan(10), links);
            BinaryPrimitives.WriteUInt32LittleEndian(record.Payload.AsSpan(12), graphemes);
            BinaryPrimitives.WriteUInt32LittleEndian(record.Payload.AsSpan(16), strings);
        }
        using GhosttySnapshotStateReader probe = new(SnapshotTestRecords.Encode(records), new());
        GhosttySnapshotReadyState ready = probe.ReadReady();
        ulong residentBytes = 0, residentRows = 0;
        foreach (GhosttySnapshotPage page in ready.Screens[0].Pages)
        {
            residentBytes += allocation.AllocatedBytes(page.Capacity);
            residentRows += (ulong)page.Grid.Rows;
        }
        GhosttySnapshotPage firstHistory = probe.ReadNextHistoryPage()!.Value.Page;
        ulong boundary = residentBytes + allocation.AllocatedBytes(firstHistory.Capacity);
        foreach (ulong bytes in new[] { 0UL, boundary - 1, boundary, boundary + 1, boundary * 2, ulong.MaxValue })
        foreach (ulong rows in new[] { 0UL, 1UL, 100UL, 500UL, 1000UL, ulong.MaxValue })
        {
            BinaryPrimitives.WriteUInt64LittleEndian(records[0].Payload.AsSpan(87), bytes);
            BinaryPrimitives.WriteUInt64LittleEndian(records[0].Payload.AsSpan(95), rows);
            byte[] source = SnapshotTestRecords.Encode(records);
            using GhosttySnapshotStateReader wire = new(source, new());
            wire.ReadReady();
            using GhosttySnapshotDecoder decoder = new(source);
            using GhosttyTerminal native = decoder.Ready();
            ulong currentBytes = residentBytes, currentRows = residentRows;
            bool apply = true;
            while (wire.ReadNextHistoryPage() is { } history)
            {
                ulong nextBytes = currentBytes + allocation.AllocatedBytes(history.Page.Capacity);
                ulong nextRows = currentRows + (ulong)history.Page.Grid.Rows;
                apply &= allocation.Fits(80, 4, nextBytes, nextRows, bytes, rows);
                Assert.True(decoder.Next());
                Assert.True(decoder.GetProgressRows() == (apply ? (nuint)history.Page.Grid.Rows : 0),
                    $"capacity={history.Page.Capacity}, limits={bytes}/{rows}, accounted={nextBytes}/{nextRows}, pool={allocation.StandardPageBytes}, expectedApply={apply}, nativeRows={decoder.GetProgressRows()}");
                if (apply) { currentBytes = nextBytes; currentRows = nextRows; }
            }
            Assert.False(decoder.Next());
        }
    }

    private static List<SnapshotTestRecord> ReadRecords(byte[] source)
    {
        using GhosttySnapshotRecordReader reader = new(source, 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            records.Add(new(tag, payload.ToArray()));
            if (tag == GhosttySnapshotRecordTag.Finish) return records;
        }
    }
}
