using System.Text;
using System.Text.Json;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class MultiplexingTransportTests
{
    private static MultiplexingTransportOptions Options(bool observer = false) => new(
        new Uri("https://terminal.example.test/mux/"), "secret-token", Guid.NewGuid(), Guid.NewGuid(), new(80, 24, 0, 0))
    { Observer = observer, HandshakeTimeout = TimeSpan.FromSeconds(2) };

    private static string Attached(bool control = true)
        => $$"""{"type":"attached","attachmentId":"{{Guid.NewGuid():D}}","hasControl":{{(control ? "true" : "false")}}} """;

    private static string Resize(long sequence, int columns = 80)
        => $$$"""{"sequence":{{{sequence}}},"kind":"resize","dimensions":{"columns":{{{columns}}},"rows":24,"widthPixels":0,"heightPixels":0}}""";

    private static string Output(long sequence, byte[] data)
        => $$"""{"sequence":{{sequence}},"kind":"output","data":"{{Convert.ToBase64String(data)}}"}""";

    private static string Ready(long sequence) => $$"""{"type":"ready","sequence":{{sequence}}}""";

    [Fact]
    public async Task ReplayPreservesByteAndGeometryOrderBeforeReady()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached());
        socket.Queue(Resize(1));
        socket.Queue(Output(2, [0xe2, 0x82]));
        socket.Queue(Resize(3, 120));
        socket.Queue(Output(4, [0xac, 0x1b, 0x5b, 0x32, 0x4a]));
        socket.Queue(Ready(4));
        TestSocketConnector connector = new(socket);
        await using MultiplexingTerminalTransport transport = new(connector);
        List<string> applied = [];
        transport.AuthoritativeDimensionsChanged += d => applied.Add("resize:" + d.Columns);
        transport.DataReceived += (data, count) => applied.Add("bytes:" + Convert.ToHexString(data.AsSpan(0, count)));

        await transport.StartAsync(Options());

        Assert.Equal(["resize:80", "bytes:E282", "resize:120", "bytes:AC1B5B324A"], applied);
        Assert.True(transport.IsRunning);
        Assert.True(transport.HasControl);
        Assert.Equal(4, transport.LastSequence);
        Assert.StartsWith("wss://terminal.example.test/mux/api/sessions/", connector.Endpoint!.AbsoluteUri);
        Assert.EndsWith("?after=0&role=controller", connector.Endpoint.AbsoluteUri);
        Assert.DoesNotContain("secret-token", connector.Endpoint.AbsoluteUri);
        Assert.Equal("secret-token", connector.Token);
        Assert.Empty(socket.Sent);
    }

    [Fact]
    public async Task SendAndResizeUseOneOrderedWriterAndFlush()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached()); socket.Queue(Resize(1)); socket.Queue(Ready(1));
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(socket));
        await transport.StartAsync(Options());

        transport.SendInput([0, 0xff, 13, 10]);
        transport.Resize(new(100, 32, 800, 640));
        await transport.FlushAsync();

        Assert.Equal(2, socket.Sent.Count);
        using JsonDocument input = JsonDocument.Parse(socket.Sent[0]);
        using JsonDocument resize = JsonDocument.Parse(socket.Sent[1]);
        Assert.Equal("input", input.RootElement.GetProperty("type").GetString());
        Assert.Equal([0, 0xff, 13, 10], Convert.FromBase64String(input.RootElement.GetProperty("data").GetString()!));
        Assert.Equal("resize", resize.RootElement.GetProperty("type").GetString());
        Assert.Equal(100, resize.RootElement.GetProperty("columns").GetInt32());
        Assert.Equal(640, resize.RootElement.GetProperty("heightPixels").GetInt32());
    }

    [Fact]
    public async Task ObserverCannotMutateAndDetachDoesNotReportProcessExit()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached(false)); socket.Queue(Resize(1)); socket.Queue(Ready(1));
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(socket));
        int exits = 0;
        transport.ProcessExited += _ => exits++;
        await transport.StartAsync(Options(true));
        Assert.False(transport.HasControl);
        Assert.Throws<InvalidOperationException>(() => transport.SendInput([1]));
        Assert.Throws<InvalidOperationException>(() => transport.Resize(new(90, 24, 0, 0)));
        await transport.StopAsync();
        await transport.StopAsync();
        Assert.Equal(0, exits);
        Assert.Empty(socket.Sent);
        Assert.True(socket.Disposed);
        Assert.False(transport.IsRunning);
    }

    [Fact]
    public async Task SequenceGapFailsBeforeApplyingLaterOutput()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached()); socket.Queue(Resize(1)); socket.Queue(Output(3, [65]));
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(socket));
        bool output = false;
        transport.DataReceived += (_, _) => output = true;
        MultiplexingProtocolException error = await Assert.ThrowsAsync<MultiplexingProtocolException>(
            async () => await transport.StartAsync(Options()));
        Assert.Equal("SequenceGap", error.Code);
        Assert.False(output);
        Assert.True(socket.Disposed);
    }

    [Fact]
    public async Task RetentionGapIsPropagatedWithoutFalseProcessExit()
    {
        TestWebSocket socket = new();
        socket.Queue("""{"type":"error","code":"HistoryGap","message":"The requested cursor was evicted."}""");
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(socket));
        int exits = 0;
        transport.ProcessExited += _ => exits++;
        MultiplexingProtocolException error = await Assert.ThrowsAsync<MultiplexingProtocolException>(
            async () => await transport.StartAsync(Options()));
        Assert.Equal("HistoryGap", error.Code);
        Assert.Equal(0, exits);
    }

    [Fact]
    public async Task ResumeUsesPreservedCursorAndSuppressesDuplicateEvents()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached()); socket.Queue(Output(9, [1])); socket.Queue(Output(10, [2])); socket.Queue(Ready(10));
        TestSocketConnector connector = new(socket);
        await using MultiplexingTerminalTransport transport = new(connector);
        List<byte> bytes = [];
        transport.DataReceived += (data, _) => bytes.AddRange(data);
        await transport.StartAsync(Options() with { AfterSequence = 9 });
        Assert.Equal([2], bytes);
        Assert.Contains("after=9", connector.Endpoint!.Query);
        Assert.Equal(10, transport.LastSequence);
    }

    [Fact]
    public async Task FragmentedMessagesAreReassembledAndSizeBounded()
    {
        TestWebSocket socket = new();
        string attached = Attached();
        socket.QueueFragment(attached[..10], false);
        socket.QueueFragment(attached[10..], true);
        socket.Queue(Resize(1)); socket.Queue(Ready(1));
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(socket));
        await transport.StartAsync(Options());
        Assert.True(transport.IsRunning);

        TestWebSocket oversized = new();
        oversized.QueueFragment(new string(' ', 800), false);
        oversized.QueueFragment(new string(' ', 800), true);
        await using MultiplexingTerminalTransport rejected = new(new TestSocketConnector(oversized));
        MultiplexingProtocolException error = await Assert.ThrowsAsync<MultiplexingProtocolException>(
            async () => await rejected.StartAsync(Options() with { MaximumMessageBytes = 1024 }));
        Assert.Equal("InvalidMessage", error.Code);
    }

    [Fact]
    public async Task SocketLossHasIndependentSignalAndLeavesResumeCursor()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached()); socket.Queue(Resize(1)); socket.Queue(Ready(1));
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(socket));
        TaskCompletionSource lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int exits = 0;
        transport.ConnectionLost += _ => lost.TrySetResult();
        transport.ProcessExited += _ => exits++;
        await transport.StartAsync(Options());
        socket.RemoteClose();
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(transport.IsRunning);
        Assert.Equal(1, transport.LastSequence);
        Assert.Equal(0, exits);
    }

    [Fact]
    public async Task ActualExitIsRaisedOnceAndReplayFenceIsVerified()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached()); socket.Queue(Resize(1));
        socket.Queue("""{"sequence":2,"kind":"exit","exitCode":7}""");
        socket.Queue("""{"sequence":3,"kind":"exit","exitCode":9}""");
        socket.Queue(Ready(3));
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(socket));
        List<int> exits = [];
        transport.ProcessExited += exits.Add;
        await transport.StartAsync(Options());
        Assert.Equal([7], exits);

        TestWebSocket badFence = new();
        badFence.Queue(Attached()); badFence.Queue(Resize(1)); badFence.Queue(Ready(2));
        await using MultiplexingTerminalTransport rejected = new(new TestSocketConnector(badFence));
        MultiplexingProtocolException error = await Assert.ThrowsAsync<MultiplexingProtocolException>(
            async () => await rejected.StartAsync(Options()));
        Assert.Equal("SequenceGap", error.Code);
    }

    [Fact]
    public async Task InputBudgetAndQueueOverflowAreExplicit()
    {
        TestWebSocket socket = new() { SendGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        socket.Queue(Attached()); socket.Queue(Resize(1)); socket.Queue(Ready(1));
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(socket));
        await transport.StartAsync(Options() with { OutboundQueueCapacity = 1, MaximumInputBytes = 4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => transport.SendInput([1, 2, 3, 4, 5]));
        bool overflowed = false;
        for (int i = 0; i < 4; i++)
        {
            try { transport.SendInput([1]); }
            catch (IOException) { overflowed = true; break; }
        }
        Assert.True(overflowed);
    }

    [Fact]
    public async Task OptionsRejectUntrustedUriShapesAndDoNotLogCredentials()
    {
        MultiplexingTransportOptions options = Options();
        Assert.DoesNotContain(options.AccessToken, options.ToString());
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(new TestWebSocket()));
        await Assert.ThrowsAsync<ArgumentException>(async () => await transport.StartAsync(options with
        { Endpoint = new Uri("https://user:password@example.test/") }));
        await Assert.ThrowsAsync<ArgumentException>(async () => await transport.StartAsync(options with { AfterSequence = -1 }));
        await Assert.ThrowsAsync<ArgumentException>(async () => await transport.StartAsync(new RawTcpTransportOptions("localhost", 80, new(80, 24, 0, 0))));
    }

    [Fact]
    public void ProviderOnlyAcceptsMultiplexingOptions()
    {
        MultiplexingTerminalTransportProvider provider = new();
        Assert.Equal(MultiplexingTransportOptions.Identifier, provider.TransportId);
        Assert.True(provider.CanHandle(Options()));
        Assert.False(provider.CanHandle(new RawTcpTransportOptions("localhost", 80, new(80, 24, 0, 0))));
        using ITerminalTransport transport = provider.Create();
        Assert.IsType<MultiplexingTerminalTransport>(transport);
    }

    [Fact]
    public async Task CancelledHandshakeDisposesSocketWithoutProcessExit()
    {
        TestWebSocket socket = new();
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(socket));
        int exits = 0;
        transport.ProcessExited += _ => exits++;
        using CancellationTokenSource cancellation = new();
        Task start = transport.StartAsync(Options(), cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.True(socket.Disposed);
        Assert.Equal(0, exits);
        Assert.False(transport.IsRunning);
    }

    [Fact]
    public async Task FreshReplayRejectsOutputWithoutInitialGeometry()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached()); socket.Queue(Output(1, [65])); socket.Queue(Ready(1));
        await using MultiplexingTerminalTransport transport = new(new TestSocketConnector(socket));
        MultiplexingProtocolException error = await Assert.ThrowsAsync<MultiplexingProtocolException>(
            async () => await transport.StartAsync(Options()));
        Assert.Equal("MissingGeometry", error.Code);
    }
}
