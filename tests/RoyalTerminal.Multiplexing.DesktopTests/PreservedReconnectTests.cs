using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Multiplexing.ClientTests;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Multiplexing.Desktop.Services;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class PreservedReconnectTests
{
    [AvaloniaFact]
    public async Task RetriesFromAppliedCursorAndFinishesEscapeSequenceInOriginalParser()
    {
        TestWebSocket first = new(), second = new();
        Begin(first); first.Queue(Output(2,"A\u001b[")); first.Queue(Ready(2));
        second.Queue(Attached()); second.Queue(Output(3,"2;3HZ")); second.Queue(Ready(3));
        SequentialConnector connector = new(first,second);
        RemoteTerminalControl control = new(connector); Window window = new(){ Width=600, Height=300, Content=control }; window.Show();
        await using WorkspaceTerminalController controller = new(control);
        TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StatusChanged += s => { if(s.StartsWith("Reconnected",StringComparison.Ordinal))resumed.TrySetResult(); };
        try
        {
            await controller.AttachAsync(Options(),TestContext.Current.CancellationToken);
            var processor = control.RemoteSession.Processor; var screen = control.Screen!;
            control.SendInput("only-once"); await ((MultiplexingTerminalTransport)control.RemoteSession.Transport!).FlushAsync(TestContext.Current.CancellationToken);
            Assert.Single(first.Sent); first.BreakConnection();
            await resumed.Task.WaitAsync(TimeSpan.FromSeconds(6),TestContext.Current.CancellationToken);
            Assert.Same(processor,control.RemoteSession.Processor); Assert.Same(screen,control.Screen);
            Assert.Equal((int)'A',screen.GetViewportRow(0)[0].Codepoint); Assert.Equal((int)'Z',screen.GetViewportRow(1)[2].Codepoint);
            Assert.Equal(2,connector.Addresses.Count); Assert.Contains("after=2",connector.Addresses[1].Query,StringComparison.Ordinal);
            Assert.Empty(second.Sent); Assert.True(controller.HasControl);
        }
        finally { await controller.DetachAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task OrderlyRemoteDisconnectDoesNotReacquireAnAdministrativelyReleasedLease()
    {
        TestWebSocket first = new(); Begin(first);first.Queue(Ready(1)); SequentialConnector connector=new(first);
        RemoteTerminalControl control = new(connector);Window window = new(){Content=control};window.Show();
        await using WorkspaceTerminalController controller = new(control);
        TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StatusChanged += s=>{if(s.StartsWith("Disconnected",StringComparison.Ordinal))disconnected.TrySetResult();};
        try
        {
            await controller.AttachAsync(Options(),TestContext.Current.CancellationToken);first.RemoteClose();
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(4),TestContext.Current.CancellationToken);
            Assert.Single(connector.Addresses);Assert.False(controller.HasControl);
        }
        finally { await controller.DetachAsync();window.Close(); }
    }

    [AvaloniaFact]
    public async Task HistoryGapOnResumeIsTerminalAndNeverSilentlyRestartsFromZero()
    {
        TestWebSocket first=new(),second=new();Begin(first);first.Queue(Ready(1));
        second.Queue("""{"type":"error","code":"HistoryGap","message":"Expired prefix"}""");
        SequentialConnector connector=new(first,second);RemoteTerminalControl control=new(connector);Window window=new(){Content=control};window.Show();
        await using WorkspaceTerminalController controller = new(control);
        TaskCompletionSource failed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StatusChanged += s=>{if(s.Contains("Expired prefix",StringComparison.Ordinal))failed.TrySetResult();};
        try
        {
            await controller.AttachAsync(Options(),TestContext.Current.CancellationToken);first.BreakConnection();
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken);
            Assert.Equal(2,connector.Addresses.Count);Assert.Contains("after=1",connector.Addresses[1].Query,StringComparison.Ordinal);Assert.False(controller.HasControl);
        }
        finally {await controller.DetachAsync();window.Close();}
    }
    private static void Begin(TestWebSocket socket)
    {
        socket.Queue(Attached());
        socket.Queue("""{"sequence":1,"kind":"resize","dimensions":{"columns":12,"rows":3,"widthPixels":0,"heightPixels":0}}""");
    }
    private static string Attached()=>$$"""{"type":"attached","attachmentId":"{{Guid.NewGuid()}}","hasControl":true}""";
    private static string Ready(long n)=>$$"""{"type":"ready","sequence":{{n}}}""";
    private static string Output(long n,string s)
    {
        using MemoryStream stream=new();
        using(Utf8JsonWriter writer=new(stream))
        {
            writer.WriteStartObject();writer.WriteNumber("sequence",n);writer.WriteString("kind","output");
            writer.WriteBase64String("data",Encoding.UTF8.GetBytes(s));writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static MultiplexingTransportOptions Options()=>new(new Uri("https://example.test/"),"secret",Guid.NewGuid(),Guid.NewGuid(),new(12,3,0,0));
    private sealed class SequentialConnector(params TestWebSocket[] sockets):IMultiplexingSocketConnector
    {
        public List<Uri> Addresses {get;}=[];
        public ValueTask<WebSocket> ConnectAsync(Uri endpoint,string accessToken,CancellationToken token)
        {token.ThrowIfCancellationRequested();int index=Addresses.Count;Addresses.Add(endpoint);return ValueTask.FromResult<WebSocket>(sockets[index]);}
    }
}
