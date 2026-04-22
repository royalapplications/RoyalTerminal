// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Terminal framebuffer shader tests.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Demo.Services;
using RoyalTerminal.Shaders;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderPostProcessorTests
{
    [Fact]
    public void DemoShaderSamples_Compile()
    {
        foreach (TerminalShaderSampleOption option in TerminalShaderSampleCatalog.Options)
        {
            IReadOnlyList<TerminalShaderSource>? sources = TerminalShaderSampleCatalog.GetSources(option.Id);
            if (sources is null)
            {
                continue;
            }

            using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create(sources);

            Assert.True(processor.HasShaders, processor.CompileLog);
            Assert.True(string.IsNullOrWhiteSpace(processor.CompileLog), processor.CompileLog);
        }
    }

    [Fact]
    public void DemoShaderPackageSamples_Validate()
    {
        foreach (TerminalShaderSampleOption option in TerminalShaderSampleCatalog.Options)
        {
            TerminalShaderPackage? package = TerminalShaderSampleCatalog.GetPackage(option.Id);
            if (package is null)
            {
                continue;
            }

            TerminalShaderPackageValidationResult result = TerminalShaderPackageValidator.Validate(package);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        }
    }

    [Fact]
    public void SkiaRuntimeEffectShader_AppliesToFramebuffer()
    {
        TerminalShaderSource source = new(
            "constant green",
            """
            uniform shader shaderTexture;

            half4 main(float2 fragCoord) {
                float4 color = shaderTexture.eval(fragCoord);
                return half4(0.0, color.g + 1.0, 0.0, 1.0);
            }
            """);
        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);
        using SKSurface input = SKSurface.Create(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul));
        using SKSurface output = SKSurface.Create(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul));
        input.Canvas.Clear(SKColors.Red);
        using SKImage inputImage = input.Snapshot();

        TerminalShaderFrameContext frameContext = CreateFrameContext();
        bool applied = processor.TryApply(output.Canvas, inputImage, new SKRect(0, 0, 4, 4), frameContext);

        Assert.True(applied, processor.CompileLog);
        using SKImage outputImage = output.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(outputImage);
        SKColor pixel = bitmap.GetPixel(0, 0);
        Assert.True(pixel.Green > 200);
        Assert.True(pixel.Red < 20);
    }

    [Fact]
    public void GhosttyShadertoyShader_CompilesThroughCompatibilityAdapter()
    {
        TerminalShaderSource source = new(
            "ghostty sample",
            """
            void mainImage(out vec4 fragColor, in vec2 fragCoord) {
                vec2 uv = fragCoord / iResolution.xy;
                vec4 color = texture(iChannel0, uv);
                fragColor = vec4(0.0, color.g, color.b, color.a);
            }
            """,
            TerminalShaderLanguage.GhosttyShadertoy);

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);

        Assert.True(processor.HasShaders, processor.CompileLog);
        Assert.True(string.IsNullOrWhiteSpace(processor.CompileLog), processor.CompileLog);
    }

    [Fact]
    public void WindowsTerminalHlslShader_CompilesThroughCompatibilityAdapter()
    {
        TerminalShaderSource source = new(
            "windows terminal sample",
            """
            struct PSInput { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Texture2D shaderTexture : register(t0);
            SamplerState samplerState : register(s0);
            cbuffer PixelShaderSettings : register(b0) {
                float Time;
                float Scale;
                float2 Resolution;
                float4 Background;
            };

            float4 main(PSInput pin) : SV_TARGET {
                float4 pos = pin.pos;
                float2 uv = pin.uv;
                float4 color = shaderTexture.Sample(samplerState, uv);
                return float4(color.r, 0.0f, color.b, color.a);
            }
            """,
            TerminalShaderLanguage.WindowsTerminalHlsl);

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);

        Assert.True(processor.HasShaders, processor.CompileLog);
        Assert.True(string.IsNullOrWhiteSpace(processor.CompileLog), processor.CompileLog);
    }

    [Fact]
    public void InvalidShader_IsSkippedWithCompileLog()
    {
        TerminalShaderSource source = new("invalid", "this is not shader code");

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);

        Assert.False(processor.HasShaders);
        Assert.False(string.IsNullOrWhiteSpace(processor.CompileLog));
        Assert.Contains("invalid", processor.CompileLog, StringComparison.OrdinalIgnoreCase);
    }

    private static TerminalShaderFrameContext CreateFrameContext()
    {
        return new TerminalShaderFrameContext(
            width: 4,
            height: 4,
            time: 0f,
            timeDelta: 0f,
            frame: 0,
            scale: 1f,
            backgroundColor: SKColors.Black,
            foregroundColor: SKColors.White,
            cursorColor: SKColors.White,
            cursorRect: new SKRect(0, 0, 1, 1),
            cursorStyle: CursorStyle.Block,
            cursorVisible: true);
    }
}
