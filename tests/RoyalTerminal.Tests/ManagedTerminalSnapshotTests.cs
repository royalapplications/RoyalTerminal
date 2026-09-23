// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedTerminalSnapshotTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("")]
    [InlineData("\u001b")]
    [InlineData("\u001b[38:2::12:34")]
    [InlineData("\u001b]2;unfinished")]
    [InlineData("\u001bP$q")]
    [InlineData("\u001b_Ga=q,i=99;")]
    [InlineData("\u001b(")]
    public void PublicRestoreReplaysContinuationAndInstallsRuntimeState(string fragment)
    {
        if (!Available()) return;
        using GhosttyTerminal source = new(16, 4);
        source.SetContinuationMaxBytes(4096);
        source.Write("primary\u001b[31m\u001b7\u001b[?47halt\u001b7\u001b[?47l\u001b[>4;2m\u001b[>1s\u001b]22;pointer\a\u001b]2;title\a\u001b]7;file:///cwd\a"u8);
        source.PasswordInput = true;
        source.Write(Encoding.UTF8.GetBytes(fragment));
        byte[] bytes = GhosttySnapshot.Encode(source);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(bytes,
            new() { ProcessorOptions = new() { ContinuationMaxBytes = 1 } });
        using GhosttyTerminal native = GhosttySnapshot.Decode(bytes, retainContinuation: true);
        Assert.Equal(source.GetContinuation(), restored.Processor.GetContinuation());
        Assert.True(restored.Processor.PasswordInput);
        Assert.True(restored.Processor.ModifyOtherKeys2);
        Assert.True(restored.Processor.MouseShiftCaptureOverride);
        Assert.Equal(TerminalMouseShape.Pointer, restored.Processor.MouseShape);
        Assert.Equal("title"u8.ToArray(), restored.Processor.SnapshotTitle.ToArray());
        Assert.Equal("file:///cwd"u8.ToArray(), restored.Processor.SnapshotWorkingDirectory.ToArray());
        // Abort pending input then exercise the already restored saved cursor and both buffers.
        foreach (byte[] command in new[] { "\u0018\u001b8X"u8.ToArray(), "\u001b[?47h\u001b8Y"u8.ToArray(), "\u001b[?47lZ"u8.ToArray() })
        {
            native.Write(command); restored.Processor.Process(command);
            Assert.Equal((native.GetCursorX(), native.GetCursorY()), ((ushort)restored.Processor.CursorCol, (ushort)restored.Processor.CursorRow));
            Assert.Equal(native.GetContinuation(), restored.Processor.GetContinuation());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Utf8ContinuationCanBeRestoredWithoutOngoingRetention(int retained)
    {
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        records[5] = new(GhosttySnapshotRecordTag.Continuation, [0xF0, 0x9F, 0x98]);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(SnapshotTestRecords.Encode(records),
            new() { ProcessorOptions = new() { ContinuationMaxBytes = retained } });
        Assert.False(restored.Processor.IsParserGround);
        if (retained == 0) Assert.Throws<InvalidOperationException>(() => restored.Processor.GetContinuation());
        else Assert.Equal(new byte[] { 0xF0, 0x9F, 0x98 }, restored.Processor.GetContinuation());
        restored.Processor.Process([0x80]);
        Assert.True(restored.Processor.IsParserGround);
    }

    [Fact]
    public void StreamOwnershipOffsetAndDecoderLifetimeAreIndependentOfTerminal()
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        using MemoryStream source = new([.. bytes, 42]);
        using ManagedTerminalSnapshotDecoder decoder = new(source);
        Assert.Throws<InvalidOperationException>(() => decoder.Next());
        using ManagedTerminalSnapshot restored = decoder.Ready();
        Assert.Throws<InvalidOperationException>(() => decoder.Ready());
        Assert.True(decoder.SourceOffset < bytes.Length);
        while (decoder.Next() is not null) { }
        Assert.Null(decoder.Next());
        Assert.Equal(bytes.Length, decoder.SourceOffset);
        Assert.Equal(42, source.ReadByte());
        decoder.Dispose(); decoder.Dispose();
        Assert.True(source.CanRead);
        Assert.Throws<ObjectDisposedException>(() => decoder.Next());
        restored.Processor.Process("mOK"u8);
        Assert.True(restored.Processor.IsParserGround);
        byte[] trailing = [.. bytes, 42];
        Assert.Throws<InvalidDataException>(() => ManagedTerminalSnapshot.Restore(trailing));
        source.Position = 0;
        using ManagedTerminalSnapshot complete = ManagedTerminalSnapshot.Restore(source);
        Assert.Equal(bytes.Length, source.Position);
    }

    [Fact]
    public void HistoryRespectsLiveQuotaAndKeepsAGapAfterQuotaIncreases()
    {
        using ManagedTerminalSnapshotDecoder decoder = new(GhosttySnapshotFramingTests.Fixture("complete-v1.hex"));
        using ManagedTerminalSnapshot restored = decoder.Ready();
        restored.Screen.ScrollbackLimit = 0;
        Assert.Equal(0, decoder.Next()!.Value.RowsApplied);
        restored.Screen.ScrollbackLimit = 10000;
        Assert.Equal(0, decoder.Next()!.Value.RowsApplied);
        Assert.Null(decoder.Next());
    }

    [Fact]
    public void HistoryDuringSynchronizedOutputIsPublishedWithTheLiveScreen()
    {
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        records[0].Payload[21] = 0; // History below belongs to primary, which must be the visible screen.
        records[0].Payload[22] = 0;
        records[5] = new(GhosttySnapshotRecordTag.Continuation, []);
        using ManagedTerminalSnapshotDecoder decoder = new(SnapshotTestRecords.Encode(records));
        using ManagedTerminalSnapshot restored = decoder.Ready();
        int before = restored.Screen.TotalRows;
        restored.Processor.Process("\u001b[?2026h"u8);
        ManagedTerminalSnapshotProgress progress = decoder.Next()!.Value;
        Assert.True(progress.RowsApplied > 0);
        Assert.Equal(before, restored.Screen.TotalRows);
        restored.Processor.Process("\u001b[?2026l"u8);
        Assert.Equal(before + progress.RowsApplied, restored.Screen.TotalRows);
    }

    [Fact]
    public void CorruptHistoryPoisonsDecoderButLeavesReadyTerminalUsable()
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        using ManagedTerminalSnapshotDecoder decoder = new(bytes);
        using ManagedTerminalSnapshot restored = decoder.Ready();
        // Borrowed source must normally be immutable. Corrupt the final trailer here
        // specifically to exercise a failure after a previously valid READY.
        bytes[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => { while (decoder.Next() is not null) { } });
        Assert.Throws<InvalidOperationException>(() => decoder.Next());
        restored.Processor.Process("mOK"u8);
        Assert.True(restored.Processor.IsParserGround);
    }

    [Fact]
    public void BoundsAndDisposalRejectWithoutPublishingPartialState()
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedTerminalSnapshotDecoder(bytes, new() { ScrollbackLimit = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedTerminalSnapshotDecoder(bytes, new() { DecodeLimits = new(MaximumPages: -1) }));
        using ManagedTerminalSnapshotDecoder bounded = new(bytes, new() { DecodeLimits = new(MaximumCells: 1) });
        Assert.Throws<InvalidDataException>(() => bounded.Ready());
        Assert.Throws<InvalidOperationException>(() => bounded.Ready());
        using ManagedTerminalSnapshotDecoder valid = new(bytes);
        ManagedTerminalSnapshot restored = valid.Ready();
        restored.Dispose(); restored.Dispose();
        Assert.Throws<ObjectDisposedException>(() => valid.Next());
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native public snapshot differential available: {available}");
        return available;
    }
}
