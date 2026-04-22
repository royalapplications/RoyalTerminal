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
        normalized = RemoveWindowsTerminalInputStructs(normalized);
        normalized = RemoveWindowsTerminalResourceDeclarations(normalized);
        normalized = RemoveHlslRegisterAnnotations(normalized);
        normalized = ReplaceHlslStorageQualifiers(normalized);
        normalized = RemoveFloatSuffixes(normalized);
        normalized = ReplaceShaderTextureSampleCalls(normalized);
        normalized = ReplaceHlslOperators(normalized);
        normalized = ReplaceHlslFunctions(normalized);

        if (!TryExtractMainBody(
            normalized,
            out int mainStart,
            out int mainEnd,
            out string mainBody,
            out string? inputParameterName,
            out string? positionParameterName,
            out string? textureCoordinateParameterName))
        {
            return RuntimePrelude + normalized;
        }

        string helperSource = normalized.Remove(mainStart, mainEnd - mainStart);
        mainBody = NormalizeWindowsTerminalInputReferences(
            mainBody,
            inputParameterName,
            positionParameterName,
            textureCoordinateParameterName);

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

    private static string RemoveFloatSuffixes(string source)
    {
        return FloatSuffixRegex().Replace(source, string.Empty);
    }

    private static string ReplaceShaderTextureSampleCalls(string source)
    {
        string result = ReplaceShaderTextureSampleCalls(source, "shaderTexture");
        return ReplaceShaderTextureSampleCalls(result, "inputTexture");
    }

    private static string ReplaceShaderTextureSampleCalls(string source, string textureName)
    {
        string result = ReplaceFunctionCall(
            source,
            textureName + ".SampleLevel",
            static arguments => $"sampleTerminal({ExtractArgument(arguments, 1, "uv")})");
        return ReplaceFunctionCall(
            result,
            textureName + ".Sample",
            static arguments => $"sampleTerminal({ExtractArgument(arguments, 1, "uv")})");
    }

    private static string ReplaceFunctionCall(
        string source,
        string call,
        Func<string, string?> replacementFactory)
    {
        StringBuilder? builder = null;
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
            builder.Append(source, searchIndex, callIndex - searchIndex);
            builder.Append(replacement);
            searchIndex = closeParen + 1;
        }

        if (builder is null)
        {
            return source;
        }

        builder.Append(source, searchIndex, source.Length - searchIndex);
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
        result = LerpFunctionRegex().Replace(result, "mix(");
        result = FracFunctionRegex().Replace(result, "fract(");
        result = FmodFunctionRegex().Replace(result, "mod(");
        result = Atan2FunctionRegex().Replace(result, "atan(");
        return RsqrtFunctionRegex().Replace(result, "inversesqrt(");
    }

    private static bool TryExtractMainBody(
        string source,
        out int mainStart,
        out int mainEnd,
        out string body,
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
            inputParameterName = null;
            positionParameterName = null;
            textureCoordinateParameterName = null;
            return false;
        }

        mainStart = match.Index;
        mainEnd = closeBrace + 1;
        body = source.Substring(openBrace + 1, closeBrace - openBrace - 1);
        string parameters = match.Groups["parameters"].Value;
        inputParameterName = TryExtractInputParameterName(parameters);
        positionParameterName = TryExtractSemanticParameterName(parameters, "SV_POSITION");
        textureCoordinateParameterName = TryExtractSemanticParameterName(parameters, "TEXCOORD");
        return true;
    }

    private static string? TryExtractInputParameterName(string parameters)
    {
        string firstParameter = ExtractArgument(parameters, 0, string.Empty);
        Match match = InputParameterNameRegex().Match(firstParameter);
        return match.Success ? match.Groups["name"].Value : null;
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

    private static string NormalizeWindowsTerminalInputReferences(
        string body,
        string? inputParameterName,
        string? positionParameterName,
        string? textureCoordinateParameterName)
    {
        string result = body;
        if (string.IsNullOrWhiteSpace(inputParameterName))
        {
            result = ReplaceIdentifier(result, positionParameterName, "pos");
            return ReplaceIdentifier(result, textureCoordinateParameterName, "uv");
        }

        string escapedName = Regex.Escape(inputParameterName);
        result = Regex.Replace(
            result,
            $@"\b(?:float4|half4)\s+pos\s*=\s*{escapedName}\s*\.\s*pos\s*;",
            string.Empty,
            RegexOptions.CultureInvariant);
        result = Regex.Replace(
            result,
            $@"\b(?:float2|half2)\s+uv\s*=\s*{escapedName}\s*\.\s*uv\s*;",
            string.Empty,
            RegexOptions.CultureInvariant);
        result = Regex.Replace(
            result,
            $@"\b{escapedName}\s*\.\s*pos\b",
            "pos",
            RegexOptions.CultureInvariant);
        result = Regex.Replace(
            result,
            $@"\b{escapedName}\s*\.\s*uv\b",
            "uv",
            RegexOptions.CultureInvariant);
        result = ReplaceIdentifier(result, positionParameterName, "pos");
        return ReplaceIdentifier(result, textureCoordinateParameterName, "uv");
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
        for (int i = openIndex; i < source.Length; i++)
        {
            char ch = source[i];
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

    [GeneratedRegex(@"uniform\s+(?:float|int|vec[234]|sampler2D)\s+(?:iResolution|iTime|iTimeDelta|iFrame|iChannel[0-3]|iChannelResolution|iBackgroundColor|iForegroundColor|iCursorColor|iCurrentCursor|iCurrentCursorColor|iCurrentCursorStyle|iCursorVisible)\s*(?:\[[^\]]+\])?\s*;", RegexOptions.Multiline)]
    private static partial Regex GhosttyKnownUniformDeclarationRegex();

    [GeneratedRegex(@"\b(?:ivec2|ivec3|ivec4|vec2|vec3|vec4|mat2|mat3|mat4)\b")]
    private static partial Regex GlslTypeNameRegex();

    [GeneratedRegex(@"#include\s+""SHADERed/[^""]+""")]
    private static partial Regex ShaderedIncludeRegex();

    [GeneratedRegex(@"struct\s+PSInput\s*\{.*?\};", RegexOptions.Singleline)]
    private static partial Regex PsInputStructRegex();

    [GeneratedRegex(@"struct\s+\w+\s*\{[^{}]*(?:SV_POSITION|TEXCOORD\d?)[^{}]*\};", RegexOptions.Singleline)]
    private static partial Regex SemanticInputStructRegex();

    [GeneratedRegex(@"Texture2D(?:\s*<[^>]+>)?\s+shaderTexture\s*(?::\s*register\s*\(\s*t0\s*\))?\s*;")]
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

    [GeneratedRegex(@"(?<=\d)f\b")]
    private static partial Regex FloatSuffixRegex();

    [GeneratedRegex(@"\b(?:float4|half4)\s+main\s*\((?<parameters>[^)]*)\)\s*(?::\s*SV_TARGET\d?)?\s*\{", RegexOptions.Multiline)]
    private static partial Regex WindowsTerminalMainRegex();

    [GeneratedRegex(@"^(?:in\s+)?(?:const\s+)?\w+\s+(?<name>\w+)(?:\s*:.*)?$")]
    private static partial Regex InputParameterNameRegex();

    [GeneratedRegex(@"^(?:in\s+)?(?:const\s+)?(?:float|half)[234]\s+(?<name>\w+)\s*:\s*(?<semantic>\w+\d?)$", RegexOptions.IgnoreCase)]
    private static partial Regex SemanticParameterNameRegex();

    [GeneratedRegex(@"\blerp\s*\(")]
    private static partial Regex LerpFunctionRegex();

    [GeneratedRegex(@"\bfrac\s*\(")]
    private static partial Regex FracFunctionRegex();

    [GeneratedRegex(@"\bfmod\s*\(")]
    private static partial Regex FmodFunctionRegex();

    [GeneratedRegex(@"\batan2\s*\(")]
    private static partial Regex Atan2FunctionRegex();

    [GeneratedRegex(@"\brsqrt\s*\(")]
    private static partial Regex RsqrtFunctionRegex();
}
