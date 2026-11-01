using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using Xunit;
namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class PaneNavigationTests
{
    private static PaneSlot Slot(int key, double x, double y, double w, double h) => new(new(new Guid(key,0,0,new byte[8]),Guid.Empty,Guid.Empty,"shell",MultiplexerBlockState.Running,new(80,24,0,0),null,1,1,0,null),new Rect(x,y,w,h),0);
    [Fact]
    public void SpatialTraversalPrefersProjectedOverlapAndStableIdentityBeforeEnumerationOrder()
    {
        var a=Slot(1,0,0,.2,.2); var diagonal=Slot(2,.15,.6,.2,.2); var far=Slot(3,.7,0,.1,.2);
        var aligned=Slot(4,.4,.08,.15,.2); var tied=Slot(5,.4,.08,.15,.2);
        PaneSlot[] slots=[a,diagonal,far,tied,aligned];
        Assert.Equal(aligned.Block.Id,PaneNavigation.Resolve(slots,a.Block.Id,"right"));
        Assert.Equal(aligned.Block.Id,PaneNavigation.Resolve(slots.Reverse().ToArray(),a.Block.Id,"right"));
        Assert.Null(PaneNavigation.Resolve(slots,a.Block.Id,"left"));
        Assert.Null(PaneNavigation.Resolve(slots,null,"right"));
    }
    [Fact]
    public void CyclicTraversalWrapsAndDoesNotSubstituteForAStaleAnchor()
    {
        PaneSlot[] slots=[Slot(1,0,0,.5,1),Slot(2,.5,0,.5,1)];
        Assert.Equal(slots[0].Block.Id,PaneNavigation.Resolve(slots,slots[1].Block.Id,"next"));
        Assert.Equal(slots[1].Block.Id,PaneNavigation.Resolve(slots,null,"previous"));
        Assert.Equal(slots[1].Block.Id,PaneNavigation.Resolve(slots,slots[0].Block.Id,"previous"));
        Assert.Null(PaneNavigation.Resolve(slots,Guid.NewGuid(),"previous"));
        Assert.Null(PaneNavigation.Resolve([],null,"next"));
    }
    [AvaloniaFact]
    public void RememberPaneWhenToolbarTakesFocusAndForgetItOnRemoval()
    {
        var panel=new OverviewPanel();var input=new TextBox();var root=new Border{Child=input};var other=new Button{Content="Toolbar"};
        var grid=new DockPanel();DockPanel.SetDock(other,Dock.Top);grid.Children.Add(other);grid.Children.Add(panel);
        panel.Place(root,new(0,0,1,1),0);var window=new Window{Content=grid};window.Show();
        try{input.Focus(); Assert.Same(root,panel.LastFocusedPane);other.Focus();Assert.Same(root,panel.LastFocusedPane);
            panel.Place(root,new(.1,0,.8,1),1);Assert.Same(root,panel.LastFocusedPane);panel.RemovePane(root);Assert.Null(panel.LastFocusedPane);}
        finally{window.Close();}
    }
}
