// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;

namespace RoyalTerminal.Multiplexing.Server;

internal readonly struct ApiArguments(JsonElement value)
{
    public JsonElement Value { get; } = value;

    public void Validate(params string[] properties)
    {
        if (Value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Method arguments must be an object.");
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in Value.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new ArgumentException($"Duplicate argument '{property.Name}'.");
            if (!properties.Contains(property.Name, StringComparer.Ordinal)) throw new ArgumentException($"Unknown argument '{property.Name}'.");
        }
    }

    public string String(string key, string? fallback = null)
    {
        if (!Value.TryGetProperty(key, out JsonElement item)) return fallback ?? throw new ArgumentException($"'{key}' is required.");
        return item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new ArgumentException($"'{key}' must be a string.");
    }

    public Guid Id(string key) => Guid.TryParse(String(key), out Guid id) && id != Guid.Empty ? id : throw new ArgumentException($"'{key}' must be a UUID.");
    public Guid? OptionalId(string key) => Value.TryGetProperty(key, out JsonElement item) && item.ValueKind != JsonValueKind.Null ? Id(key) : null;
    public long? Revision() => Value.TryGetProperty("expectedRevision", out JsonElement item) && item.ValueKind != JsonValueKind.Null
        ? item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out long revision) && revision >= 0 ? revision : throw new ArgumentException("expectedRevision must be nonnegative.") : null;
    public double Number(string key, double fallback) => Value.TryGetProperty(key, out JsonElement item)
        ? item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double number) && double.IsFinite(number) ? number : throw new ArgumentException($"'{key}' must be a finite number.") : fallback;
    public int Integer(string key, int fallback) => Value.TryGetProperty(key, out JsonElement item)
        ? item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int number) ? number : throw new ArgumentException($"'{key}' must be an integer.") : fallback;
    public JsonElement Object(string key) => Value.TryGetProperty(key, out JsonElement item) && item.ValueKind == JsonValueKind.Object
        ? item : throw new ArgumentException($"'{key}' must be an object.");
}
