// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;
using Xunit;
namespace RoyalTerminal.Multiplexing.InspectionTests;

public sealed class SessionTerminalJournalTests
{
    [Fact]
    public async Task EmptySessionWakesForNewBlockAndDoesNotEndWhenItsChildrenExit()
    {
        var journal = new SessionTerminalJournal();
        var a = new Wire(); var b = new Wire(); var aid = Guid.NewGuid(); var bid = Guid.NewGuid();
        using var first = new InspectingTerminalTransport(a, new(80,24,0,0));
        using var second = new InspectingTerminalTransport(b, new(80,24,0,0));
        var waiting = journal.ReadAsync(0, timeoutMs:5000).AsTask();
        a.Emit("\a"); a.Exit(37); journal.Include(aid, first.Metadata);
        var initial = await waiting.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, initial.Events.Count); Assert.False(initial.Ended);
        journal.Include(bid, second.Metadata); b.Emit("\a\a"); b.Exit(0);
        var all = await journal.ReadAsync(0);
        Assert.Equal(new long[]{1,2,3,4,5}, all.Events.Select(e=>e.Sequence));
        Assert.Equal(new long?[]{1,2,1,2,3}, all.Events.Select(e=>e.BlockSequence));
        Assert.Equal(new Guid?[]{aid,aid,bid,bid,bid}, all.Events.Select(e=>e.BlockId));
        Assert.False(all.Ended);
        var close = journal.ReadAsync(all.NextSequence, timeoutMs:5000).AsTask();
        journal.Complete(); Assert.True((await close.WaitAsync(TimeSpan.FromSeconds(3))).Ended);
    }
    [Fact]
    public async Task OverflowAtLaunchExplicitlyInvalidatesAnOlderSessionCursor()
    {
        var journal = new SessionTerminalJournal(); var wire = new Wire();
        using var source = new InspectingTerminalTransport(wire,new(80,24,0,0));
        wire.Emit(new string('\a',700)); journal.Include(Guid.NewGuid(),source.Metadata);
        var ex = await Assert.ThrowsAsync<MultiplexerException>(()=>journal.ReadAsync(0).AsTask());
        Assert.Equal(MultiplexerErrorCode.HistoryGap, ex.Code);
        var now = await journal.ReadAsync(); Assert.Equal(700, now.LastSequence);
        var retained = await journal.ReadAsync(now.FirstSequence-1, limit:2);
        Assert.Equal(now.FirstSequence+1, retained.NextSequence);
    }
    [Fact]
    public async Task ConcurrentStartupAndPublicationKeepEverySourceCursorOnce()
    {
        var journal = new SessionTerminalJournal(); var wire = new Wire();
        using var source = new InspectingTerminalTransport(wire,new(80,24,0,0));
        Task producer = Task.Run(()=>{for(int i=0;i<400;i++)wire.Emit("\a");});
        journal.Include(Guid.NewGuid(),source.Metadata); await producer;
        var received = new List<long?>(); long cursor=0;
        do {var page=await journal.ReadAsync(cursor, 31); received.AddRange(page.Events.Select(e=>e.BlockSequence)); cursor=page.NextSequence;} while(cursor<400);
        Assert.Equal(Enumerable.Range(1,400).Select(i=>(long?)i),received);
    }
    private sealed class Wire : ITerminalTransport
    {
        public event Action<byte[],int>? DataReceived;
        public event Action<int>? ProcessExited;
        public bool IsRunning=>true;
        internal void Emit(string s){var bytes=Encoding.UTF8.GetBytes(s);DataReceived?.Invoke(bytes,bytes.Length);}
        internal void Exit(int n)=>ProcessExited?.Invoke(n);
        public ValueTask StartAsync(ITerminalTransportOptions o,CancellationToken t=default)=>ValueTask.CompletedTask;
        public ValueTask StopAsync()=>ValueTask.CompletedTask;
        public void SendInput(ReadOnlySpan<byte> data)=>throw new InvalidOperationException();
        public void Resize(TerminalSessionDimensions dimensions) { }
        public void Dispose() { }
    }
}
