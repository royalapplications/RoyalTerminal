// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record ActionContext(Guid? SessionId = null, Guid? BlockId = null, Guid? WindowId = null);

internal sealed class AutomationService
{
    private readonly ApiCatalog _catalog;
    private readonly string? _path;
    private readonly SemaphoreSlim _updates = new(1, 1);
    private MultiplexerResolvedConfiguration _configuration = new(new(), []);

    public AutomationService(ApiCatalog catalog, string? path = null)
    {
        _catalog = catalog;
        _path = path is null ? null : Path.GetFullPath(path);
        if (_path is not null && File.Exists(_path))
        {
            if (new FileInfo(_path).Length > 1024 * 1024) throw new ArgumentException("The automation config exceeds 1 MiB.");
            _configuration = Parse(File.ReadAllBytes(_path));
        }
    }

    public MultiplexerResolvedConfiguration Current => Volatile.Read(ref _configuration);

    public async ValueTask<MultiplexerResolvedConfiguration> UpdateAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        MultiplexerResolvedConfiguration resolved = Parse(bytes);
        await _updates.WaitAsync(cancellationToken);
        try
        {
            if (_path is not null)
            {
                string directory = Path.GetDirectoryName(_path)!;
                Directory.CreateDirectory(directory);
                string temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    FileStreamOptions options = new() { Access = FileAccess.Write, Mode = FileMode.CreateNew,
                        Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.WriteThrough };
                    if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    await using (FileStream stream = new(temporary, options))
                    {
                        await stream.WriteAsync(bytes, cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                    }
                    File.Move(temporary, _path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            Volatile.Write(ref _configuration, resolved);
            return resolved;
        }
        finally { _updates.Release(); }
    }

    public async ValueTask<MultiplexerActionResult> RunAsync(string actionId, ActionContext context, AccessGrant grant,
        CancellationToken cancellationToken)
    {
        ServerHttp.RequireAdministrator(grant);
        MultiplexerAction action = Current.Configuration.Actions.FirstOrDefault(x => x.Id == actionId)
            ?? throw new ApiException(404, "action_not_found", "The configured action does not exist.");
        MultiplexerAction prepared = action with
        {
            Steps = action.Steps.Select(step => step with { Arguments = ExpandContext(step.Arguments, context) }).ToArray(),
        };
        return await Runner(new AuthorizedInvoker(_catalog, grant)).RunAsync(prepared, cancellationToken);
    }

    private MultiplexerResolvedConfiguration Parse(byte[] bytes)
    {
        MultiplexerActionRunner runner = Runner(new ValidationOnlyInvoker());
        MultiplexerAutomationConfiguration layer = MultiplexerConfigurationLoader.Parse(bytes, runner);
        foreach (MultiplexerAction action in layer.Actions)
            if (action.Id == "." || action.Id.Contains("..", StringComparison.Ordinal))
                throw new ArgumentException("HTTP action identifiers cannot be '.' or contain '..'.");
        return MultiplexerConfigurationLoader.Merge(new(), layer, new(), runner);
    }

    private MultiplexerActionRunner Runner(IMultiplexerMethodInvoker invoker)
        => new(invoker, _catalog.DescribeActionMethods());

    private static JsonElement ExpandContext(JsonElement value, ActionContext context)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer)) Write(value, writer, context);
        using JsonDocument result = JsonDocument.Parse(buffer.WrittenMemory);
        return result.RootElement.Clone();

        static void Write(JsonElement value, Utf8JsonWriter writer, ActionContext context)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                string? input = value.GetString();
                Guid? replacement = input switch
                {
                    "${sessionId}" => context.SessionId,
                    "${blockId}" => context.BlockId,
                    "${windowId}" => context.WindowId,
                    _ => null,
                };
                if (input is "${sessionId}" or "${blockId}" or "${windowId}")
                    writer.WriteStringValue(replacement?.ToString() ?? throw new ArgumentException($"Action context {input} is required."));
                else writer.WriteStringValue(input);
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject()) { writer.WritePropertyName(property.Name); Write(property.Value, writer, context); }
                writer.WriteEndObject();
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray(); foreach (JsonElement item in value.EnumerateArray()) Write(item, writer, context); writer.WriteEndArray();
            }
            else value.WriteTo(writer);
        }
    }

    private sealed class AuthorizedInvoker(ApiCatalog catalog, AccessGrant grant) : IMultiplexerMethodInvoker
    {
        public ValueTask<JsonElement> InvokeAsync(string method, JsonElement arguments, CancellationToken cancellationToken = default)
            => catalog.InvokeAsync(method, arguments, grant, cancellationToken);
    }
    private sealed class ValidationOnlyInvoker : IMultiplexerMethodInvoker
    {
        public ValueTask<JsonElement> InvokeAsync(string method, JsonElement arguments, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Configuration validation never invokes methods.");
    }
}
