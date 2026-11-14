using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using RoyalTerminal.Multiplexing.Server;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class StreamCloseTests
{
    [Fact]
    public async Task ObserverReceivesChildExitAndNormalCloseWithoutSendingInput()
    {
        const string token = "raw-websocket-close-test-0123456789abcdef";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var host = ServerHost.Build(["--Logging:LogLevel:Default=None"], administratorToken: token, listenUrl: "http://127.0.0.1:0");
        await host.StartAsync(deadline.Token);
        try
        {
            var broker = host.Services.GetRequiredService<MultiplexerBroker>();
            var operations = host.Services.GetRequiredService<WorkspaceOperations>();
            var session = broker.CreateSession("exit-fence");
            TerminalCommandSpec command = OperatingSystem.IsWindows() ? new("cmd.exe", ["/d", "/c", "exit 37"]) : new("/bin/sh", ["-c", "exit 37"]);
            var block = await operations.CreateBlockAsync(session.Id, new("exit", "pipe", command), deadline.Token);
            string address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var uri = new UriBuilder(address) { Scheme = "ws", Path = $"/api/sessions/{session.Id}/blocks/{block.Id}/stream", Query = "role=observer" };
            using var socket = new ClientWebSocket(); socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            await socket.ConnectAsync(uri.Uri, deadline.Token);
            var bytes = new byte[4096]; bool ready = false, exit = false;
            while (true)
            {
                using var frame = new MemoryStream(); WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), deadline.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    frame.Write(bytes, 0, result.Count);
                } while (!result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Assert.Equal(WebSocketCloseStatus.NormalClosure, result.CloseStatus); Assert.True(ready); Assert.True(exit);
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ack", deadline.Token); break;
                }
                using var doc = JsonDocument.Parse(frame.ToArray()); var obj = doc.RootElement;
                if (obj.TryGetProperty("type", out var type) && type.GetString() == "ready") ready = true;
                if (obj.TryGetProperty("kind", out var kind) && kind.GetString() == "exit") { Assert.Equal(37, obj.GetProperty("exitCode").GetInt32()); exit = true; }
            }
        }
        finally { await host.StopAsync(); }
    }
}
