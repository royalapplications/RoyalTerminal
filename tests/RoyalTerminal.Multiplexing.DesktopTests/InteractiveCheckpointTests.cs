using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Multiplexing.ClientTests;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Multiplexing.Desktop.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class InteractiveCheckpointTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacesVisibleParserAndResumesSplitUtf8WithoutResendingInput(bool reconnect)
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Guid.NewGuid(); var block = Guid.NewGuid();
        var options = new MultiplexingTransportOptions(new("https://example.test/"), "viewer-secret", session, block, new(12,3,0,0));
        using var processor = new BasicVtProcessor(new TerminalScreen(12, 3, 10));
        processor.Process("\u001b[2;3H"u8); processor.Process(new byte[] { 0xe2,0x82 });
        var handler = new CutHandler(session, block, processor.GetBinarySnapshot());
        using var http = new HttpClient(handler);
        var live = new TestWebSocket(); var gap = new TestWebSocket(); var suffix = new TestWebSocket();
        static string Attached() => $$"""{"type":"attached","attachmentId":"{{Guid.NewGuid()}}","hasControl":true}""";
        live.Queue(Attached()); live.Queue("""{"sequence":1,"kind":"resize","dimensions":{"columns":12,"rows":3,"widthPixels":0,"heightPixels":0}}""");
        live.Queue("""{"type":"ready","sequence":1}""");
        gap.Queue("""{"type":"error","code":"HistoryGap","message":"evicted"}""");
        suffix.Queue(Attached()); suffix.Queue("""{"sequence":81,"kind":"output","data":"rA=="}""");
        suffix.Queue("""{"type":"ready","sequence":81}""");
        var connector = new QueueConnector(reconnect ? [live, gap, suffix] : [gap, suffix]);
        var control = new RemoteTerminalControl(connector); var window = new Window { Content = control, Width = 640, Height = 320 }; window.Show();
        await using var controller = new WorkspaceTerminalController(control,http);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StatusChanged += s => { if (s.StartsWith("Reconnected",StringComparison.Ordinal)) recovered.TrySetResult(); };
        try
        {
            await controller.AttachAsync(options,ct);
            if (reconnect)
            {
                control.SendInput("once"); await ((MultiplexingTerminalTransport)control.RemoteSession.Transport!).FlushAsync(ct);
                live.BreakConnection(); await recovered.Task.WaitAsync(TimeSpan.FromSeconds(6),ct); Assert.Single(live.Sent);
            }
            control.FlushPendingTransportOutput();
            Assert.Equal(0x20ac, control.Screen!.GetViewportRow(1)[2].Codepoint);
            Assert.Equal(1,handler.Requests); Assert.Contains("after=80",connector.LastUri!.Query,StringComparison.Ordinal);
            Assert.Empty(suffix.Sent); Assert.True(controller.HasControl);
            control.SendInput("next"); await ((MultiplexingTerminalTransport)control.RemoteSession.Transport!).FlushAsync(ct); Assert.Single(suffix.Sent);
        }
        finally { await controller.DetachAsync(); window.Close(); }
    }
    private sealed class QueueConnector(TestWebSocket[] sockets) : IMultiplexingSocketConnector
    {
        private int _index;
        internal Uri? LastUri { get; private set; }
        public ValueTask<WebSocket> ConnectAsync(Uri endpoint,string token,CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); LastUri = endpoint; return ValueTask.FromResult<WebSocket>(sockets[_index++]); }
    }
    private sealed class CutHandler(Guid session,Guid block,byte[] bytes) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            Requests++; Assert.Equal("Bearer viewer-secret",request.Headers.Authorization?.ToString());
            Assert.Equal($"/api/sessions/{session:D}/blocks/{block:D}/checkpoint",request.RequestUri!.AbsolutePath);
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.royalterminal.ghostty-snapshot-v1");
            r.Headers.Add("X-Workspace-Session",session.ToString("D")); r.Headers.Add("X-Workspace-Block",block.ToString("D")); r.Headers.Add("X-Workspace-Sequence","80");
            return Task.FromResult(r);
        }
    }
}
