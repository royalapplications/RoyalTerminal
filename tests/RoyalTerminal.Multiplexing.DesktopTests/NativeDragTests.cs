using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RoyalTerminal.Multiplexing.ClientTests;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;
namespace RoyalTerminal.Multiplexing.DesktopTests;
public sealed class NativeDragTests
{
    [AvaloniaTheory][InlineData(true)][InlineData(false)]
    public async Task NativePointerDropIsOneRevisionCheckedMutationAndNeverSendsTerminalInput(bool administrator)
    {
        Guid session=Guid.NewGuid(),wid=Guid.NewGuid(),a=Guid.NewGuid(),b=Guid.NewGuid();
        MultiplexerBlockDescriptor Block(Guid id)=>new(id,session,wid,"shell",MultiplexerBlockState.Running,new(20,5,0,0),null,1,1,0,null);
        var layout=MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Horizontal,MultiplexerLayoutNode.Leaf(a),MultiplexerLayoutNode.Leaf(b));
        var state=new MultiplexerSessionDescriptor(session,"work",7,DateTimeOffset.UnixEpoch,[new(wid,"main",layout)],[Block(a),Block(b)]);
        var api=new Host(state,administrator);var sockets=new Streams();var win=new OverviewWindow(api,sockets);win.Connect(new("https://example.test/"),"secret",session);win.Show();
        void Frame(){win.UpdateLayout();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
        try
        {
            var ct=TestContext.Current.CancellationToken;await api.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(8),ct);
            var surface=win.FindControl<OverviewPanel>("Surface")!;var button=win.FindControl<Button>("EditToggle")!;
            var hint=win.FindControl<TextBlock>("EditHint")!;var layer=win.FindControl<Border>("GestureLayer")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));for(int i=0;i<100&&!button.IsEnabled;i++)await Task.Delay(10,ct);Frame();
            Assert.Equal(administrator,layer.IsVisible);Assert.True(surface.Bounds.Width>50&&surface.Bounds.Height>50,$"Missing layout: {surface.Bounds}");
            Point At(double x)=>surface.TranslatePoint(new Point(surface.Bounds.Width*x,surface.Bounds.Height*.5),win)!.Value;
            var from=At(.24);var to=At(.76);win.MouseMove(from);win.MouseDown(from,MouseButton.Left,RawInputModifiers.LeftMouseButton);Frame();
            if(administrator)Assert.StartsWith("Dragging",hint.Text);
            win.MouseMove(to,RawInputModifiers.LeftMouseButton);Frame();if(administrator)Assert.Contains("Swap",hint.Text);
            win.MouseUp(to,MouseButton.Left,RawInputModifiers.None);Frame();
            if(administrator){var args=await api.Drop.Task.WaitAsync(TimeSpan.FromSeconds(5),ct);Assert.Equal(a,args.GetProperty("firstId").GetGuid());Assert.Equal(b,args.GetProperty("secondId").GetGuid());Assert.Equal(7,args.GetProperty("expectedRevision").GetInt64());Assert.Equal(1,api.Mutations);}else Assert.Equal(0,api.Mutations);
            Assert.Equal(2,sockets.All.Count);Assert.All(sockets.All,s=>Assert.Empty(s.Sent));
        }
        finally{win.Close();await win.Shutdown.WaitAsync(TimeSpan.FromSeconds(8),TestContext.Current.CancellationToken);}
    }
    private sealed class Streams:IMultiplexingSocketConnector
    {
        internal List<TestWebSocket> All{get;}=[];
        public ValueTask<WebSocket> ConnectAsync(Uri uri,string token,CancellationToken ct){Assert.Contains("role=observer",uri.Query,StringComparison.Ordinal);var sock=new TestWebSocket();sock.Queue($$"""{"type":"attached","attachmentId":"{{Guid.NewGuid()}}","hasControl":false}""");sock.Queue("""{"sequence":1,"kind":"resize","dimensions":{"columns":20,"rows":5,"widthPixels":0,"heightPixels":0}}""");sock.Queue("""{"type":"ready","sequence":1}""");All.Add(sock);return ValueTask.FromResult<WebSocket>(sock);}
    }
    private sealed class Host(MultiplexerSessionDescriptor snapshot,bool admin):HttpMessageHandler
    {
        internal TaskCompletionSource Waiting{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously); internal TaskCompletionSource<JsonElement> Drop{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);internal int Mutations;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            if(r.RequestUri!.AbsolutePath=="/api/context")return new(HttpStatusCode.OK){Content=new StringContent(admin?"{\"administrator\":true}":"{\"administrator\":false}")};
            if(r.Method==HttpMethod.Get)return new(HttpStatusCode.OK){Content=new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(snapshot,OverviewJson.Default.MultiplexerSessionDescriptor))};
            using var doc=JsonDocument.Parse(await r.Content!.ReadAsByteArrayAsync(ct));var verb=doc.RootElement.GetProperty("method").GetString();if(verb=="session.wait_change"){Waiting.TrySetResult();await Task.Delay(Timeout.InfiniteTimeSpan,ct);}
            Assert.Equal("block.swap",verb);Interlocked.Increment(ref Mutations);Drop.TrySetResult(doc.RootElement.GetProperty("arguments").Clone());return new(HttpStatusCode.OK){Content=new StringContent("{}")};
        }
    }
}
