using Xunit;
namespace RoyalTerminal.Multiplexing.Tests;

public sealed class InitialPlacementTests
{
    [Theory]
    [InlineData(MultiplexerPaneKind.Floating)]
    [InlineData(MultiplexerPaneKind.Unplaced)]
    public async Task FirstObservableRevisionAlreadyHasTheRequestedPlacement(MultiplexerPaneKind kind)
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory=new FakeFactory{Configure=t=>{t.OnStart=()=>entered.SetResult();t.StartWait=release.Task;}};
        await using var broker=new MultiplexerBroker(factory);var s=broker.CreateSession("initial");
        var start=broker.CreatePlacedBlockAsync(s.Id,null,kind,null,"tool",MultiplexerBrokerTests.Options(),s.Revision).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));var during=broker.GetSession(s.Id);var b=Assert.Single(during.Blocks);
            Assert.Equal(s.Revision+1,during.Revision);Assert.Equal(kind,b.Placement);Assert.Null(during.Windows[0].Layout);
            if(kind==MultiplexerPaneKind.Floating){Assert.Equal(b.Id,Assert.Single(during.Windows[0].FloatingOrder!));Assert.Equal(MultiplexerFloatBounds.Default,b.FloatingBounds);}
            else{Assert.Empty(during.Windows[0].FloatingOrder!);Assert.Null(b.FloatingBounds);}
        }
        finally{release.TrySetResult();}
        var live=await start;Assert.Equal(MultiplexerBlockState.Running,live.State);Assert.Equal(s.Revision+1,broker.GetSession(s.Id).Revision);
    }
    [Fact] public async Task ClosingAFormerLayerCannotKillTheRetiledProcess()
    {
        var f=new FakeFactory();await using var broker=new MultiplexerBroker(f);var s=broker.CreateSession("race");var w=s.Windows[0].Id;
        var b=await broker.CreatePlacedBlockAsync(s.Id,w,MultiplexerPaneKind.Floating,null,"tool",MultiplexerBrokerTests.Options());var before=broker.GetSession(s.Id);
        var moved=broker.PlaceBlock(s.Id,b.Id,w,MultiplexerPaneKind.Tiled,expectedRevision:before.Revision);
        foreach(long revision in new[]{before.Revision,moved.Revision})Assert.Equal(MultiplexerErrorCode.Conflict,(await Assert.ThrowsAsync<MultiplexerException>(()=>broker.CloseFloatingLayerAsync(s.Id,b.Id,revision).AsTask())).Code);
        Assert.True(f.Last.IsRunning);Assert.Equal(0,f.Last.StopCount);Assert.Equal(b.Id,Assert.Single(broker.GetSession(s.Id).Blocks).Id);
        var again=broker.PlaceBlock(s.Id,b.Id,w,MultiplexerPaneKind.Floating);await broker.CloseFloatingLayerAsync(s.Id,b.Id,again.Revision);
        Assert.Empty(broker.GetSession(s.Id).Blocks);Assert.Equal(1,f.Last.StopCount);
    }
    [Fact] public async Task InvalidBoundsAreRejectedBeforeTransportAllocationAndStartFailureUnlinksTheFloat()
    {
        var f=new FakeFactory{Configure=t=>t.OnStart=()=>throw new IOException("start failed")};await using var broker=new MultiplexerBroker(f);var s=broker.CreateSession("failure");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>broker.CreatePlacedBlockAsync(s.Id,null,MultiplexerPaneKind.Floating,new(5000,0,6000,1000),"bad",MultiplexerBrokerTests.Options()).AsTask());
        Assert.Empty(f.Transports);Assert.Equal(s.Revision,broker.GetSession(s.Id).Revision);
        await Assert.ThrowsAsync<IOException>(()=>broker.CreatePlacedBlockAsync(s.Id,null,MultiplexerPaneKind.Floating,null,"failed",MultiplexerBrokerTests.Options()).AsTask());
        var final=broker.GetSession(s.Id);Assert.Empty(final.Blocks);Assert.Empty(final.Windows[0].FloatingOrder!);Assert.Null(final.Windows[0].Layout);Assert.Equal(1,f.Last.DisposeCount);
    }
}
