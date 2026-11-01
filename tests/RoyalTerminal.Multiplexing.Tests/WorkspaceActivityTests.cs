// Copyright (c) Royal Apps. Licensed under the MIT license.
using Xunit;

namespace RoyalTerminal.Multiplexing.Tests;

public sealed class WorkspaceActivityTests
{
    [Fact]
    public async Task ShortLivedObjectsAreNotLostAndFailedWritesLeaveNoRecord()
    {
        await using var broker = new MultiplexerBroker(new FakeFactory());
        var a = broker.CreateSession("first"); var b = broker.CreateSession("other");
        long fence = (await broker.Activity.ReadAsync(a.Id)).NextSequence;
        var w = broker.CreateWindow(a.Id,"temporary");
        broker.RenameSession(b.Id,"never leak this");
        Assert.Throws<MultiplexerException>(() => broker.RenameWindow(a.Id,w.Id,"stale",a.Revision));
        broker.RenameWindow(a.Id,w.Id,"work");
        var block=await broker.AddBlockAsync(a.Id,w.Id,"shell",MultiplexerBrokerTests.Options());
        await broker.CloseBlockAsync(a.Id,block.Id); broker.CloseWindow(a.Id,w.Id); await broker.CloseSessionAsync(a.Id);
        var seen = new List<WorkspaceActivity>();
        while(true)
        {
            var p=await broker.Activity.ReadAsync(a.Id,after:fence,limit:1);
            var e=Assert.Single(p.Events); Assert.Equal(++fence,e.Sequence); Assert.Equal(a.Id,e.SessionId);
            Assert.Equal(fence,p.NextSequence); seen.Add(e); if(p.Ended) break;
        }
        Assert.Equal(new[]{"window_created","session_view_changed","window_label_changed","session_view_changed","block_created","session_view_changed","block_closed","session_view_changed","window_closed","session_view_changed","session_destroyed"}, seen.Select(e=>e.Type));
        Assert.Equal(w.Id,seen[0].WindowId); Assert.Equal("work",seen[2].Label); Assert.Equal(block.Id,seen[4].BlockId);
        Assert.Contains((await broker.Activity.ReadAsync(after:0)).Events,e=>e.Type=="session_destroyed"&&e.SessionId==a.Id);
        Assert.DoesNotContain((await broker.Activity.ReadAsync(after:0)).Events,e=>e.Type=="window_label_changed");
    }

    [Fact]
    public async Task SlowReadersGetExplicitGapAndCancellationIsLocal()
    {
        await using var broker=new MultiplexerBroker(new FakeFactory()); var s=broker.CreateSession("old");
        long fence=(await broker.Activity.ReadAsync(s.Id)).NextSequence;
        using var cancel=new CancellationTokenSource();
        var abandoned=broker.Activity.ReadAsync(s.Id,after:fence,timeoutMs:25000,cancellationToken:cancel.Token).AsTask();
        var active=broker.Activity.ReadAsync(s.Id,after:fence,timeoutMs:25000).AsTask();
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>abandoned);
        broker.RenameSession(s.Id,"new"); Assert.Equal("session_label_changed",(await active.WaitAsync(TimeSpan.FromSeconds(3))).Events[0].Type);
        for(int i=0;i<600;i++)broker.RenameSession(s.Id,"n"+i);
        var gap=await Assert.ThrowsAsync<MultiplexerException>(()=>broker.Activity.ReadAsync(s.Id,after:0).AsTask());
        Assert.Equal(MultiplexerErrorCode.HistoryGap,gap.Code);
        var now=await broker.Activity.ReadAsync(s.Id); Assert.Empty(now.Events);
        var page=await broker.Activity.ReadAsync(s.Id,after:now.FirstSequence-1,limit:2); Assert.Equal(now.FirstSequence+1,page.NextSequence);
    }
}
