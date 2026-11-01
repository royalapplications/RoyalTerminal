using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Multiplexing.Cli;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class ShellIdentityTests
{
    private static readonly Guid Session=Guid.NewGuid(), Window=Guid.NewGuid(), Survivor=Guid.NewGuid(), Removed=Guid.NewGuid();
    private static ShellWorkspace Workspace()
    {
        using var doc=JsonDocument.Parse($$"""{"id":"{{Session}}","windows":[{"id":"{{Window}}","label":"{{Removed}}"}],"blocks":[{"id":"{{Survivor}}","label":"{{Removed}}","windowId":"{{Window}}","placement":"Tiled"}]}""");
        return new ShellWorkspace(null!,doc.RootElement.Clone()); // Selection never makes network calls.
    }
    [Fact] public void AStaleInheritedIdentityNeverPicksTheOnlySurvivor()
    {
        var ws=Workspace(); Assert.Equal(Survivor,ws.Block(null,null,null,false));
        Assert.Equal(MultiplexerAutomationError.TargetNotFound,Assert.Throws<MultiplexerAutomationException>(()=>ws.Block(null,null,Removed.ToString(),false)).Code);
        Assert.Throws<ArgumentException>(()=>ws.Block(null,null,"corrupt environment",false));
        Assert.Equal(Survivor,ws.Block(Survivor.ToString(),null,Removed.ToString(),false));
    }
    [Fact] public void IdentityLookingLabelCannotMasqueradeAsAnAbsentIdentity()
    {
        var ws=Workspace();
        Assert.Equal(MultiplexerAutomationError.TargetNotFound,Assert.Throws<MultiplexerAutomationException>(()=>ws.Window(Removed.ToString())).Code);
        Assert.Equal(MultiplexerAutomationError.TargetNotFound,Assert.Throws<MultiplexerAutomationException>(()=>ws.Block(Removed.ToString(),Window,null,true)).Code);
    }
}
