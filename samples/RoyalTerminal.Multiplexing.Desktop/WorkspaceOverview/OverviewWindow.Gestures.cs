// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia;
using Avalonia.Input;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

public sealed partial class OverviewWindow
{
    private sealed record Gesture(IPointer Pointer, MultiplexerSessionDescriptor Snapshot,Guid Window, PaneSlot Slot,Point Start,Size Size,bool Resize);
    private Gesture? _gesture;
    private void BeginDrag(object? sender, PointerPressedEventArgs e)
    {
        if(!EditBar.IsVisible || _editPending || _gesture is not null || _snapshot is not { } s || !e.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed) return;
        var size=Surface.Bounds.Size; if(size.Width<=0 || size.Height<=0) return;
        var p=e.GetPosition(Surface); var unit=new Point(p.X/size.Width,p.Y/size.Height);
        foreach(var slot in LayoutProjection.Build(s,_windowId).Slots.OrderByDescending(x=>x.Layer))
        {
            if(!slot.Area.Contains(unit)) continue;
            if(EditSource.ItemsSource is IEnumerable<Choice> choices) EditSource.SelectedItem=choices.FirstOrDefault(c=>c.Id==slot.Block.Id);
            _gesture=new(e.Pointer,s,_windowId,slot,p,size,e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Pointer.Capture(Surface); e.Handled=true; break;
        }
    }
    private void MoveDrag(object? sender,PointerEventArgs e)
    {
        if(_gesture is not { } g || g.Pointer!=e.Pointer) return;
        var p=e.GetPosition(Surface); Vector delta=p-g.Start;
        if(delta.Length>=4) EditHint.Text=g.Slot.Block.Placement==MultiplexerPaneKind.Floating ? (g.Resize?"Release to resize float.":"Release to move float.") : Drop(g,p) is { } d ? $"Release: {d.Tool} · {d.Target.Label}" : "Drop on another tile or press Escape.";
        e.Handled=true;
    }
    private async void EndDrag(object? sender,PointerReleasedEventArgs e)
    {
        if(_gesture is not { } g || g.Pointer!=e.Pointer) return;
        var p=e.GetPosition(Surface); Vector delta=p-g.Start; CancelDrag(); e.Handled=true;
        if(delta.Length<4 || Surface.Bounds.Size!=g.Size) { EditHint.Text=Instructions; return; }
        if(g.Slot.Block.FloatingBounds is { } r) await SubmitEditAsync(g.Snapshot,g.Window,LayoutTool.Float,g.Slot.Block.Id,bounds:LayoutEdits.DragFloat(r,delta,g.Size,g.Resize));
        else if(Drop(g,p) is { } d) await SubmitEditAsync(g.Snapshot,g.Window,d.Tool,g.Slot.Block.Id,d.Target.Id);
        else EditHint.Text=Instructions;
    }
    private static (LayoutTool Tool,MultiplexerBlockDescriptor Target)? Drop(Gesture g,Point p)
    {
        var unit=new Point(p.X/g.Size.Width,p.Y/g.Size.Height);
        foreach(var slot in LayoutProjection.Build(g.Snapshot,g.Window).Slots.OrderByDescending(x=>x.Layer))
            if(slot.Area.Contains(unit)) return slot.Block.Id==g.Slot.Block.Id || slot.Block.Placement!=MultiplexerPaneKind.Tiled ? null : (LayoutEdits.DropZone(slot.Area,unit),slot.Block);
        return null;
    }
    private void CancelDrag() { var old=_gesture; _gesture=null; old?.Pointer.Capture(null); }
}
