// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty snapshot/grid.zig consumes complete suffix payloads, filters invalid
// scalars/NUL, keeps first accepted entries and replays live allocation in order.
// Vector filtering must not replace that allocator replay with a final-size fit.
public sealed class GhosttySnapshotSuffixCodecTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(1025)]
    [InlineData(65535)]
    public void ValidMixedAndInvalidEntriesMatchScalarFilteringAtEveryAlignment(int length)
    {
        for (int pattern = 0; pattern < 3; pattern++)
        {
            uint[] source = Values(length, pattern);
            uint[] expected = Filter(source);
            foreach (int offset in new[] { 0, 1, 3 })
            {
                byte[] bytes = Encode(source, offset);
                uint[] actual = GhosttySnapshotSuffixCodec.Read(bytes.AsSpan(offset, length * 4), expected.Length);
                Assert.Equal(expected, actual);
                Assert.Equal(0xCC, bytes[^1]);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(1025)]
    public void AdmissionCountsOnlyValidCodepointsAndChecksTheExactLimit(int length)
    {
        uint[] source = Values(length, 1), expected = Filter(source);
        byte[] encoded = Encode(source);
        Assert.Equal(expected, GhosttySnapshotSuffixCodec.Read(encoded.AsSpan(0, length * 4), expected.Length));
        if (expected.Length > 0)
            Assert.Throws<InvalidDataException>(() => GhosttySnapshotSuffixCodec.Read(encoded.AsSpan(0, length * 4), expected.Length - 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void PartialScalarsAreStructuralErrorsEvenWithZeroQuota(int bytes)
        => Assert.Throws<EndOfStreamException>(() => GhosttySnapshotSuffixCodec.Read(new byte[bytes], 0));

    [Fact]
    public void NegativeLimitsAreRejectedBeforeFiltering()
        => Assert.Throws<ArgumentOutOfRangeException>(() => GhosttySnapshotSuffixCodec.Read([], -1));

    [Fact]
    public void BulkCopyRetainsOwnedStorageNotTheBorrowedInput()
    {
        uint[] values = Values(128, 0);
        byte[] encoded = Encode(values, 1);
        uint[] actual = GhosttySnapshotSuffixCodec.Read(encoded.AsSpan(1, values.Length * 4), values.Length);
        encoded.AsSpan().Clear();
        Assert.Equal(values, actual);
    }

    [Fact]
    public void AllInvalidEntriesAndEmptyInputAllocateNothing()
    {
        byte[] encoded = Encode(Values(128, 2));
        uint[] result = [];
        for (int i = 0; i < 16; i++) result = GhosttySnapshotSuffixCodec.Read(encoded.AsSpan(0, 512), 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            result = GhosttySnapshotSuffixCodec.Read(encoded.AsSpan(0, 512), 0);
            _ = GhosttySnapshotSuffixCodec.Read([], 0);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(1U)]
    [InlineData(16U)]
    [InlineData(256U)]
    [InlineData(1024U)]
    [InlineData(4096U)]
    public void ValidatedArrayReusePreservesScalarAllocatorReplayAndDuplicateRetry(uint capacity)
    {
        GhosttySnapshotGraphemeRestore fast = new(capacity, 4096), scalar = new(capacity, 4096);
        HashSet<int> seen = [];
        int[] lengths = [128, 63, 65, 17, 4, 5, 1, 4, 16, 1, 63, 1];
        for (int step = 0; step < lengths.Length; step++)
        {
            int cell = step % 6;
            byte[] encoded = Encode(Values(lengths[step], step % 2));
            ReadOnlySpan<byte> payload = encoded.AsSpan(0, lengths[step] * 4);
            uint[]? validated = seen.Add(cell) ? GhosttySnapshotSuffixCodec.Read(payload, 4096) : null;
            fast.Read(cell, payload, validated);
            scalar.Read(cell, payload, null);
            Assert.Equal(scalar.Storage.Count, fast.Storage.Count);
            Assert.Equal(scalar.Storage.AllocatedBytes, fast.Storage.AllocatedBytes);
            Assert.Equal(scalar.Suffixes.Count, fast.Suffixes.Count);
            for (int index = 0; index < 6; index++)
            {
                int count = scalar.Storage.SuffixLength(index);
                Assert.Equal(count, fast.Storage.SuffixLength(index));
                Assert.Equal(scalar.Storage.TryGetAllocation(index, out GhosttySnapshotBitmap.Slice expected),
                    fast.Storage.TryGetAllocation(index, out GhosttySnapshotBitmap.Slice actual));
                Assert.Equal(expected, actual);
                if (count > 0)
                    Assert.Equal(scalar.Suffixes[index].AsSpan(0, count).ToArray(), fast.Suffixes[index].AsSpan(0, count).ToArray());
            }
            if (validated is not null && fast.Suffixes.TryGetValue(cell, out uint[]? stored)) Assert.Same(validated, stored);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(64)]
    public void ReusedArraysRetainTheIndependentLiveLimit(int limit)
    {
        uint[] validated = Values(128, 0);
        byte[] encoded = Encode(validated);
        GhosttySnapshotGraphemeRestore fast = new(4096, limit), scalar = new(4096, limit);
        if (limit < 64)
        {
            Assert.Throws<InvalidDataException>(() => fast.Read(0, encoded.AsSpan(0, 512), validated));
            Assert.Throws<InvalidDataException>(() => scalar.Read(0, encoded.AsSpan(0, 512), null));
            return;
        }
        fast.Read(0, encoded.AsSpan(0, 512), validated);
        scalar.Read(0, encoded.AsSpan(0, 512), null);
        Assert.Equal(64, fast.Storage.SuffixLength(0));
        Assert.Same(validated, fast.Suffixes[0]);
        Assert.Equal(scalar.Suffixes[0], fast.Suffixes[0].AsSpan(0, 64).ToArray());
    }

    [Fact]
    public void GridKeepsFramingFirstValidDuplicateAndCumulativeAdmission()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write((byte)0); writer.Write((ushort)2); writer.Write((byte)'A'); writer.Write((byte)'B');
        writer.Write(4U);
        Entry(writer, 0, [0, 0xD800, 0x110000]);
        Entry(writer, 0, [0x301, 0, 0x1D800]);
        Entry(writer, 0, Values(65, 0)); // An accepted raw duplicate does not consume the raw quota.
        Entry(writer, 1, [0x302, 0xDFFF, 0x10FFFF]);
        int length = (int)stream.Length;
        writer.Write((byte)0xA5); writer.Flush();
        byte[] bytes = stream.ToArray();
        GhosttySnapshotGraphemeRestore live = new(4096, 4);
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.Read(bytes, 2, 1, 2, 4, out int consumed, live);
        Assert.Equal(length, consumed);
        Assert.Equal(0xA5, bytes[consumed]);
        Assert.Equal(new uint[] { 0x301, 0x1D800 }, grid.Suffix(0, 0).ToArray());
        Assert.Equal(new uint[] { 0x302, 0x10FFFF }, grid.Suffix(0, 1).ToArray());
        Assert.Equal(2, live.Storage.SuffixLength(0));
        Assert.Equal(2, live.Storage.SuffixLength(1));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotGrid.Read(bytes, 2, 1, 2, 3, out _));
    }

    private static uint[] Values(int length, int pattern)
    {
        uint[] result = new uint[length];
        ReadOnlySpan<uint> valid = [1, 0x301, 0xD7FF, 0xE000, 0xFFFF, 0x10000, 0x1D800, 0x10FFFF];
        ReadOnlySpan<uint> invalid = [0, 0xD800, 0xDBFF, 0xDC00, 0xDFFF, 0x110000, 0x80000000, uint.MaxValue];
        for (int i = 0; i < length; i++)
            result[i] = pattern == 2 || pattern == 1 && i % 3 == 0 ? invalid[i % invalid.Length] : valid[i % valid.Length];
        return result;
    }

    private static byte[] Encode(uint[] values, int offset = 0)
    {
        byte[] result = new byte[offset + values.Length * 4 + 1];
        result.AsSpan().Fill(0xCC);
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + i * 4), values[i]);
        return result;
    }

    private static uint[] Filter(uint[] values)
    {
        List<uint> result = [];
        foreach (uint cp in values)
            if (cp != 0 && cp <= 0x10FFFF && !(cp >= 0xD800 && cp <= 0xDFFF)) result.Add(cp);
        return result.ToArray();
    }

    private static void Entry(BinaryWriter writer, ushort column, uint[] values)
    {
        writer.Write((ushort)0); writer.Write(column); writer.Write((ushort)values.Length);
        foreach (uint value in values) writer.Write(value);
    }
}
