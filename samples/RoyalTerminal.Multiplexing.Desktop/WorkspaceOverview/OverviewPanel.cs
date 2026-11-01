// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

/// <summary>Projects normalized server slots onto local pixels. Zoom changes the projection, not the slots, attachments or PTY geometry.</summary>
public sealed class OverviewPanel : Panel
{
    private readonly Dictionary<Control, Rect> _areas = [];
    /// <summary>Whether the shared-layout editing overlay is active. Editing uses the unzoomed coordinates exclusively.</summary>
    public static readonly StyledProperty<bool> EditingProperty = AvaloniaProperty.Register<OverviewPanel, bool>(nameof(Editing));
    /// <summary>Gets or sets the local layout-editing mode.</summary>
    public bool Editing { get => GetValue(EditingProperty); set => SetValue(EditingProperty, value); }
    internal Control? LastFocusedPane { get; private set; }
    internal Control? ZoomedPane { get; private set; }

    /// <summary>Creates a presentation surface with local keyboard zoom (Alt+Z or Escape to restore).</summary>
    public OverviewPanel() => AddHandler(KeyDownEvent, OnPaneKey, RoutingStrategies.Tunnel);
    private void OnPaneKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Editing) return;
        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Alt) e.Handled = ToggleZoom();
        else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && ZoomedPane is not null) { SetZoom(null); e.Handled = true; }
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditingProperty && Editing && ZoomedPane is not null) SetZoom(null);
    }
    internal bool ToggleZoom()
    {
        if (Editing) return false;
        if (ZoomedPane is not null) { SetZoom(null); return true; }
        var target=LastFocusedPane ?? (Children.Count == 1 ? Children[0] : null);
        if (target is null || !_areas.ContainsKey(target)) return false;
        SetZoom(target); return true;
    }
    internal void Reveal(Control target)
    {
        if (ZoomedPane is not null && !ReferenceEquals(ZoomedPane,target)) SetZoom(null);
    }
    private void SetZoom(Control? target)
    {
        ZoomedPane = target;
        foreach (var child in Children) child.IsVisible = target is null || ReferenceEquals(child,target);
        InvalidateMeasure();
    }
    internal void Place(Control child, Rect area, int layer)
    {
        _areas[child] = area; child.ZIndex = layer;
        if (!Children.Contains(child))
        {
            child.IsVisible = ZoomedPane is null || ReferenceEquals(child,ZoomedPane);
            child.AddHandler(GotFocusEvent, RememberFocus, RoutingStrategies.Bubble, handledEventsToo:true);
            Children.Add(child);
        }
        InvalidateMeasure();
    }
    private void RememberFocus(object? sender, FocusChangedEventArgs args)
    {
        if (sender is Control pane && _areas.ContainsKey(pane)) LastFocusedPane = pane;
    }
    internal void RemovePane(Control child)
    {
        child.RemoveHandler(GotFocusEvent,RememberFocus);
        if (ReferenceEquals(child,LastFocusedPane)) LastFocusedPane = null;
        _areas.Remove(child); Children.Remove(child);
        if (ReferenceEquals(child,ZoomedPane)) SetZoom(null);
    }
    private Rect Area(Control child, Size size)
    {
        if (ReferenceEquals(child,ZoomedPane)) return new Rect(size);
        var r=_areas[child]; return new(r.X*size.Width,r.Y*size.Height,r.Width*size.Width,r.Height*size.Height);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0, double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
        foreach (var child in Children) if(child.IsVisible) child.Measure(Area(child,size).Size);
        return size;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) if(child.IsVisible) child.Arrange(Area(child,finalSize));
        return finalSize;
    }
}
