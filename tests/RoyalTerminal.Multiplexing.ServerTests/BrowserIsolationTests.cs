using RoyalTerminal.Multiplexing.Server;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class BrowserIsolationTests
{
    [Fact]
    public void InlineStylesNeverEnableInlineScriptsOrEval()
    {
        var directives = ServerHost.ContentPolicy.Split(';',StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(rule=>rule.Split(' ',StringSplitOptions.RemoveEmptyEntries)).ToDictionary(parts=>parts[0],parts=>parts.Skip(1).ToArray());
        Assert.Equal(new[]{"'self'"},directives["script-src"]);
        Assert.Equal(new[]{"'self'","'unsafe-inline'"},directives["style-src"]);
        Assert.Equal(new[]{"'none'"},directives["frame-ancestors"]);
    }
}
