// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedDcsParserParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("\u001bP")]
    [InlineData("\u001bP1")]
    [InlineData("\u001bP$")]
    [InlineData("\u001bP:")]
    [InlineData("\u001bP$q")]
    public void EveryByteInEachDcsStateMatchesNative(string prefix)
    {
        if (!NativeAvailable()) return;
        byte[] bytes = Encoding.ASCII.GetBytes(prefix + "?\u001b\\X");
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bytes[prefix.Length] = (byte)value;
            CompareEverySplit(bytes);
        }
    }

    [Theory]
    [InlineData("\u001bP1;2$qr\u001b\\X")]
    [InlineData("\u001bP999999999999999999999$qr\u001b\\X")]
    [InlineData("\u001bP\r\n$\tqr\u001b\\X")]
    [InlineData("\u001bP$q\u007fm\u001b\\X")]
    [InlineData("\u001bP$qm\r\u001b\\X")]
    [InlineData("\u001bP$qabc\u001b\\X")]
    [InlineData("\u001bP$qm\u0018X")]
    [InlineData("\u001bP$qm\u001aX")]
    [InlineData("\u001bP$qm\u001b[31mX")]
    [InlineData("\u001bP+q616d\u0018X")]
    [InlineData("\u001bP:$qr\u001b\\X")]
    [InlineData("\u001bP1?$qr\u001b\\X")]
    [InlineData("\u001bP$$qr\u001b\\X")]
    [InlineData("\u001bP$1qr\u001b\\X")]
    public void QueryHeadersPayloadControlsAndUnhookMatchNative(string input)
    {
        if (NativeAvailable()) CompareEverySplit(Encoding.ASCII.GetBytes(input));
    }

    [Theory]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    public void ParameterLimitMatchesNative(int count)
    {
        if (!NativeAvailable()) return;
        CompareEverySplit(Encoding.ASCII.GetBytes("\u001bP" + string.Join(';', new int[count]) + "$qr\u001b\\X"));
        CompareEverySplit(Encoding.ASCII.GetBytes("\u001bP" + new string(';', count) + "$qr\u001b\\X"));
    }

    [Fact]
    public void EscapeCommitsQueryOnceAndRetainsOnlyReplaySafeEscape()
    {
        using BasicVtProcessor source = new(new TerminalScreen(12, 3)), replay = new(new TerminalScreen(12, 3));
        List<byte[]> responses = [], replayResponses = [];
        source.ResponseCallback = responses.Add;
        replay.ResponseCallback = replayResponses.Add;
        source.Process("\u001bP$qr\u001b"u8);
        Assert.Single(responses);
        Assert.Equal(new byte[] { 0x1B }, source.GetContinuation());
        Assert.True(GhosttySnapshotContinuation.IsValid(source.GetContinuation()));
        replay.Process(source.GetContinuation());
        replay.Process("\\"u8);
        Assert.Empty(replayResponses);
    }

    [Theory]
    [InlineData(5000, true)]
    [InlineData(1024 * 1024 - 4, true)]
    [InlineData(1024 * 1024, false)]
    public void XtgettcapHasOneMebibytePayloadBudget(int padding, bool responds)
    {
        // Empty capability names consume bytes but produce no replies.
        byte[] bytes = Encoding.ASCII.GetBytes("\u001bP+q" + new string(';', padding) + "616d\u001b\\");
        using BasicVtProcessor managed = new(new TerminalScreen(12, 3));
        List<byte[]> replies = [];
        managed.ResponseCallback = replies.Add;
        managed.Process(bytes);
        Assert.Equal(responds ? 1 : 0, replies.Count);
        if (!NativeAvailable()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(12, 3));
        List<byte[]> nativeReplies = [];
        native.ResponseCallback = nativeReplies.Add;
        native.Process(bytes);
        Assert.Equal(nativeReplies.Count, replies.Count);
        if (responds) Assert.Equal(nativeReplies[0], replies[0]);
    }

    private bool NativeAvailable()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native DCS differential available: {available}");
        return available;
    }

    private static void CompareEverySplit(byte[] bytes)
    {
        TerminalScreen nativeScreen = new(40, 3);
        using GhosttyVtProcessor native = new(nativeScreen);
        List<byte[]> nativeResponses = [];
        native.ResponseCallback = nativeResponses.Add;
        native.Process(bytes);
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen screen = new(40, 3);
            using BasicVtProcessor managed = new(screen);
            List<byte[]> responses = [];
            managed.ResponseCallback = responses.Add;
            managed.Process(bytes.AsSpan(0, split));
            managed.Process(bytes.AsSpan(split));
            Assert.True(nativeResponses.Count == responses.Count,
                $"Response mismatch for {Convert.ToHexString(bytes)}, split {split}: native {nativeResponses.Count}, managed {responses.Count}");
            for (int i = 0; i < responses.Count; i++) Assert.Equal(nativeResponses[i], responses[i]);
            Assert.Equal((native.CursorRow, native.CursorCol), (managed.CursorRow, managed.CursorCol));
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 40; col++)
                    Assert.Equal(nativeScreen.GetViewportRow(row)[col].Codepoint, screen.GetViewportRow(row)[col].Codepoint);
            Assert.True(managed.IsParserGround);
        }
    }
}
