// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia;
using Avalonia.Input;
namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

public sealed partial class OverviewWindow
{
    private sealed record Gesture(IPointer Pointer, MultiplexerSessionDescriptor Snapshot, Guid Window, PaneSlot Slot, Point Start, Size Viewport, Size WindowSize, bool Resize);
    private Gesture? _gesture;

    private void BeginDrag(object? sender, PointerPressedEventArgs e)
    {
        if (!EditBar.IsVisible || _editPending || _gesture is not null || _snapshot is not { } s || !e.GetCurrentPoint(GestureLayer).Properties.IsLeftButtonPressed) return;
        var size = GestureLayer.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        Point p=e.GetPosition(GestureLayer), unit=new(p.X/size.Width,p.Y/size.Height);
        foreach (var slot in LayoutProjection.Build(s,_windowId).Slots.OrderByDescending(x=>x.Layer))
        {
            if (!slot.Area.Contains(unit)) continue;
            if (EditSource.ItemsSource is IEnumerable<Choice> choices) EditSource.SelectedItem=choices.FirstOrDefault(c=>c.Id==slot.Block.Id);
            GestureLayer.Focus(); e.Pointer.Capture(GestureLayer);
            _gesture=new(e.Pointer,s,_windowId,slot,p,size,ClientSize,e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            EditHint.Text="Dragging " + slot.Block.Label; e.Handled=true; return;
        }
    }
    private void MoveDrag(object? sender, PointerEventArgs e)
    {
        if (_gesture is not { } g || g.Pointer!=e.Pointer) return;
        Point p=e.GetPosition(GestureLayer); Vector delta=p-g.Start;
        if (delta.Length>=4) EditHint.Text=g.Slot.Block.Placement==MultiplexerPaneKind.Floating ? (g.Resize?"Release to resize float.":"Release to move float.") : Drop(g,p) is { } d ? $"Release: {d.Tool} · {d.Target.Label}" : "Drop on another tile or press Escape.";
        e.Handled=true;
    }
    private async void EndDrag(object? sender, PointerReleasedEventArgs e)
    {
        if (_gesture is not { } g || g.Pointer!=e.Pointer) return;
        Point p=e.GetPosition(GestureLayer); Vector delta=p-g.Start; CancelDrag();e.Handled=true;
        if (delta.Length<4 || ClientSize!=g.WindowSize || GestureLayer.Bounds.Size!=g.Viewport) { EditHint.Text=Instructions;return; }
        if (g.Slot.Block.FloatingBounds is { } r) await SubmitEditAsync(g.Snapshot,g.Window,LayoutTool.Float,g.Slot.Block.Id,bounds:LayoutEdits.DragFloat(r,delta,g.Viewport,g.Resize));
        else if (Drop(g,p) is { } d) await SubmitEditAsync(g.Snapshot,g.Window,d.Tool,g.Slot.Block.Id,d.Target.Id);
        else EditHint.Text=Instructions;
    }
    private static (LayoutTool Tool, MultiplexerBlockDescriptor Target)? Drop(Gesture g,Point p)
    {
        var unit=new Point(p.X/g.Viewport.Width,p.Y/g.Viewport.Height);
        foreach (var slot in LayoutProjection.Build(g.Snapshot,g.Window).Slots.OrderByDescending(x=>x.Layer))
            if (slot.Area.Contains(unit)) return slot.Block.Id==g.Slot.Block.Id || slot.Block.Placement!=MultiplexerPaneKind.Tiled ? null : (LayoutEdits.DropZone(slot.Area,unit),slot.Block);
        return null;
    }
    private void GestureCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    { if (_gesture?.Pointer==e.Pointer && !ReferenceEquals(e.Pointer.Captured,GestureLayer)) _gesture=null; }
    private void CancelDrag() { var previous=_gesture;_gesture=null;previous?.Pointer.Capture(null); }
}
