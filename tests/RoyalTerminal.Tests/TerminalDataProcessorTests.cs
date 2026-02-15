// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — SIMD UTF-8 data processing tests.

using RoyalTerminal.GhosttySharp.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Tests for TerminalDataProcessor — SIMD-accelerated UTF-8 processing.
/// </summary>
public class TerminalDataProcessorTests
{
    [Fact]
    public void IsValidUtf8_WithAscii_ReturnsTrue()
    {
        var data = "Hello, World!"u8;
        Assert.True(TerminalDataProcessor.IsValidUtf8(data));
    }

    [Fact]
    public void IsValidUtf8_WithMultibyteChars_ReturnsTrue()
    {
        var data = "こんにちは世界"u8;
        Assert.True(TerminalDataProcessor.IsValidUtf8(data));
    }

    [Fact]
    public void IsValidUtf8_WithEmoji_ReturnsTrue()
    {
        var data = "Hello 🌍🚀"u8;
        Assert.True(TerminalDataProcessor.IsValidUtf8(data));
    }

    [Fact]
    public void IsValidUtf8_WithInvalidBytes_ReturnsFalse()
    {
        ReadOnlySpan<byte> data = [0xFF, 0xFE, 0x00];
        Assert.False(TerminalDataProcessor.IsValidUtf8(data));
    }

    [Fact]
    public void IsValidUtf8_Empty_ReturnsTrue()
    {
        Assert.True(TerminalDataProcessor.IsValidUtf8(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void CountUtf8Characters_AsciiOnly()
    {
        var data = "Hello"u8;
        Assert.Equal(5, TerminalDataProcessor.CountUtf8Characters(data));
    }

    [Fact]
    public void CountUtf8Characters_WithMultibyte()
    {
        // "é" = 2 bytes UTF-8, "あ" = 3 bytes, "🌍" = 4 bytes
        var data = "aéあ🌍"u8;
        Assert.Equal(4, TerminalDataProcessor.CountUtf8Characters(data));
    }

    [Fact]
    public void CountUtf8Characters_Empty_ReturnsZero()
    {
        Assert.Equal(0, TerminalDataProcessor.CountUtf8Characters(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void CountUtf8Characters_LargeAscii_UsesSimd()
    {
        // Create a large ASCII buffer that should trigger SIMD paths
        var data = new byte[256];
        Array.Fill(data, (byte)'A');
        Assert.Equal(256, TerminalDataProcessor.CountUtf8Characters(data));
    }

    [Fact]
    public void StripAnsiEscapes_RemovesEscapeSequences()
    {
        // ESC[31m = red foreground, ESC[0m = reset
        byte[] input = [0x1B, (byte)'[', (byte)'3', (byte)'1', (byte)'m',
                        (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o',
                        0x1B, (byte)'[', (byte)'0', (byte)'m'];

        Span<byte> output = stackalloc byte[256];
        var len = TerminalDataProcessor.StripAnsiEscapes(input, output);

        Assert.Equal(5, len);
        Assert.True(output[..len].SequenceEqual("Hello"u8));
    }

    [Fact]
    public void StripAnsiEscapes_PlainText_Unchanged()
    {
        var input = "Hello, World!"u8;
        Span<byte> output = stackalloc byte[256];
        var len = TerminalDataProcessor.StripAnsiEscapes(input, output);

        Assert.Equal(input.Length, len);
        Assert.True(output[..len].SequenceEqual(input));
    }

    [Fact]
    public void ExtractLineRanges_SplitsLines()
    {
        var data = "Line1\nLine2\nLine3"u8;
        var ranges = TerminalDataProcessor.ExtractLineRanges(data);

        Assert.Equal(3, ranges.Count);

        var line1 = data[ranges[0]];
        Assert.True(line1.SequenceEqual("Line1"u8));

        var line2 = data[ranges[1]];
        Assert.True(line2.SequenceEqual("Line2"u8));

        var line3 = data[ranges[2]];
        Assert.True(line3.SequenceEqual("Line3"u8));
    }

    [Fact]
    public void ExtractLineRanges_HandlesCRLF()
    {
        var data = "A\r\nB\r\nC"u8;
        var ranges = TerminalDataProcessor.ExtractLineRanges(data);
        Assert.True(ranges.Count >= 3);
    }

    [Fact]
    public void DecodeUtf8_DecodesCorrectly()
    {
        var data = "Hello, 世界"u8;
        var decoded = TerminalDataProcessor.DecodeUtf8(data);
        Assert.Equal("Hello, 世界", decoded);
    }

    [Fact]
    public void EncodeUtf8_EncodesCorrectly()
    {
        Span<byte> dest = stackalloc byte[256];
        var written = TerminalDataProcessor.EncodeUtf8("Hello", dest);

        Assert.Equal(5, written);
        Assert.True(dest[..written].SequenceEqual("Hello"u8));
    }
}
