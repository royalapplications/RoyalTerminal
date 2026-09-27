// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty apc.Handler drops a failed Parser.feed/feedSlice command, not the
// previously accepted LoadingImage. A parser value has one mutable owner;
// completed commands transfer payload ownership and can outlive that parser.
public sealed class ManagedKittyParserFailureTests
{
    [Fact]
    public void CommandAllocationFailureReturnsAnInactiveParser()
    {
        Assert.False(ManagedKittyGraphicsParser.TryCreate(1024, out ManagedKittyGraphicsParser parser,
            stage => { if (stage == ManagedKittyParserAllocation.Command) throw new OutOfMemoryException(); }));
        Assert.False(parser.TryAppend("a=q,i=73,s=1,v=1;/wAA/w=="u8));
        Assert.False(parser.TryComplete(out _));
        Assert.Equal(0, parser.PayloadCapacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedKittyGraphicsParser.TryCreate(-1, out _));
        Assert.False(ManagedKittyGraphicsCommand.TryParse("a=p"u8, -1, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PayloadAllocationFailureReleasesAllStorageAndPermanentlyRejectsTheCommand(int failAt)
    {
        int allocations = 0;
        Assert.True(ManagedKittyGraphicsParser.TryCreate(1024, out ManagedKittyGraphicsParser parser, stage =>
        {
            if (stage == ManagedKittyParserAllocation.Payload && ++allocations == failAt) throw new OutOfMemoryException();
        }));
        bool first = parser.TryAppend("i=73;AAAA"u8);
        if (failAt == 1) Assert.False(first);
        else
        {
            Assert.True(first);
            Assert.Equal(256, parser.PayloadCapacity);
            Assert.False(parser.TryAppend(Encoding.ASCII.GetBytes(new string('A', 260))));
        }
        Assert.Equal(0, parser.PayloadCapacity);
        Assert.False(parser.TryAppend("AAAA"u8));
        Assert.False(parser.TryComplete(out _));
        Assert.True(ManagedKittyGraphicsParser.TryCreate(4, out parser));
        Assert.True(parser.TryAppend("i=74;AAAA"u8));
        Assert.True(parser.TryComplete(out ManagedKittyGraphicsCommand? command));
        Assert.Equal(74u, command.ImageId);
        Assert.Equal(new byte[3], command.Data.ToArray());
        Assert.Equal(0, parser.PayloadCapacity);
    }

    [Theory]
    [InlineData((int)ManagedKittyParserAllocation.Command)]
    [InlineData((int)ManagedKittyParserAllocation.Payload)]
    public void NonAllocationExceptionsAreNotSwallowed(int checkpoint)
    {
        InvalidOperationException failure = new("Not an allocation failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
        {
            ManagedKittyGraphicsParser.TryCreate(1024, out ManagedKittyGraphicsParser parser,
                stage => { if ((int)stage == checkpoint) throw failure; });
            parser.TryAppend("i=73;AAAA"u8);
        }));
    }

    public static IEnumerable<object[]> InterruptedTransfers()
    {
        foreach (ManagedKittyParserAllocation stage in new[] { ManagedKittyParserAllocation.Command, ManagedKittyParserAllocation.Payload })
        for (int quiet = 0; quiet < 3; quiet++)
        foreach (bool newTransmission in new[] { false, true }) yield return [(int)stage, quiet, newTransmission];
    }

    [Theory]
    [MemberData(nameof(InterruptedTransfers))]
    public void RejectedContinuationOrRetransmissionPreservesLoadingImageAndQuietPolicy(int checkpoint, int quiet, bool newTransmission)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0 });
        processor.NotifyResize(8, 2, 80, 20);
        List<string> replies = [];
        List<TerminalUnknownSequence> unknown = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.UnknownSequenceCallback = unknown.Add;
        Send(processor, $"a=T,i=73,p=1,s=1,v=1,f=32,C=1,m=1,q={quiet};/wAA");
        Assert.Empty(replies);
        processor.KittyParserAllocationCheckpoint = stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); };
        Send(processor, newTransmission ? "a=T,i=99,s=1,v=1,C=1,q=2;/wAA/w==" : "m=0,q=2;/w==");
        Assert.Empty(replies);
        Assert.Empty(unknown);
        Assert.False(screen.TryGetKittyImageSource(73, out _));
        Assert.True(processor.IsParserGround);
        processor.KittyParserAllocationCheckpoint = null;
        Send(processor, "m=0;/w==");
        if (quiet == 0) Assert.Equal("\u001b_Gi=73,p=1;OK\u001b\\", Assert.Single(replies));
        else Assert.Empty(replies);
        Assert.True(screen.TryGetKittyImageSource(73, out TerminalKittyImageSource? image));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image!.RgbaPixels);
        Assert.False(screen.TryGetKittyImageSource(99, out _));
        Assert.False(screen.SnapshotMutationFailed);
    }

    [Theory]
    [InlineData(0x18)]
    [InlineData(0x1A)]
    [InlineData(0x9C)]
    public void RejectedCommandStaysSilentOnApcExitAndNextCommandWorks(int exit)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2), new() { ContinuationMaxBytes = 0 });
        List<string> replies = [];
        List<TerminalUnknownSequence> unknown = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.UnknownSequenceCallback = unknown.Add;
        processor.KittyParserAllocationCheckpoint = _ => throw new OutOfMemoryException();
        processor.Process("\u001b_Ga=q,i=73,s=1,v=1;/wAA/w=="u8);
        processor.Process([(byte)exit]);
        Assert.Empty(replies);
        Assert.Empty(unknown);
        processor.KittyParserAllocationCheckpoint = null;
        Send(processor, "a=q,i=74,s=1,v=1;/wAA/w==");
        Assert.Equal("\u001b_Gi=74;OK\u001b\\", Assert.Single(replies));
        Assert.True(processor.IsParserGround);
    }

    [Fact]
    public void DisabledKittyDoesNotConstructCommandStorage()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2),
            new() { ContinuationMaxBytes = 0, KittyGraphicsStorageLimitBytes = 0 });
        processor.KittyParserAllocationCheckpoint = _ => throw new InvalidOperationException("Disabled protocol.");
        Send(processor, "a=q,i=73,s=1,v=1;/wAA/w==");
        Assert.True(processor.IsParserGround);
    }

    [Fact]
    public void CompletedCommandsOwnTheirDataAndParserReuseCannotChangeIt()
    {
        ManagedKittyGraphicsParser parser = new(8);
        Assert.True(parser.TryAppend("i=73;AQIDBA=="u8));
        Assert.True(parser.TryComplete(out ManagedKittyGraphicsCommand? first));
        Assert.Equal(0, parser.PayloadCapacity);
        parser = new(8);
        Assert.True(parser.TryAppend("i=74;AAAAAA=="u8));
        Assert.True(parser.TryComplete(out ManagedKittyGraphicsCommand? second));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, first.Data.ToArray());
        Assert.Equal(new byte[4], second.Data.ToArray());
        Assert.Equal(73u, first.ImageId);
        Assert.Equal(74u, second.ImageId);
        Assert.False(parser.TryComplete(out _));
    }

    [Fact]
    public void ReplyCallbackOomIsNotClassifiedAsAParserFailure()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0 });
        OutOfMemoryException failure = new("Host callback");
        processor.ResponseCallback = _ => throw failure;
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => Send(processor, "a=q,i=73,s=1,v=1;/wAA/w==")));
        Assert.True(processor.IsParserGround);
        Assert.False(screen.SnapshotMutationFailed);
        List<byte[]> replies = [];
        processor.ResponseCallback = replies.Add;
        Send(processor, "a=q,i=73,s=1,v=1;/wAA/w==");
        Assert.Single(replies);
    }

    [Fact]
    public void ControlOnlyParsingAllocatesOnlyTheOwnedCommand()
    {
        ManagedKittyGraphicsCommand?[] slot = new ManagedKittyGraphicsCommand?[1];
        _ = MeasureConstruction(slot, false, 100);
        _ = MeasureConstruction(slot, true, 100);
        long commandBytes = MeasureConstruction(slot, false, 1000);
        long parserBytes = MeasureConstruction(slot, true, 1000);
        Assert.Equal(commandBytes, parserBytes);
        Assert.NotNull(slot[0]);
        Assert.Equal(73u, slot[0]!.ImageId);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureConstruction(ManagedKittyGraphicsCommand?[] slot, bool parse, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
        {
            if (!parse) slot[0] = new();
            else
            {
                ManagedKittyGraphicsParser parser = new(0);
                if (!parser.TryAppend("a=p,i=73,p=1"u8) || !parser.TryComplete(out slot[0]))
                    throw new InvalidOperationException("Control command rejected.");
            }
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Send(BasicVtProcessor processor, string command)
        => processor.Process(Encoding.ASCII.GetBytes("\u001b_G" + command + "\u001b\\"));
}
