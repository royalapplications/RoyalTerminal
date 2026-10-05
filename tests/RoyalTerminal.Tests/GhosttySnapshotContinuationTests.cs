// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotContinuationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("ground")]
    [InlineData("utf8")]
    [InlineData("esc")]
    [InlineData("csi")]
    [InlineData("osc")]
    [InlineData("dcs")]
    [InlineData("apc")]
    public void EveryGoldenContinuationIsAcceptedWithoutAllocations(string kind)
    {
        byte[] fixture = GhosttySnapshotFramingTests.Fixture($"continuation-{kind}-v1.hex");
        byte[] source = [.. GhosttySnapshotFraming.Envelope, .. fixture];
        using GhosttySnapshotRecordReader reader = new(source, 1000);
        reader.ReadEnvelope();
        Assert.Equal(GhosttySnapshotRecordTag.Continuation, reader.ReadRecord(out ReadOnlySpan<byte> payload));
        GhosttySnapshotContinuation.Validate(payload);
        for (int i = 0; i < 100; i++) GhosttySnapshotContinuation.Validate(payload);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) GhosttySnapshotContinuation.Validate(payload);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void AllBytesAcrossParserStatesAndRandomFragmentsMatchNativeValidation()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native continuation validation available: {available}");
        if (!available) return;
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        string[] prefixes = ["", "\u001b", "\u001b(", "\u001b[", "\u001b[1", "\u001b[1 ", "\u001b[:",
            "\u001bP", "\u001bP1", "\u001bP1 ", "\u001bPq", "\u001bP:", "\u001b]2;x", "\u001b_G", "\u001b^", "\u001bX"];
        foreach (string prefix in prefixes)
        {
            byte[] start = Encoding.Latin1.GetBytes(prefix);
            for (int value = 0; value < 256; value++) Check([.. start, (byte)value], records);
        }
        Random random = new(672619);
        for (int i = 0; i < 1000; i++)
        {
            byte[] suffix = new byte[random.Next(0, 40)]; random.NextBytes(suffix);
            byte[] start = Encoding.Latin1.GetBytes(prefixes[random.Next(prefixes.Length)]);
            Check([.. start, .. suffix], records);
        }
        foreach (byte[] utf8 in new byte[][] { [0xE0, 0xA0], [0xE0, 0x9F], [0xED, 0x9F], [0xED, 0xA0],
            [0xF0, 0x90, 0x80], [0xF0, 0x8F], [0xF4, 0x8F, 0xBF], [0xF4, 0x90], [0xC3, 0xA9], [0xE0, 0xA0, 0xF0] })
            Check(utf8, records);
    }

    private static void Check(byte[] bytes, List<SnapshotTestRecord> records)
    {
        records[5] = new(GhosttySnapshotRecordTag.Continuation, bytes);
        bool native;
        try { using GhosttyTerminal terminal = GhosttySnapshot.Decode(SnapshotTestRecords.Encode(records)); native = true; }
        catch (InvalidOperationException) { native = false; }
        bool managed = GhosttySnapshotContinuation.IsValid(bytes);
        Assert.True(native == managed, $"Continuation {Convert.ToHexString(bytes)}: native={native}, managed={managed}");
    }
}
