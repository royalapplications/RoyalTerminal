// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotRecordBufferTests
{
    [Fact]
    public void SmallRecordsStayOnStackWithoutBoxingOrAllocating()
    {
        RecordingPool pool = new();
        Span<byte> initial = stackalloc byte[512];
        GhosttySnapshotRecordBuffer buffer = new(initial, 512, pool);
        try
        {
            for (int i = 0; i < 100; i++) WriteSmall(ref buffer);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) WriteSmall(ref buffer);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Empty(pool.Rented);
            Assert.Equal("12345678"u8.ToArray(), buffer.WrittenSpan.ToArray());
        }
        finally { buffer.Dispose(); }
        Assert.All(initial.ToArray(), value => Assert.Equal(0, value));

        static void WriteSmall(ref GhosttySnapshotRecordBuffer buffer)
        {
            buffer.Reset(512);
            WriteConstrained(ref buffer);
        }
        static void WriteConstrained<TWriter>(ref TWriter writer)
            where TWriter : IGhosttySnapshotWriter, allows ref struct
        {
            writer.Write("1234567"u8);
            writer.WriteByte((byte)'8');
        }
    }

    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(8193)]
    public void ExactStackBoundaryGrowsOnlyWhenNeededAndRetainsRental(int size)
    {
        RecordingPool pool = new();
        Span<byte> initial = stackalloc byte[512];
        initial.Fill(0xAA);
        GhosttySnapshotRecordBuffer buffer = new(initial, 10_000, pool);
        byte[] bytes = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
        try
        {
            buffer.Write(bytes);
            Assert.Equal(bytes, buffer.WrittenSpan.ToArray());
            Assert.Equal(size > 512 ? 1 : 0, pool.Rented.Count);
            buffer.Reset(10_000);
            buffer.Write("short"u8);
            Assert.Equal("short"u8.ToArray(), buffer.WrittenSpan.ToArray());
            buffer.Reset(10_000);
            buffer.Write(bytes);
            Assert.Equal(bytes, buffer.WrittenSpan.ToArray());
            Assert.Equal(size > 512 ? 1 : 0, pool.Rented.Count);
        }
        finally { buffer.Dispose(); buffer.Dispose(); }
        Assert.Equal(pool.Rented.Count, pool.Returned.Count);
        Assert.All(pool.Returned, bytes => Assert.All(bytes, b => Assert.Equal(0, b)));
    }

    [Fact]
    public void RepeatedGrowthPreservesPrefixAndClearsEveryRetiredRental()
    {
        RecordingPool pool = new();
        GhosttySnapshotRecordBuffer buffer = new(stackalloc byte[8], 10_000, pool);
        try
        {
            buffer.Write("12345678"u8);
            buffer.WriteByte((byte)'9');
            Assert.Single(pool.Rented);
            buffer.Write(new byte[1000]);
            Assert.Equal(2, pool.Rented.Count);
            Assert.Single(pool.Returned);
            Assert.Equal("123456789"u8.ToArray(), buffer.WrittenSpan[..9].ToArray());
            Assert.All(buffer.WrittenSpan[9..].ToArray(), b => Assert.Equal(0, b));
        }
        finally { buffer.Dispose(); }
        Assert.Equal(2, pool.Returned.Count);
        Assert.All(pool.Returned, bytes => Assert.All(bytes, b => Assert.Equal(0, b)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LimitsAreCheckedBeforeRentOrWriteEvenWhenCapacityIsLarger(bool resetAfterGrowth)
    {
        RecordingPool pool = new();
        GhosttySnapshotRecordBuffer buffer = new(stackalloc byte[8], 2000, pool);
        try
        {
            if (resetAfterGrowth) buffer.Write(new byte[1000]);
            int rents = pool.Rented.Count;
            buffer.Reset(3);
            buffer.Write("abc"u8);
            bool threw = false;
            try { buffer.WriteByte(4); }
            catch (InvalidDataException) { threw = true; }
            Assert.True(threw);
            threw = false;
            try { buffer.Write(new byte[4096]); }
            catch (InvalidDataException) { threw = true; }
            Assert.True(threw);
            Assert.Equal("abc"u8.ToArray(), buffer.WrittenSpan.ToArray());
            Assert.Equal(rents, pool.Rented.Count);
            buffer.Reset(0);
            buffer.Write([]);
            Assert.Empty(buffer.WrittenSpan.ToArray());
            threw = false;
            try { buffer.WriteByte(0); }
            catch (InvalidDataException) { threw = true; }
            Assert.True(threw);
        }
        finally { buffer.Dispose(); }
    }

    [Fact]
    public void FailedGrowthLeavesExistingBytesOwnedAndCanBeRetried()
    {
        RecordingPool pool = new();
        GhosttySnapshotRecordBuffer buffer = new(stackalloc byte[8], 2000, pool);
        try
        {
            buffer.Write("123456789"u8);
            pool.FailNextRent = true;
            bool threw = false;
            try { buffer.Write(new byte[1000]); }
            catch (OutOfMemoryException) { threw = true; }
            Assert.True(threw);
            Assert.Equal("123456789"u8.ToArray(), buffer.WrittenSpan.ToArray());
            Assert.Empty(pool.Returned);
            buffer.Write(new byte[1000]);
            Assert.Equal(1009, buffer.WrittenSpan.Length);
            Assert.Single(pool.Returned);
        }
        finally { buffer.Dispose(); }
        Assert.Equal(pool.Rented.Count, pool.Returned.Count);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("byte")]
    [InlineData("reset")]
    [InlineData("read")]
    public void DisposedBuffersCannotExposeOrReuseReturnedStorage(string operation)
    {
        RecordingPool pool = new();
        GhosttySnapshotRecordBuffer buffer = new(stackalloc byte[1], 100, pool);
        buffer.Write("secret"u8);
        buffer.Dispose();
        buffer.Dispose();
        bool threw = false;
        try
        {
            switch (operation)
            {
                case "write": buffer.Write("again"u8); break;
                case "byte": buffer.WriteByte(0); break;
                case "reset": buffer.Reset(100); break;
                case "read": _ = buffer.WrittenSpan.Length; break;
            }
        }
        catch (ObjectDisposedException) { threw = true; }
        Assert.True(threw);
        Assert.Single(pool.Returned);
    }

    [Fact]
    public void StackAndStreamCodecsProduceTheSameGoldenPageAndGrid()
    {
        byte[] pageBytes = GhosttySnapshotFramingTests.Fixture("page-v1.hex");
        GhosttySnapshotPage page = GhosttySnapshotPage.Read(pageBytes, 1_000_000, 1_000_000, 1_000_000);
        byte[] gridBytes = GhosttySnapshotFramingTests.Fixture("grid-v1.hex");
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.Read(gridBytes, 3, 4, 12, 64, out _);
        GhosttySnapshotRecordBuffer buffer = new(stackalloc byte[512], 1_000_000);
        try
        {
            page.WritePayloadTo(ref buffer);
            Assert.Equal(pageBytes, buffer.WrittenSpan.ToArray());
            buffer.Reset(1_000_000);
            grid.WriteTo(ref buffer);
            Assert.Equal(gridBytes, buffer.WrittenSpan.ToArray());
            buffer.Reset(1_000_000);
            GhosttySnapshotHyperlink link = new(true, 0, "id"u8, "https://example.test"u8);
            link.WriteTo(ref buffer);
            using MemoryStream stream = new();
            link.WriteTo(stream);
            Assert.Equal(stream.ToArray(), buffer.WrittenSpan.ToArray());
        }
        finally { buffer.Dispose(); }
    }

    private sealed class RecordingPool : ArrayPool<byte>
    {
        internal List<byte[]> Rented { get; } = [];
        internal List<byte[]> Returned { get; } = [];
        internal bool FailNextRent { get; set; }

        public override byte[] Rent(int minimumLength)
        {
            if (FailNextRent) { FailNextRent = false; throw new OutOfMemoryException("injected"); }
            byte[] bytes = new byte[minimumLength + 13];
            Array.Fill(bytes, (byte)0xCC); // Pooled capacity need not be zeroed or exact.
            Rented.Add(bytes);
            return bytes;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            Assert.True(clearArray);
            Assert.Contains(array, Rented);
            Assert.DoesNotContain(array, Returned);
            Array.Clear(array);
            Returned.Add(array);
        }
    }
}
