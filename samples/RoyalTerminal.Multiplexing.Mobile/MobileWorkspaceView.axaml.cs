// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RoyalTerminal.Multiplexing.Desktop;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Multiplexing.Desktop.Services;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Mobile;

/// <summary>A native observer or explicitly requested controller. Remote navigation never requests input.</summary>
public sealed partial class MobileWorkspaceView : UserControl
{
    private readonly HttpClient _http;
    private readonly IMultiplexingSocketConnector _sockets;
    private readonly CancellationTokenSource _life = new();
    private Uri? _host; private string? _token;
    private WorkspaceTerminalController? _controller; private RemoteTerminalControl? _terminal;
    private bool _busy, _closed, _controllerRequested;
    private CancellationTokenSource? _operation, _focus;
    private Guid? _focusSession; private Task _focusTask=Task.CompletedTask, _pending=Task.CompletedTask;
    internal Task Shutdown { get; private set; } = Task.CompletedTask;
    public MobileWorkspaceView() : this(new HttpClientHandler{AllowAutoRedirect=false},new MultiplexingSocketConnector()) { }
    internal MobileWorkspaceView(HttpMessageHandler handler,IMultiplexingSocketConnector sockets)
    { _http=new(handler){Timeout=TimeSpan.FromSeconds(30)};_sockets=sockets;InitializeComponent(); }
    internal static Uri ParseEndpoint(string? text)
    {
        if(!Uri.TryCreate(text,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||string.IsNullOrWhiteSpace(uri.Host)||uri.UserInfo.Length!=0||uri.Query.Length!=0||uri.Fragment.Length!=0)throw new ArgumentException("Enter an HTTPS URL without a password, query, or fragment.");return uri;
    }
    private void ConnectClicked(object? sender,RoutedEventArgs e)=>Run(async ct=>{
        var endpoint=ParseEndpoint(Host.Text);string token=Secret.Text??string.Empty;if(string.IsNullOrWhiteSpace(token))throw new ArgumentException("A workspace token is required.");
        var context=await new MultiplexingApiClient(_http,endpoint,token).SendAsync(HttpMethod.Get,"api/context",cancellationToken:ct);
        var items=await new WorkspaceClient(_http).ListAsync(endpoint,token,ct);ct.ThrowIfCancellationRequested();_host=endpoint;_token=token;Secret.Text=string.Empty;Credentials.IsVisible=false;Picker.IsVisible=Forget.IsVisible=true;
        ControlButton.IsVisible=context.GetProperty("canControl").GetBoolean();Sessions.ItemsSource=items;Sessions.SelectedItem=items.FirstOrDefault();Notice.Text=items.Count==0?"No sessions are visible with this token.":"Select a terminal. Observing does not take keyboard control.";
    });
    private void SessionSelected(object? sender,SelectionChangedEventArgs e)
    {
        if(Blocks is null)return;var s=Sessions.SelectedItem as WorkspaceSession;if(s?.Id!=_focusSession)_focus?.Cancel();Blocks.ItemsSource=s?.Blocks;Blocks.SelectedItem=s?.Blocks.FirstOrDefault();
    }
    private void AttachClicked(object? sender,RoutedEventArgs e)=>Run(async ct=>{
        if(Sessions.SelectedItem is not WorkspaceSession s||Blocks.SelectedItem is not WorkspaceBlock b)throw new InvalidOperationException("Select a session and terminal first.");
        bool observe=!ReferenceEquals(sender,ControlButton);if(!observe)_focus?.Cancel();await AttachBlockAsync(s.Id,b,observe,ct);
    });
    private async Task AttachBlockAsync(Guid session,WorkspaceBlock b,bool observe,CancellationToken ct)
    {
        var host=_host??throw new InvalidOperationException("Disconnected.");var token=_token??throw new InvalidOperationException("Disconnected.");await ReleaseAsync();ct.ThrowIfCancellationRequested();
        var t=new RemoteTerminalControl(_sockets){TerminalFontSize=12};var c=new WorkspaceTerminalController(t,_http);_terminal=t;_controller=c;_controllerRequested=!observe;Viewport.Content=t;
        c.StatusChanged+=status=>{if(!_closed&&ReferenceEquals(c,_controller)){Notice.Text=$"{b.Label} · {status}";InputPanel.IsVisible=c.HasControl;}};
        try{await c.AttachAsync(new(host,token,session,b.Id,b.Dimensions){Observer=observe},ct);ct.ThrowIfCancellationRequested();Detach.IsVisible=true;Picker.IsVisible=false;InputPanel.IsVisible=c.HasControl;}
        catch{await ReleaseAsync();throw;}
    }
    private void ToggleRemoteFocus(object? sender,RoutedEventArgs e)
    {
        if(_focus is {} existing){existing.Cancel();return;}if(_closed||_busy||_controllerRequested||_host is null||_token is null||Sessions.SelectedItem is not WorkspaceSession s){Notice.Text="Connect as an observer before allowing remote focus.";return;}
        var cts=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);_focus=cts;_focusSession=s.Id;AllowFocus.Content="Stop focus";
        var api=new MultiplexingApiClient(_http,_host,_token);_focusTask=ListenAsync();
        async Task ListenAsync(){try{await MultiplexingViewReceiver.RunAsync(api,s.Id,"mobile","Mobile observer",Present,id=>Notice.Text=$"Remote focus enabled · {id:N}",cts.Token);}catch(OperationCanceledException)when(cts.IsCancellationRequested){}catch(Exception err){if(!_closed)Notice.Text="Remote focus stopped: "+err.Message;}finally{_focus=null;_focusSession=null;AllowFocus.Content="Allow focus";cts.Dispose();}}
        async ValueTask<bool> Present(MultiplexingViewAction action,CancellationToken ct)
        {
            if(_busy||_closed||_controllerRequested||_focusSession!=s.Id)return false;
            var fresh=await api.SendAsync(HttpMethod.Get,$"api/sessions/{s.Id:D}",cancellationToken:ct);ct.ThrowIfCancellationRequested();
            if(_busy||_closed||_controllerRequested||_focusSession!=s.Id||fresh.GetProperty("id").GetGuid()!=s.Id)return false;
            var window=action.WindowId;var candidates=fresh.GetProperty("blocks").EnumerateArray().Where(b=>(!b.TryGetProperty("placement",out var p)||p.GetString()!="Unplaced")&&(window is null||b.GetProperty("windowId").GetGuid()==window));
            var block=candidates.FirstOrDefault(b=>action.BlockId is null||b.GetProperty("id").GetGuid()==action.BlockId);
            if(block.ValueKind==JsonValueKind.Undefined && (window is null||!fresh.GetProperty("windows").EnumerateArray().Any(w=>w.GetProperty("id").GetGuid()==window)))return false;
            var model=block.ValueKind==JsonValueKind.Undefined?null:block.Deserialize(DesktopJsonContext.Default.WorkspaceBlock);
            bool applied=false;Run(async op=>{using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,op);
                if(model is null){await ReleaseAsync();Picker.IsVisible=true;Notice.Text="The requested window has no visible terminals.";}
                else{await AttachBlockAsync(s.Id,model,true,linked.Token);}applied=true;});await _pending;return applied;
        }
    }
    private void KeyClicked(object? sender,RoutedEventArgs e)
    {
        if(_closed||_busy||_controller?.HasControl!=true||sender is not Button button)return;
        string? text=button.Tag switch{"text"=>Composer.Text,"enter"=>"\r","tab"=>"\t","esc"=>"\u001b","interrupt"=>"\u0003",_=>null};if(!string.IsNullOrEmpty(text))try{_terminal!.SendInput(text);if(button.Tag is "text")Composer.Text=string.Empty;}catch(Exception ex){Notice.Text=ex.Message;}
    }
    private void DetachClicked(object? sender,RoutedEventArgs e)=>Run(async _=>{_focus?.Cancel();await ReleaseAsync();Picker.IsVisible=true;Notice.Text="Detached. The remote process is unchanged.";});
    private void ForgetClicked(object? sender,RoutedEventArgs e)=>Run(async _=>{ForgetState();await ReleaseAsync();Notice.Text="Disconnected.";});
    private void ForgetState(){_focus?.Cancel();_token=null;_host=null;Secret.Text=Composer.Text=string.Empty;Sessions.ItemsSource=Blocks.ItemsSource=null;Credentials.IsVisible=true;Forget.IsVisible=Picker.IsVisible=InputPanel.IsVisible=Detach.IsVisible=false;}
    private async Task ReleaseAsync(){var c=_controller;_controller=null;_controllerRequested=false;_terminal=null;Viewport.Content=null;InputPanel.IsVisible=Detach.IsVisible=false;Composer.Text=string.Empty;if(c is not null)await c.DisposeAsync();}
    private void Run(Func<CancellationToken,Task> action)
    {
        if(_busy||_closed)return;_pending=ExecuteAsync();async Task ExecuteAsync(){_busy=true;Connect.IsEnabled=Picker.IsEnabled=Forget.IsEnabled=Detach.IsEnabled=false;using var op=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);_operation=op;
        try{await action(op.Token);}catch(OperationCanceledException)when(op.IsCancellationRequested){}catch(Exception ex){if(!_closed)Notice.Text=ex.Message;}finally{_operation=null;_busy=false;if(!_closed)Connect.IsEnabled=Picker.IsEnabled=Forget.IsEnabled=Detach.IsEnabled=true;}}
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);if(_closed)return;_closed=true;_life.Cancel();_operation?.Cancel();ForgetState();Viewport.Content=null;Shutdown=FinishAsync();async Task FinishAsync(){try{await _pending;await _focusTask;await ReleaseAsync();}finally{_http.Dispose();_life.Dispose();}}
    }
}
