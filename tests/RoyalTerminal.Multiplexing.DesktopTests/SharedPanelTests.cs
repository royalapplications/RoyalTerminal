using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using Xunit;
namespace RoyalTerminal.Multiplexing.DesktopTests;
public sealed class SharedPanelTests
{
    [AvaloniaFact] public void AnAuthoritativeZoomRetainsAllChildrenAndRestoresUpdatedUnzoomedAreas()
    {
        var surface=new OverviewPanel();var a=new Border();var b=new Border();var left=new Rect(0,0,.4,1);var right=new Rect(.4,0,.6,1);
        surface.Place(a,left,0);surface.Place(b,right,0);surface.Place(b,right,LayoutProjection.SharedZoomLayer);surface.Place(b,right,LayoutProjection.SharedZoomLayer);
        Assert.Same(b,surface.ZoomedPane);Assert.False(a.IsVisible);Assert.True(b.IsVisible);Assert.False(surface.ToggleZoom());Assert.False(surface.SelectPane(a));
        surface.Place(a,new(0,0,.3,1),0);surface.Place(b,new(.3,0,.7,1),0);surface.Measure(new(100,50));surface.Arrange(new(0,0,100,50));Assert.Null(surface.ZoomedPane);Assert.True(a.IsVisible);Assert.Equal(2,surface.Children.Count);Assert.Equal(70,b.Bounds.Width);Assert.Equal(30,b.Bounds.X);
    }
}
