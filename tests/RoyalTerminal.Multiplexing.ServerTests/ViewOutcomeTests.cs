using RoyalTerminal.Multiplexing.Server;
using Xunit;
namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class ViewOutcomeTests
{
    private readonly Guid _session=Guid.NewGuid();
    private readonly ViewMailbox _hub=new(TimeProvider.System,_=>true);
    private AccessGrant Owner()=>new(Guid.NewGuid(),false,_session,false,DateTimeOffset.MaxValue);
    [Fact]
    public async Task ReportsAreOrderedAuthenticatedAndIdempotentAndLegacyReadsDoNotClaimSuccess()
    {
        var owner=Owner();var v=_hub.Open(_session,"Phone","mobile",owner,3);var id=v.View.Id;
        var zoom=new ViewAction("pane.zoom");_hub.Publish(_session,id,[zoom,zoom]);_hub.Publish(_session,id,[zoom]);
        ViewOutcome Finish(long s,int n)=>_hub.Finish(_session,id,v.ReaderKey,owner,s,n);
        Task<ViewOutcome> Result(long s,int ms=0)=>_hub.ResultAsync(_session,id,s,ms,default).AsTask();
        Assert.Equal("queued",(await Result(1)).State);Assert.Throws<ApiException>(()=>Finish(1,2));
        Assert.Equal(2,(await _hub.ReadAsync(_session,id,v.ReaderKey,owner,0,0,default)).Batches.Length);
        Assert.Equal("offered",(await Result(1)).State);var waiting=Result(1,5000);
        Assert.Throws<ApiException>(()=>Finish(2,1));Assert.Throws<ArgumentException>(()=>Finish(1,3));
        Assert.Throws<ApiException>(()=>_hub.Finish(_session,id,v.ReaderKey,Owner(),1,1));
        Assert.Throws<ApiException>(()=>_hub.Finish(_session,id,new string('0',64),owner,1,1));
        var result=Finish(1,1);Assert.Equal("declined",result.State);Assert.Equal(1,result.Applied);Assert.Equal(result,await waiting);
        Assert.Equal(result,Finish(1,1));Assert.Throws<ApiException>(()=>Finish(1,2));
        await _hub.ReadAsync(_session,id,v.ReaderKey,owner,2,0,default);var legacy=await Result(2);
        Assert.Equal("unreported",legacy.State);Assert.Null(legacy.Applied);
        for(int i=3;i<=67;i++){_hub.Publish(_session,id,[zoom]);await _hub.ReadAsync(_session,id,v.ReaderKey,owner,i-1,0,default);Assert.Equal("applied",Finish(i,1).State);}
        Assert.Equal(410,(await Assert.ThrowsAsync<ApiException>(()=>Result(1))).Status);Assert.Equal(404,(await Assert.ThrowsAsync<ApiException>(()=>Result(68))).Status);
    }
    [Fact]
    public async Task WaitersAreBoundedCancellationIsPerReaderAndWithdrawalWakesAll()
    {
        var owner=Owner();var v=_hub.Open(_session,"Browser","browser",owner);var id=v.View.Id;_hub.Publish(_session,id,[new("pane.zoom")]);
        using var cancelled=new CancellationTokenSource();var first=_hub.ResultAsync(_session,id,1,20000,cancelled.Token).AsTask();
        var other=Enumerable.Range(0,15).Select(_=>_hub.ResultAsync(_session,id,1,20000,default).AsTask()).ToArray();
        Assert.Equal(429,(await Assert.ThrowsAsync<ApiException>(()=>_hub.ResultAsync(_session,id,1,20000,default).AsTask())).Status);
        cancelled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);
        var last=_hub.ResultAsync(_session,id,1,20000,default).AsTask();_hub.Close(_session,id,v.ReaderKey,owner);
        foreach(var pending in other.Append(last))Assert.Equal(404,(await Assert.ThrowsAsync<ApiException>(()=>pending)).Status);
    }
}
