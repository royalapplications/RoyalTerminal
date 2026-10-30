// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace RoyalTerminal.Multiplexing.Server;

/// <summary>Exports actual request contracts and a bounded projection for action preflight.</summary>
internal static class ApiInputSchemas
{
    public static JsonElement? Nested(string method, string property)
    {
        JsonTypeInfo? type = (method, property) switch
        {
            ("block.create", "block") => ServerJsonContext.Default.NewBlock,
            ("session.set_layout", "layout") => ServerJsonContext.Default.MultiplexerLayoutNode,
            _ => null,
        };
        if (type is null) return null;

        JsonObject schema = type.GetJsonSchemaAsNode().AsObject();
        schema["type"] = "object"; // The enclosing API requires these values to be non-null objects.
        if (method == "block.create") DescribeBlock(schema);
        else DescribeLayouts(schema);
        RebaseReferences(schema, "#/properties/" + property);
        return Element(schema);
    }

    public static JsonElement ForActions(JsonElement schema)
        => Element(Project(schema, "#", schema, new(StringComparer.Ordinal)));

    private static void DescribeBlock(JsonObject schema)
    {
        schema["description"] = "Local process options. A pipe block requires command; PTY defaults to the platform shell. A splitBlockId selects the split target, otherwise windowId selects the window.";
        JsonObject properties = schema["properties"]!.AsObject();
        properties["label"]!["minLength"] = 1;
        properties["label"]!["maxLength"] = 256;
        properties["transport"]!["enum"] = new JsonArray("pty", "pipe");
        properties["orientation"]!["enum"] = new JsonArray("horizontal", "vertical");
        properties["ratio"]!["exclusiveMinimum"] = 0;
        properties["ratio"]!["exclusiveMaximum"] = 1;
        properties["expectedRevision"]!["minimum"] = 0;
        properties["workingDirectory"]!["description"] = "An existing absolute directory on the server; null uses the server's default directory.";

        JsonObject command = properties["command"]!.AsObject();
        command["required"] = new JsonArray("fileName", "arguments");
        JsonObject commandProperties = command["properties"]!.AsObject();
        commandProperties["fileName"]!["minLength"] = 1;
        commandProperties["arguments"]!["maxItems"] = 256;
        commandProperties["arguments"]!["items"]!["type"] = "string";
        commandProperties["arguments"]!["items"]!["maxLength"] = 32768;

        JsonObject dimensions = properties["dimensions"]!.AsObject();
        dimensions["required"] = new JsonArray("columns", "rows");
        JsonObject sizes = dimensions["properties"]!.AsObject();
        Range(sizes["columns"]!, 2, 512);
        Range(sizes["rows"]!, 1, 256);
        Range(sizes["widthPixels"]!, 0, 32768);
        Range(sizes["heightPixels"]!, 0, 32768);
    }

    private static void DescribeLayouts(JsonNode schema)
    {
        if (schema is not JsonObject node || node["properties"] is not JsonObject properties) return;
        node["description"] = "A leaf sets blockId and omits orientation/children. A split sets orientation and both child nodes and omits blockId. The server validates the entire tree and requires each block in this window exactly once.";
        properties["ratio"]!["exclusiveMinimum"] = 0;
        properties["ratio"]!["exclusiveMaximum"] = 1;
        // JsonStringEnumConverter accepts these canonical and lowercase names.
        properties["orientation"]!["enum"] = new JsonArray("Horizontal", "Vertical", "horizontal", "vertical", null);
        if (properties["first"] is { } first) DescribeLayouts(first);
        if (properties["second"] is { } second) DescribeLayouts(second);
    }

    private static void Range(JsonNode schema, int minimum, int maximum)
    {
        schema["minimum"] = minimum;
        schema["maximum"] = maximum;
    }

    private static void RebaseReferences(JsonNode node, string prefix)
    {
        if (node is JsonObject properties)
        {
            if (properties["$ref"] is JsonValue reference)
            {
                string path = reference.GetValue<string>();
                if (!path.StartsWith('#')) throw new InvalidOperationException("Only generated local schema references are supported.");
                properties["$ref"] = prefix + path[1..];
            }
            foreach ((_, JsonNode? child) in properties)
                if (child is not null) RebaseReferences(child, prefix);
        }
        else if (node is JsonArray items)
            foreach (JsonNode? item in items)
                if (item is not null) RebaseReferences(item, prefix);
    }

    private static JsonNode Project(JsonElement schema, string path, JsonElement root, HashSet<string> active)
    {
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False) return JsonValue.Create(schema.GetBoolean())!;
        if (!active.Add(path))
        {
            // The action validator deliberately has no recursive reference engine.
            // Preserve the value kind at this boundary; deserialization and the broker
            // validate recursive descendants when the individual method is invoked.
            return new JsonObject
            {
                ["type"] = schema.TryGetProperty("type", out JsonElement type) ? Clone(type) : new JsonArray("object", "null"),
                ["description"] = "Recursive object boundary: the server validates descendant fields and tree invariants during method invocation.",
            };
        }
        try
        {
            JsonObject result = new();
            if (schema.TryGetProperty("$ref", out JsonElement reference))
            {
                string target = reference.GetString()!;
                result = Project(Resolve(root, target), target, root, active).AsObject();
            }
            foreach (JsonProperty keyword in schema.EnumerateObject())
            {
                switch (keyword.Name)
                {
                    case "$ref": case "format":
                        // UUID formats must allow ${...} context strings during template preflight.
                        break;
                    case "properties":
                        JsonObject properties = new();
                        foreach (JsonProperty property in keyword.Value.EnumerateObject())
                            properties[property.Name] = Project(property.Value,
                                path + "/properties/" + EscapePointer(property.Name), root, active);
                        result[keyword.Name] = properties;
                        break;
                    case "items": case "additionalProperties":
                        result[keyword.Name] = Project(keyword.Value, path + "/" + keyword.Name, root, active);
                        break;
                    case "$schema": case "$id": case "title": case "description": case "default": case "examples": case "deprecated":
                    case "type": case "required": case "minimum": case "maximum": case "exclusiveMinimum": case "exclusiveMaximum":
                    case "minLength": case "maxLength": case "minItems": case "maxItems": case "minProperties": case "maxProperties":
                    case "enum": case "const":
                        result[keyword.Name] = Clone(keyword.Value);
                        break;
                    default:
                        throw new InvalidOperationException($"Generated input schema uses unsupported action keyword '{keyword.Name}'.");
                }
            }
            return result;
        }
        finally { active.Remove(path); }
    }

    private static JsonElement Resolve(JsonElement root, string reference)
    {
        if (reference == "#") return root;
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            throw new InvalidOperationException("Only generated local schema references are supported.");
        JsonElement current = root;
        foreach (string part in reference[2..].Split('/'))
            current = current.GetProperty(part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
        return current;
    }

    private static string EscapePointer(string value)
        => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static JsonNode? Clone(JsonElement value) => JsonNode.Parse(value.GetRawText());

    private static JsonElement Element(JsonNode node)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer)) node.WriteTo(writer);
        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
