// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Tests for Ghostty Windows unsupported-sequence sanitizer.

using System.Buffers;
using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttyUnsupportedWindowsSequenceSanitizerTests
{
    [Fact]
    public void TrySanitize_NoEscapeBytes_ReturnsUnchangedFastPath()
    {
        TerminalUnsupportedWindowsSequenceSanitizer sanitizer = new();

        bool changed = sanitizer.TrySanitize("hello"u8, out byte[]? sanitized, out int length);

        Assert.False(changed);
        Assert.Null(sanitized);
        Assert.Equal(5, length);
    }

    [Fact]
    public void TrySanitize_Split9001SequenceAcrossChunks_StripsSequence()
    {
        TerminalUnsupportedWindowsSequenceSanitizer sanitizer = new();

        bool changed = sanitizer.TrySanitize("\x1b[?90"u8, out byte[]? firstChunk, out int firstLength);
        Assert.True(changed);
        Assert.NotNull(firstChunk);
        Assert.Equal(0, firstLength);
        Return(firstChunk);

        changed = sanitizer.TrySanitize("01hABC"u8, out byte[]? secondChunk, out int secondLength);
        Assert.True(changed);
        Assert.NotNull(secondChunk);
        Assert.Equal("ABC", Encoding.ASCII.GetString(secondChunk!, 0, secondLength));
        Return(secondChunk);
    }

    [Fact]
    public void TrySanitize_SplitBracketedPasteDelimiterAcrossChunks_StripsDelimiter()
    {
        TerminalUnsupportedWindowsSequenceSanitizer sanitizer = new();

        bool changed = sanitizer.TrySanitize("\x1b[20"u8, out byte[]? firstChunk, out int firstLength);
        Assert.True(changed);
        Assert.NotNull(firstChunk);
        Assert.Equal(0, firstLength);
        Return(firstChunk);

        changed = sanitizer.TrySanitize("0~xyz"u8, out byte[]? secondChunk, out int secondLength);
        Assert.True(changed);
        Assert.NotNull(secondChunk);
        Assert.Equal("xyz", Encoding.ASCII.GetString(secondChunk!, 0, secondLength));
        Return(secondChunk);
    }

    [Fact]
    public void TrySanitize_NonTargetCsiAcrossChunks_PreservesData()
    {
        TerminalUnsupportedWindowsSequenceSanitizer sanitizer = new();

        bool changed = sanitizer.TrySanitize("\x1b[2"u8, out byte[]? firstChunk, out int firstLength);
        Assert.True(changed);
        Assert.NotNull(firstChunk);
        Assert.Equal(0, firstLength);
        Return(firstChunk);

        changed = sanitizer.TrySanitize("Jx"u8, out byte[]? secondChunk, out int secondLength);
        Assert.True(changed);
        Assert.NotNull(secondChunk);
        Assert.Equal("\x1b[2Jx", Encoding.ASCII.GetString(secondChunk!, 0, secondLength));
        Return(secondChunk);
    }

    [Fact]
    public void Reset_ClearsPendingCarry()
    {
        TerminalUnsupportedWindowsSequenceSanitizer sanitizer = new();
        sanitizer.TrySanitize("\x1b[?90"u8, out byte[]? firstChunk, out int firstLength);
        Assert.NotNull(firstChunk);
        Assert.Equal(0, firstLength);
        Return(firstChunk);

        sanitizer.Reset();

        bool changed = sanitizer.TrySanitize("01hZ"u8, out byte[]? secondChunk, out int secondLength);
        Assert.False(changed);
        Assert.Null(secondChunk);
        Assert.Equal(4, secondLength);
    }

    private static void Return(byte[]? buffer)
    {
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
