using System.Text.Json;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using Xunit;
namespace RoyalTerminal.Multiplexing.DesktopTests;
public sealed class FloatingEditTests
{
    [Fact] public void ADesktopSplitOrDividerEditTargetsTheFrameNotTheWindowBase()
    {
        var s=Guid.NewGuid();var w=Guid.NewGuid();var a=Guid.NewGuid();var b=Guid.NewGuid();var g=Guid.NewGuid();
        var r=new MultiplexerFloatBounds(1000,1000,8000,8000);var tree=MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Horizontal,MultiplexerLayoutNode.Leaf(a),MultiplexerLayoutNode.Leaf(b),.45);
        MultiplexerBlockDescriptor Block(Guid id)=>new(id,s,w,"shell",MultiplexerBlockState.Running,new(80,24,0,0),null,1,1,0,null){Placement=MultiplexerPaneKind.Floating,FloatingLayerId=g,FloatingBounds=FloatTreeGeometry.Project(tree,r)[id]};
        var session=new MultiplexerSessionDescriptor(s,"s",15,DateTimeOffset.UnixEpoch,[new(w,"Main",null){FloatingLayers=[new(g,r,tree)],FloatingOrder=[a,b]}],[Block(a),Block(b)]);
        var grow=LayoutEdits.Plan(session,w,LayoutTool.Grow,a);Assert.Equal("session.set_layer_layout",grow.Method);Assert.Equal(g,grow.Arguments.GetProperty("layerId").GetGuid());Assert.False(grow.Arguments.TryGetProperty("windowId",out _));Assert.Equal(.50,grow.Arguments.GetProperty("layout").GetProperty("ratio").GetDouble(),10);
        var split=LayoutEdits.Plan(session,w,LayoutTool.NewShellBelow,b);var spec=split.Arguments.GetProperty("block");Assert.Equal(b,spec.GetProperty("splitBlockId").GetGuid());Assert.Equal("vertical",spec.GetProperty("orientation").GetString());Assert.Equal(15,spec.GetProperty("expectedRevision").GetInt32());
    }
}
