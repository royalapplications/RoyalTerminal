// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

/// <summary>Projects canonical pane coordinates. Zoom changes presentation only, never transport or layout ownership.</summary>
public sealed class OverviewPanel:Panel
{
    private const string ActiveClass="workspace-active-pane";
    private readonly Dictionary<Control,Rect> _areas=[];
    private Control? _local,_shared;
    /// <summary>Whether to show original workspace geometry for editing.</summary>
    public static readonly StyledProperty<bool> EditingProperty=AvaloniaProperty.Register<OverviewPanel,bool>(nameof(Editing));
    /// <summary>Gets or sets layout-editing mode.</summary>
    public bool Editing {get=>GetValue(EditingProperty);set=>SetValue(EditingProperty,value);}
    internal Control? LastFocusedPane {get;private set;}
    internal Control? ZoomedPane=>Editing?null:_shared??_local;
    /// <summary>Creates an observer surface with local zoom shortcuts.</summary>
    public OverviewPanel()=>AddHandler(KeyDownEvent,OnPaneKey,RoutingStrategies.Tunnel);
    private void OnPaneKey(object? sender,KeyEventArgs e)
    {
        if(e.Handled||Editing||_shared!=null)return;
        if(e.Key==Key.Z&&e.KeyModifiers==KeyModifiers.Alt)e.Handled=ToggleZoom();
        else if(e.Key==Key.Escape&&e.KeyModifiers==KeyModifiers.None&&_local!=null){_local=null;ApplyVisibility();e.Handled=true;}
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {base.OnPropertyChanged(change);if(change.Property==EditingProperty){if(Editing)_local=null;ApplyVisibility();}}
    internal bool ToggleZoom()
    {
        if(Editing||_shared!=null)return false;if(_local!=null){_local=null;ApplyVisibility();return true;}
        var pane=LastFocusedPane??(Children.Count==1?Children[0]:null);if(pane==null||!SelectPane(pane))return false;_local=pane;ApplyVisibility();return true;
    }
    internal bool SelectPane(Control pane)
    {
        if(Editing||!_areas.ContainsKey(pane)||(_shared!=null&&!ReferenceEquals(_shared,pane)))return false;
        if(_local!=null&&!ReferenceEquals(_local,pane)){_local=null;ApplyVisibility();}
        if(!ReferenceEquals(LastFocusedPane,pane)){LastFocusedPane?.Classes.Remove(ActiveClass);LastFocusedPane=pane;pane.Classes.Add(ActiveClass);}return true;
    }
    private void ApplyVisibility()
    {var target=ZoomedPane;foreach(var c in Children)c.IsVisible=target==null||ReferenceEquals(c,target);InvalidateMeasure();}
    internal void Place(Control child,Rect area,int layer)
    {
        _areas[child]=area;child.ZIndex=layer;
        if(!Children.Contains(child))
        {
            child.AddHandler(GotFocusEvent,RememberFocus,RoutingStrategies.Bubble,handledEventsToo:true);child.AddHandler(PointerPressedEvent,RememberPointer,RoutingStrategies.Bubble,handledEventsToo:true);Children.Add(child);
        }
        if(layer==LayoutProjection.SharedZoomLayer){if(!ReferenceEquals(child,_shared)){_shared=child;_local=null;SelectPane(child);ApplyVisibility();}}
        else if(ReferenceEquals(child,_shared)){_shared=null;ApplyVisibility();}
        child.IsVisible=ZoomedPane==null||ReferenceEquals(child,ZoomedPane);InvalidateMeasure();
    }
    private void RememberFocus(object? sender,FocusChangedEventArgs e){if(sender is Control p)SelectPane(p);}
    private void RememberPointer(object? sender,PointerPressedEventArgs e){if(sender is Control p)SelectPane(p);}
    internal void RemovePane(Control child)
    {
        child.RemoveHandler(GotFocusEvent,RememberFocus);child.RemoveHandler(PointerPressedEvent,RememberPointer);
        if(ReferenceEquals(child,LastFocusedPane)){child.Classes.Remove(ActiveClass);LastFocusedPane=null;}
        if(ReferenceEquals(child,_local))_local=null;if(ReferenceEquals(child,_shared))_shared=null;_areas.Remove(child);Children.Remove(child);ApplyVisibility();
    }
    private Rect Area(Control child,Size size){if(ReferenceEquals(child,ZoomedPane))return new Rect(size);var r=_areas[child];return new(r.X*size.Width,r.Y*size.Height,r.Width*size.Width,r.Height*size.Height);}
    protected override Size MeasureOverride(Size availableSize){var size=new Size(double.IsFinite(availableSize.Width)?availableSize.Width:0,double.IsFinite(availableSize.Height)?availableSize.Height:0);foreach(var c in Children)if(c.IsVisible)c.Measure(Area(c,size).Size);return size;}
    protected override Size ArrangeOverride(Size finalSize){foreach(var c in Children)if(c.IsVisible)c.Arrange(Area(c,finalSize));return finalSize;}
}
