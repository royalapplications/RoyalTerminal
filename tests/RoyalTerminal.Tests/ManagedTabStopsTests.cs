// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedTabStopsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(521)]
    [InlineData(4096)]
    [InlineData(65535)]
    public void DefaultsAndPackedRoundTripMatchNativeBitOrder(int columns)
    {
        ManagedTabStops stops = new(columns);
        stops.ResetDefaults();
        Assert.Equal(64 + (columns <= 512 ? 0 : ((columns - 513) / 64 + 1) * 8), stops.StorageByteLength);
        for (int column = 0; column < columns; column++)
            Assert.Equal(column > 0 && column < columns - 1 && column % 8 == 0, stops.Contains(column));

        byte[] bitmap = new byte[stops.PackedByteLength];
        Array.Fill(bitmap, (byte)0xA5);
        stops.LoadPacked(bitmap);
        if (columns % 8 != 0) bitmap[^1] &= (byte)((1 << (columns % 8)) - 1);
        byte[] actual = new byte[bitmap.Length];
        // Deliberately cross word/inline boundaries at unaligned offsets.
        for (int offset = 0; offset < actual.Length; offset += 7)
            stops.CopyPackedBytes(offset, actual.AsSpan(offset, Math.Min(7, actual.Length - offset)));
        Assert.Equal(bitmap, actual);
        stops.CopyPackedBytes(0, actual);
        Assert.Equal(bitmap, actual);
        for (int column = 0; column < columns; column++)
            Assert.Equal((bitmap[column / 8] & (1 << (column % 8))) != 0, stops.Contains(column));

        stops.Add(0);
        stops.Add(columns - 1);
        Assert.True(stops.Contains(0));
        Assert.True(stops.Contains(columns - 1));
        stops.Clear();
        Assert.Equal(-1, stops.FindNext(0, columns - 1));
        Assert.Equal(-1, stops.FindPrevious(columns - 1, 0));
        stops.ResetDefaults();
        Assert.False(stops.Contains(0));
        Assert.False(stops.Contains(columns - 1));
    }

    [Theory]
    [InlineData(65)]
    [InlineData(513)]
    [InlineData(521)]
    [InlineData(4096)]
    public void BoundedWordSearchMatchesScalarReference(int columns)
    {
        ManagedTabStops stops = new(columns);
        bool[] expected = new bool[columns];
        Random random = new(717);
        for (int i = 0; i < columns; i++)
            if (random.Next(7) == 0) { stops.Add(i); expected[i] = true; }
        for (int query = 0; query < 1024; query++)
        {
            int start = random.Next(-2, columns + 2), end = random.Next(-2, columns + 2);
            int next = -1, previous = -1;
            for (int c = Math.Max(start, 0); c <= Math.Min(end, columns - 1); c++)
                if (expected[c]) { next = c; break; }
            for (int c = Math.Min(start, columns - 1); c >= Math.Max(end, 0); c--)
                if (expected[c]) { previous = c; break; }
            Assert.Equal(next, stops.FindNext(start, end));
            Assert.Equal(previous, stops.FindPrevious(start, end));
        }
        Assert.Equal(-1, stops.FindNext(int.MaxValue, int.MaxValue));
        Assert.Equal(-1, stops.FindPrevious(int.MinValue, int.MinValue));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(520)]
    public void RemoveNeverTogglesAndSearchIncludesItsBoundary(int column)
    {
        ManagedTabStops stops = new(521);
        stops.Remove(column);
        Assert.False(stops.Contains(column));
        stops.Add(column);
        stops.Add(column);
        Assert.Equal(column, stops.FindNext(column, column));
        Assert.Equal(column, stops.FindPrevious(column, column));
        stops.Remove(column);
        stops.Remove(column);
        Assert.Equal(-1, stops.FindNext(0, 520));
    }

    [Fact]
    public void ReusedStorageOperationsAndSerializationDoNotAllocate()
    {
        ManagedTabStops stops = new(1025);
        byte[] bytes = new byte[stops.PackedByteLength];
        for (int i = 0; i < 100; i++) Exercise();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Exercise();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);

        void Exercise()
        {
            stops.ResetDefaults();
            stops.Add(512); stops.Remove(64);
            _ = stops.Contains(512);
            _ = stops.FindNext(65, 1024);
            _ = stops.FindPrevious(1024, 65);
            stops.CopyPackedBytes(0, bytes);
            stops.LoadPacked(bytes);
            stops.Clear();
        }
    }

    [Fact]
    public void InvalidBitmapLengthDoesNotMutateExistingStops()
    {
        ManagedTabStops stops = new(513);
        stops.Add(512);
        Assert.Throws<ArgumentException>(() => stops.LoadPacked(new byte[64]));
        Assert.True(stops.Contains(512));
        Assert.Throws<ArgumentOutOfRangeException>(() => stops.Add(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stops.Add(513));
        Assert.False(stops.Contains(-1)); Assert.False(stops.Contains(513));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedTabStops(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stops.CopyPackedBytes(-1, new byte[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => stops.CopyPackedBytes(65, new byte[1]));
    }
}
