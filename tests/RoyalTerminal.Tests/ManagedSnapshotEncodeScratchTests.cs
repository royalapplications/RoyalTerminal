// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedSnapshotEncodeScratchTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(8193)]
    public void MixedRecordSizesPreserveBothScreensLinksGraphemesAndContinuation(int metadataBytes)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(16, 4));
        processor.SetTitle(Encoding.ASCII.GetBytes(new string('t', metadataBytes)));
        for (int i = 0; i < 80; i++) processor.Process("\u001b[31m\u001b]8;id=one;https://example.test\a界é\r\n"u8);
        processor.Process("\u001b[?1049h\u001b[44mALT\u001b]2;unfinished"u8);
        byte[] encoded = processor.GetBinarySnapshot();
        using MemoryStream stream = new();
        processor.WriteBinarySnapshotTo(stream);
        Assert.Equal(encoded, stream.ToArray());
        Assert.True(stream.CanWrite);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(encoded);
        Assert.Equal(encoded, restored.Processor.GetBinarySnapshot());
        Assert.Equal(processor.GetContinuation(), restored.Processor.GetContinuation());
        if (!GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native Ghostty unavailable.");
        using GhosttyTerminal native = GhosttySnapshot.Decode(encoded, retainContinuation: true);
        using ManagedTerminalSnapshot bounced = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(native));
        Assert.Equal(encoded, bounced.Processor.GetBinarySnapshot());
    }

    [Theory]
    [InlineData("record")]
    [InlineData("total")]
    public void ExactLimitsSucceedAndOneByteLessNeverEmitsAnOversizedRecord(string kind)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(80, 24));
        processor.SetTitle(Encoding.ASCII.GetBytes(new string('t', 4096)));
        processor.Process("\u001b[31m\u001b[2J"u8);
        byte[] expected = processor.GetBinarySnapshot();
        int maximum = 0;
        long total = 0;
        using (GhosttySnapshotRecordReader reader = new(expected, int.MaxValue))
        {
            reader.ReadEnvelope();
            GhosttySnapshotRecordTag tag;
            do
            {
                tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
                maximum = Math.Max(maximum, payload.Length);
                total += payload.Length;
            } while (tag != GhosttySnapshotRecordTag.Finish);
        }
        GhosttySnapshotDecodeLimits limits = new(MaximumPayloadBytes: maximum, MaximumTotalPayloadBytes: total);
        Assert.Equal(expected, processor.GetBinarySnapshot(limits));
        GhosttySnapshotDecodeLimits tooSmall = kind == "record"
            ? limits with { MaximumPayloadBytes = maximum - 1 }
            : limits with { MaximumTotalPayloadBytes = total - 1 };
        using MemoryStream destination = new();
        Assert.Throws<InvalidDataException>(() => processor.WriteBinarySnapshotTo(destination, tooSmall));
        Assert.True(destination.CanWrite);
        Assert.True(destination.Length < expected.Length);
        Assert.Equal(expected.AsSpan(0, (int)destination.Length).ToArray(), destination.ToArray());
        Assert.Equal(expected, processor.GetBinarySnapshot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    public void DestinationFailuresAtDifferentRecordsLeaveStateAndCallerOwnershipIntact(int successfulWrites)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(16, 4));
        processor.SetTitle(Encoding.ASCII.GetBytes(new string('t', 8193)));
        processor.Process("\u001b[?1049hALT"u8);
        byte[] expected = processor.GetBinarySnapshot();
        using FailingStream destination = new(successfulWrites);
        Assert.Throws<IOException>(() => processor.WriteBinarySnapshotTo(destination));
        Assert.True(destination.CanWrite);
        Assert.Equal(expected.AsSpan(0, (int)destination.Length).ToArray(), destination.ToArray());
        Assert.Equal(expected, processor.GetBinarySnapshot());
    }

    private sealed class FailingStream(int successfulWrites) : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (successfulWrites-- == 0) throw new IOException("injected");
            base.Write(buffer);
        }
    }
}
