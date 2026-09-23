// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedControlStringParityTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryGroundByteMatchesNativeUtf8AndControlRules()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native ground byte matrix available: {available}");
        if (!available) return;
        TerminalScreen screen = new(40, 3), nativeScreen = new(40, 3);
        using BasicVtProcessor managed = new(screen);
        using GhosttyVtProcessor native = new(nativeScreen);
        byte[] bytes = new byte[] { 0, 0x18, (byte)'X' };
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bytes[0] = (byte)value;
            managed.Reset(); native.Reset();
            managed.Process(bytes); native.Process(bytes);
            Assert.Equal((native.CursorRow, native.CursorCol), (managed.CursorRow, managed.CursorCol));
            CompareCells(nativeScreen, screen);
            Assert.True(managed.IsParserGround);
        }
    }

    [Theory]
    [InlineData("\u001b]2;hello\u0007X")]
    [InlineData("\u001b]2;hello\u001b\\X")]
    [InlineData("\u001b]2;hello\u001b[31mX")]
    [InlineData("\u001b]2;hello\u001b\u001b[31mX")]
    [InlineData("\u001b]2;hello\u0018X")]
    [InlineData("\u001b]2;hello\u001aX")]
    [InlineData("\u001b]2;a\u0000\u0001\u0005\u0008\u0009\u000a\u000d\u000e\u000f\u0019\u001fb\u0007X")]
    [InlineData("\u001b]2;a\u007fb\u0007X")]
    [InlineData("\u001b]2;ÜÛ\u001b\\X")]
    [InlineData("\u001b]10;?\u001b[31mX")]
    [InlineData("\u001b]10;?\u0018X")]
    public void OscExitAndPayloadRulesMatchNativeAcrossEverySplit(string input)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native OSC differential available: {available}");
        if (!available) return;
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        TerminalScreen nativeScreen = new(40, 3);
        using GhosttyVtProcessor native = new(nativeScreen);
        string? nativeTitle = null;
        List<byte[]> nativeReplies = [];
        native.TitleCallback = value => nativeTitle = value;
        native.ResponseCallback = nativeReplies.Add;
        native.Process(bytes);
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen screen = new(40, 3);
            using BasicVtProcessor managed = new(screen);
            string? title = null;
            List<byte[]> replies = [];
            managed.TitleCallback = value => title = value;
            managed.ResponseCallback = replies.Add;
            managed.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(split));
            Assert.Equal(nativeTitle, title);
            Assert.Equal(nativeReplies.Count, replies.Count);
            for (int i = 0; i < replies.Count; i++) Assert.Equal(nativeReplies[i], replies[i]);
            Assert.Equal((native.CursorRow, native.CursorCol), (managed.CursorRow, managed.CursorCol));
            CompareCells(nativeScreen, screen);
            Assert.True(managed.IsParserGround);
        }
    }

    [Fact]
    public void OscCommitsAtEscapeAndReplayDoesNotRepeatItsEffect()
    {
        using BasicVtProcessor managed = new(new TerminalScreen(12, 3));
        using BasicVtProcessor replay = new(new TerminalScreen(12, 3));
        int titles = 0, replayTitles = 0;
        managed.TitleCallback = _ => titles++;
        replay.TitleCallback = _ => replayTitles++;
        managed.Process("\u001b]2;committed\u001b"u8);
        Assert.Equal(1, titles);
        Assert.Equal(new byte[] { 0x1B }, managed.GetContinuation());
        Assert.True(GhosttySnapshotContinuation.IsValid(managed.GetContinuation()));
        replay.Process(managed.GetContinuation());
        Assert.True(managed.ProcessUntilGround("\\X"u8, out int consumed));
        Assert.Equal(1, consumed);
        replay.Process("\\"u8);
        Assert.Equal(0, replayTitles);
        Assert.Equal(1, titles);
        Assert.Empty(managed.GetContinuation());
    }

    private static void CompareCells(TerminalScreen expected, TerminalScreen actual)
    {
        for (int row = 0; row < expected.ViewportRows; row++)
            for (int col = 0; col < expected.Columns; col++)
            {
                TerminalCell a = expected.GetViewportRow(row)[col], b = actual.GetViewportRow(row)[col];
                Assert.Equal(a.Codepoint, b.Codepoint);
                if (a.Codepoint != 0) Assert.Equal(a.ForegroundIdentity, b.ForegroundIdentity);
            }
    }
}
