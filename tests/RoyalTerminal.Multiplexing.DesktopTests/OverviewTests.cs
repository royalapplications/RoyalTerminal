using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using Xunit;

namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class OverviewTests
{
    private static readonly Guid SessionId = Guid.NewGuid(), WindowId = Guid.NewGuid();
    private static MultiplexerBlockDescriptor Block(MultiplexerPaneKind kind = MultiplexerPaneKind.Tiled) => new(Guid.NewGuid(), SessionId, WindowId,
        "test", MultiplexerBlockState.Running, new(80, 24, 0, 0), null, 1, 1, 0, null) { Placement = kind };
    private static MultiplexerSessionDescriptor Snapshot(MultiplexerLayoutNode? root, params MultiplexerBlockDescriptor[] blocks) =>
        new(SessionId, "work", 1, DateTimeOffset.UnixEpoch, [new(WindowId, "Main", root)], blocks);

    [Fact]
    public void LayoutPreservesNestedRatiosFloatingLayerAndShelf()
    {
        var a = Block(); var b = Block(); var c = Block(); var hidden = Block(MultiplexerPaneKind.Unplaced);
        var overlay = Block(MultiplexerPaneKind.Floating) with { FloatingBounds = new(2500, 1250, 5000, 7500) };
        var tree = MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Horizontal, MultiplexerLayoutNode.Leaf(a.Id),
            MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Vertical, MultiplexerLayoutNode.Leaf(b.Id), MultiplexerLayoutNode.Leaf(c.Id), .25), .5);
        var plan = LayoutProjection.Build(Snapshot(tree, a, b, c, overlay, hidden), WindowId);
        Assert.Equal(new Rect(0,0,.5,1),plan.Slots[0].Area); Assert.Equal(new Rect(.5,0,.5,.25),plan.Slots[1].Area);
        Assert.Equal(new Rect(.5,.25,.5,.75),plan.Slots[2].Area); Assert.Equal(new Rect(.25,.125,.5,.75),plan.Slots[3].Area);
        Assert.True(plan.Slots[3].Layer > plan.Slots[0].Layer); Assert.Equal(hidden.Id,Assert.Single(plan.Shelf).Id);
    }

    [Fact]
    public void DuplicateMissingAndForeignLeavesAreNotRendered()
    {
        var b=Block(); var leaf=MultiplexerLayoutNode.Leaf(b.Id);
        Assert.Throws<InvalidDataException>(() => LayoutProjection.Build(Snapshot(null,b),WindowId));
        Assert.Throws<InvalidDataException>(() => LayoutProjection.Build(Snapshot(leaf,b with {WindowId=Guid.NewGuid()}),WindowId));
        Assert.Throws<InvalidDataException>(() => LayoutProjection.Build(Snapshot(MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Horizontal,leaf,leaf),b),WindowId));
    }

    [AvaloniaFact]
    public void BoundsChangeReusesNativeControlsAndScalesToWindow()
    {
        var panel=new OverviewPanel(); var child=new Border(); panel.Place(child,new(0,0,.5,1),0);
        panel.Measure(new(800,400));panel.Arrange(new(0,0,800,400)); Assert.Equal(new Rect(0,0,400,400), child.Bounds);
        panel.Place(child,new(.25,.25,.5,.5),3);panel.Measure(new(600,400));panel.Arrange(new(0,0,600,400));
        Assert.Same(child,Assert.Single(panel.Children));Assert.Equal(new Rect(150,100,300,200), child.Bounds);Assert.Equal(3,child.ZIndex);
        panel.RemovePane(child);Assert.Empty(panel.Children);
    }
}
