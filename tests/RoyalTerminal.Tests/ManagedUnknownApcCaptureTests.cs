// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedUnknownApcCaptureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(4096)]
    [InlineData(4097)]
    public void QuotaIsExactAndOwnedOutputSurvivesReuse(int limit)
    {
        ManagedUnknownApcCapture capture = new();
        byte[] input = new byte[limit + 1];
        Array.Fill(input, (byte)'x');
        capture.Begin(limit);
        capture.Append(input);
        Assert.Equal(limit, capture.Capacity);
        byte[] result = capture.Finish(out bool truncated);
        Assert.True(truncated);
        Assert.Equal(input.AsSpan(0, limit).ToArray(), result);
        Assert.Equal(limit <= 4096 ? limit : 0, capture.Capacity);
        Assert.Equal(0, capture.MaximumBytes);
        capture.Begin(limit);
        Array.Fill(input, (byte)'y');
        capture.Append(input.AsSpan(0, limit));
        byte[] second = capture.Finish(out truncated);
        Assert.False(truncated);
        Assert.Equal(input.AsSpan(0, limit).ToArray(), second);
        foreach (byte value in result) Assert.Equal((byte)'x', value);
    }

    [Fact]
    public void FailedGrowthDropsOnlyThatAppendAndLaterBytesCanUseOldCapacity()
    {
        ManagedUnknownApcCapture capture = new();
        capture.Begin(16);
        capture.Append("abc"u8);
        capture.Append("d"u8); // Capacity grows to six, leaving two spare bytes.
        capture.AllocationCheckpoint = stage =>
        {
            if (stage == ManagedUnknownApcAllocation.Growth) throw new OutOfMemoryException();
        };
        capture.Append("efgh"u8);
        Assert.Equal(6, capture.Capacity);
        capture.Append("ij"u8);
        Assert.Equal("abcdij"u8.ToArray(), capture.Finish(out bool truncated));
        Assert.True(truncated);
        capture.Begin(6);
        capture.Append("OK"u8);
        Assert.Equal("OK"u8.ToArray(), capture.Finish(out truncated));
        Assert.False(truncated);
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(8, true)]
    [InlineData(8192, false)]
    [InlineData(8192, true)]
    public void FailedGrowthOrFinalCopyReturnsTruncatedAndResets(int maximum, bool finalCopy)
    {
        ManagedUnknownApcCapture capture = new();
        capture.Begin(maximum);
        capture.AllocationCheckpoint = stage =>
        {
            if (stage == (finalCopy ? ManagedUnknownApcAllocation.OwnedContent : ManagedUnknownApcAllocation.Growth))
                throw new OutOfMemoryException();
        };
        capture.Append(new byte[maximum / 2]);
        // Force doubling with spare capacity so even large captures need a
        // final owned copy instead of taking the exact-size transfer path.
        if (finalCopy) capture.Append("x"u8);
        Assert.Empty(capture.Finish(out bool truncated));
        Assert.True(truncated);
        Assert.Equal(0, capture.MaximumBytes);
        Assert.True(capture.Capacity <= ManagedUnknownApcCapture.RetainedCapacityLimit);
        Assert.NotNull(capture.AllocationCheckpoint);
        capture.AllocationCheckpoint = null;
        capture.Begin(4);
        capture.Append("ok"u8);
        Assert.Equal("ok"u8.ToArray(), capture.Finish(out truncated));
        Assert.False(truncated);
    }

    [Fact]
    public void LargeExactCaptureTransfersWithoutOwnedCopyAndIsNotRetained()
    {
        ManagedUnknownApcCapture capture = new();
        capture.Begin(8192);
        capture.Append(new byte[8192]);
        capture.AllocationCheckpoint = _ => throw new InvalidOperationException("No further allocation expected.");
        byte[] result = capture.Finish(out bool truncated);
        Assert.Equal(8192, result.Length);
        Assert.False(truncated);
        Assert.Equal(0, capture.Capacity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4095)]
    [InlineData(4096)]
    public void ResetTrimsScratchToTheCurrentPolicy(int limit)
    {
        ManagedUnknownApcCapture capture = new();
        capture.Begin(4096);
        capture.Append(new byte[4096]);
        capture.Reset(limit);
        Assert.Equal(limit == 4096 ? 4096 : 0, capture.Capacity);
        capture.Begin(limit);
        Assert.Empty(capture.Finish(out bool truncated));
        Assert.False(truncated);
    }

    [Fact]
    public void NegativeLimitsRejectWithoutChangingActiveCapture()
    {
        ManagedUnknownApcCapture capture = new();
        capture.Begin(8);
        capture.Append("ok"u8);
        Assert.Throws<ArgumentOutOfRangeException>(() => capture.Begin(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => capture.Reset(-1));
        Assert.Equal(8, capture.MaximumBytes);
        Assert.Equal("ok"u8.ToArray(), capture.Finish(out bool truncated));
        Assert.False(truncated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonAllocationFailuresAreNotSwallowed(bool finish)
    {
        ManagedUnknownApcCapture capture = new();
        capture.Begin(8);
        if (finish) capture.Append("ok"u8);
        capture.AllocationCheckpoint = _ => throw new InvalidOperationException("injected");
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (finish) capture.Finish(out _);
            else capture.Append("ok"u8);
        });
        if (finish) Assert.Equal(0, capture.MaximumBytes);
    }

    // Ghostty apc.zig feeds the identifying prefix, semicolon/overflow suffix,
    // and remaining slice separately. Failure must drop precisely that append.
    [Theory]
    [InlineData("Xabc", 1, "abc")]
    [InlineData("Xabc", 2, "X")]
    [InlineData("25;abcdef", 1, ";abcdef")]
    [InlineData("25;abcdef", 2, "25abcdef")]
    [InlineData("25;abcdef", 3, "25;")]
    [InlineData("25a1?abcdefgh", 1, "?abcdefgh")]
    [InlineData("25a1?abcdefgh", 2, "25a1abcdefgh")]
    [InlineData("25a1?abcdefgh", 3, "25a1?")]
    public void StreamGrowthFailurePreservesNativeAppendBoundaries(string payload, int failAt, string expected)
    {
        using BasicVtProcessor processor = Create();
        List<TerminalUnknownSequence> reports = [];
        processor.UnknownSequenceCallback = reports.Add;
        int growths = 0;
        processor.UnknownApcAllocationCheckpoint = stage =>
        {
            if (stage == ManagedUnknownApcAllocation.Growth && ++growths == failAt) throw new OutOfMemoryException();
        };
        Write(processor, "\u001b_" + payload + "\u001b\\");
        Assert.Equal(Encoding.ASCII.GetBytes(expected), Assert.Single(reports).Content);
        Assert.True(reports[0].Truncated);
        Write(processor, "\u001b_Xok\u001b\\");
        Assert.Equal("Xok"u8.ToArray(), reports[1].Content);
        Assert.False(reports[1].Truncated);
    }

    [Fact]
    public void FinalCopyFailureReportsEmptyTruncatedAndNextCommandRecovers()
    {
        using BasicVtProcessor processor = Create();
        List<TerminalUnknownSequence> reports = [];
        processor.UnknownSequenceCallback = reports.Add;
        processor.UnknownApcAllocationCheckpoint = stage =>
        {
            if (stage == ManagedUnknownApcAllocation.OwnedContent) throw new OutOfMemoryException();
        };
        Write(processor, "\u001b_Xabc\u001b\\");
        Assert.Empty(Assert.Single(reports).Content);
        Assert.True(reports[0].Truncated);
        processor.UnknownApcAllocationCheckpoint = null;
        Write(processor, "\u001b_Xok\u001b\\");
        Assert.Equal("Xok"u8.ToArray(), reports[1].Content);
        Assert.False(reports[1].Truncated);
    }

    [Theory]
    [InlineData(0x18)]
    [InlineData(0x1A)]
    public void AbortedFailedCaptureIsSilentAndResets(int abort)
    {
        using BasicVtProcessor processor = Create();
        List<TerminalUnknownSequence> reports = [];
        processor.UnknownSequenceCallback = reports.Add;
        processor.UnknownApcAllocationCheckpoint = _ => throw new OutOfMemoryException();
        Write(processor, "\u001b_Xbad" + (char)abort);
        Assert.Empty(reports);
        processor.UnknownApcAllocationCheckpoint = null;
        Write(processor, "\u001b_Xok\u001b\\");
        Assert.Equal("Xok"u8.ToArray(), Assert.Single(reports).Content);
        Assert.False(reports[0].Truncated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    public void IdentifiersAndDisabledGlyphsDoNotAllocateCapture(int limit)
    {
        using BasicVtProcessor processor = Create();
        processor.UnknownSequenceMaxBytes = limit;
        processor.UnknownApcAllocationCheckpoint = _ => throw new InvalidOperationException("Unexpected capture allocation.");
        processor.UnknownSequenceCallback = _ => throw new InvalidOperationException("Unexpected unknown command.");
        foreach (string prefix in new[] { "", "2", "25", "25a", "25a1", "25a1;r;cp=e000;AAAAAAAAAAAAAA==" })
            Write(processor, "\u001b_" + prefix + "\u001b\\");
        if (limit == 0) Write(processor, "\u001b_Xunknown\u001b\\");
    }

    [Fact]
    public void RecognizedGlyphReceivesOnlyItsBodyAcrossInputSplits()
    {
        TerminalScreen screen = new(80, 24);
        using BasicVtProcessor processor = new(screen, new() { GlyphProtocolEnabled = true, ContinuationMaxBytes = 0 });
        processor.UnknownApcAllocationCheckpoint = _ => throw new InvalidOperationException("Unexpected unknown capture.");
        foreach (byte value in "\u001b_25a1;r;cp=e000;AAAAAAAAAAAAAA==\u001b\\"u8)
            processor.Process([value]);
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out _));
    }

    [Fact]
    public void MissingConsumerDoesNotAllocateOwnedContent()
    {
        using BasicVtProcessor processor = Create();
        processor.UnknownApcAllocationCheckpoint = stage =>
        {
            if (stage == ManagedUnknownApcAllocation.OwnedContent) throw new InvalidOperationException("No consumer.");
        };
        Write(processor, "\u001b_Xabc\u001b\\");
    }

    [Fact]
    public void SmallScratchIsReusedAndLargeQuotaDoesNotCauseEagerAllocation()
    {
        ManagedUnknownApcCapture capture = new();
        capture.Begin(int.MaxValue);
        Assert.Equal(0, capture.Capacity);
        capture.Append(new byte[4096]);
        capture.Reset();
        capture.AllocationCheckpoint = _ => throw new InvalidOperationException("Scratch already available.");
        capture.Begin(int.MaxValue);
        capture.Append(new byte[4096]);
        Assert.Equal(4096, capture.Capacity);
        capture.Reset(0);
        Assert.Equal(0, capture.Capacity);
    }

    private static BasicVtProcessor Create() => new(new TerminalScreen(80, 24),
        new() { GlyphProtocolEnabled = false, ContinuationMaxBytes = 0 });

    private static void Write(BasicVtProcessor processor, string text) => processor.Process(Encoding.ASCII.GetBytes(text));
}
