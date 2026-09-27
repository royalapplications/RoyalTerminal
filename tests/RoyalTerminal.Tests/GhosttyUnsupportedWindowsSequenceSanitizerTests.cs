// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Tests for Ghostty Windows unsupported-sequence sanitizer.

using System.Buffers;
using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Ghostty Terminal.print/setAttribute, Windows Terminal AdaptDispatch.PrintString/
/// CarriageReturn and xterm.js InputHandler.charAttributes preserve printed spaces.
/// PowerShell StringDecorated.ToString(Ansi) also preserves the original text;
/// ConsoleLineOutput delegates line wrapping, not terminal-side whitespace removal.
/// SGR followed by CR/LF cannot identify ConPTY padding: preserve bytes regardless
/// of chunk boundaries and let Ghostty own reflow of the resulting cells.
/// </summary>
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
    public void TrySanitize_TrailingPlainSpacesBeforeLineBreak_PreservesSpaces()
    {
        TerminalUnsupportedWindowsSequenceSanitizer sanitizer = new();

        bool changed = sanitizer.TrySanitize("alpha   \r\nbeta"u8, out byte[]? sanitized, out int length);

        Assert.False(changed);
        Assert.Null(sanitized);
        Assert.Equal("alpha   \r\nbeta".Length, length);
    }

    [Fact]
    public void TrySanitize_TrailingStyledSpacesBeforeSgrResetAndLineBreak_PreservesSpaces()
    {
        TerminalUnsupportedWindowsSequenceSanitizer sanitizer = new();

        bool changed = sanitizer.TrySanitize(
            "\x1b[44;1mDIR   \x1b[0m\r\nNEXT"u8,
            out byte[]? sanitized,
            out int length);

        Assert.False(changed);
        Assert.Null(sanitized);
        Assert.Equal("\x1b[44;1mDIR   \x1b[0m\r\nNEXT".Length, length);
    }

    [Theory]
    [InlineData(" \u001b[44m\r\n", " \u001b[44m\r\n")]
    [InlineData("\u001b[44;1mDIR   \u001b[0m\r\nNEXT", "\u001b[44;1mDIR   \u001b[0m\r\nNEXT")]
    [InlineData("A  \u001b[0m\u001b[44m\rB  \u001b[31m\n", "A  \u001b[0m\u001b[44m\rB  \u001b[31m\n")]
    [InlineData("\u001b[?9001hA  \u001b[44m\r\n\u001b[201~", "A  \u001b[44m\r\n")]
    public void TrySanitize_StyledSpacesArePreservedAtEverySplit(string input, string expected)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalUnsupportedWindowsSequenceSanitizer sanitizer = new();
            List<byte> actual = new();
            Append(bytes.AsSpan(0, split));
            Append(bytes.AsSpan(split));
            Assert.Equal(expectedBytes, actual.ToArray());

            void Append(ReadOnlySpan<byte> chunk)
            {
                bool changed = sanitizer.TrySanitize(chunk, out byte[]? sanitized, out int length);
                try
                {
                    ReadOnlySpan<byte> result = changed ? sanitized.AsSpan(0, length) : chunk;
                    foreach (byte value in result) actual.Add(value);
                }
                finally { Return(sanitized); }
            }
        }
    }

    [Fact]
    public void TrySanitize_InternalSpacesBeforeStyleChange_PreservesSpaces()
    {
        TerminalUnsupportedWindowsSequenceSanitizer sanitizer = new();

        bool changed = sanitizer.TrySanitize("A  \x1b[31mB\r\n"u8, out byte[]? sanitized, out int length);

        Assert.False(changed);
        Assert.Null(sanitized);
        Assert.Equal("A  \x1b[31mB\r\n".Length, length);
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
