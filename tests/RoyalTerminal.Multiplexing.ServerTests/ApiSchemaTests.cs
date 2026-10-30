// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Multiplexing.Server;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class ApiSchemaTests
{
    [Fact]
    public async Task BlockDiscoveryIncludesNestedExecutableGeometryAndIdentifierContracts()
    {
        await using Fixture fixture = new();
        JsonElement block = fixture.Catalog.Describe("block.create").InputSchema
            .GetProperty("properties").GetProperty("block");
        JsonElement properties = block.GetProperty("properties");
        Assert.Equal("object", block.GetProperty("type").GetString());
        Assert.False(block.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["pty", "pipe"], properties.GetProperty("transport").GetProperty("enum")
            .EnumerateArray().Select(x => x.GetString()!).ToArray());

        JsonElement command = properties.GetProperty("command");
        Assert.Contains(command.GetProperty("required").EnumerateArray(), x => x.GetString() == "fileName");
        Assert.Contains(command.GetProperty("required").EnumerateArray(), x => x.GetString() == "arguments");
        Assert.Equal("array", command.GetProperty("properties").GetProperty("arguments").GetProperty("type").GetString());
        Assert.Equal(256, command.GetProperty("properties").GetProperty("arguments").GetProperty("maxItems").GetInt32());
        Assert.Equal(512, properties.GetProperty("dimensions").GetProperty("properties")
            .GetProperty("columns").GetProperty("maximum").GetInt32());
        Assert.Equal("uuid", properties.GetProperty("windowId").GetProperty("format").GetString());
        Assert.Equal("uuid", properties.GetProperty("splitBlockId").GetProperty("format").GetString());
    }

    [Fact]
    public async Task RecursiveLayoutReferencesResolveWithinTheEnclosingMethodSchema()
    {
        await using Fixture fixture = new();
        JsonElement schema = fixture.Catalog.Describe("session.set_layout").InputSchema;
        string[] references = FindProperties(schema, "$ref").Select(x => x.GetString()!).ToArray();
        Assert.NotEmpty(references);
        foreach (string reference in references)
        {
            Assert.StartsWith("#/properties/layout/", reference, StringComparison.Ordinal);
            JsonElement target = schema;
            foreach (string part in reference[2..].Split('/')) target = target.GetProperty(part);
            Assert.True(target.TryGetProperty("properties", out JsonElement properties));
            Assert.True(properties.TryGetProperty("blockId", out _));
            Assert.True(properties.TryGetProperty("first", out _));
            Assert.True(properties.TryGetProperty("second", out _));
        }
    }

    [Fact]
    public async Task ActionProjectionAcceptsNestedContractsAndContextPlaceholders()
    {
        await using Fixture fixture = new();
        NeverInvoke invoker = new();
        IReadOnlyList<MultiplexerActionMethod> methods = fixture.Catalog.DescribeActionMethods();
        MultiplexerActionRunner runner = new(invoker, methods);
        Assert.Equal(fixture.Catalog.Describe().Methods.Select(x => x.Name), methods.Select(x => x.Name));
        foreach (MultiplexerActionMethod method in methods)
        {
            Assert.Empty(FindProperties(method.InputSchema, "$ref"));
            Assert.DoesNotContain(FindProperties(method.InputSchema, "format"), x => x.ValueKind == JsonValueKind.String);
        }

        runner.Validate(Action("block.create", """
            {"sessionId":"${sessionId}","block":{"label":"Build","transport":"pipe","windowId":"${windowId}",
            "command":{"fileName":"build-tool","arguments":["--verbose"]},"dimensions":{"columns":100,"rows":28}}}
            """));
        runner.Validate(Action("session.set_layout", """
            {"sessionId":"${sessionId}","windowId":"${windowId}","layout":{"orientation":"horizontal","ratio":0.4,
            "first":{"blockId":"${blockId}"},"second":{"blockId":"33333333-3333-3333-3333-333333333333"}}}
            """));
        Assert.Equal(0, invoker.Invocations);
    }

    [Theory]
    [InlineData("{\"command\":{\"fileName\":\"tool\",\"argumnts\":[]}}")]
    [InlineData("{\"command\":{\"arguments\":[]}}")]
    [InlineData("{\"command\":{\"fileName\":\"tool\",\"arguments\":[null]}}")]
    [InlineData("{\"dimensions\":{\"columns\":\"wide\",\"rows\":24}}")]
    [InlineData("{\"dimensions\":{\"columns\":513,\"rows\":24}}")]
    [InlineData("{\"transport\":\"telnet\"}")]
    public async Task ActionPreflightRejectsMalformedNestedBlockOptions(string block)
    {
        await using Fixture fixture = new();
        MultiplexerActionRunner runner = new(new NeverInvoke(), fixture.Catalog.DescribeActionMethods());
        MultiplexerAction action = Action("block.create", "{\"sessionId\":\"${sessionId}\",\"block\":" + block + "}");
        Assert.Throws<MultiplexerAutomationException>(() => runner.Validate(action));
    }

    [Fact]
    public async Task RecursiveBoundaryDefersDescendantValidationToTheActualServerContract()
    {
        await using Fixture fixture = new();
        MultiplexerActionRunner runner = new(new NeverInvoke(), fixture.Catalog.DescribeActionMethods());
        MultiplexerAction action = Action("session.set_layout", """
            {"sessionId":"11111111-1111-1111-1111-111111111111","windowId":"22222222-2222-2222-2222-222222222222",
            "layout":{"orientation":"Horizontal","first":{"orientation":"Horizontal","first":{"misspelledBlockId":"x"},
            "second":{"blockId":"33333333-3333-3333-3333-333333333333"}},"second":{"blockId":"44444444-4444-4444-4444-444444444444"}}}
            """);

        // Preflight checks bounded schema structure, not an unrestricted reference graph.
        runner.Validate(action);
        JsonException error = await Assert.ThrowsAsync<JsonException>(() => fixture.Catalog.InvokeAsync(
            action.Steps[0].Method, action.Steps[0].Arguments, fixture.Administrator, default).AsTask());
        Assert.Equal(400, ServerHttp.Classify(error).Status);
    }

    private static MultiplexerAction Action(string method, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new() { Id = "schema-test", Steps = [new() { Method = method, Arguments = document.RootElement.Clone() }] };
    }

    private static IEnumerable<JsonElement> FindProperties(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Name == name) yield return property.Value;
                foreach (JsonElement found in FindProperties(property.Value, name)) yield return found;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray())
                foreach (JsonElement found in FindProperties(item, name)) yield return found;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly MultiplexerBroker _broker;
        public ApiCatalog Catalog { get; }
        public AccessGrant Administrator { get; }

        public Fixture()
        {
            InspectingTerminalTransportFactory inspections = new(new UnexpectedTransportFactory());
            _broker = new(inspections);
            AccessTokens tokens = new("schema-test-token-01234567890123456789");
            Catalog = new(new(_broker, inspections), tokens, new());
            Administrator = tokens.Resolve("schema-test-token-01234567890123456789")!;
        }

        public ValueTask DisposeAsync() => _broker.DisposeAsync();
    }

    private sealed class UnexpectedTransportFactory : ITerminalTransportFactory
    {
        public ITerminalTransport Create(ITerminalTransportOptions options)
            => throw new InvalidOperationException("Schema validation must not launch a process.");
    }

    private sealed class NeverInvoke : IMultiplexerMethodInvoker
    {
        public int Invocations { get; private set; }
        public ValueTask<JsonElement> InvokeAsync(string method, JsonElement arguments, CancellationToken cancellationToken = default)
        {
            Invocations++;
            throw new InvalidOperationException("Preflight must not invoke a method.");
        }
    }
}
