// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pipe;
using Xunit;

namespace RoyalTerminal.Multiplexing.Tests;

public sealed class LaunchIdentityTests
{
    [Fact]
    public async Task ARealProcessReceivesItsOwnIdentitiesButNotTheHostBearer()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
        var factory = new CompositeTerminalTransportFactory([new PipeTerminalTransportProvider()]);
        await using var broker = new MultiplexerBroker(factory);
        var session = broker.CreateSession("identity");
        var window = broker.CreateWindow(session.Id, "destination");
        var original = new Dictionary<string, string>
        {
            ["ROYALTERMINAL_MUX_BLOCK"] = "spoofed",
            ["ROYALTERMINAL_MUX_TOKEN"] = "not-a-credential",
        };
        // Keep the process alive until the reader has seen its output. Startup and pipe exit
        // are independently scheduled, so this test does not rely on fast-child exit ordering.
        TerminalCommandSpec command = OperatingSystem.IsWindows()
            ? new("cmd.exe", ["/d", "/q"])
            : new("/bin/sh", ["-c", "env | grep '^ROYALTERMINAL_MUX_'; echo IDENTITY_END; read -r line"]);
        var block = await broker.AddBlockAsync(session.Id, window.Id, "identity",
            new PipeTransportOptions(command, null, original, true, new(100, 12, 0, 0)), deadline.Token);
        using var attachment = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        if (OperatingSystem.IsWindows()) attachment.SendInput("set ROYALTERMINAL_MUX_\r\necho IDENTITY_END\r\n"u8);
        var output = new StringBuilder();
        while (await attachment.Events.WaitToReadAsync(deadline.Token))
        {
            while (attachment.Events.TryRead(out var item))
                if (item.Data is { } bytes) output.Append(Encoding.UTF8.GetString(bytes));
            if (output.ToString().Contains("IDENTITY_END", StringComparison.Ordinal)) break;
        }
        string text = output.ToString();
        Assert.Contains($"ROYALTERMINAL_MUX_SESSION={session.Id:D}", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"ROYALTERMINAL_MUX_BLOCK={block.Id:D}", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"ROYALTERMINAL_MUX_WINDOW={window.Id:D}", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-a-credential", text, StringComparison.Ordinal);
        Assert.DoesNotContain("spoofed", text, StringComparison.Ordinal);
        Assert.Equal("spoofed", original["ROYALTERMINAL_MUX_BLOCK"]);
    }
}
