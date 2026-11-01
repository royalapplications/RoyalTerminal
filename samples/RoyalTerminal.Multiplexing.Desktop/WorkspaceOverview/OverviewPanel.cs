// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

/// <summary>Projects normalized server slots onto local pixels without resizing the underlying PTYs.</summary>
public sealed class OverviewPanel : Panel
{
    private readonly Dictionary<Control, Rect> _areas = [];
    internal Control? LastFocusedPane { get; private set; }

    internal void Place(Control child, Rect area, int layer)
    {
        _areas[child] = area;
        child.ZIndex = layer;
        if (!Children.Contains(child))
        {
            child.AddHandler(GotFocusEvent, RememberFocus, RoutingStrategies.Bubble, handledEventsToo: true);
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
        child.RemoveHandler(GotFocusEvent, RememberFocus);
        if (ReferenceEquals(child, LastFocusedPane)) LastFocusedPane = null;
        _areas.Remove(child); Children.Remove(child);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
        foreach (var child in Children) child.Measure(Scale(_areas[child], size).Size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) child.Arrange(Scale(_areas[child], finalSize));
        return finalSize;
    }

    private static Rect Scale(Rect r, Size s) => new(r.X * s.Width, r.Y * s.Height, r.Width * s.Width, r.Height * s.Height);
}
