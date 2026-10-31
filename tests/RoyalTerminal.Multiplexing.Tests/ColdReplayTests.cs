// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.Tests;

public sealed class ColdReplayTests
{
    private static MultiplexerOptions Options(int budget) => new()
    {
        MaxJournalBytes = 4096, MaxJournalEvents = 3, MaxEventBytes = 2048,
        MaxPendingBytesPerAttachment = 8192, MaxPendingEventsPerAttachment = 8, MaxColdReplayBytes = budget,
    };

    [Fact]
    public async Task ByteGeometryAndCursorAreIdenticalAcrossColdHotAndLiveBoundaries()
    {
        FakeFactory factory = new(); await using MultiplexerBroker broker = new(factory, Options(64 * 1024));
        Guid s = broker.CreateSession("archive").Id; var b = await broker.AddBlockAsync(s, "work", MultiplexerBrokerTests.Options());
        using var control = broker.Attach(s,b.Id,MultiplexerRole.Controller);
        var golden = new List<MultiplexerEvent>(); Drain(control, golden);
        // More output than both the compressed storage and the live queue. Include fragments that
        // only a parser with the correct preceding state can interpret, and arbitrary non-text bytes.
        for(int i=0;i<80;i++)
        {
            byte[] chunk = new byte[2048]; chunk.AsSpan().Fill((byte)(i%7));
            chunk[0] = 0x1b; chunk[1] = (byte)'['; chunk[^1] = (byte)i;
            factory.Last.Emit(chunk); Drain(control,golden);
            if(i==29 || i==54) { control.Resize(new(80+i,24+i,1000+i,500)); Drain(control,golden); }
        }
        Assert.Equal(1,broker.GetSession(s).Blocks[0].FirstRetainedSequence);
        using var fresh = broker.Attach(s,b.Id); long fence = fresh.ReplayThroughSequence;
        factory.Last.Emit(new byte[]{0xf0,0x9f}); factory.Last.Emit(new byte[]{0x98,0x80}); Drain(control,golden);
        List<MultiplexerEvent> actual=[]; Drain(fresh,actual); Assert.Equal(golden.Count,actual.Count);
        for(int i=0;i<actual.Count;i++) Check(golden[i],actual[i]);
        Assert.Equal(golden.Count-2L,fence); Assert.True(actual[0].Kind==MultiplexerEventKind.Resize);
        // Seek inside a compressed page then mutate the returned data. No other reader may observe it.
        using var middle = broker.Attach(s,b.Id,lastSequence:17);
        var first = await middle.Events.ReadAsync(); Assert.Equal(18,first.Sequence); first.Data![10]^=0xff;
        using var same = broker.Attach(s,b.Id,lastSequence:17); Check(golden[17],await same.Events.ReadAsync());
    }

    [Fact]
    public async Task UnknownPrefixNeverMasqueradesAsAFreshScreen()
    {
        FakeFactory factory=new(); await using MultiplexerBroker broker=new(factory,Options(2048));
        Guid s=broker.CreateSession("bounded").Id;var b=await broker.AddBlockAsync(s,"shell",MultiplexerBrokerTests.Options());
        Random rng=new(431);for(int i=0;i<64;i++){byte[] noise=new byte[1024];rng.NextBytes(noise);factory.Last.Emit(noise);}
        var current=broker.GetSession(s).Blocks[0];Assert.True(current.FirstRetainedSequence>1);
        Assert.Equal(MultiplexerErrorCode.HistoryGap,Assert.Throws<MultiplexerException>(()=>broker.Attach(s,b.Id)).Code);
        using var resume=broker.Attach(s,b.Id,lastSequence:current.FirstRetainedSequence-1);
        List<MultiplexerEvent> rest=[];Drain(resume,rest);
        Assert.Equal(current.FirstRetainedSequence,rest[0].Sequence);Assert.Equal(current.LastSequence,rest[^1].Sequence);
        for(int i=1;i<rest.Count;i++) Assert.Equal(rest[i-1].Sequence+1,rest[i].Sequence);
    }

    [Fact]
    public async Task ExitedReplayPinsAreBoundedAndDisposeReleasesThemWithoutInflation()
    {
        FakeFactory factory=new();await using MultiplexerBroker broker=new(factory,Options(65536) with {MaxAttachmentsPerBlock=1});
        Guid s=broker.CreateSession("exit").Id;var b=await broker.AddBlockAsync(s,"short",MultiplexerBrokerTests.Options());
        for(int i=0;i<100;i++)factory.Last.Emit("hello");factory.Last.Exit(37);
        var unread=broker.Attach(s,b.Id);Assert.False(unread.HasControl);
        Assert.Equal(MultiplexerErrorCode.CapacityExceeded,Assert.Throws<MultiplexerException>(()=>broker.Attach(s,b.Id)).Code);
        unread.Dispose();await unread.Events.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        using var late=broker.Attach(s,b.Id);List<MultiplexerEvent> all=[];Drain(late,all);
        Assert.Equal(102,all.Count);Assert.Equal(37,all[^1].ExitCode);await late.Events.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        using var later=broker.Attach(s,b.Id); Assert.Equal(1,(await later.Events.ReadAsync()).Sequence);
    }

    private static void Check(MultiplexerEvent expected,MultiplexerEvent actual)
    {
        Assert.Equal(expected.Sequence,actual.Sequence);Assert.Equal(expected.Kind,actual.Kind);Assert.Equal(expected.Dimensions,actual.Dimensions);
        Assert.Equal(expected.Message,actual.Message);Assert.Equal(expected.ExitCode,actual.ExitCode);Assert.Equal(expected.Data,actual.Data);
    }
    private static void Drain(MultiplexerAttachment a,List<MultiplexerEvent> list)
    {while(a.Events.TryRead(out var item))list.Add(item);}
}
