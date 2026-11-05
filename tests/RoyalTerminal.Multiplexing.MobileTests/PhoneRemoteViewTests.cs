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
public sealed class PhoneRemoteViewTests
{
    [AvaloniaFact]
    public async Task NavigateAcrossSplitAndEmptyTabAndRestoreFocusModeWithoutPtyInput()
    {
        using var ct=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);ct.CancelAfter(TimeSpan.FromSeconds(12));
        var api=new Host();var sockets=new Connections();var phone=new MobileWorkspaceView(api,sockets);var window=new Window{Content=phone,Width=390,Height=820};window.Show();
        T Get<T>(string name)where T:Control=>phone.FindControl<T>(name)!;
        void Tap(string name)=>Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Until(Func<bool> predicate){while(!predicate())await Task.Delay(10,ct.Token);}
        async Task Send(string name,string? direction=null,int applied=1){api.Commands.Writer.TryWrite(new{name,direction});Assert.Equal(applied,await api.Reports.Reader.ReadAsync(ct.Token));}
        try{
            Get<TextBox>("Host").Text="https://phone.test/";Get<TextBox>("Secret").Text="private";Tap("Connect");await Until(()=>Get<ComboBox>("Blocks").SelectedItem is not null);
            Tap("Observe");await Until(()=>Get<Button>("Detach").IsVisible);Tap("AllowFocus");
            await Send("pane.focus","right");Assert.Equal(2,sockets.Endpoints.Count);Assert.Contains(api.B.ToString(),sockets.Endpoints[1].AbsolutePath,StringComparison.Ordinal);
            var original=Get<ScrollViewer>("Viewport").Content;await Send("pane.zoom");Assert.False(Get<Grid>("Chrome").IsVisible);Assert.True(Get<Button>("Detach").IsVisible);Assert.Equal("Restore",Get<Button>("Zoom").Content);
            await Send("pane.focus","right",0);Assert.Same(original,Get<ScrollViewer>("Viewport").Content);Tap("Zoom");Assert.True(Get<Grid>("Chrome").IsVisible);
            await Send("client.tab.next");Assert.Null(Get<ScrollViewer>("Viewport").Content);await Send("client.tab.previous");Assert.Equal(3,sockets.Endpoints.Count);
            Assert.Contains(api.A.ToString(),sockets.Endpoints[2].AbsolutePath,StringComparison.Ordinal);Assert.All(sockets.Endpoints,u=>Assert.Contains("role=observer",u.Query,StringComparison.Ordinal));
            Assert.All(sockets.Sockets,x=>Assert.Empty(x.Sent));Assert.False(Get<StackPanel>("InputPanel").IsVisible);
        }finally{window.Close();await phone.Shutdown.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken);}
    }
    private sealed class Connections:IMultiplexingSocketConnector
    {
        internal List<Uri> Endpoints {get;}=[];internal List<TestWebSocket> Sockets {get;}=[];
        public ValueTask<WebSocket> ConnectAsync(Uri endpoint,string accessToken,CancellationToken ct){
            ct.ThrowIfCancellationRequested();Endpoints.Add(endpoint);var s=new TestWebSocket();Sockets.Add(s);
            s.Queue(JsonSerializer.Serialize(new{type="attached",attachmentId=Guid.NewGuid(),hasControl=false}));s.Queue("""{"sequence":1,"kind":"resize","dimensions":{"columns":20,"rows":4,"widthPixels":0,"heightPixels":0}}""");s.Queue("""{"type":"ready","sequence":1}""");return ValueTask.FromResult<WebSocket>(s);
        }
    }
    private sealed class Host:HttpMessageHandler
    {
        internal readonly Guid A=Guid.NewGuid(),B=Guid.NewGuid();private readonly Guid _session=Guid.NewGuid(),_window=Guid.NewGuid(),_empty=Guid.NewGuid(),_client=Guid.NewGuid();
        internal readonly Channel<object> Commands=Channel.CreateUnbounded<object>();internal readonly Channel<int> Reports=Channel.CreateUnbounded<int>();private long _sequence;
        private object Block(Guid id)=>new{id,sessionId=_session,windowId=_window,label=id==A?"Left":"Right",state="Running",dimensions=new{columns=20,rows=4,widthPixels=0,heightPixels=0},placement="Tiled"};
        private object Snapshot()=>new{id=_session,label="Build",revision=1,createdUtc=DateTimeOffset.UnixEpoch,blocks=new[]{Block(A),Block(B)},windows=new object[]{new{id=_window,label="Code",layout=new{orientation="Horizontal",ratio=0.5,first=new{blockId=A},second=new{blockId=B}}},new{id=_empty,label="Empty",layout=(object?)null}}};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal("private",request.Headers.Authorization?.Parameter);object response;
            if(request.Method==HttpMethod.Get){var path=request.RequestUri!.AbsolutePath;response=path.EndsWith("/context",StringComparison.Ordinal)?new{canControl=false}:path.EndsWith("/sessions",StringComparison.Ordinal)?new{sessions=new[]{Snapshot()}}:Snapshot();}
            else{using var body=JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(ct));var args=body.RootElement.GetProperty("arguments");
                switch(body.RootElement.GetProperty("method").GetString()){
                    case "client.negotiate_view":Assert.Equal("mobile",args.GetProperty("kind").GetString());response=new{view=new{id=_client,sessionId=_session,kind="mobile",navigationVersion=3},readerKey=new string('a',64),completionProtocol=1};break;
                    case "client.read_view":var action=await Commands.Reader.ReadAsync(ct);response=new{cursor=++_sequence,batches=new[]{new{sequence=_sequence,actions=new[]{action}}}};break;
                    case "client.finish_view":Reports.Writer.TryWrite(args.GetProperty("applied").GetInt32());response=new{ok=true};break;
                    case "client.close_view":response=new{ok=true};break;
                    default:throw new InvalidOperationException("Unexpected RPC");
                }}
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(response),Encoding.UTF8,"application/json")};
        }
    }
}
