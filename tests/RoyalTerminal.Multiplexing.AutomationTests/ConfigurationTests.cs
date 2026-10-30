// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;
using Xunit;
using static RoyalTerminal.Multiplexing.AutomationTests.AutomationTestData;

namespace RoyalTerminal.Multiplexing.AutomationTests;

public sealed class ConfigurationTests
{
    [Fact]
    public void ClientOverridesServerWhichOverridesDefaultsIncludingPrefixConflicts()
    {
        MultiplexerAutomationConfiguration defaults = new()
        {
            Actions = [Action("one"), Action("two"), Action("three")],
            Bindings = [new() { Sequence = "ctrl+k", ActionId = "one" }],
        };
        MultiplexerAutomationConfiguration server = new() { Bindings = [new() { Sequence = "ctrl+k>s", ActionId = "two" }] };
        MultiplexerResolvedConfiguration first = MultiplexerConfigurationLoader.Merge(defaults, server, new(), Runner());
        Assert.Single(first.Diagnostics);
        MultiplexerKeyEngine engine = new(first);
        Assert.Equal(MultiplexerKeyDispatchKind.Pending, engine.ProcessKey("ctrl+k").Kind);
        Assert.Equal("two", engine.ProcessKey("s").ActionId);
        MultiplexerAutomationConfiguration client = new() { Bindings = [new() { Sequence = "ctrl+k", ActionId = "three" }] };
        MultiplexerResolvedConfiguration final = MultiplexerConfigurationLoader.Merge(defaults, server, client, Runner());
        Assert.Equal(2, final.Diagnostics.Count);
        Assert.Equal("three", new MultiplexerKeyEngine(final).ProcessKey("ctrl+k").ActionId);
        Assert.Equal("one", defaults.Bindings[0].ActionId);
    }

    [Fact]
    public void SiblingSequencesCoexistAndExactReplacementDoesNotEraseSiblings()
    {
        MultiplexerResolvedConfiguration config = Config([
            new() { Sequence = "ctrl+b>c", ActionId = "one" },
            new() { Sequence = "ctrl+b>n", ActionId = "two" },
            new() { Sequence = "Control+B>c", ActionId = "three" },
        ]);
        Assert.Equal(2, config.Configuration.Bindings.Count);
        MultiplexerKeyEngine engine = new(config);
        engine.ProcessKey("ctrl+b"); Assert.Equal("three", engine.ProcessKey("c").ActionId);
        engine.ProcessKey("ctrl+b"); Assert.Equal("two", engine.ProcessKey("n").ActionId);
    }

    [Theory]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":1,\"typo\":true}")]
    [InlineData("{\"version\":1,\"version\":1}")]
    [InlineData("{\"actions\":null}")]
    [InlineData("{\"bindings\":[{\"sequence\":\"x\",\"block\":true,\"exitMode\":true}]}")]
    [InlineData("{\"modes\":[{\"name\":\"Uppercase\"}]}")]
    [InlineData("{\"actions\":[{\"id\":\"bad\",\"steps\":[]}]}")]
    public void InvalidConfigCannotInvokeAnyAction(string json)
    {
        RecordingInvoker invoker = new();
        Assert.Throws<MultiplexerAutomationException>(() => MultiplexerConfigurationLoader.Parse(Encoding.UTF8.GetBytes(json), Runner(invoker)));
        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public void SourceGeneratedConfigurationRoundTripsWithReflectionDisabled()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        MultiplexerAutomationConfiguration original = new()
        {
            Actions = [Action("launch")],
            Modes = [new() { Name = "resize", Exclusive = true }],
            Bindings = [new() { Sequence = "ctrl+b>c", ActionId = "launch" }],
        };
        byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(original, MultiplexerAutomationJsonContext.Default.MultiplexerAutomationConfiguration);
        MultiplexerAutomationConfiguration parsed = MultiplexerConfigurationLoader.Parse(encoded, Runner());
        Assert.Equal("launch", parsed.Actions[0].Id);
        Assert.Equal("hello", parsed.Actions[0].Steps[0].Arguments.GetProperty("text").GetString());
        Assert.True(parsed.Modes[0].Exclusive);
        Assert.Equal("ctrl+b>c", parsed.Bindings[0].Sequence);
    }

    [Fact]
    public void ParsingServerLayerCanReferenceAnActionFromDefaultsWithoutExecutingIt()
    {
        RecordingInvoker invoker = new();
        MultiplexerActionRunner runner = Runner(invoker);
        MultiplexerAutomationConfiguration server = MultiplexerConfigurationLoader.Parse(
            "{\"bindings\":[{\"sequence\":\"x\",\"actionId\":\"default-action\"}]}"u8, runner);
        MultiplexerResolvedConfiguration merged = MultiplexerConfigurationLoader.Merge(
            new() { Actions = [Action("default-action")] }, server, new(), runner);
        Assert.Equal("default-action", new MultiplexerKeyEngine(merged).ProcessKey("x").ActionId);
        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public void UnknownActionOrModeReferencesAreRejectedByFinalMerge()
    {
        Assert.Throws<MultiplexerAutomationException>(() => Config([new() { Sequence = "x", ActionId = "unknown" }]));
        Assert.Throws<MultiplexerAutomationException>(() => Config([new() { Sequence = "x", EnterMode = "unknown" }]));
    }

    [Fact]
    public void LaterModeOptionsAndActionsOverrideEarlierDefinitions()
    {
        MultiplexerAutomationConfiguration defaults = new() { Actions = [Action("one")], Modes = [new() { Name = "mode", Exclusive = false }] };
        MultiplexerAutomationConfiguration server = new()
        {
            Actions = [Action("one", "other")],
            Modes = [new() { Name = "mode", Exclusive = true }],
            Bindings = [new() { Mode = "mode", Sequence = "x", ActionId = "one" }],
        };
        MultiplexerResolvedConfiguration result = MultiplexerConfigurationLoader.Merge(defaults, server, new(), Runner());
        Assert.Equal("other", Assert.Single(result.Configuration.Actions).Steps[0].Method);
        MultiplexerKeyEngine engine = new(result);
        engine.EnterMode("mode");
        Assert.Equal(MultiplexerKeyDispatchKind.Blocked, engine.ProcessKey("y").Kind);
    }
}
