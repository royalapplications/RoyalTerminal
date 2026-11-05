// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;

namespace RoyalTerminal.Multiplexing.AutomationTests;

internal static class AutomationTestData
{
    internal static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    internal static MultiplexerAction Action(string id = "test", params string[] methods) => new()
    {
        Id = id,
        Steps = (methods.Length == 0 ? ["echo"] : methods).Select(method => new MultiplexerActionStep
        {
            Method = method,
            Arguments = Json("{\"text\":\"hello\"}"),
        }).ToArray(),
    };

    internal static MultiplexerActionRunner Runner(RecordingInvoker? invoker = null,
        MultiplexerAutomationLimits? limits = null) => new(invoker ?? new(),
        [new("echo", Json("""
        {"type":"object","properties":{"text":{"type":"string","minLength":1,"maxLength":32}},"required":["text"],"additionalProperties":false}
        """)), new("other", Json("{" + "\"type\":\"object\"}"))], limits);

    internal static MultiplexerResolvedConfiguration Config(MultiplexerKeyBinding[] bindings,
        MultiplexerKeyModeDefinition[]? modes = null) => MultiplexerConfigurationLoader.Merge(new(),
        new() { Actions = [Action("one"), Action("two"), Action("three")], Modes = modes ?? [], Bindings = bindings },
        new(), Runner());
}

internal sealed class RecordingInvoker : IMultiplexerMethodInvoker
{
    internal List<(string Method, JsonElement Arguments)> Calls { get; } = [];
    internal Func<string, JsonElement, CancellationToken, ValueTask<JsonElement>>? Handler { get; init; }

    public ValueTask<JsonElement> InvokeAsync(string method, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        Calls.Add((method, arguments.Clone()));
        return Handler?.Invoke(method, arguments, cancellationToken) ?? new(arguments.Clone());
    }
}

internal sealed class KeyTimeProvider : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(_ticks);
    internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
}
