// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedContinuationTests
{
    [Theory]
    [InlineData("\x1b[38;2;1;2;3", "mX")]
    [InlineData("\x1b]2;héllo", "\x1b\\X")]
    [InlineData("\x1b]2;hello\x1b", "\\X")]
    [InlineData("\x1bP$q", "m\x1b\\X")]
    [InlineData("\x1b_hello", "\x1b\\X")]
    [InlineData("\x1b(", "0X")]
    public void Continuation_ReplaysEveryInputSplitWithoutRepeatingCompletedWork(string prefix, string tail)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(prefix);
        byte[] expected = prefix.EndsWith('\x1b') ? new byte[] { 0x1B } : bytes;
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen sourceScreen = new(20, 3, 0);
            TerminalScreen replayScreen = new(20, 3, 0);
            using BasicVtProcessor source = new(sourceScreen);
            using BasicVtProcessor replay = new(replayScreen);
            source.Process("A"u8);
            replay.Process("A"u8);
            source.Process(bytes.AsSpan(0, split));
            source.Process(bytes.AsSpan(split));
            Assert.False(source.IsParserGround);
            Assert.Equal(expected, source.GetContinuation());
            using MemoryStream stream = new();
            source.WriteContinuationTo(stream);
            Assert.Equal(expected, stream.ToArray());
            replay.Process(source.GetContinuation());
            Assert.Equal(source.GetContinuation(), replay.GetContinuation());
            source.Process(Encoding.UTF8.GetBytes(tail));
            replay.Process(Encoding.UTF8.GetBytes(tail));
            Assert.True(source.IsParserGround);
            Assert.Empty(source.GetContinuation());
            Assert.Equal(source.CursorCol, replay.CursorCol);
            for (int column = 0; column < sourceScreen.Columns; column++)
            {
                Assert.Equal(sourceScreen.GetRow(0).ReadOnlyCells[column], replayScreen.GetRow(0).ReadOnlyCells[column]);
            }
        }
    }

    [Fact]
    public void UntilGround_ConsumesExactlyTheFinishingSequenceAndNoFollowingText()
    {
        TerminalScreen screen = new(8, 2, 0);
        using BasicVtProcessor processor = new(screen);
        Assert.True(processor.ProcessUntilGround("untouched"u8, out int consumed));
        Assert.Equal(0, consumed);
        processor.Process("A\x1b[3"u8);
        Assert.False(processor.ProcessUntilGround("1"u8, out consumed));
        Assert.Equal(1, consumed);
        Assert.True(processor.ProcessUntilGround("mXYZ"u8, out consumed));
        Assert.Equal(1, consumed);
        Assert.Equal(1, processor.CursorCol);
        Assert.Empty(processor.GetContinuation());
        processor.Process("X"u8);
        Assert.Equal(screen.Theme.Palette[1], screen.GetRow(0).ReadOnlyCells[1].Foreground);
    }

    [Fact]
    public void Utf8Continuation_IsRetainedAndCompletesBeforeFollowingAscii()
    {
        TerminalScreen screen = new(8, 2, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process(new byte[] { 0xF0, 0x9F });
        Assert.False(processor.IsParserGround);
        Assert.Equal(new byte[] { 0xF0, 0x9F }, processor.GetContinuation());
        Assert.True(processor.ProcessUntilGround(new byte[] { 0x98, 0x80, (byte)'X' }, out int consumed));
        Assert.Equal(2, consumed);
        Assert.Equal(0x1F600, screen.GetRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Empty(processor.GetContinuation());
    }

    [Fact]
    public void InvalidUtf8FollowedByNewLead_ReplacesTheUnfinishedFragment()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        processor.Process(new byte[] { 0xF0, 0x9F });
        processor.Process(new byte[] { 0xC3 });
        Assert.Equal(new byte[] { 0xC3 }, processor.GetContinuation());
    }

    [Fact]
    public void ContinuationLimit_DoesNotStopParsingAndRecoversAtGround()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0), new() { ContinuationMaxBytes = 4 });
        processor.Process("\x1b]2;long"u8);
        Assert.Throws<InvalidOperationException>(() => processor.GetContinuation());
        Assert.True(processor.ProcessUntilGround("\x1b\\NEXT"u8, out int consumed));
        Assert.Equal(2, consumed);
        Assert.Empty(processor.GetContinuation());
        processor.Process("\x1b["u8);
        Assert.Equal("\x1b["u8.ToArray(), processor.GetContinuation());
    }

    [Fact]
    public void CancelAndResetDiscardContinuationIncludingPartialUtf8()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        processor.Process("\x1b]2;title"u8);
        Assert.True(processor.ProcessUntilGround("\x18NEXT"u8, out int consumed));
        Assert.Equal(1, consumed);
        processor.Process(new byte[] { 0xF0, 0x9F });
        processor.Reset();
        Assert.True(processor.IsParserGround);
        Assert.Empty(processor.GetContinuation());
    }

    [Fact]
    public void DisabledRetentionStillSupportsGroundBoundaryProcessing()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0), new() { ContinuationMaxBytes = 0 });
        processor.Process("\x1b[31"u8);
        Assert.Throws<InvalidOperationException>(() => processor.GetContinuation());
        Assert.True(processor.ProcessUntilGround("mX"u8, out int consumed));
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void GroundEightBitControlsAreInvalidUtf8NotReplayFragments()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        processor.Process(new byte[] { 0x9D, (byte)'2', (byte)';', (byte)'A' });
        Assert.True(processor.IsParserGround);
        Assert.Empty(processor.GetContinuation());
        Assert.True(processor.ProcessUntilGround(new byte[] { 0x9C, (byte)'X' }, out int consumed));
        Assert.Equal(0, consumed);
    }
}
