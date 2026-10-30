// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Text.Json;

namespace RoyalTerminal.Multiplexing.Automation;

/// <summary>Bounded validation of the explicitly supported JSON Schema vocabulary.</summary>
/// <remarks>Supports types/unions, object properties and required fields, additional properties,
/// arrays/items, lengths, numeric ranges, enum and const. Unknown validation keywords are errors.</remarks>
public static class MultiplexerArgumentValidator
{
    /// <summary>Validates a schema and argument value without modifying either.</summary>
    public static void Validate(JsonElement arguments, JsonElement schema, int maxJsonBytes = 64 * 1024)
    {
        ValidateSchema(schema, maxJsonBytes);
        ValidateArguments(arguments, schema, maxJsonBytes);
    }

    internal static void ValidateSchema(JsonElement schema, int maximumBytes)
    {
        ValidateJson(schema, maximumBytes);
        Schema(schema, "$schema", 0);
    }

    internal static void ValidateArguments(JsonElement value, JsonElement schema, int maximumBytes)
    {
        ValidateJson(value, maximumBytes);
        Value(value, schema, "$", 0);
    }

    internal static void ValidateJson(JsonElement value, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        if (value.ValueKind == JsonValueKind.Undefined) throw Invalid("A JSON value is required.");
        string raw = value.GetRawText();
        if (raw.Length > maximumBytes || Encoding.UTF8.GetByteCount(raw) > maximumBytes)
            throw Invalid("JSON exceeds the configured byte budget.");
        int remaining = 16_384;
        ValidateShape(value, 0, ref remaining);
    }

    private static void ValidateShape(JsonElement value, int depth, ref int remaining)
    {
        if (depth > 32 || --remaining < 0) throw Invalid("JSON exceeds the depth or node budget.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Name.Length > 256 || !names.Add(property.Name)) throw Invalid("JSON contains an overlong or duplicate property name.");
                ValidateShape(property.Value, depth + 1, ref remaining);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray()) ValidateShape(item, depth + 1, ref remaining);
        }
    }

    private static void Schema(JsonElement schema, string path, int depth)
    {
        if (depth > 32) throw Invalid("Schema nesting exceeds the depth budget.");
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False) return;
        if (schema.ValueKind != JsonValueKind.Object) throw Invalid($"{path} must be an object or boolean schema.");
        foreach (JsonProperty keyword in schema.EnumerateObject())
        {
            JsonElement value = keyword.Value;
            switch (keyword.Name)
            {
                case "$schema": case "$id": case "title": case "description":
                    if (value.ValueKind != JsonValueKind.String) throw Invalid($"{path}.{keyword.Name} must be text.");
                    break;
                case "default": case "examples": case "deprecated":
                    // Annotations do not change validation and are never evaluated.
                    break;
                case "type":
                    if (value.ValueKind == JsonValueKind.String) CheckTypeName(value.GetString()!, path);
                    else if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() is > 0 and <= 7)
                    {
                        HashSet<string> types = new(StringComparer.Ordinal);
                        foreach (JsonElement item in value.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.String || !types.Add(item.GetString()!)) throw Invalid($"{path}.type contains invalid or duplicate types.");
                            CheckTypeName(item.GetString()!, path);
                        }
                    }
                    else throw Invalid($"{path}.type must be a type name or a nonempty type union.");
                    break;
                case "properties":
                    if (value.ValueKind != JsonValueKind.Object) throw Invalid($"{path}.properties must be an object.");
                    foreach (JsonProperty property in value.EnumerateObject()) Schema(property.Value, path + ".properties." + property.Name, depth + 1);
                    break;
                case "required":
                    if (value.ValueKind != JsonValueKind.Array) throw Invalid($"{path}.required must be an array.");
                    HashSet<string> required = new(StringComparer.Ordinal);
                    foreach (JsonElement item in value.EnumerateArray())
                        if (item.ValueKind != JsonValueKind.String || !required.Add(item.GetString()!)) throw Invalid($"{path}.required contains an invalid or duplicate name.");
                    break;
                case "additionalProperties": case "items":
                    Schema(value, path + "." + keyword.Name, depth + 1);
                    break;
                case "minimum": case "maximum": case "exclusiveMinimum": case "exclusiveMaximum":
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double boundary) || !double.IsFinite(boundary)) throw Invalid($"{path}.{keyword.Name} must be finite numeric data.");
                    break;
                case "minLength": case "maxLength": case "minItems": case "maxItems": case "minProperties": case "maxProperties":
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int size) || size < 0) throw Invalid($"{path}.{keyword.Name} must be a nonnegative integer.");
                    break;
                case "enum":
                    if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 256) throw Invalid($"{path}.enum must contain between one and 256 entries.");
                    break;
                case "const": break;
                default: throw Invalid($"Unsupported schema keyword '{keyword.Name}' at {path}.");
            }
        }
    }

    private static void Value(JsonElement value, JsonElement schema, string path, int depth)
    {
        if (depth > 32) throw Invalid("Argument nesting exceeds the depth budget.");
        if (schema.ValueKind == JsonValueKind.True) return;
        if (schema.ValueKind == JsonValueKind.False) throw Invalid($"{path} is forbidden by its schema.");
        if (schema.TryGetProperty("type", out JsonElement type))
        {
            bool matches = type.ValueKind == JsonValueKind.String && Matches(value, type.GetString()!);
            if (type.ValueKind == JsonValueKind.Array)
                foreach (JsonElement alternative in type.EnumerateArray()) matches |= Matches(value, alternative.GetString()!);
            if (!matches) throw Invalid($"{path} has an incompatible JSON type.");
        }
        if (schema.TryGetProperty("const", out JsonElement constant) && !JsonElement.DeepEquals(value, constant))
            throw Invalid($"{path} does not match its constant constraint.");
        if (schema.TryGetProperty("enum", out JsonElement choices))
        {
            bool found = false;
            foreach (JsonElement choice in choices.EnumerateArray()) found |= JsonElement.DeepEquals(value, choice);
            if (!found) throw Invalid($"{path} is not a declared enum member.");
        }
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                ValidateObject(value, schema, path, depth);
                break;
            case JsonValueKind.Array:
                BoundLength(value.GetArrayLength(), schema, "minItems", "maxItems", path);
                if (schema.TryGetProperty("items", out JsonElement itemSchema))
                {
                    int index = 0;
                    foreach (JsonElement item in value.EnumerateArray()) Value(item, itemSchema, $"{path}[{index++}]", depth + 1);
                }
                break;
            case JsonValueKind.String:
                int length = 0;
                foreach (Rune _ in value.GetString()!.EnumerateRunes()) length++;
                BoundLength(length, schema, "minLength", "maxLength", path);
                break;
            case JsonValueKind.Number:
                if (!value.TryGetDouble(out double number) || !double.IsFinite(number)) throw Invalid($"{path} must be a finite supported number.");
                if (schema.TryGetProperty("minimum", out JsonElement minimum) && CompareNumbers(value, minimum) < 0 ||
                    schema.TryGetProperty("maximum", out JsonElement maximum) && CompareNumbers(value, maximum) > 0 ||
                    schema.TryGetProperty("exclusiveMinimum", out JsonElement exclusiveMinimum) && CompareNumbers(value, exclusiveMinimum) <= 0 ||
                    schema.TryGetProperty("exclusiveMaximum", out JsonElement exclusiveMaximum) && CompareNumbers(value, exclusiveMaximum) >= 0)
                    throw Invalid($"{path} is outside its numeric bounds.");
                break;
        }
    }

    private static void ValidateObject(JsonElement value, JsonElement schema, string path, int depth)
    {
        if (schema.TryGetProperty("required", out JsonElement required))
            foreach (JsonElement name in required.EnumerateArray())
                if (!value.TryGetProperty(name.GetString()!, out _)) throw Invalid($"{path} is missing required property '{name.GetString()}'.");
        bool hasProperties = schema.TryGetProperty("properties", out JsonElement properties);
        bool hasAdditional = schema.TryGetProperty("additionalProperties", out JsonElement additional);
        int count = 0;
        foreach (JsonProperty property in value.EnumerateObject())
        {
            count++;
            if (hasProperties && properties.TryGetProperty(property.Name, out JsonElement child))
                Value(property.Value, child, path + "." + property.Name, depth + 1);
            else if (hasAdditional)
                Value(property.Value, additional, path + "." + property.Name, depth + 1);
        }
        BoundLength(count, schema, "minProperties", "maxProperties", path);
    }

    private static void BoundLength(int length, JsonElement schema, string minimumName, string maximumName, string path)
    {
        if (schema.TryGetProperty(minimumName, out JsonElement minimum) && length < minimum.GetInt32() ||
            schema.TryGetProperty(maximumName, out JsonElement maximum) && length > maximum.GetInt32())
            throw Invalid($"{path} is outside its length bounds.");
    }

    private static bool Matches(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => IsInteger(value),
        _ => false,
    };

    private static int CompareNumbers(JsonElement first, JsonElement second)
        => first.TryGetDecimal(out decimal a) && second.TryGetDecimal(out decimal b)
            ? a.CompareTo(b) : first.GetDouble().CompareTo(second.GetDouble());

    private static bool IsInteger(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number) return false;
        string raw = value.GetRawText();
        int exponentOffset = raw.IndexOfAny(['e', 'E']);
        ReadOnlySpan<char> mantissa = exponentOffset < 0 ? raw.AsSpan() : raw.AsSpan(0, exponentOffset);
        int dot = mantissa.IndexOf('.');
        int fractionDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
        int trailingZeroes = 0;
        bool zero = true;
        for (int index = 0; index < mantissa.Length; index++)
            if (mantissa[index] is >= '1' and <= '9') zero = false;
        if (zero) return true;
        for (int index = mantissa.Length - 1; index >= 0; index--)
        {
            if (mantissa[index] == '.') continue;
            if (mantissa[index] != '0') break;
            trailingZeroes++;
        }
        int exponent = 0;
        if (exponentOffset >= 0 && !int.TryParse(raw.AsSpan(exponentOffset + 1), System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out exponent))
            return raw[exponentOffset + 1] != '-';
        return (long)fractionDigits - exponent <= trailingZeroes;
    }

    private static void CheckTypeName(string name, string path)
    {
        if (name is not ("object" or "array" or "string" or "boolean" or "null" or "number" or "integer"))
            throw Invalid($"{path} uses an unsupported JSON type.");
    }

    internal static MultiplexerAutomationException Invalid(string message)
        => new(MultiplexerAutomationError.InvalidConfiguration, message);
}
