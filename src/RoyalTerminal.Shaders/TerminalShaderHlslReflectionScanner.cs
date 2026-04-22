// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - HLSL source-side reflection scanner.

using System.Globalization;
using System.Text.RegularExpressions;

namespace RoyalTerminal.Shaders;

/// <summary>
/// Scans HLSL package source for resource declarations, entry-point semantics, and compute thread-group metadata.
/// </summary>
/// <remarks>
/// This is a deterministic preflight scanner, not a replacement for compiler reflection.
/// Runtime backends should prefer native compiler reflection when available.
/// </remarks>
public static partial class TerminalShaderHlslReflectionScanner
{
    /// <summary>
    /// Scans a shader package for source-side reflection data.
    /// </summary>
    /// <param name="package">Shader package.</param>
    /// <param name="resolvedFiles">Optional resolved file set. When omitted, package files are used.</param>
    /// <returns>The reflection result.</returns>
    public static TerminalShaderReflectionResult ScanPackage(
        TerminalShaderPackage package,
        IReadOnlyList<TerminalShaderFile>? resolvedFiles = null)
    {
        ArgumentNullException.ThrowIfNull(package);

        IReadOnlyList<TerminalShaderFile> files = resolvedFiles ?? package.Files;
        Dictionary<string, string> sourceByPath = BuildSourceLookup(files);
        string combinedSource = string.Join(Environment.NewLine, files.Select(static file => file.Source));
        string normalizedCombinedSource = RemoveComments(combinedSource);
        IReadOnlyDictionary<string, IReadOnlyList<TerminalShaderSemanticReflection>> structs =
            ScanStructSemantics(normalizedCombinedSource);

        List<TerminalShaderDiagnostic> diagnostics = [];
        List<TerminalShaderEntryPointReflection> entryPoints = [];
        for (int i = 0; i < package.Passes.Count; i++)
        {
            TerminalShaderPass pass = package.Passes[i];
            if (pass.Stage is not (TerminalShaderStage.Pixel or TerminalShaderStage.Compute))
            {
                continue;
            }

            TerminalShaderReflectionResult passResult = ScanPass(files, pass);
            diagnostics.AddRange(passResult.Diagnostics);
            entryPoints.AddRange(passResult.Reflection.EntryPoints);
        }

        TerminalShaderReflection reflection = new(
            entryPoints,
            ScanResources(normalizedCombinedSource));
        return new TerminalShaderReflectionResult(reflection, diagnostics);
    }

    /// <summary>
    /// Scans one pass from a resolved source file set.
    /// </summary>
    /// <param name="resolvedFiles">Resolved source files.</param>
    /// <param name="pass">Pass to scan.</param>
    /// <returns>The reflection result for the pass.</returns>
    public static TerminalShaderReflectionResult ScanPass(
        IReadOnlyList<TerminalShaderFile> resolvedFiles,
        TerminalShaderPass pass)
    {
        ArgumentNullException.ThrowIfNull(resolvedFiles);
        ArgumentNullException.ThrowIfNull(pass);

        Dictionary<string, string> sourceByPath = BuildSourceLookup(resolvedFiles);
        string combinedSource = string.Join(Environment.NewLine, resolvedFiles.Select(static file => file.Source));
        string normalizedCombinedSource = RemoveComments(combinedSource);
        IReadOnlyDictionary<string, IReadOnlyList<TerminalShaderSemanticReflection>> structs =
            ScanStructSemantics(normalizedCombinedSource);
        List<TerminalShaderDiagnostic> diagnostics = [];
        List<TerminalShaderEntryPointReflection> entryPoints = [];

        if (pass.SourcePath is null || pass.EntryPoint is null)
        {
            diagnostics.Add(new TerminalShaderDiagnostic(
                TerminalShaderDiagnosticSeverity.Warning,
                "RTSHADERREF001",
                $"Pass '{pass.Name}' has no source path or entry point."));
        }
        else if (!sourceByPath.TryGetValue(pass.SourcePath, out string? passSource))
        {
            diagnostics.Add(new TerminalShaderDiagnostic(
                TerminalShaderDiagnosticSeverity.Warning,
                "RTSHADERREF002",
                $"Pass '{pass.Name}' references source file '{pass.SourcePath}', but it was not available for reflection.",
                pass.SourcePath));
        }
        else
        {
            string normalizedPassSource = RemoveComments(passSource);
            TerminalShaderEntryPointReflection entryPoint = ScanEntryPoint(
                pass,
                normalizedPassSource,
                structs);
            entryPoints.Add(entryPoint);
        }

        TerminalShaderReflection reflection = new(
            entryPoints,
            ScanResources(normalizedCombinedSource));
        return new TerminalShaderReflectionResult(reflection, diagnostics);
    }

    private static Dictionary<string, string> BuildSourceLookup(IReadOnlyList<TerminalShaderFile> files)
    {
        Dictionary<string, string> lookup = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < files.Count; i++)
        {
            TerminalShaderFile file = files[i];
            lookup[file.VirtualPath] = file.Source;
        }

        return lookup;
    }

    private static IReadOnlyList<TerminalShaderResourceReflection> ScanResources(string source)
    {
        Dictionary<string, TerminalShaderResourceReflection> resources = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in CBufferRegex().Matches(source))
        {
            string name = match.Groups["name"].Value;
            (int registerIndex, int registerSpace) = ParseRegister(match);
            int? sizeInBytes = ComputeConstantBufferSize(match.Groups["body"].Value);
            resources[name] = new TerminalShaderResourceReflection(
                name,
                TerminalShaderResourceKind.ConstantBuffer,
                TerminalShaderValueType.Unknown,
                registerIndex,
                registerSpace,
                sizeInBytes);
        }

        foreach (Match match in ResourceRegex().Matches(source))
        {
            string type = NormalizeTypeName(match.Groups["type"].Value);
            string name = match.Groups["name"].Value;
            (TerminalShaderResourceKind kind, TerminalShaderValueType valueType) = MapResourceType(type);
            (int registerIndex, int registerSpace) = ParseRegister(match);
            resources[name] = new TerminalShaderResourceReflection(
                name,
                kind,
                valueType,
                registerIndex,
                registerSpace);
        }

        return resources.Values
            .OrderBy(static resource => resource.RegisterSpace)
            .ThenBy(static resource => resource.RegisterIndex)
            .ThenBy(static resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TerminalShaderSemanticReflection>> ScanStructSemantics(
        string source)
    {
        Dictionary<string, IReadOnlyList<TerminalShaderSemanticReflection>> structs = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match structMatch in StructRegex().Matches(source))
        {
            List<TerminalShaderSemanticReflection> semantics = [];
            string body = structMatch.Groups["body"].Value;
            foreach (Match fieldMatch in SemanticFieldRegex().Matches(body))
            {
                semantics.Add(CreateSemantic(
                    fieldMatch.Groups["semantic"].Value,
                    MapValueType(fieldMatch.Groups["type"].Value)));
            }

            structs[structMatch.Groups["name"].Value] = semantics;
        }

        return structs;
    }

    private static TerminalShaderEntryPointReflection ScanEntryPoint(
        TerminalShaderPass pass,
        string source,
        IReadOnlyDictionary<string, IReadOnlyList<TerminalShaderSemanticReflection>> structs)
    {
        List<TerminalShaderSemanticReflection> inputs = [];
        List<TerminalShaderSemanticReflection> outputs = [];
        TerminalShaderDispatch? threadGroupSize = pass.Stage == TerminalShaderStage.Compute
            ? ScanThreadGroupSize(pass.EntryPoint!, source) ?? pass.Dispatch
            : null;

        Match functionMatch = FunctionRegex(pass.EntryPoint!).Match(source);
        if (functionMatch.Success)
        {
            string returnType = NormalizeTypeName(functionMatch.Groups["returnType"].Value);
            string outputSemantic = functionMatch.Groups["semantic"].Value;
            string parameters = functionMatch.Groups["parameters"].Value;

            foreach (string parameter in SplitParameters(parameters))
            {
                Match directSemanticMatch = ParameterSemanticRegex().Match(parameter);
                if (!directSemanticMatch.Success)
                {
                    continue;
                }

                string parameterType = NormalizeTypeName(directSemanticMatch.Groups["type"].Value);
                if (structs.TryGetValue(parameterType, out IReadOnlyList<TerminalShaderSemanticReflection>? structInputs))
                {
                    inputs.AddRange(structInputs);
                    continue;
                }

                string semanticName = directSemanticMatch.Groups["semantic"].Value;
                if (!string.IsNullOrWhiteSpace(semanticName))
                {
                    inputs.Add(CreateSemantic(semanticName, MapValueType(parameterType)));
                }
            }

            if (!string.IsNullOrWhiteSpace(outputSemantic))
            {
                outputs.Add(CreateSemantic(outputSemantic, MapValueType(returnType)));
            }
            else if (structs.TryGetValue(returnType, out IReadOnlyList<TerminalShaderSemanticReflection>? structOutputs))
            {
                outputs.AddRange(structOutputs);
            }
        }

        return new TerminalShaderEntryPointReflection(
            pass.EntryPoint!,
            pass.Stage,
            threadGroupSize,
            DeduplicateSemantics(inputs),
            DeduplicateSemantics(outputs));
    }

    private static TerminalShaderDispatch? ScanThreadGroupSize(string entryPoint, string source)
    {
        Match match = ThreadGroupRegex(entryPoint).Match(source);
        if (!match.Success)
        {
            return null;
        }

        return new TerminalShaderDispatch(
            ParsePositiveInt(match.Groups["x"].Value),
            ParsePositiveInt(match.Groups["y"].Value),
            ParsePositiveInt(match.Groups["z"].Value));
    }

    private static IReadOnlyList<TerminalShaderSemanticReflection> DeduplicateSemantics(
        IReadOnlyList<TerminalShaderSemanticReflection> semantics)
    {
        Dictionary<string, TerminalShaderSemanticReflection> deduplicated = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < semantics.Count; i++)
        {
            TerminalShaderSemanticReflection semantic = semantics[i];
            string key = string.Create(
                CultureInfo.InvariantCulture,
                $"{semantic.Name}{semantic.SemanticIndex}");
            deduplicated[key] = semantic;
        }

        return deduplicated.Values.ToArray();
    }

    private static IEnumerable<string> SplitParameters(string parameters)
    {
        foreach (string parameter in parameters.Split(','))
        {
            string trimmed = parameter.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    private static TerminalShaderSemanticReflection CreateSemantic(
        string rawSemantic,
        TerminalShaderValueType valueType)
    {
        Match match = SemanticNameRegex().Match(rawSemantic.Trim());
        string name = match.Success ? match.Groups["name"].Value : rawSemantic.Trim();
        int index = match.Success && match.Groups["index"].Success
            ? ParseNonNegativeInt(match.Groups["index"].Value)
            : 0;
        return new TerminalShaderSemanticReflection(name, index, valueType);
    }

    private static (TerminalShaderResourceKind Kind, TerminalShaderValueType ValueType) MapResourceType(string type)
    {
        if (type.StartsWith("RWTexture2D", StringComparison.OrdinalIgnoreCase))
        {
            return (TerminalShaderResourceKind.UavTexture2D, TerminalShaderValueType.Texture2D);
        }

        if (type.StartsWith("Texture2D", StringComparison.OrdinalIgnoreCase))
        {
            return (TerminalShaderResourceKind.Texture2D, TerminalShaderValueType.Texture2D);
        }

        if (type.StartsWith("Sampler", StringComparison.OrdinalIgnoreCase))
        {
            return (TerminalShaderResourceKind.Sampler, TerminalShaderValueType.Sampler);
        }

        if (type.StartsWith("RWStructuredBuffer", StringComparison.OrdinalIgnoreCase) ||
            type.StartsWith("RWByteAddressBuffer", StringComparison.OrdinalIgnoreCase))
        {
            return (TerminalShaderResourceKind.UavBuffer, TerminalShaderValueType.StructuredBuffer);
        }

        if (type.StartsWith("StructuredBuffer", StringComparison.OrdinalIgnoreCase) ||
            type.StartsWith("Buffer", StringComparison.OrdinalIgnoreCase))
        {
            return (TerminalShaderResourceKind.StructuredBuffer, TerminalShaderValueType.StructuredBuffer);
        }

        if (type.StartsWith("ByteAddressBuffer", StringComparison.OrdinalIgnoreCase))
        {
            return (TerminalShaderResourceKind.ByteAddressBuffer, TerminalShaderValueType.ByteAddressBuffer);
        }

        return (TerminalShaderResourceKind.Texture2D, TerminalShaderValueType.Unknown);
    }

    private static TerminalShaderValueType MapValueType(string rawType)
    {
        string type = NormalizeTypeName(rawType);
        return type switch
        {
            "float" or "half" => TerminalShaderValueType.Float,
            "float2" or "half2" => TerminalShaderValueType.Float2,
            "float3" or "half3" => TerminalShaderValueType.Float3,
            "float4" or "half4" => TerminalShaderValueType.Float4,
            "float4x4" or "half4x4" => TerminalShaderValueType.Matrix4x4,
            "int" or "int1" => TerminalShaderValueType.Int,
            "uint" or "uint1" => TerminalShaderValueType.UInt,
            "bool" => TerminalShaderValueType.Bool,
            _ => TerminalShaderValueType.Unknown,
        };
    }

    private static int? ComputeConstantBufferSize(string body)
    {
        int offset = 0;
        int rowUsed = 0;
        bool matchedAny = false;
        foreach (Match match in ConstantBufferFieldRegex().Matches(body))
        {
            matchedAny = true;
            string type = NormalizeTypeName(match.Groups["type"].Value);
            int arrayLength = match.Groups["array"].Success
                ? Math.Max(1, ParsePositiveInt(match.Groups["array"].Value))
                : 1;
            (int bytes, int rows, bool forceRowAlignment) = GetConstantBufferFieldLayout(type, arrayLength);
            if (bytes <= 0)
            {
                continue;
            }

            if (forceRowAlignment)
            {
                if (rowUsed > 0)
                {
                    offset += 16 - rowUsed;
                    rowUsed = 0;
                }

                offset += rows * 16;
                continue;
            }

            if (rowUsed + bytes > 16)
            {
                offset += 16 - rowUsed;
                rowUsed = 0;
            }

            offset += bytes;
            rowUsed += bytes;
            if (rowUsed == 16)
            {
                rowUsed = 0;
            }
        }

        if (!matchedAny)
        {
            return null;
        }

        if (rowUsed > 0)
        {
            offset += 16 - rowUsed;
        }

        return Math.Max(16, offset);
    }

    private static (int Bytes, int Rows, bool ForceRowAlignment) GetConstantBufferFieldLayout(
        string type,
        int arrayLength)
    {
        int scalarSize = type.StartsWith("bool", StringComparison.OrdinalIgnoreCase) ? 4 : 4;
        if (type.Contains('x', StringComparison.Ordinal))
        {
            string[] parts = type.TrimStart('f', 'l', 'o', 'a', 't', 'h', 'a', 'l', 'f')
                .Split('x', StringSplitOptions.RemoveEmptyEntries);
            int rows = parts.Length > 0 ? ParsePositiveInt(parts[0]) : 4;
            return (0, rows * arrayLength, true);
        }

        Match vectorMatch = VectorTypeRegex().Match(type);
        int components = vectorMatch.Success && vectorMatch.Groups["components"].Success
            ? ParsePositiveInt(vectorMatch.Groups["components"].Value)
            : 1;
        int bytes = Math.Clamp(components, 1, 4) * scalarSize;
        if (arrayLength > 1)
        {
            return (0, arrayLength, true);
        }

        return (bytes, 1, false);
    }

    private static (int RegisterIndex, int RegisterSpace) ParseRegister(Match match)
    {
        int registerIndex = match.Groups["index"].Success
            ? ParseNonNegativeInt(match.Groups["index"].Value)
            : -1;
        int registerSpace = match.Groups["space"].Success
            ? ParseNonNegativeInt(match.Groups["space"].Value)
            : 0;
        return (registerIndex, registerSpace);
    }

    private static string NormalizeTypeName(string type)
    {
        return GenericArgumentRegex().Replace(type.Trim(), string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string RemoveComments(string source)
    {
        return BlockCommentRegex().Replace(LineCommentRegex().Replace(source, string.Empty), string.Empty);
    }

    private static int ParsePositiveInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) && result > 0
            ? result
            : 1;
    }

    private static int ParseNonNegativeInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) && result >= 0
            ? result
            : 0;
    }

    [GeneratedRegex(@"/\*[\s\S]*?\*/", RegexOptions.CultureInvariant)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"//.*?$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex(@"\bcbuffer\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*register\s*\(\s*b(?<index>\d+)(?:\s*,\s*space(?<space>\d+))?\s*\))?\s*\{(?<body>[\s\S]*?)\}\s*;?", RegexOptions.CultureInvariant)]
    private static partial Regex CBufferRegex();

    [GeneratedRegex(@"\b(?<type>(?:RW)?Texture2D(?:\s*<[^>]+>)?|Sampler(?:Comparison)?State|(?:RW)?StructuredBuffer\s*<[^>]+>|(?:RW)?ByteAddressBuffer|ByteAddressBuffer|Buffer\s*<[^>]+>)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*register\s*\(\s*(?<class>[tus])(?<index>\d+)(?:\s*,\s*space(?<space>\d+))?\s*\))?\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceRegex();

    [GeneratedRegex(@"\bstruct\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{(?<body>[\s\S]*?)\}\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex StructRegex();

    [GeneratedRegex(@"\b(?<type>[A-Za-z_][A-Za-z0-9_]*(?:\d(?:x\d)?)?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s*\[[^\]]+\])?\s*:\s*(?<semantic>[A-Za-z_][A-Za-z0-9_]*)\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticFieldRegex();

    [GeneratedRegex(@"^\s*(?<type>[A-Za-z_][A-Za-z0-9_]*(?:\d(?:x\d)?)?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s*:\s*(?<semantic>[A-Za-z_][A-Za-z0-9_]*))?", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterSemanticRegex();

    [GeneratedRegex(@"^(?<name>[A-Za-z_]+?)(?<index>\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticNameRegex();

    [GeneratedRegex(@"(?<type>(?:float|half|int|uint|bool)(?:\d(?:x\d)?)?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s*\[\s*(?<array>\d+)\s*\])?\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex ConstantBufferFieldRegex();

    [GeneratedRegex(@"^(?:float|half|int|uint|bool)(?<components>[1-4])?$", RegexOptions.CultureInvariant)]
    private static partial Regex VectorTypeRegex();

    [GeneratedRegex(@"\s*<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex GenericArgumentRegex();

    private static Regex FunctionRegex(string entryPoint)
    {
        return new Regex(
            string.Create(
                CultureInfo.InvariantCulture,
                $@"\b(?<returnType>[A-Za-z_][A-Za-z0-9_]*(?:\d(?:x\d)?)?)\s+{Regex.Escape(entryPoint)}\s*\((?<parameters>[^)]*)\)\s*(?::\s*(?<semantic>[A-Za-z_][A-Za-z0-9_]*))?"),
            RegexOptions.CultureInvariant);
    }

    private static Regex ThreadGroupRegex(string entryPoint)
    {
        return new Regex(
            string.Create(
                CultureInfo.InvariantCulture,
                $@"\[numthreads\s*\(\s*(?<x>\d+)\s*,\s*(?<y>\d+)\s*,\s*(?<z>\d+)\s*\)\]\s*[\s\S]*?\b{Regex.Escape(entryPoint)}\s*\("),
            RegexOptions.CultureInvariant);
    }
}
