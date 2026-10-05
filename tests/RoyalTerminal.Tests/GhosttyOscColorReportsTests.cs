// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttyOscColorReportsTests
{
    [Theory]
    [InlineData("\u001b]10;rgb:1111/2222/3333\a", "\u001b]10;rgb:11/22/33\a")]
    [InlineData("\u001b]4;255;rgb:ABAB/CDCD/EFEF\u001b\\", "\u001b]4;255;rgb:AB/CD/EF\u001b\\")]
    [InlineData("\u001b]10;rgb:ffff/0000/0000\a\u001b]11;rgb:0000/ffff/0000\u001b\\\u001b]12;rgb:0000/0000/ffff\a",
        "\u001b]10;rgb:ff/00/00\a\u001b]11;rgb:00/ff/00\u001b\\\u001b]12;rgb:00/00/ff\a")]
    public void CompleteNativeReportsRetainBatchOrderAndTerminators(string input, string expected)
    {
        byte[] source = Encoding.ASCII.GetBytes(input);
        byte[] result = new byte[GhosttyOscColorReports.GetEightBitLength(source)];
        Assert.Equal(result.Length, GhosttyOscColorReports.WriteEightBit(source, result));
        Assert.Equal(expected, Encoding.ASCII.GetString(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\u001b]10;rgb:1111/2222/3333")]
    [InlineData("\u001b]10;rgb:1111/2222/3333\u001b")]
    [InlineData("\u001b]10;rgb:1122/2222/3333\a")]
    [InlineData("\u001b]10;rgb:zzzz/2222/3333\a")]
    [InlineData("\u001b]10;rgb:11/22/33\a")]
    [InlineData("\u001b]21;foreground=rgb:1111/2222/3333\a")]
    [InlineData("\u001b]4;256;rgb:1111/2222/3333\a")]
    [InlineData("\u001b]4;x;rgb:1111/2222/3333\a")]
    [InlineData("\u001b]4;;rgb:1111/2222/3333\a")]
    [InlineData("\u001b]4;1;")]
    [InlineData("text\u001b]10;rgb:1111/2222/3333\a")]
    [InlineData("\u001b]10;rgb:1111/2222/3333\aother reply")]
    [InlineData("\u001b]10;rgb:1111/2222/3333\a\u001b[1;1R")]
    public void NonColorMalformedAndIncompleteBatchesAreByteExact(string input)
    {
        byte[] source = Encoding.ASCII.GetBytes(input);
        Assert.Equal(source.Length, GhosttyOscColorReports.GetEightBitLength(source));
        byte[] result = new byte[source.Length];
        Assert.Equal(result.Length, GhosttyOscColorReports.WriteEightBit(source, result));
        Assert.Equal(source, result);
    }

    [Fact]
    public void WarmFormattingDoesNotAllocate()
    {
        ReadOnlySpan<byte> source = "\u001b]4;255;rgb:1111/2222/3333\u001b\\\u001b]10;rgb:1111/2222/3333\a"u8;
        Span<byte> destination = stackalloc byte[source.Length];
        for (int i = 0; i < 1000; i++) GhosttyOscColorReports.WriteEightBit(source, destination);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) GhosttyOscColorReports.WriteEightBit(source, destination);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
