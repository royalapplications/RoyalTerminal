// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Core data processing tests (no Avalonia required).

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Tests for TerminalBuffer — ring buffer with ArrayPool.
/// </summary>
public class TerminalBufferTests
{
    [Fact]
    public void NewBuffer_HasZeroCount()
    {
        using var buffer = new TerminalBuffer(1024);
        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.Capacity >= 1024);
    }

    [Fact]
    public void Write_IncreasesCount()
    {
        using var buffer = new TerminalBuffer(1024);
        var data = "Hello"u8;
        buffer.Write(data);
        Assert.Equal(5, buffer.Count);
    }

    [Fact]
    public void WriteAndRead_RoundTrips()
    {
        using var buffer = new TerminalBuffer(1024);
        var input = "Hello, World!"u8;
        buffer.Write(input);

        Span<byte> output = stackalloc byte[256];
        var read = buffer.Read(output);

        Assert.Equal(input.Length, read);
        Assert.True(output[..read].SequenceEqual(input));
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Read_OnEmpty_ReturnsZero()
    {
        using var buffer = new TerminalBuffer(1024);
        Span<byte> output = stackalloc byte[256];
        var read = buffer.Read(output);
        Assert.Equal(0, read);
    }

    [Fact]
    public void Write_WrapsAround()
    {
        using var buffer = new TerminalBuffer(16);
        var capacity = buffer.Capacity;

        // Fill the buffer
        var fill = new byte[capacity];
        for (int i = 0; i < capacity; i++) fill[i] = (byte)(i & 0xFF);
        buffer.Write(fill);
        Assert.Equal(capacity, buffer.Count);

        // Read half
        Span<byte> half = stackalloc byte[capacity / 2];
        buffer.Read(half);

        // Write again — this wraps around
        var more = new byte[capacity / 2];
        for (int i = 0; i < more.Length; i++) more[i] = 0xAA;
        buffer.Write(more);

        // Read everything back
        Span<byte> all = stackalloc byte[capacity];
        var readCount = buffer.Read(all);
        Assert.Equal(capacity, readCount);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        using var buffer = new TerminalBuffer(1024);
        buffer.Write("Some data"u8);
        Assert.True(buffer.Count > 0);

        buffer.Clear();
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Peek_DoesNotConsumeData()
    {
        using var buffer = new TerminalBuffer(1024);
        buffer.Write("Peek test"u8);
        var countBefore = buffer.Count;

        Span<byte> peeked = stackalloc byte[256];
        var peekCount = buffer.Peek(peeked);

        Assert.Equal(countBefore, buffer.Count);
        Assert.Equal(countBefore, peekCount);
    }

    [Fact]
    public void EnsureCapacity_Grows()
    {
        using var buffer = new TerminalBuffer(16);
        var oldCapacity = buffer.Capacity;

        buffer.Write("Initial data"u8);
        buffer.EnsureCapacity(1024);

        Assert.True(buffer.Capacity >= 1024);

        // Data should be preserved
        Span<byte> output = stackalloc byte[256];
        var read = buffer.Read(output);
        Assert.Equal("Initial data"u8.Length, read);
    }

    [Fact]
    public void Overwrite_WhenFull_KeepsTailData()
    {
        using var buffer = new TerminalBuffer(16);
        var capacity = buffer.Capacity;

        // Write more than capacity
        var large = new byte[capacity + 10];
        for (int i = 0; i < large.Length; i++) large[i] = (byte)(i & 0xFF);
        buffer.Write(large);

        // Should still have capacity count
        Assert.Equal(capacity, buffer.Count);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var buffer = new TerminalBuffer(1024);
        buffer.Dispose();
        buffer.Dispose(); // Should not throw
    }
}
