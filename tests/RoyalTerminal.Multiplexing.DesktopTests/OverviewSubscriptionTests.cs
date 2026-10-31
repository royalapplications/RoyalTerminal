using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Multiplexing.ClientTests;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class OverviewSubscriptionTests
{
    [AvaloniaFact]
    public async Task TwoObserversReceiveDistinctStreamsAndRevocationClosesBoth()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid session = Guid.NewGuid(), window = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();
        MultiplexerBlockDescriptor Block(Guid id) => new(id, session, window, "Shell", MultiplexerBlockState.Running, new(16, 4, 0, 0), null, 2, 1, 0, null);
        var split = MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Horizontal, MultiplexerLayoutNode.Leaf(a), MultiplexerLayoutNode.Leaf(b));
        var state = new MultiplexerSessionDescriptor(session, "Research", 1, DateTimeOffset.UnixEpoch, [new(window, "Main", split)], [Block(a), Block(b)]);
        var api = new Management(state); var sockets = new Observers();
        var overview = new OverviewWindow(api, sockets);
        overview.Connect(new("https://work.test/"), "scoped-test", session);
        overview.Show();
        try
        {
            await api.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(8), ct);
            await sockets.Both.Task.WaitAsync(TimeSpan.FromSeconds(8), ct);
            Assert.Equal(2, overview.FindControl<OverviewPanel>("Surface")!.Children.Count);
            Assert.Equal(new[] { a, b }.Order(), sockets.Streams.Keys.Order());
            Assert.All(sockets.Addresses, u => Assert.Contains("role=observer", u.Query, StringComparison.Ordinal));
            Assert.All(sockets.Streams.Values, s => Assert.Empty(s.Sent));
            api.Revoke.TrySetResult();
            await Task.WhenAll(sockets.Streams.Values.Select(s => s.CloseSent.Task)).WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Empty(overview.FindControl<OverviewPanel>("Surface")!.Children);
            Assert.Null(overview.FindControl<ComboBox>("WindowsList")!.ItemsSource);
        }
        finally { overview.Close(); await overview.Shutdown.WaitAsync(TimeSpan.FromSeconds(10), ct); }
        Assert.All(sockets.Streams.Values, s => Assert.True(s.Disposed));
    }

    private sealed class Management(MultiplexerSessionDescriptor session) : HttpMessageHandler
    {
        internal TaskCompletionSource PollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Revoke { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Assert.Equal("scoped-test", r.Headers.Authorization?.Parameter);
            if (r.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(session, OverviewJson.Default.MultiplexerSessionDescriptor)) };
            using var p = JsonDocument.Parse(await r.Content!.ReadAsByteArrayAsync(ct));
            Assert.Equal("session.wait_change", p.RootElement.GetProperty("method").GetString());
            PollStarted.TrySetResult(); await Revoke.Task.WaitAsync(ct);
            return new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"code\":\"expired\"}") };
        }
    }

    private sealed class Observers : IMultiplexingSocketConnector
    {
        internal Dictionary<Guid,TestWebSocket> Streams { get; } = [];
        internal List<Uri> Addresses { get; } = [];
        internal TaskCompletionSource Both { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<WebSocket> ConnectAsync(Uri endpoint, string accessToken, CancellationToken ct)
        {
            Assert.Equal("scoped-test", accessToken); Guid block = Guid.Parse(endpoint.Segments[^2].TrimEnd('/'));
            var s = new TestWebSocket(); s.Queue($$"""{"type":"attached","attachmentId":"{{Guid.NewGuid()}}","hasControl":false}""");
            s.Queue("""{"sequence":1,"kind":"resize","dimensions":{"columns":16,"rows":4,"widthPixels":0,"heightPixels":0}}""");
            s.Queue("""{"sequence":2,"kind":"output","data":"b2s="}""");s.Queue("""{"type":"ready","sequence":2}""");
            lock(Streams) { Streams.Add(block, s); Addresses.Add(endpoint); if (Streams.Count == 2) Both.TrySetResult(); }
            return ValueTask.FromResult<WebSocket>(s);
        }
    }
}
