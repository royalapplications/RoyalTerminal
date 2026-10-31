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
        var ct=TestContext.Current.CancellationToken;
        var factory=new CompositeTerminalTransportFactory([new PipeTerminalTransportProvider()]);
        await using var broker=new MultiplexerBroker(factory);
        var session=broker.CreateSession("identity"); var win=broker.CreateWindow(session.Id,"destination");
        var old=new Dictionary<string,string> { ["ROYALTERMINAL_MUX_BLOCK"]="spoofed", ["ROYALTERMINAL_MUX_TOKEN"]="not-a-credential" };
        TerminalCommandSpec cmd=OperatingSystem.IsWindows()
            ? new("cmd.exe",["/d","/q","/c","set ROYALTERMINAL_MUX_"])
            : new("/bin/sh",["-c","env | grep '^ROYALTERMINAL_MUX_' "]);
        var block=await broker.AddBlockAsync(session.Id,win.Id,"print",new PipeTransportOptions(cmd,null,old,true,new(100,12,0,0)),ct);
        using var observer=broker.Attach(session.Id,block.Id);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var output=new MemoryStream();
        await foreach(var e in observer.Events.ReadAllAsync(timeout.Token))if(e.Data is {} d)output.Write(d);
        var text=Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains($"ROYALTERMINAL_MUX_SESSION={session.Id:D}",text,StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"ROYALTERMINAL_MUX_BLOCK={block.Id:D}",text,StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"ROYALTERMINAL_MUX_WINDOW={win.Id:D}",text,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-a-credential",text,StringComparison.Ordinal);
        Assert.DoesNotContain("spoofed",text,StringComparison.Ordinal);
        Assert.Equal("spoofed",old["ROYALTERMINAL_MUX_BLOCK"]);
    }
}
