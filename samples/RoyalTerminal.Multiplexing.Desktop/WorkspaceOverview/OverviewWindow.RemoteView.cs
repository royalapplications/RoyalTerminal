// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia.Interactivity;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

public sealed partial class OverviewWindow
{
    private CancellationTokenSource? _viewConsent;
    private async void ToggleRemoteView(object? sender,RoutedEventArgs e)
    {
        if(_viewConsent is {} current){current.Cancel();return;}
        if(_snapshot is null || _endpoint is null || _life.IsCancellationRequested)return;
        using var consent=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);_viewConsent=consent;
        ViewConsent.Content="Stop remote focus";
        try
        {
            await MultiplexingViewReceiver.RunAsync(new(_http,_endpoint,_token),_sessionId,"native","Desktop overview",PresentAsync,
                id=>StatusText.Text=$"Remote focus enabled for this overview · {id:N}",consent.Token);
        }
        catch(OperationCanceledException) when(consent.IsCancellationRequested) { }
        catch(Exception error){if(!consent.IsCancellationRequested)StatusText.Text="Remote focus stopped: "+error.Message;}
        finally{_viewConsent=null;ViewConsent.Content="Allow remote focus";}
    }
    private async ValueTask<bool> PresentAsync(MultiplexingViewAction action,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(_snapshot is not {} s || EditBar.IsVisible)return false;
        var block=action.BlockId.HasValue?s.Blocks.FirstOrDefault(b=>b.Id==action.BlockId):null;
        if(action.BlockId.HasValue && (block is null || block.Placement==MultiplexerPaneKind.Unplaced))return false;
        var window=block?.WindowId??action.WindowId;
        if(WindowsList.ItemsSource is not IEnumerable<Choice> choices || choices.FirstOrDefault(c=>c.Id==window) is not {} choice)return false;
        if(_windowId!=choice.Id)
        {
            _binding=true;try{WindowsList.SelectedItem=choice;_windowId=choice.Id;}finally{_binding=false;}
        }
        await RenderAsync();ct.ThrowIfCancellationRequested();
        if(EditBar.IsVisible || _windowId!=choice.Id || _snapshot is null)return false;
        if(block is null)return true;
        if(!_panes.TryGetValue(block.Id,out var pane))return false;
        pane.Terminal.Focus();return true;
    }
}
