// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader reflection model.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoyalTerminal.Shaders;

/// <summary>
/// Reads reflection metadata emitted by Slang's JSON reflection output.
/// </summary>
public static partial class TerminalShaderSlangReflectionJsonReader
{
    /// <summary>
    /// Attempts to read shader reflection from Slang JSON reflection text.
    /// </summary>
    /// <param name="jsonText">Slang JSON reflection text.</param>
    /// <param name="pass">Shader pass that produced the reflection.</param>
    /// <returns>Reflected metadata and diagnostics.</returns>
    public static TerminalShaderReflectionResult TryRead(
        string jsonText,
        TerminalShaderPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return MissingPayload();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonText);
            List<TerminalShaderEntryPointReflection> entryPoints = ReadEntryPoints(document.RootElement, pass);
            List<TerminalShaderResourceReflection> resources = ReadResources(document.RootElement);
            if (entryPoints.Count == 0 && resources.Count > 0)
            {
                entryPoints.Add(new TerminalShaderEntryPointReflection(
                    pass.EntryPoint ?? pass.Name,
                    pass.Stage));
            }

            TerminalShaderReflection reflection = new(entryPoints, resources);
            return HasReflectionPayload(reflection)
                ? new TerminalShaderReflectionResult(reflection)
                : MissingPayload();
        }
        catch (JsonException ex)
        {
            return new TerminalShaderReflectionResult(
                diagnostics:
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Warning,
                        "RTSHADERSLANGREFLECT002",
                        $"Slang JSON reflection could not be parsed: {ex.Message}. Falling back to source-side reflection."),
                ]);
        }
    }

    private static TerminalShaderReflectionResult MissingPayload()
    {
        return new TerminalShaderReflectionResult(
            diagnostics:
            [
                new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Warning,
                    "RTSHADERSLANGREFLECT001",
                    "Slang JSON reflection did not contain enough metadata; falling back to source-side reflection."),
            ]);
    }

    private static List<TerminalShaderEntryPointReflection> ReadEntryPoints(
        JsonElement root,
        TerminalShaderPass pass)
    {
        List<TerminalShaderEntryPointReflection> entryPoints = [];
        VisitNamedArrays(root, (name, array) =>
        {
            if (!IsEntryPointArrayName(name))
            {
                return;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string entryPointName = GetString(item, "name", "entryPoint", "entryPointName") ??
                    pass.EntryPoint ??
                    pass.Name;
                int? stageNumber = GetNumber(item, "stage");
                TerminalShaderStage stage = ResolveStage(
                    GetString(item, "stage", "shaderStage") ??
                    (stageNumber is null
                        ? null
                        : stageNumber.Value.ToString(CultureInfo.InvariantCulture)),
                    pass.Stage);
                TerminalShaderDispatch? threadGroupSize = GetDispatch(item);
                List<TerminalShaderSemanticReflection> inputs = ReadSemantics(
                    item,
                    "inputs",
                    "inputParameters",
                    "varyingInputs",
                    "varyingInput");
                List<TerminalShaderSemanticReflection> outputs = ReadSemantics(
                    item,
                    "outputs",
                    "outputParameters",
                    "varyingOutputs",
                    "varyingOutput");

                entryPoints.Add(new TerminalShaderEntryPointReflection(
                    entryPointName,
                    stage,
                    threadGroupSize,
                    inputs,
                    outputs));
            }
        });

        return entryPoints;
    }

    private static List<TerminalShaderResourceReflection> ReadResources(JsonElement root)
    {
        List<TerminalShaderResourceReflection> resources = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        VisitNamedArrays(root, (name, array) =>
        {
            if (!IsResourceArrayName(name))
            {
                return;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                if (TryReadResource(item, out TerminalShaderResourceReflection? resource) &&
                    resource is not null)
                {
                    string key = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{resource.Name}|{resource.Kind}|{resource.RegisterSpace}|{resource.RegisterIndex}");
                    if (seen.Add(key))
                    {
                        resources.Add(resource);
                    }
                }
            }
        });

        return resources;
    }

    private static bool TryReadResource(
        JsonElement item,
        out TerminalShaderResourceReflection? resource)
    {
        resource = null;
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? name = GetString(item, "name", "parameterName", "fieldName", "varName");
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string typeText = CollectTypeText(item);
        TerminalShaderResourceKind kind = ResolveResourceKind(typeText);
        if (kind == TerminalShaderResourceKind.RenderTarget)
        {
            return false;
        }

        ReadBinding(item, out int registerIndex, out int registerSpace);
        TerminalShaderValueType valueType = kind switch
        {
            TerminalShaderResourceKind.Texture2D => TerminalShaderValueType.Texture2D,
            TerminalShaderResourceKind.Sampler => TerminalShaderValueType.Sampler,
            TerminalShaderResourceKind.StructuredBuffer => TerminalShaderValueType.StructuredBuffer,
            TerminalShaderResourceKind.ByteAddressBuffer => TerminalShaderValueType.ByteAddressBuffer,
            _ => TerminalShaderValueType.Unknown,
        };

        resource = new TerminalShaderResourceReflection(
            name,
            kind,
            valueType,
            registerIndex,
            registerSpace,
            GetNumber(item, "size", "sizeInBytes", "byteSize"));
        return true;
    }

    private static List<TerminalShaderSemanticReflection> ReadSemantics(
        JsonElement item,
        params string[] arrayNames)
    {
        List<TerminalShaderSemanticReflection> semantics = [];
        for (int i = 0; i < arrayNames.Length; i++)
        {
            if (!TryGetProperty(item, arrayNames[i], out JsonElement array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? name = GetString(element, "semanticName", "semantic", "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                semantics.Add(new TerminalShaderSemanticReflection(
                    name,
                    GetNumber(element, "semanticIndex", "index", "location") ?? 0,
                    ResolveValueType(CollectTypeText(element))));
            }
        }

        return semantics;
    }

    private static TerminalShaderDispatch? GetDispatch(JsonElement item)
    {
        foreach (string propertyName in new[] { "threadGroupSize", "computeThreadGroupSize", "numThreads" })
        {
            if (!TryGetProperty(item, propertyName, out JsonElement value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                int[] values = value.EnumerateArray()
                    .Where(static element => element.ValueKind == JsonValueKind.Number)
                    .Select(static element => element.GetInt32())
                    .Take(3)
                    .ToArray();
                if (values.Length == 3)
                {
                    return new TerminalShaderDispatch(values[0], values[1], values[2]);
                }
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                int? x = GetNumber(value, "x", "X", "width");
                int? y = GetNumber(value, "y", "Y", "height");
                int? z = GetNumber(value, "z", "Z", "depth");
                if (x is not null && y is not null && z is not null)
                {
                    return new TerminalShaderDispatch(x.Value, y.Value, z.Value);
                }
            }
        }

        return null;
    }

    private static void ReadBinding(
        JsonElement item,
        out int registerIndex,
        out int registerSpace)
    {
        registerIndex = GetNumber(item, "bindingIndex", "binding", "registerIndex", "index") ?? -1;
        registerSpace = GetNumber(item, "registerSpace", "space", "descriptorSet", "set") ?? 0;

        if (TryGetProperty(item, "binding", out JsonElement binding) &&
            binding.ValueKind == JsonValueKind.Object)
        {
            registerIndex = GetNumber(binding, "index", "binding", "registerIndex") ?? registerIndex;
            registerSpace = GetNumber(binding, "space", "registerSpace", "set", "descriptorSet") ?? registerSpace;
        }

        string? registerText = GetString(item, "register", "hlslBinding", "bindingName");
        if (registerText is null)
        {
            return;
        }

        Match registerMatch = RegisterRegex().Match(registerText);
        if (registerMatch.Success)
        {
            registerIndex = int.Parse(registerMatch.Groups["index"].Value, CultureInfo.InvariantCulture);
        }

        Match spaceMatch = SpaceRegex().Match(registerText);
        if (spaceMatch.Success)
        {
            registerSpace = int.Parse(spaceMatch.Groups["space"].Value, CultureInfo.InvariantCulture);
        }
    }

    private static TerminalShaderResourceKind ResolveResourceKind(string typeText)
    {
        if (Contains(typeText, "sampler"))
        {
            return TerminalShaderResourceKind.Sampler;
        }

        if (Contains(typeText, "constantbuffer") ||
            Contains(typeText, "cbuffer") ||
            Contains(typeText, "uniform"))
        {
            return TerminalShaderResourceKind.ConstantBuffer;
        }

        if (Contains(typeText, "byteaddress"))
        {
            return TerminalShaderResourceKind.ByteAddressBuffer;
        }

        if (Contains(typeText, "rwtexture") || Contains(typeText, "storageimage"))
        {
            return TerminalShaderResourceKind.UavTexture2D;
        }

        if (Contains(typeText, "rwstructuredbuffer") || Contains(typeText, "storagebuffer"))
        {
            return TerminalShaderResourceKind.UavBuffer;
        }

        if (Contains(typeText, "structuredbuffer"))
        {
            return TerminalShaderResourceKind.StructuredBuffer;
        }

        if (Contains(typeText, "texture"))
        {
            return TerminalShaderResourceKind.Texture2D;
        }

        return TerminalShaderResourceKind.RenderTarget;
    }

    private static TerminalShaderValueType ResolveValueType(string typeText)
    {
        if (Contains(typeText, "float4"))
        {
            return TerminalShaderValueType.Float4;
        }

        if (Contains(typeText, "float3"))
        {
            return TerminalShaderValueType.Float3;
        }

        if (Contains(typeText, "float2"))
        {
            return TerminalShaderValueType.Float2;
        }

        if (Contains(typeText, "uint"))
        {
            return TerminalShaderValueType.UInt;
        }

        if (Contains(typeText, "int"))
        {
            return TerminalShaderValueType.Int;
        }

        return Contains(typeText, "float")
            ? TerminalShaderValueType.Float
            : TerminalShaderValueType.Unknown;
    }

    private static TerminalShaderStage ResolveStage(string? stageText, TerminalShaderStage fallback)
    {
        if (stageText is null)
        {
            return fallback;
        }

        if (stageText.Equals("fragment", StringComparison.OrdinalIgnoreCase) ||
            stageText.Equals("pixel", StringComparison.OrdinalIgnoreCase) ||
            stageText.Equals("5", StringComparison.Ordinal))
        {
            return TerminalShaderStage.Pixel;
        }

        if (stageText.Equals("compute", StringComparison.OrdinalIgnoreCase) ||
            stageText.Equals("6", StringComparison.Ordinal))
        {
            return TerminalShaderStage.Compute;
        }

        return fallback;
    }

    private static string CollectTypeText(JsonElement element)
    {
        List<string> parts = [];
        CollectTypeText(element, parts);
        return string.Join(' ', parts);
    }

    private static void CollectTypeText(JsonElement element, List<string> parts)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                IsTypePropertyName(property.Name))
            {
                string? value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value);
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.Object &&
                     IsTypePropertyName(property.Name))
            {
                CollectTypeText(property.Value, parts);
            }
        }
    }

    private static bool IsTypePropertyName(string name)
    {
        return name.Contains("type", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("kind", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("shape", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("category", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEntryPointArrayName(string name)
    {
        return name.Equals("entryPoints", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("entry_points", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("entrypoints", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsResourceArrayName(string name)
    {
        return name.Equals("parameters", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("resources", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("bindings", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("shaderParameters", StringComparison.OrdinalIgnoreCase);
    }

    private static void VisitNamedArrays(JsonElement element, Action<string, JsonElement> visit)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                visit(property.Name, property.Value);
                foreach (JsonElement child in property.Value.EnumerateArray())
                {
                    VisitNamedArrays(child, visit);
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
            {
                VisitNamedArrays(property.Value, visit);
            }
        }
    }

    private static bool TryGetProperty(JsonElement item, string name, out JsonElement value)
    {
        foreach (JsonProperty property in item.EnumerateObject())
        {
            if (property.NameEquals(name) ||
                property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement item, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (TryGetProperty(item, names[i], out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static int? GetNumber(JsonElement item, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (TryGetProperty(item, names[i], out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out int number))
            {
                return number;
            }
        }

        return null;
    }

    private static bool Contains(string value, string fragment)
    {
        return value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReflectionPayload(TerminalShaderReflection reflection)
    {
        return reflection.EntryPoints.Count > 0 || reflection.Resources.Count > 0;
    }

    [GeneratedRegex(@"(?<kind>[tusbTUSB])(?<index>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex RegisterRegex();

    [GeneratedRegex(@"space(?<space>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpaceRegex();
}
