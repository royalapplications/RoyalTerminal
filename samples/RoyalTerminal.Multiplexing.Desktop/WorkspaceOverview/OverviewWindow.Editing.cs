// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

public sealed partial class OverviewWindow
{
    private sealed record ToolChoice(LayoutTool Value, string Label) { public override string ToString() => Label; }
    private sealed record Gesture(IPointer Pointer, MultiplexerSessionDescriptor Snapshot, Guid Window, PaneSlot Slot, Point Start, Size Size, bool Resize);
    private static readonly ToolChoice[] Tools = [new(LayoutTool.Tile,"Tile / restore"),new(LayoutTool.Float,"Float"),new(LayoutTool.Shelf,"Shelve"),new(LayoutTool.Left,"Tile left of…"),new(LayoutTool.Right,"Tile right of…"),new(LayoutTool.Above,"Tile above…"),new(LayoutTool.Below,"Tile below…"),new(LayoutTool.Swap,"Swap with…"),new(LayoutTool.Move,"Move to window…"),new(LayoutTool.Grow,"Grow split"),new(LayoutTool.Shrink,"Shrink split"),new(LayoutTool.NewShellRight,"New shell right"),new(LayoutTool.NewShellBelow,"New shell below"),new(LayoutTool.RenameBlock,"Rename terminal"),new(LayoutTool.NewWindow,"New window"),new(LayoutTool.RenameWindow,"Rename window"),new(LayoutTool.EarlierWindow,"Move window earlier"),new(LayoutTool.LaterWindow,"Move window later"),new(LayoutTool.CloseWindow,"Close window; preserve terminals")];
    private const string Instructions = "Drag a tile to another tile’s edge to split, or to its center to swap. Drag a float to move it; Shift-drag to resize. Escape cancels.";
    private MultiplexerSessionDescriptor? _editorSnapshot;
    private Guid _editorWindow;
    private Gesture? _gesture;
    private bool _editPending;

    private void InitializeEditing(object? sender, EventArgs args)
    {
        EditOperation.ItemsSource = Tools; EditOperation.SelectedIndex = 0;
        LayoutUpdated += (_, _) => SyncEditor();
        Surface.AddHandler(PointerPressedEvent, BeginDrag, RoutingStrategies.Tunnel);
        Surface.AddHandler(PointerMovedEvent, MoveDrag, RoutingStrategies.Tunnel);
        Surface.AddHandler(PointerReleasedEvent, EndDrag, RoutingStrategies.Tunnel);
        Surface.PointerCaptureLost += (_, e) => { if (_gesture?.Pointer == e.Pointer) _gesture = null; };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape && _gesture is not null) { CancelDrag(); EditHint.Text = Instructions; e.Handled = true; } }, RoutingStrategies.Tunnel);
    }

    // The overview can be used by scoped observers. Authoring is opt-in and every mutation is independently authorized on the server.
    private async void ToggleEditing(object? sender, RoutedEventArgs args)
    {
        if (EditBar.IsVisible) { HideEditor(); return; }
        if (_snapshot is null || _endpoint is null || _editPending) return;
        EditToggle.IsEnabled = false;
        try
        {
            var api = new MultiplexingApiClient(_http, _endpoint, _token);
            var context = await api.SendAsync(HttpMethod.Get, "api/context", cancellationToken:_life.Token);
            if (_life.IsCancellationRequested || _snapshot is null) return;
            if (!context.GetProperty("administrator").GetBoolean()) { StatusText.Text = "Layout editing requires an administrator. The terminals remain read-only."; return; }
            EditBar.IsVisible = true; EditToggle.Content = "Finish editing"; EditHint.Text = Instructions; SyncEditor();
        }
        catch (Exception ex) when (!_life.IsCancellationRequested) { StatusText.Text = ex.Message; }
        catch (OperationCanceledException) { }
        finally { EditToggle.IsEnabled = true; }
    }

    private void HideEditor()
    {
        CancelDrag(); EditBar.IsVisible = false; EditToggle.Content = "Edit layout…";
        EditSource.ItemsSource = EditTarget.ItemsSource = EditDestination.ItemsSource = null;
        EditLabel.Text = string.Empty; _editorSnapshot = null;
    }
    private void SyncEditor()
    {
        if (!EditBar.IsVisible) return;
        if (_snapshot is not { } s) { HideEditor(); return; }
        if (ReferenceEquals(s, _editorSnapshot) && _windowId == _editorWindow) return;
        _editorSnapshot = s; _editorWindow = _windowId;
        if (_gesture is not null) { CancelDrag(); EditHint.Text = "Workspace changed; drag again."; }
        var windows = s.Windows.ToDictionary(w => w.Id, w => w.Label);
        Choice Option(MultiplexerBlockDescriptor b) => new(b.Id, $"{windows[b.WindowId]} / {b.Label} · {b.Placement} · {b.Id.ToString("N")[..6]}");
        Bind(EditSource, s.Blocks.Where(b => b.WindowId == _windowId).Select(Option));
        Bind(EditTarget, s.Blocks.Select(Option));
        Bind(EditDestination, s.Windows.Where(w => w.Id != _windowId).Select(w => new Choice(w.Id, w.Label)));
        static void Bind(ComboBox box, IEnumerable<Choice> items)
        {
            var previous = (box.SelectedItem as Choice)?.Id; var options = items.ToArray();
            box.ItemsSource = options; box.SelectedItem = options.FirstOrDefault(x => x.Id == previous) ?? options.FirstOrDefault();
        }
    }

    private async void ApplyLayoutEdit(object? sender, RoutedEventArgs args)
    {
        SyncEditor();
        if (!EditBar.IsVisible || _editorSnapshot is not { } s || EditOperation.SelectedItem is not ToolChoice op) return;
        await SubmitEditAsync(s, _editorWindow, op.Value, (EditSource.SelectedItem as Choice)?.Id, (EditTarget.SelectedItem as Choice)?.Id, (EditDestination.SelectedItem as Choice)?.Id);
    }
    private async Task SubmitEditAsync(MultiplexerSessionDescriptor s, Guid window, LayoutTool op, Guid? block, Guid? target = null, Guid? dest = null, MultiplexerFloatBounds? bounds = null)
    {
        if (_editPending || _endpoint is null || !EditBar.IsVisible) return;
        if (!ReferenceEquals(s,_snapshot) || window != _windowId) { EditHint.Text = "Layout changed. Select again."; return; }
        _editPending = true; ApplyEdit.IsEnabled = false;
        try
        {
            var edit = LayoutEdits.Plan(s, window, op, block, target, dest, EditLabel.Text, bounds);
            await new MultiplexingApiClient(_http, _endpoint, _token).CallAsync(edit.Method, edit.Arguments, _life.Token);
            // Only the session watch publishes visual state. Never replay a possibly applied mutation on transport failure.
            EditHint.Text = $"{op} accepted. {Instructions}";
        }
        catch (MultiplexingApiException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) { HideEditor(); StatusText.Text = "Layout authorization was lost."; }
        catch (MultiplexingApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { EditHint.Text = "The workspace changed. Review the current layout and retry explicitly."; }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
        catch (Exception ex) { EditHint.Text = ex.Message; }
        finally { ApplyEdit.IsEnabled = true; _editPending = false; }
    }
    private void BeginDrag(object? sender, PointerPressedEventArgs e)
    {
        if (!EditBar.IsVisible || _editPending || _gesture is not null || _snapshot is not { } s || !e.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed) return;
        var size = Surface.Bounds.Size; if (size.Width <= 0 || size.Height <= 0) return;
        var p = e.GetPosition(Surface); var unit = new Point(p.X/size.Width,p.Y/size.Height);
        foreach (var slot in LayoutProjection.Build(s,_windowId).Slots.OrderByDescending(x=>x.Layer))
        {
            if (!slot.Area.Contains(unit)) continue;
            if (EditSource.ItemsSource is IEnumerable<Choice> choices) EditSource.SelectedItem = choices.FirstOrDefault(c=>c.Id==slot.Block.Id);
            _gesture = new(e.Pointer,s,_windowId,slot,p,size,e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Pointer.Capture(Surface); e.Handled = true; break;
        }
    }
    private void MoveDrag(object? sender, PointerEventArgs e)
    {
        if (_gesture is not { } g || g.Pointer != e.Pointer) return;
        var p=e.GetPosition(Surface); var delta=p-g.Start;
        if (delta.Length >= 4) EditHint.Text = g.Slot.Block.Placement == MultiplexerPaneKind.Floating ? (g.Resize ? "Release to resize float." : "Release to move float.") : Drop(g,p) is { } d ? $"Release: {d.Tool} · {d.Target.Label}" : "Drop on another tile or press Escape.";
        e.Handled=true;
    }
    private async void EndDrag(object? sender, PointerReleasedEventArgs e)
    {
        if (_gesture is not { } g || g.Pointer != e.Pointer) return;
        var p=e.GetPosition(Surface); CancelDrag(); e.Handled=true;
        if ((p-g.Start).Length < 4 || Surface.Bounds.Size != g.Size) { EditHint.Text = Instructions; return; }
        if (g.Slot.Block.FloatingBounds is { } r)
            await SubmitEditAsync(g.Snapshot,g.Window,LayoutTool.Float,g.Slot.Block.Id,bounds:LayoutEdits.DragFloat(r,p-g.Start,g.Size,g.Resize));
        else if (Drop(g,p) is { } d) await SubmitEditAsync(g.Snapshot,g.Window,d.Tool,g.Slot.Block.Id,d.Target.Id);
        else EditHint.Text = Instructions;
    }
    private static (LayoutTool Tool, MultiplexerBlockDescriptor Target)? Drop(Gesture g, Point p)
    {
        var unit = new Point(p.X/g.Size.Width,p.Y/g.Size.Height);
        foreach (var slot in LayoutProjection.Build(g.Snapshot,g.Window).Slots.OrderByDescending(x=>x.Layer))
            if (slot.Area.Contains(unit)) return slot.Block.Id == g.Slot.Block.Id || slot.Block.Placement != MultiplexerPaneKind.Tiled ? null : (LayoutEdits.DropZone(slot.Area,unit),slot.Block);
        return null;
    }
    private void CancelDrag() { var old = _gesture; _gesture=null; old?.Pointer.Capture(null); }
}
