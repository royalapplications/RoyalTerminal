// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Net.Sockets;
using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Raw;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class RawTcpTerminalTransportTests
{
    [Fact]
    public async Task StartAsync_ReceivesAndSendsBytesOverTcp()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        Task serverTask = Task.Run(async () =>
        {
            using TcpClient serverClient = await listener.AcceptTcpClientAsync(cts.Token);
            using NetworkStream stream = serverClient.GetStream();

            await stream.WriteAsync(Encoding.UTF8.GetBytes("WELCOME\n"), cts.Token);
            byte[] buffer = new byte[128];
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
            string received = Encoding.UTF8.GetString(buffer, 0, read);
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"ECHO:{received}"), cts.Token);
        }, cts.Token);

        using RawTcpTerminalTransport transport = new();
        StringBuilder output = new();
        TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        transport.DataReceived += (data, length) =>
        {
            output.Append(Encoding.UTF8.GetString(data, 0, length));
        };
        transport.ProcessExited += code => exited.TrySetResult(code);

        await transport.StartAsync(new RawTcpTransportOptions(
            Host: "127.0.0.1",
            Port: port,
            Dimensions: new TerminalSessionDimensions(80, 24, 640, 480)));

        transport.SendInput("ping"u8);

        await WaitForMarkerAsync(output, "WELCOME", TimeSpan.FromSeconds(3));
        await WaitForMarkerAsync(output, "ECHO:ping", TimeSpan.FromSeconds(3));

        await serverTask;
        _ = await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await transport.StopAsync();
    }

    private static async Task WaitForMarkerAsync(StringBuilder output, string marker, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (output.ToString().Contains(marker, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Marker '{marker}' not found in output '{output}'.");
    }
}
