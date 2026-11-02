using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using RoyalTerminal.Multiplexing.ClientTests;
using RoyalTerminal.Multiplexing.Mobile;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;
namespace RoyalTerminal.Multiplexing.MobileTests;

public sealed class PhoneSharedZoomTests
{
    [AvaloniaFact]
    public async Task RepeatedSharedSetsReuseTheSameObserverAndClearingRestoresItsOriginalPane()
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(12));var ct=timeout.Token;
        var service=new Changes();var connections=new Sockets();var phone=new MobileWorkspaceView(service,connections);
        var window=new Window{Content=phone,Width=390,Height=820};window.Show();
        T Get<T>(string name)where T:Control=>phone.FindControl<T>(name)!;
        void Tap(string name)=>Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Until(Func<bool> pred){while(!pred())await Task.Delay(10,ct);}
        try
        {
            Get<TextBox>("Host").Text="https://phone.test/";Get<TextBox>("Secret").Text="reader";Tap("Connect");await Until(()=>Get<ComboBox>("Blocks").SelectedItem!=null);
            Tap("Observe");await Until(()=>Get<Button>("Detach").IsVisible);Tap("FollowZoom");Assert.Equal(1L,await service.Polling.Reader.ReadAsync(ct));
            service.ChangesToShow.Writer.TryWrite((2,service.Right));Assert.Equal(2L,await service.Polling.Reader.ReadAsync(ct));
            Assert.False(Get<Grid>("Chrome").IsVisible);Assert.Equal(2,connections.Opened.Count);Assert.Contains(service.Right.ToString(),connections.Opened[1].Uri.AbsolutePath,StringComparison.Ordinal);
            var same=Get<ScrollViewer>("Viewport").Content;service.ChangesToShow.Writer.TryWrite((3,service.Right));Assert.Equal(3L,await service.Polling.Reader.ReadAsync(ct));Assert.Same(same,Get<ScrollViewer>("Viewport").Content);
            service.ChangesToShow.Writer.TryWrite((4,null));Assert.Equal(4L,await service.Polling.Reader.ReadAsync(ct));Assert.True(Get<Grid>("Chrome").IsVisible);Assert.Equal(3,connections.Opened.Count);Assert.Contains(service.Left.ToString(),connections.Opened[2].Uri.AbsolutePath,StringComparison.Ordinal);
            Assert.All(connections.Opened,c=>{Assert.Contains("role=observer",c.Uri.Query,StringComparison.Ordinal);Assert.Empty(c.Socket.Sent);});Assert.False(Get<StackPanel>("InputPanel").IsVisible);
            Tap("Detach");await Until(()=>Get<ScrollViewer>("Viewport").Content==null&&Equals(Get<Button>("FollowZoom").Content,"Follow"));
        }
        finally{window.Close();await phone.Shutdown.WaitAsync(TimeSpan.FromSeconds(4),ct);}
    }
    private sealed class Sockets:IMultiplexingSocketConnector
    {
        internal List<(Uri Uri, TestWebSocket Socket)> Opened {get;}=[];
        public ValueTask<WebSocket> ConnectAsync(Uri endpoint,string accessToken,CancellationToken ct){ct.ThrowIfCancellationRequested();var socket=new TestWebSocket();Opened.Add((endpoint,socket));socket.Queue(JsonSerializer.Serialize(new{type="attached",attachmentId=Guid.NewGuid(),hasControl=false}));socket.Queue("""{"sequence":1,"kind":"resize","dimensions":{"columns":20,"rows":4,"widthPixels":0,"heightPixels":0}}""");socket.Queue("""{"type":"ready","sequence":1}""");return ValueTask.FromResult<WebSocket>(socket);}
    }
    private sealed class Changes:HttpMessageHandler
    {
        internal readonly Guid Left=Guid.NewGuid(),Right=Guid.NewGuid();private readonly Guid _session=Guid.NewGuid(),_window=Guid.NewGuid();
        internal readonly Channel<(long Revision,Guid? Target)> ChangesToShow=Channel.CreateUnbounded<(long,Guid?)>();internal readonly Channel<long> Polling=Channel.CreateUnbounded<long>();
        private object Snapshot(long revision,Guid? zoom)=>new{id=_session,label="Phone",revision,createdUtc=DateTimeOffset.UnixEpoch,windows=new[]{new{id=_window,label="Main",zoomedBlockId=zoom,layout=new{orientation="Horizontal",ratio=.5,first=new{blockId=Left},second=new{blockId=Right}}}},blocks=new[]{Left,Right}.Select(id=>new{id,sessionId=_session,windowId=_window,label=id==Left?"Left":"Right",state="Running",dimensions=new{columns=20,rows=4,widthPixels=0,heightPixels=0},placement="Tiled"})};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            Assert.Equal("reader",req.Headers.Authorization?.Parameter);var path=req.RequestUri!.AbsolutePath;object result;
            if(req.Method==HttpMethod.Get)result=path.EndsWith("/context",StringComparison.Ordinal)?new{administrator=false,canControl=false}:path.EndsWith("/sessions",StringComparison.Ordinal)?new{sessions=new[]{Snapshot(1,null)}}:Snapshot(1,null);
            else{using var body=JsonDocument.Parse(await req.Content!.ReadAsByteArrayAsync(ct));Assert.Equal("session.wait_change",body.RootElement.GetProperty("method").GetString());Polling.Writer.TryWrite(body.RootElement.GetProperty("arguments").GetProperty("afterRevision").GetInt64());var update=await ChangesToShow.Reader.ReadAsync(ct);result=Snapshot(update.Revision,update.Target);}
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(result),Encoding.UTF8,"application/json")};
        }
    }
}
