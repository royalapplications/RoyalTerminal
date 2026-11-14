// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pipe;
using Xunit;

namespace RoyalTerminal.Multiplexing.Tests;

public sealed class MultiplexerProcessIntegrationTests
{
    [Fact]
    public async Task RealChildProcessRemainsInteractiveAfterClientDetachment()
    {
        CompositeTerminalTransportFactory factory = new([new PipeTerminalTransportProvider()]);
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("process");
        TerminalCommandSpec command = OperatingSystem.IsWindows()
            ? new("cmd.exe", ["/d", "/q"])
            : new("/bin/sh", ["-c", "while IFS= read -r line; do printf 'reply:%s\\n' \"$line\"; done"]);
        PipeTransportOptions options = new(command, null, null, true, new(80, 24, 800, 480));
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "real shell", options);
        using (MultiplexerAttachment first = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller))
        {
            first.SendInput(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? "echo first-marker\r\n" : "first-marker\n"));
            await ReadUntilAsync(first, "first-marker");
        }
        Assert.Equal(MultiplexerBlockState.Running, broker.GetSession(session.Id).Blocks[0].State);
        using MultiplexerAttachment second = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        second.SendInput(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? "echo second-marker\r\n" : "second-marker\n"));
        await ReadUntilAsync(second, "second-marker");
        await broker.CloseBlockAsync(session.Id, block.Id);
        Assert.Empty(broker.GetSession(session.Id).Blocks);
    }

    private static async Task ReadUntilAsync(MultiplexerAttachment attachment, string marker)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        StringBuilder output = new();
        while (await attachment.Events.WaitToReadAsync(timeout.Token))
        {
            while (attachment.Events.TryRead(out MultiplexerEvent? item))
            {
                if (item.Data is { } data) output.Append(Encoding.UTF8.GetString(data));
                if (output.ToString().Contains(marker, StringComparison.Ordinal)) return;
            }
        }
        Assert.Fail($"Process stream ended before marker '{marker}'. Output: {output}");
    }
}
