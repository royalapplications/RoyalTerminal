// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotGridTests
{
    [Fact]
    public void GridMatchesUpstreamGoldenAndStreamsWithoutAllocating()
    {
        byte[] golden = GhosttySnapshotFramingTests.Fixture("grid-v1.hex");
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.Read(golden, 3, 4, 12, 64, out int consumed);
        Assert.Equal(golden.Length, consumed);
        Assert.Equal(new byte[] { 4, 11, 0, 0 }, grid.RowFlags.ToArray());
        Assert.Equal(new uint[] { 0x301, 0x302 }, grid.Suffix(0, 2).ToArray());
        Assert.Equal((ulong)'h' << 2, grid.Cells[6]);
        Assert.Equal(0x416UL << 2, grid.Cells[9]);
        using MemoryStream encoded = new();
        grid.WriteTo(encoded);
        Assert.Equal(golden, encoded.ToArray());
        grid.WriteTo(Stream.Null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) grid.WriteTo(Stream.Null);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void GridRejectsAllTruncationsAndStructuralOverflow()
    {
        byte[] golden = GhosttySnapshotFramingTests.Fixture("grid-v1.hex");
        for (int length = 0; length < golden.Length; length++)
            Assert.Throws<EndOfStreamException>(() => GhosttySnapshotGrid.Read(golden.AsSpan(0, length), 3, 4, 12, 64, out _));
        golden[1] = 4;
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotGrid.Read(golden, 3, 4, 12, 64, out _));
    }

    [Fact]
    public void GridEnforcesCellAndGraphemeLimitsBeforeUnboundedAllocation()
    {
        byte[] golden = GhosttySnapshotFramingTests.Fixture("grid-v1.hex");
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotGrid.Read(golden, 3, 4, 11, 64, out _));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotGrid.Read(golden, 3, 4, 12, 1, out _));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotGrid.Read([], 65535, 65535, 1000, 64, out _));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotGrid.Read([], 0, 1, 10, 64, out _));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void NoncanonicalWidthsDecodeAndReencodeAtMinimumWidth(int selector, int width)
    {
        byte[] bytes = new byte[3 + width + 4];
        bytes[0] = (byte)(selector << 4);
        bytes[1] = 1;
        ulong word = width <= 2 ? (ulong)'A' : (ulong)'A' << 2;
        for (int i = 0; i < width; i++) bytes[3 + i] = (byte)(word >> (i * 8));
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.Read(bytes, 4, 1, 4, 0, out _);
        using MemoryStream output = new();
        grid.WriteTo(output);
        Assert.Equal(new byte[] { 0, 1, 0, 65, 0, 0, 0, 0 }, output.ToArray());
        Assert.Equal(new ulong[] { 65UL << 2, 0, 0, 0 }, grid.Cells.ToArray());
    }

    [Fact]
    public void ReservedFlagsInvalidScalarsAndHyperlinkFlagsNormalize()
    {
        ulong[] words = [(0xD800UL << 2) | (3UL << 46) | (1UL << 45),
            2 | (0x123407UL << 2), ((ulong)'Z' << 2) | (12UL << 48), 0x110000UL << 2];
        GhosttySnapshotGrid grid = ReadWords(words, flags: 0xCF);
        Assert.Equal(3, grid.RowFlags[0]);
        Assert.Equal(0xFFFDUL << 2, grid.Cells[0]);
        Assert.Equal(2UL | (7UL << 2), grid.Cells[1]);
        Assert.Equal(words[2] | (1UL << 45), grid.Cells[2]);
        Assert.Equal(0xFFFDUL << 2, grid.Cells[3]);
    }

    [Fact]
    public void WidePairsAndWrappedHeadsNormalizeWithoutChangingContent()
    {
        const ulong wide = 1UL << 42, tail = 2UL << 42, head = 3UL << 42;
        GhosttySnapshotGrid grid = ReadWords([tail, head, wide | (65UL << 2), tail, wide | (66UL << 2)]);
        Assert.Equal(new ulong[] { 0, 0, wide | (65UL << 2), tail, 66UL << 2 }, grid.Cells.ToArray());
        Assert.Equal(head, ReadWords([head], flags: 1).Cells[0]);
        Assert.Equal(0UL, ReadWords([head]).Cells[0]);
        // A wide marker followed by an implicit default cannot retain its width.
        byte[] bytes = EncodeWords([wide | (65UL << 2)]);
        Assert.Equal(65UL << 2, GhosttySnapshotGrid.Read(bytes, 2, 1, 2, 0, out _).Cells[0]);
    }

    [Fact]
    public void GraphemesDropInvalidTargetsScalarsAndDuplicatesButAcceptPlainCells()
    {
        byte[] words = EncodeWords([(65UL << 2) | 1, 66UL << 2, 0, 2 | (7UL << 2)]);
        using MemoryStream input = new();
        input.Write(words.AsSpan(0, words.Length - 4));
        using BinaryWriter writer = new(input, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(7u);
        Entry(writer, 9, 0, [0x301]);
        Entry(writer, 0, 2, [0x301]);
        Entry(writer, 0, 3, [0x301]);
        Entry(writer, 0, 0, [0, 0xD800, 0x110000]);
        Entry(writer, 0, 0, [0x301, 0, 0x1F3FB]);
        Entry(writer, 0, 0, [0x302]);
        Entry(writer, 0, 1, [0x303]);
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.Read(input.ToArray(), 4, 1, 4, 3, out int consumed);
        Assert.Equal(input.Length, consumed);
        Assert.Equal(new uint[] { 0x301, 0x1F3FB }, grid.Suffix(0, 0).ToArray());
        Assert.Equal(new uint[] { 0x303 }, grid.Suffix(0, 1).ToArray());
        Assert.Empty(grid.Suffix(0, 2).ToArray());
        Assert.Empty(grid.Suffix(0, 3).ToArray());
        using MemoryStream encoded = new();
        grid.WriteTo(encoded);
        GhosttySnapshotGrid roundtrip = GhosttySnapshotGrid.Read(encoded.ToArray(), 4, 1, 4, 3, out _);
        Assert.Equal(grid.Cells.ToArray(), roundtrip.Cells.ToArray());
    }

    [Fact]
    public void GridReportsExactConsumptionWithoutConsumingFollowingRecord()
    {
        byte[] golden = GhosttySnapshotFramingTests.Fixture("grid-v1.hex");
        byte[] bytes = [.. golden, 0xA5, 0x5A];
        GhosttySnapshotGrid.Read(bytes, 3, 4, 12, 64, out int consumed);
        Assert.Equal(golden.Length, consumed);
        Assert.Equal(0xA5, bytes[consumed]);
    }

    [Fact]
    public void RandomWireWordsCanonicalizeIdempotently()
    {
        Random random = new(0x47524944);
        byte[] wordBytes = new byte[8];
        for (int sample = 0; sample < 1000; sample++)
        {
            ulong[] words = new ulong[random.Next(1, 33)];
            for (int i = 0; i < words.Length; i++)
            {
                random.NextBytes(wordBytes);
                words[i] = BinaryPrimitives.ReadUInt64LittleEndian(wordBytes);
            }
            GhosttySnapshotGrid first = ReadWords(words, (byte)random.Next(256));
            using MemoryStream encoded = new();
            first.WriteTo(encoded);
            GhosttySnapshotGrid second = GhosttySnapshotGrid.Read(encoded.ToArray(), words.Length, 1, words.Length, 0, out _);
            Assert.Equal(first.Cells.ToArray(), second.Cells.ToArray());
            Assert.Equal(first.RowFlags.ToArray(), second.RowFlags.ToArray());
            using MemoryStream canonical = new();
            second.WriteTo(canonical);
            Assert.Equal(encoded.ToArray(), canonical.ToArray());
        }
    }

    [Fact]
    public void SuffixLookupRejectsCoordinatesInsteadOfAliasingAnotherRow()
    {
        GhosttySnapshotGrid grid = ReadWords([65UL << 2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => { grid.Suffix(-1, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { grid.Suffix(0, -1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { grid.Suffix(1, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { grid.Suffix(0, 1); });
    }

    private static GhosttySnapshotGrid ReadWords(ulong[] words, byte flags = 0)
        => GhosttySnapshotGrid.Read(EncodeWords(words, flags), words.Length, 1, words.Length, 0, out _);

    private static byte[] EncodeWords(ulong[] words, byte flags = 0)
    {
        byte[] bytes = new byte[3 + words.Length * 8 + 4];
        bytes[0] = (byte)(flags | 0x30);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1), (ushort)words.Length);
        for (int i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(3 + i * 8), words[i]);
        return bytes;
    }

    private static void Entry(BinaryWriter writer, ushort row, ushort column, uint[] suffix)
    {
        writer.Write(row); writer.Write(column); writer.Write((ushort)suffix.Length);
        foreach (uint cp in suffix) writer.Write(cp);
    }
}
