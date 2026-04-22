// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader reflection model.

using System.Globalization;
using System.Text.RegularExpressions;

namespace RoyalTerminal.Shaders;

/// <summary>
/// Reads reflection metadata from DXC text listings.
/// </summary>
public static partial class TerminalShaderDxcReflectionListingReader
{
    private enum SectionKind
    {
        None,
        InputSignature,
        OutputSignature,
        ResourceBindings,
    }

    /// <summary>
    /// Attempts to read shader reflection from DXC listing text.
    /// </summary>
    /// <param name="listingText">DXC listing text.</param>
    /// <param name="pass">Shader pass that produced the listing.</param>
    /// <returns>Reflected metadata and diagnostics.</returns>
    public static TerminalShaderReflectionResult TryRead(
        string listingText,
        TerminalShaderPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);

        if (string.IsNullOrWhiteSpace(listingText))
        {
            return MissingPayload();
        }

        List<TerminalShaderSemanticReflection> inputs = [];
        List<TerminalShaderSemanticReflection> outputs = [];
        List<TerminalShaderResourceReflection> resources = [];
        TerminalShaderDispatch? threadGroupSize = null;
        SectionKind section = SectionKind.None;

        string[] lines = listingText.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = NormalizeListingLine(lines[i]);
            if (line.Length == 0)
            {
                continue;
            }

            if (TryUpdateSection(line, out SectionKind nextSection))
            {
                section = nextSection;
                continue;
            }

            threadGroupSize ??= TryParseThreadGroupSize(line);
            if (!IsDataLine(line))
            {
                continue;
            }

            switch (section)
            {
                case SectionKind.InputSignature:
                    if (TryParseSemantic(line, out TerminalShaderSemanticReflection? input) && input is not null)
                    {
                        inputs.Add(input);
                    }

                    break;

                case SectionKind.OutputSignature:
                    if (TryParseSemantic(line, out TerminalShaderSemanticReflection? output) && output is not null)
                    {
                        outputs.Add(output);
                    }

                    break;

                case SectionKind.ResourceBindings:
                    if (TryParseResource(line, out TerminalShaderResourceReflection? resource) && resource is not null)
                    {
                        resources.Add(resource);
                    }

                    break;
            }
        }

        TerminalShaderReflection reflection = new(
            [
                new TerminalShaderEntryPointReflection(
                    pass.EntryPoint ?? pass.Name,
                    pass.Stage,
                    threadGroupSize,
                    inputs,
                    outputs),
            ],
            resources);

        return HasReflectionPayload(reflection)
            ? new TerminalShaderReflectionResult(reflection)
            : MissingPayload();
    }

    private static TerminalShaderReflectionResult MissingPayload()
    {
        return new TerminalShaderReflectionResult(
            diagnostics:
            [
                new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Warning,
                    "RTSHADERDXCREFLECT001",
                    "DXC listing did not contain enough reflection metadata; falling back to source-side reflection."),
            ]);
    }

    private static bool TryUpdateSection(string line, out SectionKind section)
    {
        if (line.Contains("Input signature:", StringComparison.OrdinalIgnoreCase))
        {
            section = SectionKind.InputSignature;
            return true;
        }

        if (line.Contains("Output signature:", StringComparison.OrdinalIgnoreCase))
        {
            section = SectionKind.OutputSignature;
            return true;
        }

        if (line.Contains("Resource Bindings:", StringComparison.OrdinalIgnoreCase))
        {
            section = SectionKind.ResourceBindings;
            return true;
        }

        section = SectionKind.None;
        return false;
    }

    private static string NormalizeListingLine(string line)
    {
        string value = line.Trim();
        while (value.StartsWith(';'))
        {
            value = value[1..].TrimStart();
        }

        return value;
    }

    private static bool IsDataLine(string line)
    {
        return line.Length > 0 &&
               !line.StartsWith('-') &&
               !line.StartsWith("Name ", StringComparison.OrdinalIgnoreCase) &&
               !line.StartsWith("----", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSemantic(
        string line,
        out TerminalShaderSemanticReflection? semantic)
    {
        semantic = null;
        string[] tokens = SplitColumns(line);
        if (tokens.Length < 2)
        {
            return false;
        }

        string name = tokens[0];
        if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            index = 0;
        }

        TerminalShaderValueType valueType = TerminalShaderValueType.Unknown;
        string? mask = tokens.Length > 2 ? tokens[2] : null;
        string? format = tokens.FirstOrDefault(
            static token => token.Contains("float", StringComparison.OrdinalIgnoreCase) ||
                            token.Contains("uint", StringComparison.OrdinalIgnoreCase) ||
                            token.Contains("int", StringComparison.OrdinalIgnoreCase));
        if (format is not null)
        {
            valueType = ResolveScalarVectorValueType(format, mask);
        }

        semantic = new TerminalShaderSemanticReflection(name, index, valueType);
        return true;
    }

    private static bool TryParseResource(
        string line,
        out TerminalShaderResourceReflection? resource)
    {
        resource = null;
        string[] tokens = SplitColumns(line);
        if (tokens.Length < 2)
        {
            return false;
        }

        string name = tokens[0];
        string type = tokens[1];
        string? dimension = tokens.FirstOrDefault(
            static token => token.Equals("2d", StringComparison.OrdinalIgnoreCase) ||
                            token.Equals("buf", StringComparison.OrdinalIgnoreCase) ||
                            token.Contains("buffer", StringComparison.OrdinalIgnoreCase));
        TryParseRegister(tokens, out char registerKind, out int registerIndex, out int registerSpace);

        TerminalShaderResourceKind kind = ResolveResourceKind(type, dimension, registerKind);
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
            registerSpace);
        return true;
    }

    private static TerminalShaderResourceKind ResolveResourceKind(
        string type,
        string? dimension,
        char registerKind)
    {
        if (type.Contains("sampler", StringComparison.OrdinalIgnoreCase) || registerKind == 's')
        {
            return TerminalShaderResourceKind.Sampler;
        }

        if (type.Contains("cbuffer", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("constant", StringComparison.OrdinalIgnoreCase) ||
            registerKind == 'b')
        {
            return TerminalShaderResourceKind.ConstantBuffer;
        }

        if (type.Contains("byte", StringComparison.OrdinalIgnoreCase))
        {
            return TerminalShaderResourceKind.ByteAddressBuffer;
        }

        if (type.Contains("structured", StringComparison.OrdinalIgnoreCase))
        {
            return registerKind == 'u'
                ? TerminalShaderResourceKind.UavBuffer
                : TerminalShaderResourceKind.StructuredBuffer;
        }

        if (registerKind == 'u' ||
            type.Contains("uav", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("rw", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(dimension, "2d", StringComparison.OrdinalIgnoreCase)
                ? TerminalShaderResourceKind.UavTexture2D
                : TerminalShaderResourceKind.UavBuffer;
        }

        return TerminalShaderResourceKind.Texture2D;
    }

    private static void TryParseRegister(
        IReadOnlyList<string> tokens,
        out char registerKind,
        out int registerIndex,
        out int registerSpace)
    {
        registerKind = '\0';
        registerIndex = -1;
        registerSpace = 0;
        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            Match match = RegisterRegex().Match(tokens[i]);
            if (match.Success)
            {
                registerKind = char.ToLowerInvariant(match.Groups["kind"].Value[0]);
                _ = int.TryParse(
                    match.Groups["index"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out registerIndex);
            }

            Match spaceMatch = SpaceRegex().Match(tokens[i]);
            if (spaceMatch.Success)
            {
                _ = int.TryParse(
                    spaceMatch.Groups["space"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out registerSpace);
            }
        }
    }

    private static TerminalShaderDispatch? TryParseThreadGroupSize(string line)
    {
        if (!line.Contains("numthreads", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("thread group", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        MatchCollection matches = IntegerRegex().Matches(line);
        if (matches.Count < 3)
        {
            return null;
        }

        return new TerminalShaderDispatch(
            int.Parse(matches[0].Value, CultureInfo.InvariantCulture),
            int.Parse(matches[1].Value, CultureInfo.InvariantCulture),
            int.Parse(matches[2].Value, CultureInfo.InvariantCulture));
    }

    private static TerminalShaderValueType ResolveScalarVectorValueType(
        string format,
        string? mask)
    {
        int componentCount = string.IsNullOrWhiteSpace(mask)
            ? 1
            : mask.Count(static c => c is 'x' or 'y' or 'z' or 'w');
        if (format.Contains("uint", StringComparison.OrdinalIgnoreCase))
        {
            return TerminalShaderValueType.UInt;
        }

        if (format.Contains("int", StringComparison.OrdinalIgnoreCase))
        {
            return TerminalShaderValueType.Int;
        }

        return componentCount switch
        {
            2 => TerminalShaderValueType.Float2,
            3 => TerminalShaderValueType.Float3,
            4 => TerminalShaderValueType.Float4,
            _ => TerminalShaderValueType.Float,
        };
    }

    private static string[] SplitColumns(string line)
    {
        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool HasReflectionPayload(TerminalShaderReflection reflection)
    {
        return reflection.EntryPoints.Count > 0 &&
               (reflection.EntryPoints[0].Inputs.Count > 0 ||
                reflection.EntryPoints[0].Outputs.Count > 0 ||
                reflection.Resources.Count > 0 ||
                reflection.EntryPoints[0].ThreadGroupSize is not null);
    }

    [GeneratedRegex(@"^(?<kind>[tusbTUSB])(?<index>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex RegisterRegex();

    [GeneratedRegex(@"space(?<space>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerRegex();
}
