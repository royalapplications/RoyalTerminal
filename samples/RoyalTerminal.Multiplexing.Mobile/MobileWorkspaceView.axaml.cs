// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RoyalTerminal.Multiplexing.Desktop;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Multiplexing.Desktop.Services;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Mobile;

/// <summary>A single native terminal, with no persisted credential, no automatic control acquisition and no local process launch.</summary>
public sealed partial class MobileWorkspaceView : UserControl
{
    private readonly HttpClient _http;
    private readonly IMultiplexingSocketConnector _sockets;
    private readonly CancellationTokenSource _life = new();
    private Uri? _host;
    private string? _token;
    private WorkspaceTerminalController? _controller;
    private RemoteTerminalControl? _terminal;
    private bool _busy, _closed;
    private CancellationTokenSource? _operation;
    private Task _pending = Task.CompletedTask;
    internal Task Shutdown { get; private set; } = Task.CompletedTask;

    public MobileWorkspaceView() : this(new HttpClientHandler { AllowAutoRedirect=false },new MultiplexingSocketConnector()) { }
    internal MobileWorkspaceView(HttpMessageHandler handler, IMultiplexingSocketConnector sockets)
    {
        _http=new(handler){Timeout=TimeSpan.FromSeconds(30)}; _sockets=sockets; InitializeComponent();
    }
    internal static Uri ParseEndpoint(string? text)
    {
        if (!Uri.TryCreate(text,UriKind.Absolute,out var uri) || uri.Scheme!=Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length!=0 || uri.Query.Length!=0 || uri.Fragment.Length!=0)
            throw new ArgumentException("Enter an HTTPS URL without a password, query, or fragment.");
        return uri;
    }
    private void ConnectClicked(object? sender,RoutedEventArgs e) => Run(async ct =>
    {
        var endpoint=ParseEndpoint(Host.Text); string token=Secret.Text??string.Empty;
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("A workspace token is required.");
        var context=await new MultiplexingApiClient(_http,endpoint,token).SendAsync(HttpMethod.Get,"api/context",cancellationToken:ct);
        var items=await new WorkspaceClient(_http).ListAsync(endpoint,token,ct); ct.ThrowIfCancellationRequested();
        _host=endpoint; _token=token; Secret.Text=string.Empty; Credentials.IsVisible=false; Picker.IsVisible=true; Forget.IsVisible=true;
        ControlButton.IsVisible=context.GetProperty("canControl").GetBoolean(); Sessions.ItemsSource=items; Sessions.SelectedItem=items.FirstOrDefault();
        Notice.Text=items.Count==0?"No sessions are visible with this token.":"Select a terminal. Observing does not take keyboard control.";
    });
    private void SessionSelected(object? sender,SelectionChangedEventArgs e)
    {
        if (Blocks is null) return; var blocks=(Sessions.SelectedItem as WorkspaceSession)?.Blocks;
        Blocks.ItemsSource=blocks; Blocks.SelectedItem=blocks?.FirstOrDefault();
    }
    private void AttachClicked(object? sender,RoutedEventArgs e)=>Run(async ct=>
    {
        if(_host is null || _token is null || Sessions.SelectedItem is not WorkspaceSession s || Blocks.SelectedItem is not WorkspaceBlock b) throw new InvalidOperationException("Select a session and a terminal first.");
        bool observer=!ReferenceEquals(sender,ControlButton); await ReleaseAsync(); ct.ThrowIfCancellationRequested();
        var t=new RemoteTerminalControl(_sockets){TerminalFontSize=12}; var c=new WorkspaceTerminalController(t,_http);
        _terminal=t; _controller=c; Viewport.Content=t;
        void Updated(string status) { if (_closed || !ReferenceEquals(c,_controller)) return; Notice.Text=$"{b.Label} · {status}"; InputPanel.IsVisible=c.HasControl; }
        c.StatusChanged+=Updated;
        try { await c.AttachAsync(new(_host,_token,s.Id,b.Id,b.Dimensions){Observer=observer},ct); ct.ThrowIfCancellationRequested(); Detach.IsVisible=true; Picker.IsVisible=false; InputPanel.IsVisible=c.HasControl; }
        catch { await ReleaseAsync(); throw; }
    });
    private void KeyClicked(object? sender,RoutedEventArgs e)
    {
        if(_closed || _busy || _controller?.HasControl!=true || sender is not Button button) return;
        string? text=button.Tag switch {"text"=>Composer.Text,"enter"=>"\r","tab"=>"\t","esc"=>"\u001b","interrupt"=>"\u0003",_=>null};
        if(!string.IsNullOrEmpty(text)) try { _terminal!.SendInput(text); if(button.Tag is "text") Composer.Text=string.Empty; } catch(Exception error) { Notice.Text=error.Message; }
    }
    private void DetachClicked(object? sender,RoutedEventArgs e)=>Run(async _=>{await ReleaseAsync(); Picker.IsVisible=true; Notice.Text="Detached. The remote process is unchanged.";});
    private void ForgetClicked(object? sender,RoutedEventArgs e)=>Run(async _=>{ForgetState(); await ReleaseAsync(); Notice.Text="Disconnected.";});
    private void ForgetState()
    {
        _token=null; _host=null; Secret.Text=Composer.Text=string.Empty; Sessions.ItemsSource=Blocks.ItemsSource=null;
        Credentials.IsVisible=true; Forget.IsVisible=Picker.IsVisible=InputPanel.IsVisible=Detach.IsVisible=false;
    }
    private async Task ReleaseAsync()
    {
        var c=_controller; _controller=null; _terminal=null; Viewport.Content=null;
        InputPanel.IsVisible=Detach.IsVisible=false; Composer.Text=string.Empty;
        if(c is not null) await c.DisposeAsync();
    }
    private void Run(Func<CancellationToken,Task> action)
    {
        if(_busy || _closed) return;
        _pending=ExecuteAsync();
        async Task ExecuteAsync()
        {
            _busy=true; Connect.IsEnabled=Picker.IsEnabled=Forget.IsEnabled=Detach.IsEnabled=false;
            using var op=CancellationTokenSource.CreateLinkedTokenSource(_life.Token); _operation=op;
            try { await action(op.Token); }
            catch(OperationCanceledException) when(op.IsCancellationRequested) { }
            catch(Exception error) { if(!_closed) Notice.Text=error.Message; }
            finally { _operation=null; _busy=false; if(!_closed) Connect.IsEnabled=Picker.IsEnabled=Forget.IsEnabled=Detach.IsEnabled=true; }
        }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e); if(_closed) return; _closed=true; _life.Cancel(); _operation?.Cancel(); ForgetState(); Viewport.Content=null;
        Shutdown=FinishAsync();
        async Task FinishAsync(){try { await _pending; await ReleaseAsync(); } finally { _http.Dispose();_life.Dispose(); }}
    }
}
