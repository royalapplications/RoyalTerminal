using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RoyalTerminal.Multiplexing.ClientTests;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class OverviewSubscriptionTests
{
    private sealed class Pair(bool delayRight) : IAsyncDisposable
    {
        internal readonly Guid Session=Guid.NewGuid(), Main=Guid.NewGuid(), Left=Guid.NewGuid(), Right=Guid.NewGuid();
        internal OverviewWindow Window {get; private set;}=null!;
        internal Management Api {get; private set;}=null!;
        internal Observers Sockets {get; private set;}=null!;
        internal OverviewPanel Surface=>Window.FindControl<OverviewPanel>("Surface")!;
        internal async Task OpenAsync(CancellationToken ct)
        {
            MultiplexerBlockDescriptor Block(Guid id)=>new(id,Session,Main,"Shell",MultiplexerBlockState.Running,new(16,4,0,0),null,2,1,0,null);
            var split=MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Horizontal,MultiplexerLayoutNode.Leaf(Left),MultiplexerLayoutNode.Leaf(Right));
            Api=new(new(Session,"Research",1,DateTimeOffset.UnixEpoch,[new(Main,"Main",split),new(Guid.NewGuid(),"Empty",null)],[Block(Left),Block(Right)]));
            Sockets=new(delayRight?Right:null); Window=new(Api,Sockets);Window.Connect(new("https://work.test/"),"scoped-test",Session);Window.Show();
            await Api.PollStarted.Task.WaitAsync(ct);await Sockets.Both.Task.WaitAsync(ct);
        }
        internal async Task PresentAsync(MultiplexingViewAction action,CancellationToken ct)
        {
            bool shown=await Window.PresentAsync(action,ct);
            Assert.True(shown,$"{action.Name}/{action.Direction}: {Window.FindControl<TextBlock>("StatusText")!.Text}");
        }
        public async ValueTask DisposeAsync(){if(Window is null)return;Window.Close();await Window.Shutdown.WaitAsync(TimeSpan.FromSeconds(10),TestContext.Current.CancellationToken);}
    }
    [AvaloniaFact]
    public async Task TwoObserversReceiveDistinctStreamsAndRevocationClosesBoth()
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(15));var ct=deadline.Token;
        await using var pair=new Pair(false);await pair.OpenAsync(ct);
        Assert.Equal(2,pair.Surface.Children.Count);Assert.Equal(new[]{pair.Left,pair.Right}.Order(),pair.Sockets.Streams.Keys.Order());
        Assert.All(pair.Sockets.Addresses,u=>Assert.Contains("role=observer",u.Query,StringComparison.Ordinal));
        Assert.All(pair.Sockets.Streams.Values,s=>Assert.Empty(s.Sent));pair.Api.Revoke.TrySetResult();
        await Task.WhenAll(pair.Sockets.Streams.Values.Select(s=>s.CloseSent.Task)).WaitAsync(ct);
        Assert.Empty(pair.Surface.Children);Assert.Null(pair.Window.FindControl<ComboBox>("WindowsList")!.ItemsSource);
        await pair.Window.Shutdown; // The view is still open; wait for actual socket disposal, not just close transmission.
        while(pair.Sockets.Streams.Values.Any(s=>!s.Disposed))await Task.Delay(10,ct);
    }
    [AvaloniaFact]
    public async Task LateObserverDoesNotStealFocusAndNavigationDoesNotWriteToEitherPty()
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(15));var ct=deadline.Token;
        await using var pair=new Pair(true);await pair.OpenAsync(ct);
        var panel=pair.Surface;var left=panel.Children[0];var right=panel.Children[1];
        static bool Ready(Control root)=>root.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("Attached · observer",StringComparison.Ordinal)==true);
        while(!Ready(left))await Task.Delay(10,ct);
        await pair.PresentAsync(new("pane.focus",pair.Left,null,null),ct);Assert.Same(left,panel.LastFocusedPane);
        pair.Sockets.Streams[pair.Right].Queue(Observers.Ready);while(!Ready(right))await Task.Delay(10,ct);
        Assert.Same(left,panel.LastFocusedPane);Assert.Contains("workspace-active-pane",left.Classes);
        await pair.PresentAsync(new("pane.zoom",null,null,null),ct);Assert.False(right.IsVisible);
        await pair.PresentAsync(new("pane.focus",null,null,"right"),ct);Assert.Same(right,panel.LastFocusedPane);Assert.True(right.IsVisible);Assert.Null(panel.ZoomedPane);
        Assert.False(await pair.Window.PresentAsync(new("pane.focus",null,null,"right"),ct));
        await pair.PresentAsync(new("pane.focus",null,null,"next"),ct);Assert.Same(left,panel.LastFocusedPane);Assert.Equal(2,pair.Sockets.Addresses.Count);
        await pair.PresentAsync(new("client.tab.next",null,null,null),ct);Assert.Equal(1,pair.Window.FindControl<ComboBox>("WindowsList")!.SelectedIndex);Assert.Empty(panel.Children);
        Assert.False(await pair.Window.PresentAsync(new("pane.focus",null,null,"next"),ct));
        Assert.All(pair.Sockets.Streams.Values,s=>{Assert.Empty(s.Sent);Assert.True(s.Disposed);});
    }
    private sealed class Management(MultiplexerSessionDescriptor session):HttpMessageHandler
    {
        internal TaskCompletionSource PollStarted {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Revoke {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            Assert.Equal("scoped-test",r.Headers.Authorization?.Parameter);
            if(r.Method==HttpMethod.Get)return new(HttpStatusCode.OK){Content=new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(session,OverviewJson.Default.MultiplexerSessionDescriptor))};
            using var doc=JsonDocument.Parse(await r.Content!.ReadAsByteArrayAsync(ct));Assert.Equal("session.wait_change",doc.RootElement.GetProperty("method").GetString());
            PollStarted.TrySetResult();await Revoke.Task.WaitAsync(ct);return new(HttpStatusCode.Forbidden){Content=new StringContent("{\"code\":\"expired\"}")};
        }
    }
    private sealed class Observers(Guid? delayed):IMultiplexingSocketConnector
    {
        internal const string Ready="""{"type":"ready","sequence":2}""";
        internal Dictionary<Guid,TestWebSocket> Streams {get;}=[];internal List<Uri> Addresses {get;}=[];
        internal TaskCompletionSource Both {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<WebSocket> ConnectAsync(Uri endpoint,string accessToken,CancellationToken ct)
        {
            Assert.Equal("scoped-test",accessToken);Guid block=Guid.Parse(endpoint.Segments[^2].TrimEnd('/'));
            var s=new TestWebSocket();s.Queue($$"""{"type":"attached","attachmentId":"{{Guid.NewGuid()}}","hasControl":false}""");
            s.Queue("""{"sequence":1,"kind":"resize","dimensions":{"columns":16,"rows":4,"widthPixels":0,"heightPixels":0}}""");
            s.Queue("""{"sequence":2,"kind":"output","data":"b2s="}""");if(block!=delayed)s.Queue(Ready);
            lock(Streams){Streams.Add(block,s);Addresses.Add(endpoint);if(Streams.Count==2)Both.TrySetResult();}return ValueTask.FromResult<WebSocket>(s);
        }
    }
}
