// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty snapshot/grid.zig uses vector row classification, narrow transport
// conversion and batched suffix writes (#13848). These independent scalar
// oracles preserve the old managed wire/normalization behavior across SIMD
// boundaries; existing native golden and malformed-grid cases remain relevant.
public sealed class GhosttySnapshotGridCodecTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void EncodingMatchesScalarForUnalignedSlicesAndVectorBatchTails(int width)
    {
        Random random = new(1701 + width);
        foreach (int count in Lengths)
        {
            ulong[] cells = Cells(random, count, width);
            byte[] expected = ScalarEncode(cells, width);
            byte[] actual = new byte[expected.Length + 2];
            actual.AsSpan().Fill(0xCC);
            GhosttySnapshotGridCodec.Encode(cells, actual.AsSpan(1, expected.Length), width);
            Assert.Equal(expected, actual.AsSpan(1, expected.Length).ToArray());
            Assert.Equal(0xCC, actual[0]);
            Assert.Equal(0xCC, actual[^1]);
            Assert.Equal(ScalarClassify(cells), GhosttySnapshotGridCodec.Classify(cells));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void DecodingMatchesScalarNormalizationWithoutTouchingAdjacentCells(int width)
    {
        Random random = new(2701 + width);
        foreach (int count in Lengths)
        {
            byte[] encoded = ScalarEncode(Cells(random, count, width), width);
            foreach (int offset in new[] { 0, 1, 3 })
            {
                byte[] unaligned = new byte[encoded.Length + offset + 1];
                encoded.CopyTo(unaligned, offset);
                ulong[] actual = new ulong[count + 2];
                actual.AsSpan().Fill(ulong.MaxValue);
                bool wide = GhosttySnapshotGridCodec.Decode(unaligned.AsSpan(offset, encoded.Length), actual.AsSpan(1, count), width);
                ulong combined = 0;
                for (int i = 0; i < count; i++)
                {
                    ulong raw = ReadWord(encoded.AsSpan(i * width), width);
                    combined |= raw;
                    Assert.Equal(Normalize(raw), actual[i + 1]);
                }
                Assert.Equal((combined & (3UL << 42)) != 0, wide);
                Assert.Equal(ulong.MaxValue, actual[0]);
                Assert.Equal(ulong.MaxValue, actual[^1]);
            }
        }
    }

    [Fact]
    public void EveryBmpLaneNormalizesSurrogatesAndPreservesOtherCodepoints()
    {
        byte[] input = new byte[65536 * 2];
        ulong[] output = new ulong[65536];
        for (int cp = 0; cp <= ushort.MaxValue; cp++)
            BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(cp * 2), (ushort)cp);
        Assert.False(GhosttySnapshotGridCodec.Decode(input, output, 2));
        for (int cp = 0; cp <= ushort.MaxValue; cp++)
            Assert.Equal((ulong)(cp is >= 0xD800 and <= 0xDFFF ? 0xFFFD : cp) << 2, output[cp]);
    }

    [Fact]
    public void EveryBitAndTrailingBlankBoundaryRetainsTheExactWidthSelector()
    {
        foreach (int count in Lengths)
        {
            ulong[] cells = new ulong[count];
            Assert.Equal((0, 0), GhosttySnapshotGridCodec.Classify(cells));
            for (int bit = 0; bit < 64 && count > 0; bit++)
            {
                foreach (int index in new[] { 0, count / 2, count - 1 })
                {
                    cells[index] = 1UL << bit;
                    Assert.Equal(ScalarClassify(cells), GhosttySnapshotGridCodec.Classify(cells));
                    cells[index] = 0;
                }
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void UndersizedSpansFailBeforeChangingAnyDestination(int width)
    {
        ulong[] cells = new ulong[65];
        byte[] bytes = new byte[cells.Length * width];
        bytes.AsSpan().Fill(0xCC);
        Assert.Throws<ArgumentOutOfRangeException>(() => GhosttySnapshotGridCodec.Encode(cells, bytes.AsSpan(0, bytes.Length - 1), width));
        Assert.All(bytes, value => Assert.Equal(0xCC, value));
        cells.AsSpan().Fill(ulong.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => GhosttySnapshotGridCodec.Decode(bytes.AsSpan(0, bytes.Length - 1), cells, width));
        Assert.All(cells, value => Assert.Equal(ulong.MaxValue, value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(16)]
    public void InvalidTransportWidthsAreRejectedEvenForEmptyRows(int width)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GhosttySnapshotGridCodec.Encode([], [], width));
        Assert.Throws<ArgumentOutOfRangeException>(() => GhosttySnapshotGridCodec.Decode([], [], width));
    }

    [Fact]
    public void SmallGraphemeEntriesShareOneBoundedWrite()
    {
        ulong[] cells = new ulong[100];
        Dictionary<int, uint[]> suffixes = [];
        for (int i = cells.Length - 1; i >= 0; i--)
        {
            cells[i] = ((ulong)'A' << 2) | 1;
            suffixes.Add(i, [0x301]); // Deliberately not insertion-order encoding.
        }
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.FromOwnedCells(100, [0], cells, suffixes);
        using CountingStream destination = new();
        grid.WriteTo(destination);
        Assert.Equal(4, destination.Writes); // Row header/payload, suffix count, suffix batch.
        Assert.Equal(1000, destination.MaximumWrite);
        Assert.Equal(ScalarGrid(cells, suffixes), destination.ToArray());
    }

    [Theory]
    [InlineData(1022)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(65535)]
    public void LargeSuffixEntriesFlushAndStreamWithoutChangingWireOrder(int length)
    {
        ulong[] cells = [((ulong)'A' << 2) | 1, ((ulong)'B' << 2) | 1, ((ulong)'C' << 2) | 1];
        uint[] large = new uint[length];
        for (int i = 0; i < length; i++) large[i] = i % 2 == 0 ? 0x301U : 0x1F3FBU;
        Dictionary<int, uint[]> suffixes = new() { [2] = [0x303], [0] = [0x301], [1] = large };
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.FromOwnedCells(3, [0], cells, suffixes);
        using CountingStream destination = new();
        grid.WriteTo(destination);
        Assert.InRange(destination.MaximumWrite, 1, 4096);
        Assert.Equal(ScalarGrid(cells, suffixes), destination.ToArray());
        GhosttySnapshotGrid restored = GhosttySnapshotGrid.Read(destination.ToArray(), 3, 1, 3, length + 2, out int consumed);
        Assert.Equal(destination.Length, consumed);
        Assert.Equal(large, restored.Suffix(0, 1).ToArray());
        Assert.Equal(new uint[] { 0x301 }, restored.Suffix(0, 0).ToArray());
        Assert.Equal(new uint[] { 0x303 }, restored.Suffix(0, 2).ToArray());
    }

    [Fact]
    public void OversizedOwnedSuffixIsNotSilentlyTruncatedInItsWireHeader()
    {
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.FromOwnedCells(1, [0], [((ulong)'A' << 2) | 1],
            new() { [0] = new uint[65536] });
        Assert.Throws<InvalidDataException>(() => grid.WriteTo(Stream.Null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void WarmRowTransportAndClassificationDoNotAllocate(int width)
    {
        ulong[] cells = Cells(new(31), 4097, width), decoded = new ulong[cells.Length];
        byte[] encoded = new byte[cells.Length * width];
        for (int i = 0; i < 16; i++) Roundtrip();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) Roundtrip();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        void Roundtrip()
        {
            _ = GhosttySnapshotGridCodec.Classify(cells);
            GhosttySnapshotGridCodec.Encode(cells, encoded, width);
            _ = GhosttySnapshotGridCodec.Decode(encoded, decoded, width);
        }
    }

    private static ReadOnlySpan<int> Lengths => [0, 1, 2, 3, 7, 8, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129, 511, 512, 513, 1023, 1024, 1025, 4095, 4096, 4097];

    private static ulong[] Cells(Random random, int count, int width)
    {
        ulong[] result = new ulong[count];
        Span<byte> bytes = stackalloc byte[8];
        for (int i = 0; i < count; i++)
        {
            random.NextBytes(bytes);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            result[i] = width switch { 1 => (value & 255) << 2, 2 => (value & 65535) << 2, 4 => value & uint.MaxValue, _ => value };
        }
        return result;
    }

    private static (int, int) ScalarClassify(ReadOnlySpan<ulong> cells)
    {
        int count = cells.Length;
        while (count > 0 && cells[count - 1] == 0) count--;
        ulong combined = 0;
        for (int i = 0; i < count; i++) combined |= cells[i];
        return (count, (combined & ~(255UL << 2)) == 0 ? 0 : (combined & ~(65535UL << 2)) == 0 ? 1 : combined <= uint.MaxValue ? 2 : 3);
    }

    private static byte[] ScalarEncode(ReadOnlySpan<ulong> cells, int width)
    {
        byte[] output = new byte[cells.Length * width];
        for (int i = 0; i < cells.Length; i++)
        {
            ulong value = width <= 2 ? cells[i] >> 2 : cells[i];
            for (int j = 0; j < width; j++) output[i * width + j] = (byte)(value >> (j * 8));
        }
        return output;
    }

    private static ulong ReadWord(ReadOnlySpan<byte> bytes, int width)
    {
        ulong result = 0;
        for (int i = 0; i < width; i++) result |= (ulong)bytes[i] << (i * 8);
        return width <= 2 ? result << 2 : result;
    }

    private static ulong Normalize(ulong word)
    {
        ulong kind = word & 3, content = (word >> 2) & 0xFFFFFF;
        if (kind <= 1)
        {
            word &= ~3UL;
            if (content > 0x10FFFF || content is >= 0xD800 and <= 0xDFFF)
                word = (word & ~(0xFFFFFFUL << 2)) | (0xFFFDUL << 2);
        }
        else if (kind == 2) word = (word & ~(0xFFFFFFUL << 2)) | ((content & 255) << 2);
        if (((word >> 46) & 3) == 3) word &= ~(3UL << 46);
        return (word >> 48) == 0 ? word & ~(1UL << 45) : word | (1UL << 45);
    }

    private static byte[] ScalarGrid(ulong[] cells, Dictionary<int, uint[]> suffixes)
    {
        using MemoryStream output = new();
        using BinaryWriter writer = new(output);
        (int count, int selector) = ScalarClassify(cells);
        writer.Write((byte)(selector << 4));
        writer.Write((ushort)count);
        writer.Write(ScalarEncode(cells.AsSpan(0, count), 1 << selector));
        writer.Write((uint)suffixes.Count);
        for (int index = 0; index < cells.Length; index++)
        {
            if (!suffixes.TryGetValue(index, out uint[]? suffix)) continue;
            writer.Write((ushort)0); writer.Write((ushort)index); writer.Write((ushort)suffix.Length);
            foreach (uint cp in suffix) writer.Write(cp);
        }
        writer.Flush();
        return output.ToArray();
    }

    private sealed class CountingStream : MemoryStream
    {
        internal int Writes { get; private set; }
        internal int MaximumWrite { get; private set; }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Writes++;
            MaximumWrite = Math.Max(MaximumWrite, buffer.Length);
            base.Write(buffer);
        }
    }
}
