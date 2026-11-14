using RoyalTerminal.Multiplexing.Cli;
using Xunit;
namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class ShellSyntaxTests
{
    [Fact] public void ChildArgumentsAreOpaqueAndRatiosArePercentages()
    {
        var o = ShellSyntax.Parse("split", ["-s", "work", "--split=below", "--ratio", "30", "--wait", "--for", "1.5s", "--", "app", "", "--ratio", "a b", "$(whoami)"]);
        Assert.Equal(new[]{"app", "", "--ratio", "a b", "$(whoami)"}, o.Tail);
        Assert.Equal(.3, o.Share); Assert.Equal(TimeSpan.FromMilliseconds(1500), o.Deadline);
    }
    [Theory] [InlineData("0")] [InlineData("100")] [InlineData("NaN")] [InlineData("23,5")]
    public void RejectInvalidPercentageBeforeLaunching(string value) => Assert.Throws<ArgumentException>(() => ShellSyntax.Parse("split", ["--ratio",value]));
    [Fact] public void AmbiguousAndUnsupportedArgumentsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ShellSyntax.Parse("send", ["-s", "a", "--session=b"]));
        Assert.Throws<ArgumentException>(() => ShellSyntax.Parse("split", ["--focus=true"]));
        Assert.Throws<ArgumentException>(() => ShellSyntax.Parse("run", ["--for", "30s"]));
        Assert.Throws<ArgumentException>(() => ShellSyntax.Parse("capture", ["--block", "x", "y"]));
        Assert.Equal("--block", Assert.Single(ShellSyntax.Parse("send", ["--", "--block"]).Words));
    }
    [Fact] public void ExistingPositionalInvocationsNeverChangeMeaning()
    {
        Assert.False(ShellSyntax.Handles("run", ["work", "--wait", "--", "app", "-s", "another"]));
        Assert.False(ShellSyntax.Handles("run", ["--wait", "work", "--", "app"]));
        Assert.False(ShellSyntax.Handles("send", ["work", "block", "--json"]));
        Assert.False(ShellSyntax.Handles("capture", ["work", "block"]));
        Assert.False(ShellSyntax.Handles("new", ["work"]));
    }
}
