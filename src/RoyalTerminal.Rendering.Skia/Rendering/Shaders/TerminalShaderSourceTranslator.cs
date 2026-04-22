// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Terminal shader compatibility translator.

using System.Text;
using System.Text.RegularExpressions;

namespace RoyalTerminal.Avalonia.Rendering;

internal static partial class TerminalShaderSourceTranslator
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
        normalized = RemovePsInputStruct(normalized);
        normalized = RemoveWindowsTerminalResourceDeclarations(normalized);
        normalized = RemoveHlslRegisterAnnotations(normalized);
        normalized = RemoveFloatSuffixes(normalized);
        normalized = ReplaceShaderTextureSampleCalls(normalized);
        normalized = ReplaceHlslOperators(normalized);
        normalized = ReplaceHlslFunctions(normalized);

        if (!TryExtractMainBody(normalized, out int mainStart, out int mainEnd, out string mainBody))
        {
            return RuntimePrelude + normalized;
        }

        string helperSource = normalized.Remove(mainStart, mainEnd - mainStart);
        mainBody = RemoveWindowsTerminalInputAliases(mainBody);

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
        string result = GhosttyKnownUniformDeclarationRegex().Replace(source, string.Empty);
        return result.Replace("uniform sampler2D iChannel0;", string.Empty, StringComparison.Ordinal);
    }

    private static string ReplaceGlslTextureCalls(string source)
    {
        string result = source;
        result = TextureIChannel0Regex().Replace(result, "sampleGhosttyChannel($1)");
        result = Texture2DIChannel0Regex().Replace(result, "sampleGhosttyChannel($1)");
        return result;
    }

    private static string ReplaceGlslTypeNames(string source)
    {
        return source
            .Replace("ivec2", "int2", StringComparison.Ordinal)
            .Replace("ivec3", "int3", StringComparison.Ordinal)
            .Replace("ivec4", "int4", StringComparison.Ordinal)
            .Replace("vec2", "float2", StringComparison.Ordinal)
            .Replace("vec3", "float3", StringComparison.Ordinal)
            .Replace("vec4", "float4", StringComparison.Ordinal);
    }

    private static string RemoveShaderedIncludes(string source)
    {
        return ShaderedIncludeRegex().Replace(source, string.Empty);
    }

    private static string RemovePsInputStruct(string source)
    {
        return PsInputStructRegex().Replace(source, string.Empty);
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

    private static string RemoveFloatSuffixes(string source)
    {
        return FloatSuffixRegex().Replace(source, string.Empty);
    }

    private static string ReplaceShaderTextureSampleCalls(string source)
    {
        const string call = "shaderTexture.Sample";
        StringBuilder builder = new(source.Length);
        int index = 0;

        while (index < source.Length)
        {
            int callIndex = source.IndexOf(call, index, StringComparison.Ordinal);
            if (callIndex < 0)
            {
                builder.Append(source, index, source.Length - index);
                break;
            }

            builder.Append(source, index, callIndex - index);
            int openParen = source.IndexOf('(', callIndex + call.Length);
            if (openParen < 0)
            {
                builder.Append(source, callIndex, source.Length - callIndex);
                break;
            }

            int closeParen = FindMatchingBrace(source, openParen, '(', ')');
            if (closeParen < 0)
            {
                builder.Append(source, callIndex, source.Length - callIndex);
                break;
            }

            string arguments = source.Substring(openParen + 1, closeParen - openParen - 1);
            string textureCoordinate = ExtractSecondArgument(arguments);
            builder.Append("sampleTerminal(");
            builder.Append(textureCoordinate);
            builder.Append(')');
            index = closeParen + 1;
        }

        return builder.ToString();
    }

    private static string ExtractSecondArgument(string arguments)
    {
        int depth = 0;
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
                return arguments[(i + 1)..].Trim();
            }
        }

        return "uv";
    }

    private static string ReplaceHlslOperators(string source)
    {
        return source.Replace(" | ", " || ", StringComparison.Ordinal);
    }

    private static string ReplaceHlslFunctions(string source)
    {
        return source
            .Replace("lerp(", "mix(", StringComparison.Ordinal)
            .Replace("frac(", "fract(", StringComparison.Ordinal)
            .Replace("fmod(", "mod(", StringComparison.Ordinal);
    }

    private static bool TryExtractMainBody(string source, out int mainStart, out int mainEnd, out string body)
    {
        Match match = WindowsTerminalMainRegex().Match(source);
        if (!match.Success)
        {
            mainStart = 0;
            mainEnd = 0;
            body = string.Empty;
            return false;
        }

        int openBrace = source.IndexOf('{', match.Index + match.Length - 1);
        if (openBrace < 0)
        {
            mainStart = 0;
            mainEnd = 0;
            body = string.Empty;
            return false;
        }

        int closeBrace = FindMatchingBrace(source, openBrace, '{', '}');
        if (closeBrace < 0)
        {
            mainStart = 0;
            mainEnd = 0;
            body = string.Empty;
            return false;
        }

        mainStart = match.Index;
        mainEnd = closeBrace + 1;
        body = source.Substring(openBrace + 1, closeBrace - openBrace - 1);
        return true;
    }

    private static string RemoveWindowsTerminalInputAliases(string body)
    {
        string result = PinAliasRegex().Replace(body, string.Empty);
        return PatchedPinAliasRegex().Replace(result, string.Empty);
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

    [GeneratedRegex(@"uniform\s+(?:float|int|vec[234]|sampler2D)\s+(?:iResolution|iTime|iTimeDelta|iFrame|iChannel0|iBackgroundColor|iForegroundColor|iCursorColor)\s*(?:\[[^\]]+\])?\s*;", RegexOptions.Multiline)]
    private static partial Regex GhosttyKnownUniformDeclarationRegex();

    [GeneratedRegex(@"texture\s*\(\s*iChannel0\s*,\s*([^;\n]+?)\s*\)")]
    private static partial Regex TextureIChannel0Regex();

    [GeneratedRegex(@"texture2D\s*\(\s*iChannel0\s*,\s*([^;\n]+?)\s*\)")]
    private static partial Regex Texture2DIChannel0Regex();

    [GeneratedRegex(@"#include\s+""SHADERed/[^""]+""")]
    private static partial Regex ShaderedIncludeRegex();

    [GeneratedRegex(@"struct\s+PSInput\s*\{.*?\};", RegexOptions.Singleline)]
    private static partial Regex PsInputStructRegex();

    [GeneratedRegex(@"Texture2D\s+shaderTexture\s*(?::\s*register\s*\(\s*t0\s*\))?\s*;")]
    private static partial Regex TextureDeclarationRegex();

    [GeneratedRegex(@"SamplerState\s+samplerState\s*(?::\s*register\s*\(\s*s0\s*\))?\s*;")]
    private static partial Regex SamplerDeclarationRegex();

    [GeneratedRegex(@"cbuffer\s+PixelShaderSettings\s*(?::\s*register\s*\(\s*b0\s*\))?\s*\{.*?\};", RegexOptions.Singleline)]
    private static partial Regex PixelShaderSettingsCbufferRegex();

    [GeneratedRegex(@"\s*:\s*register\s*\([^)]+\)")]
    private static partial Regex RegisterAnnotationRegex();

    [GeneratedRegex(@"(?<=\d)f\b")]
    private static partial Regex FloatSuffixRegex();

    [GeneratedRegex(@"float4\s+main\s*\([^)]*\)\s*(?::\s*SV_TARGET)?\s*\{", RegexOptions.Multiline)]
    private static partial Regex WindowsTerminalMainRegex();

    [GeneratedRegex(@"float4\s+pos\s*=\s*pin\.pos\s*;\s*float2\s+uv\s*=\s*pin\.uv\s*;")]
    private static partial Regex PinAliasRegex();

    [GeneratedRegex(@"PSInput\s+patchedPin\s*=.*?;\s*pos\s*=\s*patchedPin\.pos\s*;\s*uv\s*=\s*patchedPin\.uv\s*;", RegexOptions.Singleline)]
    private static partial Regex PatchedPinAliasRegex();
}
