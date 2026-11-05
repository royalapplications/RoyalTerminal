using System.Text.Json;
using Avalonia;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using Xunit;

namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class LayoutEditingTests
{
    [Theory]
    [InlineData(.1,.5,"Left")][InlineData(.9,.5,"Right")][InlineData(.5,.1,"Above")][InlineData(.5,.9,"Below")][InlineData(.5,.5,"Swap")]
    public void DropTargetsAreRelativeToDestinationNotWindow(double x,double y,string tool)=>Assert.Equal(tool,LayoutEdits.DropZone(new(.25,.1,.5,.8),new(.25+.5*x,.1+.8*y)).ToString());
    [Fact]
    public void FloatingGeometryNeverEscapesWindowAndUsesLogicalUnits()
    {
        var r=new MultiplexerFloatBounds(2000,1000,6000,5000);
        Assert.Equal(new(4000,0,6000,5000),LayoutEdits.DragFloat(r,new(800,-400),new(800,400),false));
        Assert.Equal(new(2000,1000,8000,300),LayoutEdits.DragFloat(r,new(800,-400),new(800,400),true));
    }
    [Fact]
    public void TilingPreservesRevisionAndResizePreservesIdentities()
    {
        Guid sid=Guid.NewGuid(),wid=Guid.NewGuid(),dest=Guid.NewGuid(),a=Guid.NewGuid(),b=Guid.NewGuid(),c=Guid.NewGuid();
        MultiplexerBlockDescriptor Block(Guid id, Guid w)=>new(id,sid,w,"dup",MultiplexerBlockState.Running,new(80,24,0,0),null,1,1,0,null);
        var root=MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Vertical,MultiplexerLayoutNode.Leaf(a),MultiplexerLayoutNode.Leaf(b),.4);
        var s=new MultiplexerSessionDescriptor(sid,"session",17,DateTimeOffset.UnixEpoch,[new(wid,"A",root),new(dest,"B",MultiplexerLayoutNode.Leaf(c))],[Block(a,wid),Block(b,wid),Block(c,dest)]);
        var move=LayoutEdits.Plan(s,wid,LayoutTool.Above,b,c); Assert.Equal("block.place",move.Method); Assert.True(move.Arguments.GetProperty("before").GetBoolean()); Assert.Equal(dest,move.Arguments.GetProperty("windowId").GetGuid()); Assert.Equal(17,move.Arguments.GetProperty("expectedRevision").GetInt64());
        var resize=LayoutEdits.Plan(s,wid,LayoutTool.Grow,b); var tree=resize.Arguments.GetProperty("layout").Deserialize(OverviewJson.Default.MultiplexerLayoutNode)!; Assert.Equal(.35,tree.Ratio,10); Assert.Equal(a,tree.First!.BlockId); Assert.Equal(b,tree.Second!.BlockId); Assert.Equal(.4,root.Ratio);
        Assert.Throws<ArgumentException>(()=>LayoutEdits.Plan(s,wid,LayoutTool.CloseWindow,destination:wid));
        Assert.Equal(dest,LayoutEdits.Plan(s,wid,LayoutTool.CloseWindow,destination:dest).Arguments.GetProperty("preserveIn").GetGuid());
        var spec=LayoutEdits.Plan(s,wid,LayoutTool.NewShellBelow,b).Arguments.GetProperty("block"); Assert.Equal(17,spec.GetProperty("expectedRevision").GetInt64()); Assert.Equal(b,spec.GetProperty("splitBlockId").GetGuid());
    }
}
