// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Net.Sockets;
using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Telnet;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TelnetTerminalTransportTests
{
    private const byte Iac = 255;
    private const byte Will = 251;
    private const byte Do = 253;
    private const byte EchoOption = 1;

    [Fact]
    public async Task StartAsync_StripsNegotiationBytes_FromOutputStream()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        Task<byte[]> serverTask = Task.Run(async () =>
        {
            using TcpClient serverClient = await listener.AcceptTcpClientAsync(cts.Token);
            using NetworkStream stream = serverClient.GetStream();

            byte[] payload =
            [
                Iac, Will, EchoOption,
                (byte)'h', (byte)'i', (byte)'\n',
            ];
            await stream.WriteAsync(payload, cts.Token);

            byte[] response = new byte[16];
            int read = await stream.ReadAsync(response.AsMemory(0, response.Length), cts.Token);
            return response.AsSpan(0, read).ToArray();
        }, cts.Token);

        using TelnetTerminalTransport transport = new();
        StringBuilder output = new();
        TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        transport.DataReceived += (data, length) =>
        {
            output.Append(Encoding.UTF8.GetString(data, 0, length));
        };
        transport.ProcessExited += code => exited.TrySetResult(code);

        await transport.StartAsync(new TelnetTransportOptions(
            Host: "127.0.0.1",
            Port: port,
            TerminalType: "xterm",
            Dimensions: new TerminalSessionDimensions(80, 24, 640, 480)));

        await WaitForMarkerAsync(output, "hi", TimeSpan.FromSeconds(3));
        byte[] responseBytes = await serverTask;

        Assert.True(
            responseBytes.Length >= 3 &&
            responseBytes[0] == Iac &&
            responseBytes[1] == Do &&
            responseBytes[2] == EchoOption);

        _ = await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await transport.StopAsync();
    }

    [Fact]
    public async Task SendInput_EscapesIacByte()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        Task<byte[]> serverTask = Task.Run(async () =>
        {
            using TcpClient serverClient = await listener.AcceptTcpClientAsync(cts.Token);
            using NetworkStream stream = serverClient.GetStream();
            byte[] buffer = new byte[8];
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
            return buffer.AsSpan(0, read).ToArray();
        }, cts.Token);

        using TelnetTerminalTransport transport = new();
        await transport.StartAsync(new TelnetTransportOptions(
            Host: "127.0.0.1",
            Port: port,
            TerminalType: "xterm",
            Dimensions: new TerminalSessionDimensions(80, 24, 640, 480)));

        transport.SendInput(new byte[] { Iac });
        byte[] bytes = await serverTask;

        Assert.Equal(2, bytes.Length);
        Assert.Equal(Iac, bytes[0]);
        Assert.Equal(Iac, bytes[1]);

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
