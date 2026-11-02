using Xunit;
namespace RoyalTerminal.Multiplexing.Tests;

public sealed class FloatingFrameTests
{
    [Fact] public async Task NestedFrameMovesAsOneAndClosingOneLeafOnlyCollapsesItsDivider()
    {
        var factory=new FakeFactory();await using var broker=new MultiplexerBroker(factory);var s=broker.CreateSession("setup");Guid w=s.Windows[0].Id;
        async Task<Guid> New(MultiplexerPaneKind p)=>(await broker.CreatePlacedBlockAsync(s.Id,w,p,null,"tool",MultiplexerBrokerTests.Options())).Id;
        var tile=await New(MultiplexerPaneKind.Tiled);var back=await New(MultiplexerPaneKind.Floating);var a=await New(MultiplexerPaneKind.Unplaced);var b=await New(MultiplexerPaneKind.Unplaced);var c=await New(MultiplexerPaneKind.Unplaced);
        var tree=MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Horizontal,MultiplexerLayoutNode.Leaf(a),MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Vertical,MultiplexerLayoutNode.Leaf(b),MultiplexerLayoutNode.Leaf(c),.25),.4);
        var g=broker.GroupFloating(s.Id,w,tree,new(1000,2000,6000,4000));var view=broker.GetSession(s.Id);
        Assert.Equal(tile,view.Windows[0].Layout!.BlockId);Assert.Equal(new[]{back,a,b,c},view.Windows[0].FloatingOrder);Assert.Equal(new MultiplexerFloatBounds(3400,3000,3600,3000),view.Blocks.Single(x=>x.Id==c).FloatingBounds);
        Assert.Throws<ArgumentException>(()=>broker.SetLayerOrder(s.Id,w,new[]{a,back,b,c}));Assert.Equal(view.Revision,broker.GetSession(s.Id).Revision);
        Assert.Equal(new[]{a,b,c,back},broker.StackLayer(s.Id,c,false).Windows[0].FloatingOrder);
        using var observer=broker.Attach(s.Id,c);var shrunk=broker.EditFloating(s.Id,g.Id,bounds:new(0,0,5000,5000));Assert.Equal(1,shrunk.Blocks.Single(x=>x.Id==c).AttachmentCount);
        await broker.CloseBlockAsync(s.Id,a);view=broker.GetSession(s.Id);var group=Assert.Single(view.Windows[0].FloatingLayers!);Assert.Equal(g.Id,group.Id);Assert.Equal(MultiplexerSplitOrientation.Vertical,group.Layout.Orientation);
        Assert.Equal(new MultiplexerFloatBounds(0,1250,5000,3750),view.Blocks.Single(x=>x.Id==c).FloatingBounds);
        broker.PlaceBlock(s.Id,b,w,MultiplexerPaneKind.Tiled);await Assert.ThrowsAsync<MultiplexerException>(()=>broker.CloseFrameAsync(s.Id,g.Id,view.Revision).AsTask());
        await broker.CloseFrameAsync(s.Id,g.Id,broker.GetSession(s.Id).Revision);view=broker.GetSession(s.Id);Assert.Contains(view.Blocks,x=>x.Id==b);Assert.DoesNotContain(view.Blocks,x=>x.Id==c);Assert.Equal(new[]{back},view.Windows[0].FloatingOrder);
    }
    [Fact] public async Task ExplicitZoomIsScopedIdempotentAndDoesNotAllocateOrAcquireAnAttachment()
    {
        var f=new FakeFactory();await using var broker=new MultiplexerBroker(f);var s=broker.CreateSession("s");var w=s.Windows[0].Id;var a=await broker.AddBlockAsync(s.Id,"a",MultiplexerBrokerTests.Options());var b=await broker.AddBlockAsync(s.Id,"b",MultiplexerBrokerTests.Options());var before=broker.GetSession(s.Id);
        Assert.Equal(a.Id,broker.ZoomBlock(s.Id,a.Id,true).ZoomedBlockId);broker.ZoomBlock(s.Id,a.Id,true);Assert.Equal(before.Revision+1,broker.GetSession(s.Id).Revision);
        broker.ZoomBlock(s.Id,b.Id,true);Assert.Equal(b.Id,broker.ZoomBlock(s.Id,a.Id,false).ZoomedBlockId);broker.ZoomBlock(s.Id,b.Id,false);broker.ZoomBlock(s.Id,b.Id,false);
        var after=broker.GetSession(s.Id);Assert.Null(after.Windows[0].ZoomedBlockId);Assert.Equal(before.Revision+3,after.Revision);Assert.Equal(before.Windows[0].Layout,after.Windows[0].Layout);
        Assert.All(after.Blocks,x=>{Assert.Null(x.ControllerId);Assert.Equal(0,x.AttachmentCount);});Assert.Equal(2,f.Transports.Count);
        broker.ZoomBlock(s.Id,a.Id);var hidden=broker.PlaceBlock(s.Id,a.Id,w,MultiplexerPaneKind.Unplaced);Assert.Null(hidden.Windows[0].ZoomedBlockId);Assert.Throws<MultiplexerException>(()=>broker.ZoomBlock(s.Id,a.Id,true));
    }
    [Fact] public void GeometryRejectsDuplicateOrUnrepresentableChildren()
    {
        var a=MultiplexerLayoutNode.Leaf(Guid.NewGuid());var b=MultiplexerLayoutNode.Leaf(Guid.NewGuid());var axis=MultiplexerSplitOrientation.Horizontal;
        Assert.Throws<ArgumentException>(()=>FloatTreeGeometry.Project(MultiplexerLayoutNode.Split(axis,a,a),new(0,0,20,20)));
        Assert.Throws<ArgumentException>(()=>FloatTreeGeometry.Project(MultiplexerLayoutNode.Split(axis,a,b),new(0,0,1,20)));
    }
}
