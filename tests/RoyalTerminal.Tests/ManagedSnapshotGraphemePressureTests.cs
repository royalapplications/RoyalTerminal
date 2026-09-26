// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty snapshot/grid.zig drops a complete suffix on page.appendGrapheme OOM.
// xterm.js combined strings and Windows Terminal ROW text storage do not have
// this PAGE contract. Raw codecs preserve the wire suffixes; live restore follows
// Ghostty, including fixed map capacity, word-local bitmap spans and wire order.
public sealed class ManagedSnapshotGraphemePressureTests
{
    private const int Columns = 8;
    private readonly record struct Entry(ushort Row, ushort Column, uint[] Suffix);

    [Theory]
    [InlineData(0U, 0)]
    [InlineData(1U, 1)]
    [InlineData(16U, 1)]
    [InlineData(17U, 2)]
    [InlineData(33U, 4)]
    public void DeclaredMapCapacityLimitsLiveSuffixesWithoutChangingRawData(uint capacity, int accepted)
    {
        byte[] payload = Payload(capacity, Cluster(0, 1), Cluster(1, 1), Cluster(2, 1), Cluster(3, 1));
        GhosttySnapshotPage page = Read(payload);
        TerminalRow row = Assert.Single(GhosttySnapshotLivePage.Decode(page, new(Columns, 1)));
        for (int column = 0; column < 4; column++)
        {
            Assert.Equal(1, page.Grid.Suffix(0, column).Length);
            Assert.Equal(column < accepted ? 1 : 0, page.LiveSuffix(0, column).Length);
            Assert.Equal(column < accepted ? Text(column, [0x0300]) : null, row.ReadOnlyCells[column].Grapheme);
            Assert.Equal('A' + column, row.ReadOnlyCells[column].Codepoint);
        }
        using MemoryStream rewritten = new();
        page.WritePayloadTo(rewritten);
        GhosttySnapshotPage rawRoundtrip = Read(rewritten.ToArray());
        Assert.Equal(page.Capacity, rawRoundtrip.Capacity);
        Assert.Equal(page.Grid.Cells.ToArray(), rawRoundtrip.Grid.Cells.ToArray());
        for (int column = 0; column < 4; column++)
            Assert.Equal(page.Grid.Suffix(0, column).ToArray(), rawRoundtrip.Grid.Suffix(0, column).ToArray());
    }

    [Fact]
    public void ReplacementPressureDropsTheEntireClusterAndReleasesItsPrefix()
    {
        // Upstream's regression: 64, 64 and 8 suffix scalars occupy 34 chunks.
        // The fourth cluster needs 16 new chunks with 15 old chunks still live.
        // Its failed prefix must be released so the fifth cluster can fit.
        GhosttySnapshotPage page = Read(Payload(1024, Cluster(0, 64), Cluster(1, 64), Cluster(2, 8), Cluster(3, 61), Cluster(4, 4)));
        Assert.Equal(new[] { 64, 64, 8, 0, 4 }, LiveLengths(page, 5));
        Assert.Equal(61, page.Grid.Suffix(0, 3).Length);
        TerminalRow row = Assert.Single(GhosttySnapshotLivePage.Decode(page, new(Columns, 1)));
        Assert.Null(row.ReadOnlyCells[3].Grapheme);
        Assert.Equal('D', row.ReadOnlyCells[3].Codepoint);
        Assert.Equal(Text(4, Cluster(4, 4).Suffix), row.ReadOnlyCells[4].Grapheme);
    }

    [Fact]
    public void AllocationUsesWireOrderNotCellOrder()
    {
        GhosttySnapshotPage page = Read(Payload(1, Cluster(3, 1), Cluster(0, 1)));
        Assert.Equal(new[] { 0, 0, 0, 1 }, LiveLengths(page, 4));
        Assert.Equal(1, page.Grid.Suffix(0, 0).Length);
        Assert.Equal(1, page.Grid.Suffix(0, 3).Length);
    }

    [Fact]
    public void DuplicateCanRecoverAfterFailureButCannotReplaceAStoredSuffix()
    {
        GhosttySnapshotPage page = Read(Payload(1024, Cluster(0, 64), Cluster(1, 64), Cluster(2, 8),
            Cluster(3, 61), new(0, 3, [0x301]), new(0, 3, [0x302])));
        Assert.Equal(61, page.Grid.Suffix(0, 3).Length); // Raw first-valid-entry behavior is unchanged.
        Assert.Equal(new uint[] { 0x301 }, page.LiveSuffix(0, 3).ToArray());
        TerminalRow row = Assert.Single(GhosttySnapshotLivePage.Decode(page, new(Columns, 1)));
        Assert.Equal("D\u0301", row.ReadOnlyCells[3].Grapheme);
    }

    [Fact]
    public void LivePageRetainsTheWireOrderAllocatorSeedWithoutSharingMutableOwnership()
    {
        GhosttySnapshotPage page = Read(Payload(1024, Cluster(0, 64), Cluster(1, 64), Cluster(2, 8),
            Cluster(3, 61), new(0, 3, [0x301]), new(0, 3, [0x302])));
        TerminalRow row = Assert.Single(GhosttySnapshotLivePage.Decode(page, new(Columns, 1)));
        GhosttySnapshotPageAllocation allocation = row.SnapshotAllocation!;
        Assert.True(allocation.HasGraphemeSeed);
        GhosttySnapshotGraphemeStorage first = allocation.CopyRestoredGraphemes();
        GhosttySnapshotGraphemeStorage second = allocation.CopyRestoredGraphemes();
        Assert.Equal(4, first.Count);
        for (int column = 0; column < 4; column++)
        {
            Assert.Equal(page.LiveSuffix(0, column).Length, first.SuffixLength(column));
            Assert.True(first.TryGetAllocation(column, out GhosttySnapshotBitmap.Slice expected));
            Assert.True(second.TryGetAllocation(column, out GhosttySnapshotBitmap.Slice actual));
            Assert.Equal(expected, actual);
        }
        Assert.Equal(1, first.SuffixLength(3)); // Accepted duplicate, not raw 61-scalar suffix.
        first.Clear(0);
        Assert.Equal(64, second.SuffixLength(0));
        Assert.Equal(64, allocation.CopyRestoredGraphemes().SuffixLength(0));
        Assert.Equal(64, page.LiveSuffix(0, 0).Length);
        Assert.Equal(61, page.Grid.Suffix(0, 3).Length);
    }

    [Fact]
    public void RestoreSeedIncludesFailedPrefixReleaseAndPreservesFutureAllocationPlacement()
    {
        GhosttySnapshotPage page = Read(Payload(1024, Cluster(0, 64), Cluster(1, 64), Cluster(2, 8), Cluster(3, 61), Cluster(4, 4)));
        GhosttySnapshotGraphemeStorage restored = page.CreateAllocationIdentity().CopyRestoredGraphemes();
        GhosttySnapshotGraphemeStorage replay = new(1024);
        foreach ((int cell, int length) in new[] { (0, 64), (1, 64), (2, 8), (3, 61), (4, 4) })
            if (replay.AppendToLength(cell, length) != GhosttySnapshotGraphemeAddResult.Success) replay.Clear(cell);
        Assert.Equal(replay.Count, restored.Count);
        Assert.Equal(replay.AllocatedBytes, restored.AllocatedBytes);
        Assert.False(restored.TryGetAllocation(3, out _));
        Assert.Equal(replay.Set(5, 8), restored.Set(5, 8));
        Assert.True(replay.TryGetAllocation(5, out GhosttySnapshotBitmap.Slice expected));
        Assert.True(restored.TryGetAllocation(5, out GhosttySnapshotBitmap.Slice actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InvalidTargetsAndScalarsDoNotConsumeCapacity()
    {
        GhosttySnapshotPage page = Read(Payload(1, new(8, 0, [0x301]), new(0, 80, [0x301]),
            new(0, 0, [0, 0xD800, 0x110000]), new(0, 1, [0, 0x301, 0xD800, 0x1F3FB, 0x110000]), Cluster(2, 1)));
        Assert.Equal(new[] { 0, 2, 0 }, LiveLengths(page, 3));
        Assert.Equal(new uint[] { 0x301, 0x1F3FB }, page.LiveSuffix(0, 1).ToArray());
    }

    [Fact]
    public void DroppedLiveSuffixesStillCountAgainstRawDecodeBudget()
    {
        byte[] payload = Payload(0, Cluster(0, 65));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotPage.Read(payload, Columns, 64, 1024));
        GhosttySnapshotPage page = GhosttySnapshotPage.Read(payload, Columns, 65, 1024);
        Assert.Equal(65, page.Grid.Suffix(0, 0).Length);
        Assert.Empty(page.LiveSuffix(0, 0).ToArray());
    }

    [Fact]
    public void HugeHintsDoNotReserveHugeBitmapOrMapStorage()
    {
        byte[] payload = Payload(uint.MaxValue, Cluster(0, 64));
        Read(payload);
        long before = GC.GetAllocatedBytesForCurrentThread();
        GhosttySnapshotPage page = Read(payload);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(64, page.LiveSuffix(0, 0).Length);
        Assert.InRange(allocated, 1, 64 * 1024);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(1U)]
    [InlineData(17U)]
    [InlineData(1024U)]
    [InlineData(1025U)]
    [InlineData(2048U)]
    public void PressureWireOrderDuplicatesAndContinuationMatchNative(uint capacity)
    {
        RequireNative();
        Entry[][] cases =
        [
            [Cluster(0, 64), Cluster(1, 64), Cluster(2, 8), Cluster(3, 61), Cluster(4, 4)],
            [Cluster(3, 65), Cluster(0, 1), Cluster(2, 16), Cluster(4, 64), Cluster(1, 64)],
            [Cluster(0, 64), Cluster(1, 64), Cluster(2, 8), Cluster(3, 61), new(0, 3, [0x301]), new(0, 3, [0x302])],
            [new(8, 0, [0x301]), new(0, 80, [0x301]), new(0, 0, [0, 0xD800, 0x110000]),
                new(0, 1, [0, 0x301, 0xD800, 0x1F3FB, 0x110000]), Cluster(2, 1)],
        ];
        foreach (Entry[] entries in cases) CompareNative(Payload(capacity, entries), continueInput: true);
    }

    [Fact]
    public void SeededFragmentedSuffixLayoutsMatchNative()
    {
        RequireNative();
        Random random = new(0x47524150);
        uint[] capacities = [1, 17, 64, 512, 1024, 1025, 2048, 4096];
        for (int sample = 0; sample < 64; sample++)
        {
            Entry[] entries = new Entry[random.Next(1, 24)];
            for (int i = 0; i < entries.Length; i++) entries[i] = Cluster(random.Next(Columns), random.Next(1, 81));
            CompareNative(Payload(capacities[sample % capacities.Length], entries), continueInput: false);
        }
    }

    private static void CompareNative(byte[] payload, bool continueInput)
    {
        byte[] snapshot = Snapshot(payload);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        CompareCells(native, managed);
        if (!continueInput) return;
        // CUP + combining input exercises growth after the restored page has
        // accepted and dropped entries. A subsequent overwrite frees a cluster.
        foreach (string input in new[] { "\u001b[1;5H\u0301", "\u001b[1;1Hx", "\u001b[1;4H\u0302" })
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            native.Write(bytes);
            managed.Processor.Process(bytes);
            CompareCells(native, managed);
        }
    }

    private static void CompareCells(GhosttyTerminal native, ManagedTerminalSnapshot managed)
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotGrid grid = Assert.Single(reader.ReadReady().Screens[0].Pages.ToArray()).Grid;
        TerminalRow row = managed.Screen.GetViewportRow(0);
        for (int column = 0; column < Columns; column++)
        {
            int cp = (int)((grid.Cells[column] >> 2) & 0xFFFFFF);
            ReadOnlySpan<uint> suffix = grid.Suffix(0, column);
            string? expected = suffix.IsEmpty ? null : char.ConvertFromUtf32(cp) + Scalars(suffix);
            Assert.Equal(cp, row.ReadOnlyCells[column].Codepoint);
            Assert.Equal(expected, row.ReadOnlyCells[column].Grapheme);
        }
        Assert.Equal((int)native.GetCursorX(), managed.Processor.CursorCol);
        Assert.Equal((int)native.GetCursorY(), managed.Processor.CursorRow);
    }

    private static GhosttySnapshotPage Read(byte[] payload) => GhosttySnapshotPage.Read(payload, Columns, 4096, 1024);
    private static Entry Cluster(int column, int count)
    {
        uint[] suffix = new uint[count];
        for (int i = 0; i < count; i++) suffix[i] = (uint)(0x0300 + i);
        return new(0, (ushort)column, suffix);
    }

    private static string Text(int column, ReadOnlySpan<uint> suffix) => (char)('A' + column) + Scalars(suffix);
    private static string Scalars(ReadOnlySpan<uint> suffix)
    {
        StringBuilder result = new();
        foreach (uint cp in suffix) result.Append(char.ConvertFromUtf32((int)cp));
        return result.ToString();
    }

    private static int[] LiveLengths(GhosttySnapshotPage page, int columns)
    {
        int[] result = new int[columns];
        for (int i = 0; i < columns; i++) result[i] = page.LiveSuffix(0, i).Length;
        return result;
    }

    private static byte[] Payload(uint capacity, params Entry[] entries)
    {
        using MemoryStream output = new();
        Span<byte> header = stackalloc byte[20];
        header.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(header, Columns);
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], capacity);
        output.Write(header);
        using BinaryWriter writer = new(output, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)0); // one row, byte-width bare ASCII cells
        writer.Write((ushort)Columns);
        for (int i = 0; i < Columns; i++) writer.Write((byte)('A' + i));
        writer.Write((uint)entries.Length);
        foreach (Entry entry in entries)
        {
            writer.Write(entry.Row); writer.Write(entry.Column); writer.Write((ushort)entry.Suffix.Length);
            foreach (uint cp in entry.Suffix) writer.Write(cp);
        }
        return output.ToArray();
    }

    private static byte[] Snapshot(byte[] page)
    {
        using BasicVtProcessor source = new(new TerminalScreen(Columns, 1));
        using GhosttySnapshotRecordReader reader = new(source.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            records.Add(new(tag, tag == GhosttySnapshotRecordTag.Page ? page : payload.ToArray()));
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
