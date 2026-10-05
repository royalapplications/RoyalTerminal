// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty crc32c.zig defines the binary snapshot checksum. WT text export and
// xterm.js SerializeAddon have no equivalent binary framing. The independent
// bitwise oracle exercises software directly even on CRC-capable test hosts.
public sealed class GhosttySnapshotSoftwareCrc32CTests
{
    [Fact]
    public void StandardCheckValueAndEmptyInputMatchTheWireParameters()
    {
        Assert.Equal(0xE3069283U, ~GhosttySnapshotSoftwareCrc32C.Append(uint.MaxValue, "123456789"u8));
        Assert.Equal(0x12345678U, GhosttySnapshotSoftwareCrc32C.Append(0x12345678, []));
        Assert.Equal(0U, GhosttySnapshotFraming.ComputeChecksum([], []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(4193)]
    [InlineData(12288)]
    [InlineData(65567)]
    [InlineData(98304)]
    [InlineData(196631)]
    public void SlicesTailsInterleavingAndAlignmentsMatchBitwiseReference(int length)
    {
        byte[] bytes = Data(length + 16);
        foreach (int offset in new[] { 0, 1, 3, 15 })
        foreach (uint initial in new[] { 0U, uint.MaxValue, 0x12345678U })
        {
            ReadOnlySpan<byte> input = bytes.AsSpan(offset, length);
            uint expected = Reference(initial, input);
            Assert.Equal(expected, GhosttySnapshotSoftwareCrc32C.Append(initial, input));
            if (initial == uint.MaxValue) Assert.Equal(~expected, GhosttySnapshotFraming.ComputeChecksum([], input));
        }
    }

    [Theory]
    [InlineData(17)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(12288)]
    [InlineData(65567)]
    public void ArbitraryStreamingBoundariesDoNotChangeTheChecksum(int length)
    {
        byte[] bytes = Data(length);
        uint expected = Reference(uint.MaxValue, bytes);
        foreach (int split in new[] { 0, 1, 15, 16, 17, length / 3, length / 2, length - 1, length })
        {
            uint first = GhosttySnapshotSoftwareCrc32C.Append(uint.MaxValue, bytes.AsSpan(0, split));
            Assert.Equal(expected, GhosttySnapshotSoftwareCrc32C.Append(first, bytes.AsSpan(split)));
            Assert.Equal(~expected, GhosttySnapshotFraming.ComputeChecksum(bytes.AsSpan(0, split), bytes.AsSpan(split)));
        }
        foreach (int chunk in new[] { 1, 3, 16, 4095, 4096, 4097 })
        {
            uint crc = uint.MaxValue;
            for (int offset = 0; offset < length; offset += chunk)
                crc = GhosttySnapshotSoftwareCrc32C.Append(crc, bytes.AsSpan(offset, Math.Min(chunk, length - offset)));
            Assert.Equal(expected, crc);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4096)]
    [InlineData(536870912)]
    [InlineData(1073741824)]
    [InlineData(int.MaxValue)]
    public void ZeroShiftMatchesIndependentFullPowerMatricesAtEveryStateBit(int length)
    {
        for (int bit = 0; bit < 32; bit++)
        {
            uint state = 1U << bit;
            Assert.Equal(ReferenceZeros(state, length), GhosttySnapshotSoftwareCrc32C.AdvanceZeros(state, length));
        }
        Assert.Equal(ReferenceZeros(uint.MaxValue, length), GhosttySnapshotSoftwareCrc32C.AdvanceZeros(uint.MaxValue, length));
    }

    [Fact]
    public void ZeroShiftMatchesByteUpdatesAndRejectsNegativeCounts()
    {
        byte[] zeros = new byte[1025];
        for (int length = 0; length < zeros.Length; length++)
            Assert.Equal(Reference(uint.MaxValue, zeros.AsSpan(0, length)),
                GhosttySnapshotSoftwareCrc32C.AdvanceZeros(uint.MaxValue, length));
        Assert.Throws<ArgumentOutOfRangeException>(() => GhosttySnapshotSoftwareCrc32C.AdvanceZeros(0, -1));
    }

    [Fact]
    public void RepeatedAndOrderedBytesUseTheSameParameters()
    {
        byte[] bytes = new byte[8193];
        foreach (int pattern in new[] { 0, 255, -1 })
        {
            for (int i = 0; i < bytes.Length; i++) bytes[i] = pattern < 0 ? (byte)i : (byte)pattern;
            Assert.Equal(Reference(uint.MaxValue, bytes), GhosttySnapshotSoftwareCrc32C.Append(uint.MaxValue, bytes));
        }
    }

    [Fact]
    public void CompleteGoldenSnapshotChecksumsMatchSoftwareForEveryRecord()
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        int offset = GhosttySnapshotFraming.Envelope.Length;
        while (offset < bytes.Length)
        {
            ReadOnlySpan<byte> header = bytes.AsSpan(offset, GhosttySnapshotFraming.HeaderLength);
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header[2..]));
            uint crc = GhosttySnapshotSoftwareCrc32C.Append(uint.MaxValue, header[..6]);
            crc = GhosttySnapshotSoftwareCrc32C.Append(crc, bytes.AsSpan(offset + header.Length, length));
            Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(header[6..]), ~crc);
            offset += header.Length + length;
        }
        Assert.Equal(bytes.Length, offset);
    }

    [Fact]
    public void RecordReadersAcceptIndependentChecksumsAndRejectLargePayloadCorruption()
    {
        byte[] payload = Data(65567);
        byte[] bytes = new byte[20 + payload.Length];
        GhosttySnapshotFraming.Envelope.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), (ushort)GhosttySnapshotRecordTag.Page);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)payload.Length);
        uint crc = Reference(Reference(uint.MaxValue, bytes.AsSpan(10, 6)), payload);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), ~crc);
        payload.CopyTo(bytes, 20);
        foreach (bool streamed in new[] { false, true })
        {
            using MemoryStream source = new(bytes);
            using GhosttySnapshotRecordReader reader = streamed ? new(source, payload.Length) : new(bytes.AsMemory(), payload.Length);
            reader.ReadEnvelope();
            Assert.Equal(GhosttySnapshotRecordTag.Page, reader.ReadRecord(out ReadOnlySpan<byte> actual));
            Assert.True(actual.SequenceEqual(payload));
        }
        bytes[20 + payload.Length / 2] ^= 1;
        using GhosttySnapshotRecordReader corrupt = new(bytes.AsMemory(), payload.Length);
        corrupt.ReadEnvelope();
        Assert.Throws<InvalidDataException>(() => corrupt.ReadRecord(out _));
        Assert.Throws<InvalidOperationException>(() => corrupt.ReadRecord(out _));
    }

    [Fact]
    public void WarmSoftwareUpdatesAndCombinesAllocateNothing()
    {
        byte[] bytes = Data(8193);
        uint crc = 0;
        for (int i = 0; i < 16; i++) crc = GhosttySnapshotSoftwareCrc32C.Append(crc, bytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            crc = GhosttySnapshotSoftwareCrc32C.Append(crc, bytes);
            crc = GhosttySnapshotSoftwareCrc32C.Append(crc, bytes.AsSpan(0, 17));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(crc);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task SharedConstantTablesAreSafeForIndependentConcurrentUpdates()
    {
        byte[] bytes = Data(12289);
        uint expected = Reference(uint.MaxValue, bytes);
        Task<uint>[] tasks = new Task<uint>[8];
        for (int i = 0; i < tasks.Length; i++)
            tasks[i] = Task.Run(() => GhosttySnapshotSoftwareCrc32C.Append(uint.MaxValue, bytes));
        foreach (uint actual in await Task.WhenAll(tasks)) Assert.Equal(expected, actual);
    }

    private static byte[] Data(int length)
    {
        byte[] bytes = new byte[length];
        new Random(0x35171A3B).NextBytes(bytes);
        return bytes;
    }

    private static uint Reference(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0x82F63B78 : crc >> 1;
        }
        return crc;
    }

    private static uint ReferenceZeros(uint crc, int length)
    {
        Span<uint> matrix = stackalloc uint[32], squared = stackalloc uint[32];
        for (int bit = 0; bit < 32; bit++) matrix[bit] = Reference(1U << bit, [0]);
        while (length != 0)
        {
            if ((length & 1) != 0) crc = MatrixProduct(matrix, crc);
            length >>= 1;
            for (int bit = 0; bit < 32; bit++) squared[bit] = MatrixProduct(matrix, matrix[bit]);
            squared.CopyTo(matrix);
        }
        return crc;
    }

    private static uint MatrixProduct(ReadOnlySpan<uint> matrix, uint state)
    {
        uint result = 0;
        for (int bit = 0; bit < 32; bit++) if ((state & (1U << bit)) != 0) result ^= matrix[bit];
        return result;
    }
}
