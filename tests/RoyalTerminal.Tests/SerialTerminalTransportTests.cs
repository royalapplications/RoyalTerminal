// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Serial;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SerialTerminalTransportTests
{
    [Fact]
    public void Provider_ReportsSupportForSerialOptions()
    {
        SerialTerminalTransportProvider provider = new();
        SerialTransportOptions options = new(
            PortName: "COM1",
            BaudRate: 9600,
            DataBits: 8,
            Parity: TerminalSerialParity.None,
            StopBits: TerminalSerialStopBits.One,
            Handshake: TerminalSerialHandshake.None,
            Dimensions: new TerminalSessionDimensions(80, 24, 640, 480));

        Assert.Equal(TerminalTransportIds.Serial, provider.TransportId);
        Assert.True(provider.CanHandle(options));
        Assert.IsType<SerialTerminalTransport>(provider.Create());
    }

    [Fact]
    public async Task StartAsync_WithEmptyPortName_Throws()
    {
        using SerialTerminalTransport transport = new();
        SerialTransportOptions options = new(
            PortName: "   ",
            BaudRate: 9600,
            DataBits: 8,
            Parity: TerminalSerialParity.None,
            StopBits: TerminalSerialStopBits.One,
            Handshake: TerminalSerialHandshake.None,
            Dimensions: new TerminalSessionDimensions(80, 24, 640, 480));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.StartAsync(options));
    }
}
