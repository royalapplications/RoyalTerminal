// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Net;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

public sealed partial class OverviewWindow
{
    private sealed record ToolChoice(LayoutTool Value,string Label) { public override string ToString()=>Label; }
    private static readonly ToolChoice[] Tools = [new(LayoutTool.Tile,"Tile / restore"), new(LayoutTool.Float,"Float"), new(LayoutTool.Shelf,"Shelve"), new(LayoutTool.Left,"Tile left of…"), new(LayoutTool.Right,"Tile right of…"), new(LayoutTool.Above,"Tile above…"), new(LayoutTool.Below,"Tile below…"), new(LayoutTool.Swap,"Swap with…"), new(LayoutTool.Move,"Move to window…"), new(LayoutTool.Grow,"Grow split"), new(LayoutTool.Shrink,"Shrink split"), new(LayoutTool.NewShellRight,"New shell right"), new(LayoutTool.NewShellBelow,"New shell below"), new(LayoutTool.RenameBlock,"Rename terminal"), new(LayoutTool.NewWindow,"New window"), new(LayoutTool.RenameWindow,"Rename window"), new(LayoutTool.EarlierWindow,"Move window earlier"), new(LayoutTool.LaterWindow,"Move window later"), new(LayoutTool.CloseWindow,"Close window; preserve terminals")];
    private const string Instructions="Drag a tile to an edge to split or to the center to swap. Drag a float to move; Shift-drag to resize. Escape cancels.";
    private MultiplexerSessionDescriptor? _editorSnapshot;
    private Guid _editorWindow;
    private bool _editPending;

    private void InitializeEditing(object? sender,EventArgs e)
    {
        EditOperation.ItemsSource=Tools; EditOperation.SelectedIndex=0;
        LayoutUpdated+=(_,_)=>SyncEditor();
        Surface.AddHandler(PointerPressedEvent,BeginDrag,RoutingStrategies.Tunnel);
        Surface.AddHandler(PointerMovedEvent,MoveDrag,RoutingStrategies.Tunnel);
        Surface.AddHandler(PointerReleasedEvent,EndDrag,RoutingStrategies.Tunnel);
        Surface.PointerCaptureLost+=(_,ev)=>{ if (_gesture?.Pointer==ev.Pointer) _gesture=null; };
        AddHandler(KeyDownEvent,(_,ev)=>{ if(ev.Key==Key.Escape && _gesture is not null) { CancelDrag(); EditHint.Text=Instructions; ev.Handled=true; } },RoutingStrategies.Tunnel);
    }
    private async void ToggleEditing(object? sender,RoutedEventArgs args)
    {
        if(EditBar.IsVisible) { HideEditor(); return; }
        if(_snapshot is null || _endpoint is null || _editPending) return;
        EditToggle.IsEnabled=false;
        try
        {
            var context=await new MultiplexingApiClient(_http,_endpoint,_token).SendAsync(HttpMethod.Get,"api/context",cancellationToken:_life.Token);
            if(_life.IsCancellationRequested || _snapshot is null) return;
            if(!context.GetProperty("administrator").GetBoolean()) { StatusText.Text="Layout editing requires an administrator. Terminals remain read-only."; return; }
            EditBar.IsVisible=true; EditToggle.Content="Finish editing"; EditHint.Text=Instructions; SyncEditor();
        }
        catch(Exception) when(_life.IsCancellationRequested) { }
        catch(Exception ex) { StatusText.Text=ex.Message; }
        finally { EditToggle.IsEnabled=true; }
    }
    private void HideEditor()
    {
        CancelDrag(); EditBar.IsVisible=false; EditToggle.Content="Edit layout…"; EditSource.ItemsSource=EditTarget.ItemsSource=EditDestination.ItemsSource=null;
        EditLabel.Text=string.Empty; _editorSnapshot=null;
    }
    private void SyncEditor()
    {
        if(!EditBar.IsVisible) return;
        if(_snapshot is not { } s) { HideEditor(); return; }
        if(ReferenceEquals(s,_editorSnapshot) && _windowId==_editorWindow) return;
        _editorSnapshot=s; _editorWindow=_windowId;
        if(_gesture is not null) { CancelDrag(); EditHint.Text="Workspace changed; drag again."; }
        var windows=s.Windows.ToDictionary(w=>w.Id,w=>w.Label);
        Choice Option(MultiplexerBlockDescriptor b)=>new(b.Id,$"{windows[b.WindowId]} / {b.Label} · {b.Placement} · {b.Id.ToString("N")[..6]}");
        Bind(EditSource,s.Blocks.Where(b=>b.WindowId==_windowId).Select(Option)); Bind(EditTarget,s.Blocks.Select(Option));
        Bind(EditDestination,s.Windows.Where(w=>w.Id!=_windowId).Select(w=>new Choice(w.Id,w.Label)));
        static void Bind(ComboBox box,IEnumerable<Choice> source)
        { var previous=(box.SelectedItem as Choice)?.Id; var items=source.ToArray(); box.ItemsSource=items; box.SelectedItem=items.FirstOrDefault(x=>x.Id==previous)??items.FirstOrDefault(); }
    }
    private async void ApplyLayoutEdit(object? sender,RoutedEventArgs args)
    {
        SyncEditor(); if(!EditBar.IsVisible || _editorSnapshot is not { } s || EditOperation.SelectedItem is not ToolChoice op) return;
        await SubmitEditAsync(s,_editorWindow,op.Value,(EditSource.SelectedItem as Choice)?.Id,(EditTarget.SelectedItem as Choice)?.Id,(EditDestination.SelectedItem as Choice)?.Id);
    }
    private async Task SubmitEditAsync(MultiplexerSessionDescriptor s,Guid window,LayoutTool op,Guid? block,Guid? target=null,Guid? dest=null,MultiplexerFloatBounds? bounds=null)
    {
        if(_editPending || _endpoint is null || !EditBar.IsVisible) return;
        if(!ReferenceEquals(s,_snapshot) || window!=_windowId) { EditHint.Text="Layout changed. Select again."; return; }
        _editPending=true; ApplyEdit.IsEnabled=false;
        try
        {
            var edit=LayoutEdits.Plan(s,window,op,block,target,dest,EditLabel.Text,bounds);
            await new MultiplexingApiClient(_http,_endpoint,_token).CallAsync(edit.Method,edit.Arguments,_life.Token);
            EditHint.Text=$"{op} accepted. {Instructions}";
        }
        catch(MultiplexingApiException ex) when(ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) { HideEditor(); StatusText.Text="Layout authorization was lost."; }
        catch(MultiplexingApiException ex) when(ex.StatusCode==HttpStatusCode.Conflict) { EditHint.Text="The workspace changed. Review the current layout and retry explicitly."; }
        catch(Exception) when(_life.IsCancellationRequested) { }
        catch(Exception ex) { EditHint.Text=ex.Message; }
        finally { ApplyEdit.IsEnabled=true; _editPending=false; }
    }
}
