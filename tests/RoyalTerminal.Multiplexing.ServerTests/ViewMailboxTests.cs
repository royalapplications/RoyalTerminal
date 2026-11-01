using RoyalTerminal.Multiplexing.Server;
using Xunit;
namespace RoyalTerminal.Multiplexing.ServerTests;
public sealed class ViewMailboxTests
{
    private static readonly Guid Session=Guid.NewGuid();
    private static AccessGrant Owner()=>new(Guid.NewGuid(),false,Session,false,DateTimeOffset.MaxValue);
    private static readonly ViewAction[] Zoom=[new("pane.zoom")];
    [Fact] public async Task IssuedIsNotOfferedAndOfferedIsNotAcknowledged()
    {
        var g=Owner();var hub=new ViewMailbox(TimeProvider.System,_=>true);var v=hub.Open(Session,"A","browser",g);var id=v.View.Id;
        for(int i=1;i<=32;i++)Assert.Equal((long)i,hub.Publish(Session,id,Zoom).Sequence);
        Assert.Throws<ApiException>(()=>hub.Publish(Session,id,Zoom));
        await Assert.ThrowsAsync<ApiException>(()=>hub.ReadAsync(Session,id,v.ReaderKey,g,1,0,default).AsTask());
        var p=await hub.ReadAsync(Session,id,v.ReaderKey,g,0,0,default);Assert.Equal(8,p.Batches.Length);Assert.Equal(8L,p.Cursor);
        var repeat=await hub.ReadAsync(Session,id,v.ReaderKey,g,0,0,default);Assert.Equal(p.Batches.Select(b=>b.Sequence),repeat.Batches.Select(b=>b.Sequence));
        Assert.Equal(16L,(await hub.ReadAsync(Session,id,v.ReaderKey,g,8,0,default)).Cursor);Assert.Equal(33L,hub.Publish(Session,id,Zoom).Sequence);
        await Assert.ThrowsAsync<ApiException>(()=>hub.ReadAsync(Session,id,v.ReaderKey,g,7,0,default).AsTask());
    }
    [Fact] public async Task ActivePollIsPrivateAndRevocationAtDeliveryRejectsIt()
    {
        bool active=true;var g=Owner();var hub=new ViewMailbox(TimeProvider.System,_=>active);var v=hub.Open(Session,"A","browser",g);var id=v.View.Id;
        await Assert.ThrowsAsync<ApiException>(()=>hub.ReadAsync(Session,id,v.ReaderKey,Owner(),0,0,default).AsTask());
        await Assert.ThrowsAsync<ApiException>(()=>hub.ReadAsync(Session,id,new string('0',64),g,0,0,default).AsTask());
        var pending=hub.ReadAsync(Session,id,v.ReaderKey,g,0,10000,default).AsTask();
        await Assert.ThrowsAsync<ApiException>(()=>hub.ReadAsync(Session,id,v.ReaderKey,g,0,0,default).AsTask());
        active=false;Assert.Empty(hub.List(Session));await Assert.ThrowsAsync<ApiException>(()=>pending);
    }
    [Fact] public void LeaseExpiresWithoutHeartbeatsAndPublishCannotExtendIt()
    {
        var clock=new Clock();var hub=new ViewMailbox(clock,_=>true);var v=hub.Open(Session,"A","browser",Owner());
        clock.Now=59;hub.Publish(Session,v.View.Id,Zoom);clock.Now=60;Assert.Empty(hub.List(Session));Assert.Throws<ApiException>(()=>hub.Publish(Session,v.View.Id,Zoom));
    }
    private sealed class Clock:TimeProvider{internal long Now;public override long TimestampFrequency=>1;public override long GetTimestamp()=>Now;}
}
