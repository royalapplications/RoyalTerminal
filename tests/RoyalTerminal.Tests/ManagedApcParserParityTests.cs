// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedApcParserParityTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryApcPayloadByteMatchesNativeTransitionsAndUnknownReporting()
    {
        if (!NativeAvailable()) return;
        byte[] bytes = new byte[] { 0x1B, (byte)'_', (byte)'x', 0, 0x1B, (byte)'\\', (byte)'X' };
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bytes[3] = (byte)value;
            CompareEverySplit(bytes);
        }
    }

    [Theory]
    [InlineData(0x18)]
    [InlineData(0x1A)]
    [InlineData(0x1B)]
    [InlineData(0x9C)]
    [InlineData(0x9B)]
    [InlineData(0x85)]
    public void CompletedKittyQueryMatchesNativeEvenOnAbortingExit(int exit)
    {
        if (!NativeAvailable()) return;
        byte[] prefix = "\u001b_Ga=q,i=73,s=1,v=1,f=32;AAAA/w=="u8.ToArray();
        byte[] bytes = new byte[prefix.Length + 4];
        prefix.CopyTo(bytes, 0);
        bytes[prefix.Length] = (byte)exit;
        bytes[^3] = 0x1B; bytes[^2] = (byte)'\\'; bytes[^1] = (byte)'X';
        CompareEverySplit(bytes);
    }

    [Fact]
    public void ApcCommitsOnEscapeAndContinuationCannotRepeatIt()
    {
        using BasicVtProcessor source = new(new TerminalScreen(12, 3)), replay = new(new TerminalScreen(12, 3));
        List<TerminalUnknownSequence> sourceEvents = [], replayEvents = [];
        source.UnknownSequenceCallback = sourceEvents.Add;
        replay.UnknownSequenceCallback = replayEvents.Add;
        source.Process("\u001b_payload\u001b"u8);
        Assert.Equal("payload", Encoding.ASCII.GetString(Assert.Single(sourceEvents).Content));
        Assert.Equal(new byte[] { 0x1B }, source.GetContinuation());
        replay.Process(source.GetContinuation());
        replay.Process("\\"u8);
        Assert.Empty(replayEvents);
    }

    [Fact]
    public void C1ExitAfterKittyCommitRetainsOnlyCanonicalNewSequence()
    {
        using BasicVtProcessor source = new(new TerminalScreen(12, 3)), replay = new(new TerminalScreen(12, 3));
        List<byte[]> responses = [], replayResponses = [];
        source.ResponseCallback = responses.Add;
        replay.ResponseCallback = replayResponses.Add;
        source.Process("\u001b_Ga=q,i=73,s=1,v=1,f=32;AAAA/w=="u8);
        source.Process(new byte[] { 0x9B, (byte)'3' });
        Assert.Single(responses);
        Assert.Equal("\u001b[3"u8.ToArray(), source.GetContinuation());
        Assert.True(GhosttySnapshotContinuation.IsValid(source.GetContinuation()));
        replay.Process(source.GetContinuation());
        replay.Process("1m"u8);
        Assert.Empty(replayResponses);
    }

    [Fact]
    public void BulkPayloadHonorsCaptureCapAndWarmIngestionDoesNotAllocate()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(12, 3), new() { ContinuationMaxBytes = 0 });
        byte[] payload = new byte[1024 * 1024]; Array.Fill(payload, (byte)'x');
        List<TerminalUnknownSequence> events = [];
        processor.UnknownSequenceCallback = events.Add;
        processor.Process("\u001b_"u8); processor.Process(payload); processor.Process("\u001b\\"u8);
        TerminalUnknownSequence captured = Assert.Single(events);
        Assert.Equal(4096, captured.Content.Length);
        Assert.True(captured.Truncated);
        processor.Process("\u001b_"u8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        processor.Process(payload);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        processor.Process("\u0018"u8);
        Assert.Single(events); // An aborted unknown APC is not published.
    }

    private bool NativeAvailable()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native APC differential available: {available}");
        return available;
    }

    private static void CompareEverySplit(byte[] bytes)
    {
        TerminalScreen nativeScreen = new(40, 3);
        using GhosttyVtProcessor native = new(nativeScreen);
        List<TerminalUnknownSequence> nativeEvents = [];
        List<byte[]> nativeResponses = [];
        native.UnknownSequenceCallback = nativeEvents.Add; native.ResponseCallback = nativeResponses.Add;
        native.Process(bytes);
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen screen = new(40, 3);
            using BasicVtProcessor managed = new(screen);
            List<TerminalUnknownSequence> events = [];
            List<byte[]> responses = [];
            managed.UnknownSequenceCallback = events.Add; managed.ResponseCallback = responses.Add;
            managed.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(split));
            Assert.Equal(nativeEvents.Count, events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                Assert.Equal(nativeEvents[i].Content, events[i].Content);
                Assert.Equal(nativeEvents[i].Truncated, events[i].Truncated);
            }
            Assert.True(nativeResponses.Count == responses.Count,
                $"Response count mismatch for {Convert.ToHexString(bytes)}, split {split}: native {nativeResponses.Count}, managed {responses.Count}");
            for (int i = 0; i < responses.Count; i++) Assert.Equal(nativeResponses[i], responses[i]);
            Assert.Equal((native.CursorRow, native.CursorCol), (managed.CursorRow, managed.CursorCol));
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 40; col++)
                    Assert.Equal(nativeScreen.GetViewportRow(row)[col].Codepoint, screen.GetViewportRow(row)[col].Codepoint);
            Assert.True(managed.IsParserGround);
        }
    }
}
