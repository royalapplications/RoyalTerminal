// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text;
using System.Text.Json;

namespace RoyalTerminal.Multiplexing.Automation;

/// <summary>Worker-local registry for named Lua action input schemas. Contains no executable code or host capabilities.</summary>
public sealed class LuaActionContracts
{
    private const int PerValueBytes = 64 * 1024, TotalSchemaBytes = 256 * 1024;
    private readonly Dictionary<string, JsonElement> _schemas = new(StringComparer.Ordinal);
    private int _bytes;

    /// <summary>Registers a schema or validates one invocation. No network or process operations are performed.</summary>
    public void Process(JsonElement frame)
    {
        if (frame.ValueKind != JsonValueKind.Object || !frame.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String || n.GetString() is not { Length: > 0 and <= 80 } name)
            throw new ArgumentException("A bounded action name is required.");
        if (frame.TryGetProperty("schema", out var schema))
        {
            if (frame.TryGetProperty("arguments", out _) || _schemas.ContainsKey(name) || _schemas.Count >= 256)
                throw new ArgumentException("Duplicate or excessive action declarations.");
            MultiplexerArgumentValidator.ValidateSchema(schema, PerValueBytes);
            int size = Encoding.UTF8.GetByteCount(schema.GetRawText());
            if (size > TotalSchemaBytes - _bytes) throw new ArgumentException("Action schemas exceed the worker budget.");
            _schemas.Add(name, schema.Clone());
            _bytes += size;
            return;
        }
        if (!_schemas.TryGetValue(name, out var registered) || !frame.TryGetProperty("arguments", out var args) || args.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Expected a registered action and a JSON argument object.");
        MultiplexerArgumentValidator.ValidateArguments(args, registered, PerValueBytes);
    }
}
