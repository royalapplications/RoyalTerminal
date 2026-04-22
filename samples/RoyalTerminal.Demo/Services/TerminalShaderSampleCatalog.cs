// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo - Built-in shader samples.

using System;
using System.Collections.Generic;
using RoyalTerminal.Shaders;

namespace RoyalTerminal.Demo.Services;

internal static class TerminalShaderSampleCatalog
{
    public const string OffShaderId = "off";
    public const string CrtAmberShaderId = "crt-amber";
    public const string HueShiftShaderId = "hue-shift";
    public const string TransparentKeyShaderId = "transparent-key";
    public const string RetroScanlinesShaderId = "retro-scanlines";
    public const string WindowsTerminalCrtShaderId = "windows-terminal-crt";
    public const string GhosttyShadertoyShaderId = "ghostty-shadertoy";

    public static IReadOnlyList<TerminalShaderSampleOption> Options { get; } =
    [
        new TerminalShaderSampleOption(OffShaderId, "Off"),
        new TerminalShaderSampleOption(CrtAmberShaderId, "CRT Amber"),
        new TerminalShaderSampleOption(HueShiftShaderId, "Hue Shift"),
        new TerminalShaderSampleOption(TransparentKeyShaderId, "Transparent Key"),
        new TerminalShaderSampleOption(RetroScanlinesShaderId, "Retro Scanlines"),
        new TerminalShaderSampleOption(WindowsTerminalCrtShaderId, "Windows Terminal CRT"),
        new TerminalShaderSampleOption(GhosttyShadertoyShaderId, "Ghostty Shadertoy"),
    ];

    public static IReadOnlyList<TerminalShaderSource>? GetSources(string? shaderId)
    {
        return shaderId switch
        {
            CrtAmberShaderId =>
            [
                new TerminalShaderSource(
                    "CRT Amber",
                    CrtAmberSource,
                    requiresContinuousAnimation: true),
            ],
            HueShiftShaderId =>
            [
                new TerminalShaderSource(
                    "Hue Shift",
                    HueShiftSource,
                    requiresContinuousAnimation: true),
            ],
            TransparentKeyShaderId =>
            [
                new TerminalShaderSource(
                    "Transparent Key",
                    TransparentKeySource),
            ],
            RetroScanlinesShaderId =>
            [
                new TerminalShaderSource(
                    "Retro Scanlines",
                    RetroScanlinesSource,
                    requiresContinuousAnimation: true),
            ],
            WindowsTerminalCrtShaderId =>
            [
                new TerminalShaderSource(
                    "Windows Terminal CRT",
                    WindowsTerminalCrtSource,
                    TerminalShaderLanguage.WindowsTerminalHlsl,
                    requiresContinuousAnimation: true),
            ],
            GhosttyShadertoyShaderId =>
            [
                new TerminalShaderSource(
                    "Ghostty Shadertoy",
                    GhosttyShadertoySource,
                    TerminalShaderLanguage.GhosttyShadertoy,
                    requiresContinuousAnimation: true),
            ],
            _ => null,
        };
    }

    public static TerminalShaderSampleOption FindOption(string? shaderId)
    {
        IReadOnlyList<TerminalShaderSampleOption> options = Options;
        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Id, shaderId, StringComparison.Ordinal))
            {
                return options[i];
            }
        }

        return options[0];
    }

    private const string HueShiftSource = """
        uniform shader shaderTexture;
        uniform float Time;
        uniform float2 Resolution;

        float3 rgb2hsv(float3 c) {
            float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
            float4 p = c.g < c.b ? float4(c.bg, K.wz) : float4(c.gb, K.xy);
            float4 q = c.r < p.x ? float4(p.xyw, c.r) : float4(c.r, p.yzx);
            float d = q.x - min(q.w, q.y);
            float e = 0.0000000001;
            return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
        }

        float3 hsv2rgb(float3 c) {
            float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
            float3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
            return c.z * mix(K.xxx, clamp(p - K.xxx, float3(0.0), float3(1.0)), c.y);
        }

        half4 main(float2 fragCoord) {
            float4 color = shaderTexture.eval(fragCoord);
            float3 hsv = rgb2hsv(color.rgb);
            if (hsv.y >= 0.266) {
                hsv.x = mod(hsv.x + Time * 0.01, 1.0);
            }

            return half4(hsv2rgb(hsv), color.a);
        }
        """;

    private const string TransparentKeySource = """
        uniform shader shaderTexture;

        half4 main(float2 fragCoord) {
            float4 color = shaderTexture.eval(fragCoord);
            float3 chromaKey = float3(8.0 / 255.0);
            if (distance(color.rgb, chromaKey) < 0.002) {
                return half4(0.0, 0.0, 0.0, 0.0);
            }

            return half4(color);
        }
        """;

    private const string RetroScanlinesSource = """
        uniform shader shaderTexture;
        uniform float Time;
        uniform float Scale;
        uniform float2 Resolution;

        float4 sampleFrame(float2 coord) {
            return shaderTexture.eval(clamp(coord, float2(0.0), Resolution));
        }

        float hash(float2 p) {
            return fract(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
        }

        half4 main(float2 fragCoord) {
            float2 texel = max(float2(1.0), Scale.xx);
            float4 color = sampleFrame(fragCoord) * 0.72;
            color += sampleFrame(fragCoord + float2(texel.x, 0.0)) * 0.07;
            color += sampleFrame(fragCoord - float2(texel.x, 0.0)) * 0.07;
            color += sampleFrame(fragCoord + float2(0.0, texel.y)) * 0.04;
            color += sampleFrame(fragCoord - float2(0.0, texel.y)) * 0.04;

            float scanline = 1.0 - mod(floor(fragCoord.y / max(1.0, Scale)), 2.0) * 0.28;
            float vignette = smoothstep(1.15, 0.18, length((fragCoord / Resolution - 0.5) * float2(1.18, 1.0)));
            float grain = (hash(fragCoord + Time * 37.0) - 0.5) * 0.025;
            color.rgb = clamp(color.rgb * scanline * vignette + grain, float3(0.0), float3(1.0));
            return half4(color);
        }
        """;

    private const string CrtAmberSource = """
        uniform shader shaderTexture;
        uniform float Time;
        uniform float Scale;
        uniform float2 Resolution;
        uniform float4 Background;

        float4 sampleFrame(float2 coord) {
            return shaderTexture.eval(clamp(coord, float2(0.0), Resolution));
        }

        float2 curve(float2 uv) {
            float2 centered = uv - 0.5;
            float r = dot(centered, centered);
            centered *= 1.0 + r * 0.36;
            return centered + 0.5;
        }

        float hash(float2 p) {
            return fract(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        half4 main(float2 fragCoord) {
            float2 screenUv = fragCoord / Resolution;
            float2 uv = curve(screenUv);
            if (uv.x < -0.015 || uv.y < -0.015 || uv.x > 1.015 || uv.y > 1.015) {
                return half4(0.0, 0.0, 0.0, 1.0);
            }

            if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) {
                return half4(Background.rgb * 0.08, 1.0);
            }

            float2 coord = uv * Resolution;
            float4 color = sampleFrame(coord);
            float3 bloom = color.rgb - sampleFrame(coord + float2(-1.5, 0.0) * max(1.0, Scale)).rgb;
            color.rgb = clamp(color.rgb + bloom * 0.42, float3(0.0), float3(1.0));

            float luma = dot(color.rgb, float3(0.2989, 0.5866, 0.1145));
            float3 amber = float3(1.0, 0.7, 0.0);
            color.rgb = max(float3(0.025, 0.0175, 0.0), luma.xxx * amber);

            float refresh = smoothstep(0.03, 0.0, abs(screenUv.y - mod(Time * 0.22, 1.35) + 0.18));
            float scanline = 1.0 - mod(floor(fragCoord.y / max(1.0, Scale)), 2.0) * 0.32;
            float vignette = smoothstep(1.18, 0.28, length((screenUv - 0.5) * float2(1.18, 1.0)));
            float grain = (hash(fragCoord + Time * 53.0) - 0.5) * 0.035;
            color.rgb = clamp(color.rgb * scanline * vignette + refresh * 0.08 + grain, float3(0.0), float3(1.0));
            return half4(color.rgb, color.a);
        }
        """;

    private const string WindowsTerminalCrtSource = """
        struct PSInput { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        Texture2D shaderTexture : register(t0);
        SamplerState samplerState : register(s0);
        cbuffer PixelShaderSettings : register(b0)
        {
            float Time;
            float Scale;
            float2 Resolution;
            float4 Background;
        };

        float Hash(float2 p)
        {
            return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
        }

        float2 Curve(float2 uv)
        {
            float2 centered = uv - 0.5;
            float radius = dot(centered, centered);
            return centered * (1.0 + radius * 0.3) + 0.5;
        }

        float4 main(PSInput pin) : SV_TARGET
        {
            float4 pos = pin.pos;
            float2 uv = pin.uv;
            float2 curved = Curve(uv);
            if (curved.x < 0.0 || curved.y < 0.0 || curved.x > 1.0 || curved.y > 1.0)
            {
                return float4(Background.rgb * 0.08, 1.0);
            }

            float2 texel = float2(max(Scale, 1.0), max(Scale, 1.0)) / max(Resolution, float2(1.0, 1.0));
            float4 color = shaderTexture.Sample(samplerState, curved);
            float3 glow = shaderTexture.Sample(samplerState, curved + float2(texel.x * 1.5, 0.0)).rgb;
            glow += shaderTexture.Sample(samplerState, curved - float2(texel.x * 1.5, 0.0)).rgb;
            glow += shaderTexture.Sample(samplerState, curved + float2(0.0, texel.y * 1.5)).rgb;
            glow += shaderTexture.Sample(samplerState, curved - float2(0.0, texel.y * 1.5)).rgb;

            float scanline = 1.0 - mod(floor(pos.y / max(Scale, 1.0)), 2.0) * 0.25;
            float vignette = smoothstep(1.16, 0.24, length((uv - 0.5) * float2(1.2, 1.0)));
            float grain = (Hash(pos.xy + Time * 45.0) - 0.5) * 0.03;
            float3 rgb = clamp((color.rgb + glow * 0.12) * scanline * vignette + grain, float3(0.0), float3(1.0));
            return float4(rgb, color.a);
        }
        """;

    private const string GhosttyShadertoySource = """
        void mainImage(out vec4 fragColor, in vec2 fragCoord)
        {
            vec2 uv = fragCoord / iResolution.xy;
            vec4 color = texture(iChannel0, uv);
            float scanline = 0.82 + 0.18 * sin(fragCoord.y * 3.14159);
            float sweep = smoothstep(0.03, 0.0, abs(uv.y - mod(iTime * 0.18, 1.2)));
            fragColor = vec4(clamp(color.rgb * scanline + sweep * 0.08, vec3(0.0), vec3(1.0)), color.a);
        }
        """;
}

public sealed class TerminalShaderSampleOption
{
    public TerminalShaderSampleOption(string id, string displayName)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Shader sample id is required.", nameof(id)) : id;
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("Shader sample display name is required.", nameof(displayName))
            : displayName;
    }

    public string Id { get; }

    public string DisplayName { get; }
}
