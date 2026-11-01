using Xunit;
namespace RoyalTerminal.Multiplexing.Tests;
public sealed class LayerOrderTests
{
    [Fact] public async Task DepthIsIndependentOfIdentityInputAndGeometry()
    {
        await using var broker=new MultiplexerBroker(new FakeFactory());var s=broker.CreateSession("workspace");var w=s.Windows[0].Id;
        var a=await broker.AddBlockAsync(s.Id,"a",MultiplexerBrokerTests.Options());var b=await broker.AddBlockAsync(s.Id,"b",MultiplexerBrokerTests.Options());var c=await broker.AddBlockAsync(s.Id,"c",MultiplexerBrokerTests.Options());
        foreach(var id in new[]{c.Id,b.Id,a.Id})broker.PlaceBlock(s.Id,id,w,MultiplexerPaneKind.Floating);
        using var controller=broker.Attach(s.Id,b.Id,MultiplexerRole.Controller);
        var v=broker.StackLayer(s.Id,c.Id,true);Assert.Equal(new[]{b.Id,c.Id,a.Id},v.Windows[0].FloatingOrder);Assert.Equal(new[]{a.Id,b.Id,c.Id},v.Blocks.Select(x=>x.Id));
        Assert.Equal(controller.Id,v.Blocks[1].ControllerId);var unchanged=broker.StackLayer(s.Id,a.Id,true,true,v.Revision);Assert.Equal(v.Revision,unchanged.Revision);
        Assert.Throws<MultiplexerException>(()=>broker.StackLayer(s.Id,b.Id,true,true,v.Revision-1));Assert.Throws<ArgumentException>(()=>broker.SetLayerOrder(s.Id,w,[a.Id,a.Id,c.Id]));
        broker.PlaceBlock(s.Id,c.Id,w,MultiplexerPaneKind.Floating,bounds:new(100,200,5000,4000));var swap=broker.SwapBlocks(s.Id,b.Id,a.Id);Assert.Equal(new[]{a.Id,c.Id,b.Id},swap.Windows[0].FloatingOrder);
        var other=broker.CreateWindow(s.Id,"other");var moved=broker.PlaceBlock(s.Id,c.Id,other.Id,MultiplexerPaneKind.Floating);Assert.Equal(new[]{a.Id,b.Id},moved.Windows[0].FloatingOrder);Assert.Equal(new[]{c.Id},moved.Windows[1].FloatingOrder);
        await broker.CloseBlockAsync(s.Id,a.Id);Assert.Equal(new[]{b.Id},broker.GetSession(s.Id).Windows[0].FloatingOrder);
    }
}
