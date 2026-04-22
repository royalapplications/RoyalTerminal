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
    public const string PackageCrtBloomShaderId = "package-crt-bloom";
    public const string PackageBloomBlurShaderId = "package-bloom-blur";
    public const string PackageComputePhosphorShaderId = "package-compute-phosphor";

    public static IReadOnlyList<TerminalShaderSampleOption> Options { get; } =
    [
        new TerminalShaderSampleOption(OffShaderId, "Off"),
        new TerminalShaderSampleOption(CrtAmberShaderId, "CRT Amber"),
        new TerminalShaderSampleOption(HueShiftShaderId, "Hue Shift"),
        new TerminalShaderSampleOption(TransparentKeyShaderId, "Transparent Key"),
        new TerminalShaderSampleOption(RetroScanlinesShaderId, "Retro Scanlines"),
        new TerminalShaderSampleOption(PackageCrtBloomShaderId, "Full HLSL CRT Bloom"),
        new TerminalShaderSampleOption(PackageBloomBlurShaderId, "Full HLSL Bloom Blur"),
        new TerminalShaderSampleOption(PackageComputePhosphorShaderId, "Full HLSL Compute Phosphor"),
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
            _ => null,
        };
    }

    public static TerminalShaderPackage? GetPackage(string? shaderId)
    {
        return shaderId switch
        {
            PackageCrtBloomShaderId => CreateCrtBloomPackage(),
            PackageBloomBlurShaderId => CreateBloomBlurPackage(),
            PackageComputePhosphorShaderId => CreateComputePhosphorPackage(),
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

    private static TerminalShaderPackage CreateCrtBloomPackage()
    {
        return new TerminalShaderPackage(
            "Full HLSL CRT Bloom",
            [new TerminalShaderFile("crt-bloom.hlsl", PackageCrtBloomSource)],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "crt-bloom.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader50,
                    inputs:
                    [
                        new TerminalShaderPassInput(TerminalShaderBuiltInResourceNames.TerminalFramebuffer),
                    ]),
            ],
            CreateFrameResources());
    }

    private static TerminalShaderPackage CreateBloomBlurPackage()
    {
        return new TerminalShaderPackage(
            "Full HLSL Bloom Blur",
            [
                new TerminalShaderFile("bloom-extract.hlsl", PackageBloomExtractSource),
                new TerminalShaderFile("bloom-composite.hlsl", PackageBloomCompositeSource),
            ],
            [
                new TerminalShaderPass(
                    "extract",
                    TerminalShaderStage.Pixel,
                    "bloom-extract.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader50,
                    inputs:
                    [
                        new TerminalShaderPassInput(TerminalShaderBuiltInResourceNames.TerminalFramebuffer),
                    ],
                    outputs:
                    [
                        new TerminalShaderPassOutput("BloomTexture"),
                    ]),
                new TerminalShaderPass(
                    "composite",
                    TerminalShaderStage.Pixel,
                    "bloom-composite.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader50,
                    inputs:
                    [
                        new TerminalShaderPassInput(TerminalShaderBuiltInResourceNames.TerminalFramebuffer),
                        new TerminalShaderPassInput("BloomTexture"),
                    ]),
            ],
            CreateFrameResources());
    }

    private static TerminalShaderPackage CreateComputePhosphorPackage()
    {
        return new TerminalShaderPackage(
            "Full HLSL Compute Phosphor",
            [
                new TerminalShaderFile("phosphor-compute.hlsl", PackagePhosphorComputeSource),
                new TerminalShaderFile("phosphor-present.hlsl", PackagePhosphorPresentSource),
            ],
            [
                new TerminalShaderPass(
                    "compute",
                    TerminalShaderStage.Compute,
                    "phosphor-compute.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.ComputeShader50,
                    new TerminalShaderDispatch(8, 8, kind: TerminalShaderDispatchKind.CoverOutput),
                    inputs:
                    [
                        new TerminalShaderPassInput(TerminalShaderBuiltInResourceNames.TerminalFramebuffer),
                    ],
                    outputs:
                    [
                        new TerminalShaderPassOutput("PhosphorTexture", TerminalShaderResourceKind.UavTexture2D),
                    ]),
                new TerminalShaderPass(
                    "present",
                    TerminalShaderStage.Pixel,
                    "phosphor-present.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader50,
                    inputs:
                    [
                        new TerminalShaderPassInput("PhosphorTexture"),
                    ]),
            ],
            CreateFrameResources());
    }

    private static IReadOnlyList<TerminalShaderResourceBinding> CreateFrameResources()
    {
        return
        [
            new TerminalShaderResourceBinding(
                TerminalShaderBuiltInResourceNames.TerminalFramebuffer,
                TerminalShaderResourceKind.TerminalFramebuffer,
                TerminalShaderResourceSource.BuiltIn,
                TerminalShaderValueType.Texture2D,
                registerIndex: 0),
            new TerminalShaderResourceBinding(
                "TerminalSampler",
                TerminalShaderResourceKind.Sampler,
                TerminalShaderResourceSource.BuiltIn,
                registerIndex: 0),
            new TerminalShaderResourceBinding(
                "TerminalFrame",
                TerminalShaderResourceKind.ConstantBuffer,
                TerminalShaderResourceSource.BuiltIn,
                TerminalShaderValueType.Float4,
                registerIndex: 0),
        ];
    }

    private const string PackageFramePrelude = """
        Texture2D TerminalFramebuffer : register(t0);
        SamplerState TerminalSampler : register(s0);

        cbuffer TerminalFrame : register(b0)
        {
            float2 Resolution;
            float Time;
            float TimeDelta;
            float Scale;
            float3 Padding0;
            float4 Background;
        };

        struct PixelInput
        {
            float4 Position : SV_Position;
            float2 TexCoord : TEXCOORD0;
        };

        float Hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        float2 CurvedUv(float2 uv)
        {
            float2 centered = uv - 0.5;
            float radius = dot(centered, centered);
            return centered * (1.0 + radius * 0.28) + 0.5;
        }
        """;

    private const string PackageCrtBloomSource = PackageFramePrelude + """

        float4 Main(PixelInput input) : SV_Target
        {
            float2 uv = CurvedUv(input.Position.xy / max(Resolution, 1.0));
            if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0)
            {
                return float4(Background.rgb * 0.08, 1.0);
            }

            float2 texel = max(Scale, 1.0) / max(Resolution, 1.0);
            float4 color = TerminalFramebuffer.Sample(TerminalSampler, uv);
            float3 glow = TerminalFramebuffer.Sample(TerminalSampler, uv + float2(texel.x * 1.5, 0.0)).rgb;
            glow += TerminalFramebuffer.Sample(TerminalSampler, uv - float2(texel.x * 1.5, 0.0)).rgb;
            glow += TerminalFramebuffer.Sample(TerminalSampler, uv + float2(0.0, texel.y * 1.5)).rgb;
            glow += TerminalFramebuffer.Sample(TerminalSampler, uv - float2(0.0, texel.y * 1.5)).rgb;
            glow *= 0.16;

            float luma = dot(color.rgb, float3(0.299, 0.587, 0.114));
            float scanline = 1.0 - (fmod(floor(input.Position.y / max(Scale, 1.0)), 2.0) * 0.32);
            float vignette = smoothstep(1.2, 0.28, length((uv - 0.5) * float2(1.18, 1.0)));
            float grain = (Hash(input.Position.xy + Time * 53.0) - 0.5) * 0.035;
            float3 amber = max(float3(0.025, 0.0175, 0.0), luma.xxx * float3(1.0, 0.72, 0.08));
            return float4(saturate((amber + glow) * scanline * vignette + grain), color.a);
        }
        """;

    private const string PackageBloomExtractSource = PackageFramePrelude + """

        float4 Main(PixelInput input) : SV_Target
        {
            float2 uv = input.Position.xy / max(Resolution, 1.0);
            float2 texel = 1.0 / max(Resolution, 1.0);
            float4 color = TerminalFramebuffer.Sample(TerminalSampler, uv);
            float3 blur = color.rgb * 0.48;
            blur += TerminalFramebuffer.Sample(TerminalSampler, uv + float2(texel.x * 2.0, 0.0)).rgb * 0.13;
            blur += TerminalFramebuffer.Sample(TerminalSampler, uv - float2(texel.x * 2.0, 0.0)).rgb * 0.13;
            blur += TerminalFramebuffer.Sample(TerminalSampler, uv + float2(0.0, texel.y * 2.0)).rgb * 0.13;
            blur += TerminalFramebuffer.Sample(TerminalSampler, uv - float2(0.0, texel.y * 2.0)).rgb * 0.13;
            float brightness = max(max(blur.r, blur.g), blur.b);
            return float4(saturate((blur - 0.18) * 1.6) * smoothstep(0.1, 0.7, brightness), color.a);
        }
        """;

    private const string PackageBloomCompositeSource = """
        Texture2D TerminalFramebuffer : register(t0);
        Texture2D BloomTexture : register(t1);
        SamplerState TerminalSampler : register(s0);

        cbuffer TerminalFrame : register(b0)
        {
            float2 Resolution;
            float Time;
            float TimeDelta;
            float Scale;
            float3 Padding0;
            float4 Background;
        };

        struct PixelInput
        {
            float4 Position : SV_Position;
            float2 TexCoord : TEXCOORD0;
        };

        float4 Main(PixelInput input) : SV_Target
        {
            float2 uv = input.Position.xy / max(Resolution, 1.0);
            float4 baseColor = TerminalFramebuffer.Sample(TerminalSampler, uv);
            float3 bloom = BloomTexture.Sample(TerminalSampler, uv).rgb;
            float scanline = 1.0 - (fmod(floor(input.Position.y / max(Scale, 1.0)), 2.0) * 0.18);
            return float4(saturate((baseColor.rgb + bloom * 0.55) * scanline), baseColor.a);
        }
        """;

    private const string PackagePhosphorComputeSource = """
        Texture2D TerminalFramebuffer : register(t0);
        SamplerState TerminalSampler : register(s0);
        RWTexture2D<float4> PhosphorTexture : register(u0);

        cbuffer TerminalFrame : register(b0)
        {
            float2 Resolution;
            float Time;
            float TimeDelta;
            float Scale;
            float3 Padding0;
            float4 Background;
        };

        [numthreads(8, 8, 1)]
        void Main(uint3 id : SV_DispatchThreadID)
        {
            if (id.x >= (uint)Resolution.x || id.y >= (uint)Resolution.y)
            {
                return;
            }

            float2 uv = (float2(id.xy) + 0.5) / max(Resolution, 1.0);
            float4 color = TerminalFramebuffer.SampleLevel(TerminalSampler, uv, 0.0);
            float luma = dot(color.rgb, float3(0.299, 0.587, 0.114));
            float triad = frac((float)id.x / max(Scale, 1.0));
            float3 mask = triad < 0.333 ? float3(1.0, 0.56, 0.42) : (triad < 0.666 ? float3(0.58, 1.0, 0.46) : float3(0.48, 0.68, 1.0));
            PhosphorTexture[id.xy] = float4(saturate(luma.xxx * mask + color.rgb * 0.22), color.a);
        }
        """;

    private const string PackagePhosphorPresentSource = """
        Texture2D PhosphorTexture : register(t1);
        SamplerState TerminalSampler : register(s0);

        cbuffer TerminalFrame : register(b0)
        {
            float2 Resolution;
            float Time;
            float TimeDelta;
            float Scale;
            float3 Padding0;
            float4 Background;
        };

        struct PixelInput
        {
            float4 Position : SV_Position;
            float2 TexCoord : TEXCOORD0;
        };

        float4 Main(PixelInput input) : SV_Target
        {
            float2 uv = input.Position.xy / max(Resolution, 1.0);
            float4 color = PhosphorTexture.Sample(TerminalSampler, uv);
            float scanline = 1.0 - (fmod(floor(input.Position.y / max(Scale, 1.0)), 2.0) * 0.22);
            return float4(saturate(color.rgb * scanline), color.a);
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
