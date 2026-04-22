// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Terminal shader compatibility translator.

using System.Text;
using System.Text.RegularExpressions;

namespace RoyalTerminal.Shaders;

/// <summary>
/// Translates compatibility shader sources into Skia Runtime Effect source.
/// </summary>
public static partial class TerminalShaderSourceTranslator
{
    private const string RuntimePrelude = """
        uniform shader shaderTexture;
        uniform shader iChannel0;
        uniform float3 iResolution;
        uniform float iTime;
        uniform float iTimeDelta;
        uniform int iFrame;
        uniform float3 iChannelResolution[4];
        uniform float3 iBackgroundColor;
        uniform float3 iForegroundColor;
        uniform float3 iCursorColor;
        uniform float4 iCurrentCursor;
        uniform float4 iCurrentCursorColor;
        uniform float4 iCurrentCursorStyle;
        uniform float4 iCursorVisible;
        uniform float Time;
        uniform float Scale;
        uniform float2 Resolution;
        uniform float4 Background;

        float4 sampleTerminal(float2 uv) {
            float2 clampedUv = clamp(uv, float2(0.0), float2(1.0));
            return shaderTexture.eval(clampedUv * Resolution);
        }

        float4 sampleTerminalAt(float2 coord) {
            float2 clampedCoord = clamp(coord, float2(0.0), Resolution);
            return shaderTexture.eval(clampedCoord);
        }

        float4 sampleGhosttyChannel(float2 uv) {
            float2 clampedUv = clamp(uv, float2(0.0), float2(1.0));
            return iChannel0.eval(clampedUv * iResolution.xy);
        }

        """;

    /// <summary>
    /// Translates the supplied shader source to the runtime source expected by Skia Runtime Effect.
    /// </summary>
    /// <param name="source">Shader source to translate.</param>
    /// <returns>Skia Runtime Effect source text.</returns>
    public static string Translate(TerminalShaderSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Language switch
        {
            TerminalShaderLanguage.SkiaRuntimeEffect => source.Source,
            TerminalShaderLanguage.GhosttyShadertoy => TranslateGhosttyShadertoy(source.Source),
            TerminalShaderLanguage.WindowsTerminalHlsl => TranslateWindowsTerminalHlsl(source.Source),
            _ => source.Source,
        };
    }

    private static string TranslateGhosttyShadertoy(string source)
    {
        string normalized = NormalizeSource(source);
        normalized = RemoveGlslCompatibilityDirectives(normalized);
        normalized = RemoveKnownGhosttyUniforms(normalized);
        normalized = ReplaceGlslTextureCalls(normalized);
        normalized = ReplaceGlslTypeNames(normalized);

        return RuntimePrelude + normalized + """

            half4 main(float2 fragCoord) {
                float4 fragColor = float4(0.0);
                mainImage(fragColor, fragCoord);
                return half4(fragColor);
            }
            """;
    }

    private static string TranslateWindowsTerminalHlsl(string source)
    {
        string normalized = NormalizeSource(source);
        normalized = RemoveShaderedIncludes(normalized);
        IReadOnlyList<string> textureNames = GetWindowsTerminalTextureNames(normalized);
        string inputStructSource = normalized;
        normalized = RemoveWindowsTerminalInputStructs(normalized);
        normalized = RemoveWindowsTerminalResourceDeclarations(normalized);
        normalized = RemoveHlslRegisterAnnotations(normalized);
        normalized = ReplaceHlslStorageQualifiers(normalized);
        normalized = ReplaceHlslTypeNames(normalized);
        normalized = RemoveFloatSuffixes(normalized);
        normalized = ReplaceShaderTextureSampleCalls(normalized, textureNames);
        normalized = ReplaceHlslOperators(normalized);
        normalized = ReplaceHlslFunctions(normalized);

        if (!TryExtractMainBody(
            normalized,
            out int mainStart,
            out int mainEnd,
            out string mainBody,
            out string? inputParameterType,
            out string? inputParameterName,
            out string? positionParameterName,
            out string? textureCoordinateParameterName))
        {
            return RuntimePrelude + normalized;
        }

        WindowsTerminalInputFields inputFields = FindWindowsTerminalInputFields(inputStructSource, inputParameterType);
        string helperSource = normalized.Remove(mainStart, mainEnd - mainStart);
        mainBody = NormalizeWindowsTerminalInputReferences(
            mainBody,
            inputParameterName,
            positionParameterName,
            textureCoordinateParameterName,
            inputFields);

        return RuntimePrelude + helperSource + """

            half4 main(float2 fragCoord) {
                float4 pos = float4(fragCoord, 0.0, 1.0);
                float2 uv = fragCoord / Resolution;
            """ + mainBody + """

            }
            """;
    }

    private static string NormalizeSource(string source)
    {
        return source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static string RemoveGlslCompatibilityDirectives(string source)
    {
        string result = GlslVersionDirectiveRegex().Replace(source, string.Empty);
        result = RemoveGlslEsGuardLines(result);
        return GlslPrecisionDirectiveRegex().Replace(result, string.Empty);
    }

    private static string RemoveGlslEsGuardLines(string source)
    {
        StringBuilder? builder = null;
        int copyIndex = 0;
        int lineStart = 0;
        int glEsGuardDepth = 0;

        while (lineStart < source.Length)
        {
            int lineEnd = source.IndexOf('\n', lineStart);
            int nextLineStart = lineEnd < 0 ? source.Length : lineEnd + 1;
            ReadOnlySpan<char> line = source.AsSpan(lineStart, nextLineStart - lineStart);
            ReadOnlySpan<char> trimmedLine = line.Trim();
            bool removeLine = IsGlslEsStartGuard(trimmedLine);
            if (removeLine)
            {
                glEsGuardDepth++;
            }
            else if (glEsGuardDepth > 0 && IsGlslEndGuard(trimmedLine))
            {
                glEsGuardDepth--;
                removeLine = true;
            }

            if (removeLine)
            {
                builder ??= new StringBuilder(source.Length);
                builder.Append(source, copyIndex, lineStart - copyIndex);
                copyIndex = nextLineStart;
            }

            lineStart = nextLineStart;
        }

        if (builder is null)
        {
            return source;
        }

        builder.Append(source, copyIndex, source.Length - copyIndex);
        return builder.ToString();
    }

    private static bool IsGlslEsStartGuard(ReadOnlySpan<char> line)
    {
        return line.SequenceEqual("#ifdef GL_ES".AsSpan()) ||
               line.SequenceEqual("#ifndef GL_ES".AsSpan());
    }

    private static bool IsGlslEndGuard(ReadOnlySpan<char> line)
    {
        return line.SequenceEqual("#endif".AsSpan()) ||
               line.SequenceEqual("#endif // GL_ES".AsSpan());
    }

    private static string RemoveKnownGhosttyUniforms(string source)
    {
        return GhosttyKnownUniformDeclarationRegex().Replace(source, string.Empty);
    }

    private static string ReplaceGlslTextureCalls(string source)
    {
        string result = ReplaceFunctionCall(
            source,
            "texture2D",
            static arguments => TryGetTextureCoordinate(arguments, "iChannel0", out string coordinate)
                ? $"sampleGhosttyChannel({coordinate})"
                : null);
        return ReplaceFunctionCall(
            result,
            "texture",
            static arguments => TryGetTextureCoordinate(arguments, "iChannel0", out string coordinate)
                ? $"sampleGhosttyChannel({coordinate})"
                : null);
    }

    private static string ReplaceGlslTypeNames(string source)
    {
        return GlslTypeNameRegex().Replace(
            source,
            static match => match.Value switch
            {
                "ivec2" => "int2",
                "ivec3" => "int3",
                "ivec4" => "int4",
                "vec2" => "float2",
                "vec3" => "float3",
                "vec4" => "float4",
                "mat2" => "float2x2",
                "mat3" => "float3x3",
                "mat4" => "float4x4",
                _ => match.Value,
            });
    }

    private static string RemoveShaderedIncludes(string source)
    {
        return ShaderedIncludeRegex().Replace(source, string.Empty);
    }

    private static string RemoveWindowsTerminalInputStructs(string source)
    {
        string result = PsInputStructRegex().Replace(source, string.Empty);
        return SemanticInputStructRegex().Replace(result, string.Empty);
    }

    private static string RemoveWindowsTerminalResourceDeclarations(string source)
    {
        string result = TextureDeclarationRegex().Replace(source, string.Empty);
        result = SamplerDeclarationRegex().Replace(result, string.Empty);
        return PixelShaderSettingsCbufferRegex().Replace(result, string.Empty);
    }

    private static string RemoveHlslRegisterAnnotations(string source)
    {
        return RegisterAnnotationRegex().Replace(source, string.Empty);
    }

    private static string ReplaceHlslStorageQualifiers(string source)
    {
        string result = StaticConstRegex().Replace(source, "const ");
        return StaticKeywordRegex().Replace(result, string.Empty);
    }

    private static string ReplaceHlslTypeNames(string source)
    {
        return HlslMinPrecisionFloatRegex().Replace(
            source,
            static match => "half" + match.Groups["width"].Value);
    }

    private static string RemoveFloatSuffixes(string source)
    {
        return FloatSuffixRegex().Replace(source, static match => match.Groups["value"].Value);
    }

    private static IReadOnlyList<string> GetWindowsTerminalTextureNames(string source)
    {
        List<string> names = ["shaderTexture", "inputTexture"];
        foreach (Match match in TextureDeclarationRegex().Matches(source))
        {
            string name = match.Groups["name"].Value;
            string registerName = match.Groups["register"].Value;
            if (string.IsNullOrWhiteSpace(name) ||
                (!string.IsNullOrWhiteSpace(registerName) &&
                 !string.Equals(registerName, "t0", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string ReplaceShaderTextureSampleCalls(string source, IReadOnlyList<string> textureNames)
    {
        string result = source;
        foreach (string textureName in textureNames)
        {
            result = ReplaceShaderTextureSampleCalls(result, textureName);
        }

        return result;
    }

    private static string ReplaceShaderTextureSampleCalls(string source, string textureName)
    {
        string result = ReplaceFunctionCall(
            source,
            textureName + ".SampleLevel",
            static arguments => $"sampleTerminal({ExtractArgument(arguments, 1, "uv")})");
        result = ReplaceFunctionCall(
            result,
            textureName + ".SampleBias",
            static arguments => $"sampleTerminal({ExtractArgument(arguments, 1, "uv")})");
        result = ReplaceFunctionCall(
            result,
            textureName + ".SampleGrad",
            static arguments => $"sampleTerminal({ExtractArgument(arguments, 1, "uv")})");
        result = ReplaceFunctionCall(
            result,
            textureName + ".Sample",
            static arguments => $"sampleTerminal({ExtractArgument(arguments, 1, "uv")})");
        return ReplaceFunctionCall(
            result,
            textureName + ".Load",
            static arguments => $"sampleTerminalAt(float2(({ExtractArgument(arguments, 0, "pos")}).xy))");
    }

    private static string ReplaceFunctionCall(
        string source,
        string call,
        Func<string, string?> replacementFactory)
    {
        StringBuilder? builder = null;
        int copyIndex = 0;
        int searchIndex = 0;

        while (searchIndex < source.Length)
        {
            int callIndex = source.IndexOf(call, searchIndex, StringComparison.Ordinal);
            if (callIndex < 0)
            {
                break;
            }

            if (!HasFunctionCallBoundary(source, callIndex))
            {
                searchIndex = callIndex + call.Length;
                continue;
            }

            int openParen = callIndex + call.Length;
            while (openParen < source.Length && char.IsWhiteSpace(source[openParen]))
            {
                openParen++;
            }

            if (openParen >= source.Length || source[openParen] != '(')
            {
                searchIndex = callIndex + call.Length;
                continue;
            }

            int closeParen = FindMatchingBrace(source, openParen, '(', ')');
            if (closeParen < 0)
            {
                break;
            }

            string arguments = source.Substring(openParen + 1, closeParen - openParen - 1);
            string? replacement = replacementFactory(arguments);
            if (replacement is null)
            {
                searchIndex = closeParen + 1;
                continue;
            }

            builder ??= new StringBuilder(source.Length);
            builder.Append(source, copyIndex, callIndex - copyIndex);
            builder.Append(replacement);
            copyIndex = closeParen + 1;
            searchIndex = closeParen + 1;
        }

        if (builder is null)
        {
            return source;
        }

        builder.Append(source, copyIndex, source.Length - copyIndex);
        return builder.ToString();
    }

    private static bool HasFunctionCallBoundary(string source, int callIndex)
    {
        if (callIndex == 0)
        {
            return true;
        }

        char previous = source[callIndex - 1];
        return previous != '.' && !IsShaderIdentifierCharacter(previous);
    }

    private static bool IsShaderIdentifierCharacter(char value)
    {
        return value == '_' || char.IsAsciiLetterOrDigit(value);
    }

    private static bool TryGetTextureCoordinate(string arguments, string expectedTextureName, out string coordinate)
    {
        string textureName = ExtractArgument(arguments, 0, string.Empty);
        if (!string.Equals(textureName, expectedTextureName, StringComparison.Ordinal))
        {
            coordinate = string.Empty;
            return false;
        }

        coordinate = ExtractArgument(arguments, 1, "uv");
        return true;
    }

    private static string ExtractArgument(string arguments, int argumentIndex, string fallback)
    {
        int depth = 0;
        int start = 0;
        int currentArgument = 0;
        for (int i = 0; i < arguments.Length; i++)
        {
            char ch = arguments[i];
            if (ch == '(' || ch == '[' || ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == ')' || ch == ']' || ch == '}')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (ch == ',' && depth == 0)
            {
                if (currentArgument == argumentIndex)
                {
                    return arguments[start..i].Trim();
                }

                currentArgument++;
                start = i + 1;
            }
        }

        return currentArgument == argumentIndex
            ? arguments[start..].Trim()
            : fallback;
    }

    private static string ReplaceHlslOperators(string source)
    {
        return source.Replace(" | ", " || ", StringComparison.Ordinal);
    }

    private static string ReplaceHlslFunctions(string source)
    {
        string result = ReplaceFunctionCall(
            source,
            "saturate",
            static arguments => $"clamp({arguments}, 0.0, 1.0)");
        result = ReplaceFunctionCall(result, "lerp", static arguments => $"mix({arguments})");
        result = ReplaceFunctionCall(result, "frac", static arguments => $"fract({arguments})");
        result = ReplaceFunctionCall(result, "fmod", static arguments => $"mod({arguments})");
        result = ReplaceFunctionCall(result, "atan2", static arguments => $"atan({arguments})");
        result = ReplaceFunctionCall(result, "rsqrt", static arguments => $"inversesqrt({arguments})");
        result = ReplaceFunctionCall(result, "mad", static arguments =>
        {
            string first = ExtractArgument(arguments, 0, "0.0");
            string second = ExtractArgument(arguments, 1, "0.0");
            string third = ExtractArgument(arguments, 2, "0.0");
            return $"(({first}) * ({second}) + ({third}))";
        });
        return ReplaceFunctionCall(result, "mul", static arguments =>
        {
            string first = ExtractArgument(arguments, 0, "0.0");
            string second = ExtractArgument(arguments, 1, "0.0");
            return $"(({first}) * ({second}))";
        });
    }

    private static bool TryExtractMainBody(
        string source,
        out int mainStart,
        out int mainEnd,
        out string body,
        out string? inputParameterType,
        out string? inputParameterName,
        out string? positionParameterName,
        out string? textureCoordinateParameterName)
    {
        Match match = WindowsTerminalMainRegex().Match(source);
        if (!match.Success)
        {
            mainStart = 0;
            mainEnd = 0;
            body = string.Empty;
            inputParameterType = null;
            inputParameterName = null;
            positionParameterName = null;
            textureCoordinateParameterName = null;
            return false;
        }

        int openBrace = source.IndexOf('{', match.Index + match.Length - 1);
        if (openBrace < 0)
        {
            mainStart = 0;
            mainEnd = 0;
            body = string.Empty;
            inputParameterType = null;
            inputParameterName = null;
            positionParameterName = null;
            textureCoordinateParameterName = null;
            return false;
        }

        int closeBrace = FindMatchingBrace(source, openBrace, '{', '}');
        if (closeBrace < 0)
        {
            mainStart = 0;
            mainEnd = 0;
            body = string.Empty;
            inputParameterType = null;
            inputParameterName = null;
            positionParameterName = null;
            textureCoordinateParameterName = null;
            return false;
        }

        mainStart = match.Index;
        mainEnd = closeBrace + 1;
        body = source.Substring(openBrace + 1, closeBrace - openBrace - 1);
        string parameters = match.Groups["parameters"].Value;
        WindowsTerminalInputParameter? inputParameter = TryExtractInputParameter(parameters);
        inputParameterType = inputParameter?.TypeName;
        inputParameterName = inputParameter?.Name;
        positionParameterName = TryExtractSemanticParameterName(parameters, "SV_POSITION");
        textureCoordinateParameterName = TryExtractSemanticParameterName(parameters, "TEXCOORD");
        return true;
    }

    private static WindowsTerminalInputParameter? TryExtractInputParameter(string parameters)
    {
        string firstParameter = ExtractArgument(parameters, 0, string.Empty);
        Match match = InputParameterNameRegex().Match(firstParameter);
        return match.Success
            ? new WindowsTerminalInputParameter(match.Groups["type"].Value, match.Groups["name"].Value)
            : null;
    }

    private static string? TryExtractSemanticParameterName(string parameters, string semanticPrefix)
    {
        for (int argumentIndex = 0; ; argumentIndex++)
        {
            string parameter = ExtractArgument(parameters, argumentIndex, string.Empty);
            if (string.IsNullOrWhiteSpace(parameter))
            {
                return null;
            }

            Match match = SemanticParameterNameRegex().Match(parameter);
            if (!match.Success)
            {
                continue;
            }

            string semantic = match.Groups["semantic"].Value;
            if (semantic.StartsWith(semanticPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["name"].Value;
            }
        }
    }

    private static WindowsTerminalInputFields FindWindowsTerminalInputFields(string source, string? inputParameterType)
    {
        if (string.IsNullOrWhiteSpace(inputParameterType))
        {
            return default;
        }

        foreach (Match structMatch in HlslStructRegex().Matches(source))
        {
            if (!string.Equals(structMatch.Groups["name"].Value, inputParameterType, StringComparison.Ordinal))
            {
                continue;
            }

            string? positionFieldName = null;
            string? textureCoordinateFieldName = null;
            string body = structMatch.Groups["body"].Value;
            foreach (Match fieldMatch in HlslSemanticFieldRegex().Matches(body))
            {
                string semantic = fieldMatch.Groups["semantic"].Value;
                if (semantic.StartsWith("SV_POSITION", StringComparison.OrdinalIgnoreCase))
                {
                    positionFieldName = fieldMatch.Groups["name"].Value;
                    continue;
                }

                if (semantic.StartsWith("TEXCOORD", StringComparison.OrdinalIgnoreCase))
                {
                    textureCoordinateFieldName = fieldMatch.Groups["name"].Value;
                }
            }

            return new WindowsTerminalInputFields(positionFieldName, textureCoordinateFieldName);
        }

        return default;
    }

    private static string NormalizeWindowsTerminalInputReferences(
        string body,
        string? inputParameterName,
        string? positionParameterName,
        string? textureCoordinateParameterName,
        WindowsTerminalInputFields inputFields)
    {
        string result = body;
        if (string.IsNullOrWhiteSpace(inputParameterName))
        {
            result = ReplaceIdentifier(result, positionParameterName, "pos");
            return ReplaceIdentifier(result, textureCoordinateParameterName, "uv");
        }

        string escapedName = Regex.Escape(inputParameterName);
        result = NormalizeInputFieldReference(
            result,
            escapedName,
            "pos",
            "pos",
            @"(?:float4|half4|min16float4|min10float4)");
        result = NormalizeInputFieldReference(
            result,
            escapedName,
            inputFields.PositionFieldName,
            "pos",
            @"(?:float4|half4|min16float4|min10float4)");
        result = NormalizeInputFieldReference(
            result,
            escapedName,
            "uv",
            "uv",
            @"(?:float2|half2|min16float2|min10float2)");
        result = NormalizeInputFieldReference(
            result,
            escapedName,
            inputFields.TextureCoordinateFieldName,
            "uv",
            @"(?:float2|half2|min16float2|min10float2)");
        result = ReplaceIdentifier(result, positionParameterName, "pos");
        return ReplaceIdentifier(result, textureCoordinateParameterName, "uv");
    }

    private static string NormalizeInputFieldReference(
        string source,
        string escapedInputName,
        string? fieldName,
        string replacement,
        string valueTypePattern)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return source;
        }

        string escapedFieldName = Regex.Escape(fieldName);
        string result = Regex.Replace(
            source,
            $@"\b{valueTypePattern}\s+{replacement}\s*=\s*{escapedInputName}\s*\.\s*{escapedFieldName}\s*;",
            string.Empty,
            RegexOptions.CultureInvariant);
        return Regex.Replace(
            result,
            $@"\b{escapedInputName}\s*\.\s*{escapedFieldName}\b",
            replacement,
            RegexOptions.CultureInvariant);
    }

    private static string ReplaceIdentifier(string source, string? identifier, string replacement)
    {
        if (string.IsNullOrWhiteSpace(identifier) ||
            string.Equals(identifier, replacement, StringComparison.Ordinal))
        {
            return source;
        }

        return Regex.Replace(
            source,
            $@"\b{Regex.Escape(identifier)}\b",
            replacement,
            RegexOptions.CultureInvariant);
    }

    private static int FindMatchingBrace(string source, int openIndex, char open, char close)
    {
        int depth = 0;
        bool inLineComment = false;
        bool inBlockComment = false;
        bool inStringLiteral = false;
        bool inCharacterLiteral = false;

        for (int i = openIndex; i < source.Length; i++)
        {
            char ch = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (inLineComment)
            {
                inLineComment = ch != '\n';
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inStringLiteral)
            {
                if (ch == '\\')
                {
                    i++;
                    continue;
                }

                inStringLiteral = ch != '"';
                continue;
            }

            if (inCharacterLiteral)
            {
                if (ch == '\\')
                {
                    i++;
                    continue;
                }

                inCharacterLiteral = ch != '\'';
                continue;
            }

            if (ch == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (ch == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (ch == '"')
            {
                inStringLiteral = true;
                continue;
            }

            if (ch == '\'')
            {
                inCharacterLiteral = true;
                continue;
            }

            if (ch == open)
            {
                depth++;
                continue;
            }

            if (ch != close)
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private readonly record struct WindowsTerminalInputParameter(string TypeName, string Name);

    private readonly record struct WindowsTerminalInputFields(
        string? PositionFieldName,
        string? TextureCoordinateFieldName);

    [GeneratedRegex(@"uniform\s+(?:float|int|vec[234]|sampler2D)\s+(?:iResolution|iTime|iTimeDelta|iFrame|iChannel[0-3]|iChannelResolution|iBackgroundColor|iForegroundColor|iCursorColor|iCurrentCursor|iCurrentCursorColor|iCurrentCursorStyle|iCursorVisible)\s*(?:\[[^\]]+\])?\s*;", RegexOptions.Multiline)]
    private static partial Regex GhosttyKnownUniformDeclarationRegex();

    [GeneratedRegex(@"^\s*#version[^\n]*(?:\n|$)", RegexOptions.Multiline)]
    private static partial Regex GlslVersionDirectiveRegex();

    [GeneratedRegex(@"^\s*precision\s+(?:lowp|mediump|highp)\s+(?:float|int)\s*;\s*(?:\n|$)", RegexOptions.Multiline)]
    private static partial Regex GlslPrecisionDirectiveRegex();

    [GeneratedRegex(@"\b(?:ivec2|ivec3|ivec4|vec2|vec3|vec4|mat2|mat3|mat4)\b")]
    private static partial Regex GlslTypeNameRegex();

    [GeneratedRegex(@"#include\s+""SHADERed/[^""]+""")]
    private static partial Regex ShaderedIncludeRegex();

    [GeneratedRegex(@"struct\s+PSInput\s*\{.*?\};", RegexOptions.Singleline)]
    private static partial Regex PsInputStructRegex();

    [GeneratedRegex(@"struct\s+\w+\s*\{[^{}]*(?:SV_POSITION|TEXCOORD\d?)[^{}]*\};", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SemanticInputStructRegex();

    [GeneratedRegex(@"Texture2D(?:\s*<[^>]+>)?\s+(?<name>\w+)\s*(?::\s*register\s*\(\s*(?<register>t\d+)\s*\))?\s*;", RegexOptions.IgnoreCase)]
    private static partial Regex TextureDeclarationRegex();

    [GeneratedRegex(@"SamplerState\s+\w+\s*(?::\s*register\s*\(\s*s\d+\s*\))?\s*;")]
    private static partial Regex SamplerDeclarationRegex();

    [GeneratedRegex(@"cbuffer\s+PixelShaderSettings\s*(?::\s*register\s*\(\s*b0\s*\))?\s*\{.*?\};", RegexOptions.Singleline)]
    private static partial Regex PixelShaderSettingsCbufferRegex();

    [GeneratedRegex(@"\s*:\s*register\s*\([^)]+\)")]
    private static partial Regex RegisterAnnotationRegex();

    [GeneratedRegex(@"\bstatic\s+const\s+")]
    private static partial Regex StaticConstRegex();

    [GeneratedRegex(@"\bstatic\s+")]
    private static partial Regex StaticKeywordRegex();

    [GeneratedRegex(@"\bmin(?:10|16)float(?<width>[234]?)\b")]
    private static partial Regex HlslMinPrecisionFloatRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<value>(?:\d+\.\d*|\.\d+|\d+)(?:[eE][+-]?\d+)?)[fF]\b")]
    private static partial Regex FloatSuffixRegex();

    [GeneratedRegex(@"\b(?:float4|half4|min16float4|min10float4)\s+main\s*\((?<parameters>[^)]*)\)\s*(?::\s*SV_TARGET\d?)?\s*\{", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex WindowsTerminalMainRegex();

    [GeneratedRegex(@"^(?:(?:in|out|inout)\s+)?(?:const\s+)?(?<type>\w+)\s+(?<name>\w+)(?:\s*:.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex InputParameterNameRegex();

    [GeneratedRegex(@"^(?:(?:in|out|inout)\s+)?(?:const\s+)?(?:float|half|min16float|min10float)[234]\s+(?<name>\w+)\s*:\s*(?<semantic>\w+\d?)$", RegexOptions.IgnoreCase)]
    private static partial Regex SemanticParameterNameRegex();

    [GeneratedRegex(@"struct\s+(?<name>\w+)\s*\{(?<body>.*?)\};", RegexOptions.Singleline)]
    private static partial Regex HlslStructRegex();

    [GeneratedRegex(@"(?:(?:linear|centroid|nointerpolation|noperspective|sample)\s+)*(?:float|half|min16float|min10float)[234]\s+(?<name>\w+)\s*:\s*(?<semantic>\w+\d?)", RegexOptions.IgnoreCase)]
    private static partial Regex HlslSemanticFieldRegex();
}
