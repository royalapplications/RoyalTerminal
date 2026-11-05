using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class OrderlyDetachTests
{
    [Fact]
    public async Task StopWaitsForPeerReleaseWithoutSendingInputOrReportingProcessExit()
    {
        var socket = new TestWebSocket { HoldCloseReply = true };
        socket.Queue($$"""{"type":"attached","attachmentId":"{{Guid.NewGuid():D}}","hasControl":true}""");
        socket.Queue("""{"sequence":1,"kind":"resize","dimensions":{"columns":80,"rows":24,"widthPixels":0,"heightPixels":0}}""");
        socket.Queue("""{"type":"ready","sequence":1}""");
        await using var client = new MultiplexingTerminalTransport(new TestSocketConnector(socket));
        int exits = 0; client.ProcessExited += _ => exits++;
        await client.StartAsync(new MultiplexingTransportOptions(new("https://example.test/"), "token", Guid.NewGuid(), Guid.NewGuid(), new(80,24,0,0)));
        var stop = client.StopAsync().AsTask();
        await socket.CloseSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stop.IsCompleted); Assert.False(client.HasControl); Assert.Empty(socket.Sent);
        socket.RemoteClose();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, exits); Assert.True(socket.Disposed);
    }
}
