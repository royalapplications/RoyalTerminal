// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using Avalonia.Interactivity;
using RoyalTerminal.Multiplexing.Desktop;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using RoyalTerminal.Terminal.Transport.Multiplexing;
namespace RoyalTerminal.Multiplexing.Mobile;

public sealed partial class MobileWorkspaceView
{
    private CancellationTokenSource? _sharedWatch;
    private async void FollowWindowZoom(object? sender,RoutedEventArgs args)
    {
        if(_sharedWatch is {} previous){previous.Cancel();return;}
        if(_busy||_closed||_host is null||_token is null||_shown is not Guid initial||Sessions.SelectedItem is not WorkspaceSession session||_controllerRequested)
        {Notice.Text="Observe a terminal before following shared zoom.";return;}
        var host=_host;var api=new MultiplexingApiClient(_http,host,_token);
        using var watch=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);var ct=watch.Token;_sharedWatch=watch;
        FollowZoom.Content="Unfollow";bool applied=false;
        // Stopping the app, detaching or an explicit local zoom always wins over a slow server response.
        void Stop(object? s,RoutedEventArgs e)=>watch.Cancel();
        Detach.Click+=Stop;Zoom.Click+=Stop;Forget.Click+=Stop;ControlButton.Click+=Stop;
        try
        {
            var json=await api.SendAsync(HttpMethod.Get,$"api/sessions/{session.Id:D}",cancellationToken:ct);
            Guid? windowId=null;Guid? last=null;
            while(true)
            {
                ct.ThrowIfCancellationRequested();if(_closed||_host!=host||_controllerRequested||_shown is null||Sessions.SelectedItem is not WorkspaceSession selected||selected.Id!=session.Id)break;
                var snapshot=json.Deserialize(OverviewJson.Default.MultiplexerSessionDescriptor)??throw new InvalidDataException("Missing session.");
                if(snapshot.Id!=session.Id)throw new InvalidDataException("Unexpected session.");
                windowId??=snapshot.Blocks.FirstOrDefault(b=>b.Id==initial)?.WindowId;
                var window=snapshot.Windows.FirstOrDefault(w=>w.Id==windowId);if(window is null)break;
                var target=window.ZoomedBlockId;
                if(!_busy&&target!=last)
                {
                    var desired=target??initial;
                    var block=snapshot.Blocks.FirstOrDefault(b=>b.Id==desired&&b.WindowId==window.Id&&b.Placement!=MultiplexerPaneKind.Unplaced);
                    if(target.HasValue&&block is null)throw new InvalidDataException("Invalid shared zoom target.");
                    if(block is not null&&_shown!=desired)
                    {
                        bool attached=false;
                        Run(async operation=>{using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,operation);await AttachBlockAsync(session.Id,new(block.Id,block.Label,block.State.ToString(),block.Dimensions),true,linked.Token);_window=window.Id;attached=true;});
                        await _pending;ct.ThrowIfCancellationRequested();if(!attached)break;
                    }
                    SetZoom(target.HasValue);applied=target.HasValue;last=target;
                }
                var query=JsonSerializer.SerializeToElement(new RevisionPoll(session.Id,snapshot.Revision,15000),OverviewJson.Default.RevisionPoll);
                json=await api.CallAsync("session.wait_change",query,ct);
            }
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){}
        catch(Exception error){if(!_closed&&!ct.IsCancellationRequested)Notice.Text="Shared zoom stopped: "+error.Message;}
        finally
        {
            Detach.Click-=Stop;Zoom.Click-=Stop;Forget.Click-=Stop;ControlButton.Click-=Stop;
            if(ReferenceEquals(_sharedWatch,watch)){_sharedWatch=null;if(!_closed){FollowZoom.Content="Follow";if(applied)SetZoom(false);}}
        }
    }
}
