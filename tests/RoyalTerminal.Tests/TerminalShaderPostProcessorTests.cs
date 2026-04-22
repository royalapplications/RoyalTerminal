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
    public void GhosttyShadertoyShader_WithDeclaredUniformsAndNestedTexture_Compiles()
    {
        TerminalShaderSource source = new(
            "ghostty nested sample",
            """
            uniform sampler2D iChannel0;
            uniform vec3 iResolution;
            uniform vec3 iChannelResolution[4];
            uniform vec4 iCurrentCursor;

            void mainImage(out vec4 fragColor, in vec2 fragCoord) {
                vec2 uv = fragCoord / iResolution.xy;
                vec2 offset = vec2(sin(iTime), cos(iTime)) * 0.0;
                vec4 color = texture(iChannel0, uv + offset);
                vec4 baseColor = texture2D(iChannel0, uv);
                fragColor = vec4(color.rgb + baseColor.rgb * 0.0 + iCurrentCursor.xxx * 0.0, color.a);
            }
            """,
            TerminalShaderLanguage.GhosttyShadertoy);

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);

        Assert.True(processor.HasShaders, processor.CompileLog);
        Assert.True(string.IsNullOrWhiteSpace(processor.CompileLog), processor.CompileLog);
    }

    [Fact]
    public void GhosttyShadertoyShader_WithCompatibilityDirectivesAndMatrixTypes_Compiles()
    {
        TerminalShaderSource source = new(
            "ghostty compatibility directives sample",
            """
            #version 300 es
            #ifdef GL_ES
            precision mediump float;
            #endif

            void mainImage(out vec4 fragColor, in vec2 fragCoord) {
                vec2 uv = fragCoord / iResolution.xy;
                mat2 identity = mat2(1.0, 0.0, 0.0, 1.0);
                vec2 sampleUv = identity * uv;
                fragColor = texture(iChannel0, sampleUv);
            }
            """,
            TerminalShaderLanguage.GhosttyShadertoy);

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);

        Assert.True(processor.HasShaders, processor.CompileLog);
        Assert.True(string.IsNullOrWhiteSpace(processor.CompileLog), processor.CompileLog);
    }

    [Fact]
    public void GhosttyShadertoyShader_PreservesUnsupportedTextureCallsDuringPartialRewrite()
    {
        TerminalShaderSource source = new(
            "ghostty partial rewrite sample",
            """
            void mainImage(out vec4 fragColor, in vec2 fragCoord) {
                vec2 uv = fragCoord / iResolution.xy;
                vec4 color = texture(iChannel0, uv);
                fragColor = color + texture(iChannel1, uv) * 0.0;
            }
            """,
            TerminalShaderLanguage.GhosttyShadertoy);

        string translated = TerminalShaderSourceTranslator.Translate(source);

        Assert.Contains("sampleGhosttyChannel(uv)", translated, StringComparison.Ordinal);
        Assert.Contains("texture(iChannel1, uv)", translated, StringComparison.Ordinal);
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
    public void WindowsTerminalHlslShader_WithCommonSamplePatterns_Compiles()
    {
        TerminalShaderSource source = new(
            "windows terminal extended sample",
            """
            struct PixelInput { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Texture2D<float4> shaderTexture : register(t0);
            SamplerState pointSampler : register(s0);

            cbuffer PixelShaderSettings : register(b0) {
                float Time;
                float Scale;
                float2 Resolution;
                float4 Background;
            };

            static const float Glow = 0.1f;

            float4 main(PixelInput input) : SV_TARGET {
                float4 baseColor = shaderTexture.Sample(pointSampler, input.uv);
                float4 detail = shaderTexture.SampleLevel(pointSampler, input.uv + float2(0.0f, 0.0f), 0.0f);
                float3 rgb = saturate(baseColor.rgb + detail.rgb * Glow);
                return float4(rgb, baseColor.a);
            }
            """,
            TerminalShaderLanguage.WindowsTerminalHlsl);

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);

        Assert.True(processor.HasShaders, processor.CompileLog);
        Assert.True(string.IsNullOrWhiteSpace(processor.CompileLog), processor.CompileLog);
    }

    [Fact]
    public void WindowsTerminalHlslShader_WithSemanticParameters_Compiles()
    {
        TerminalShaderSource source = new(
            "windows terminal semantic parameter sample",
            """
            Texture2D shaderTexture : register(t0);
            SamplerState samplerState : register(s0);
            cbuffer PixelShaderSettings : register(b0) {
                float Time;
                float Scale;
                float2 Resolution;
                float4 Background;
            };

            float4 main(float4 pixelPosition : SV_POSITION, float2 texCoord : TEXCOORD0) : SV_TARGET {
                float4 color = shaderTexture.Sample(samplerState, texCoord);
                float stripe = frac(pixelPosition.y / max(Scale, 1.0));
                return float4(color.rgb * (0.9 + stripe * 0.1), color.a);
            }
            """,
            TerminalShaderLanguage.WindowsTerminalHlsl);

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);

        Assert.True(processor.HasShaders, processor.CompileLog);
        Assert.True(string.IsNullOrWhiteSpace(processor.CompileLog), processor.CompileLog);
    }

    [Fact]
    public void WindowsTerminalHlslShader_WithTextureAndSemanticAliases_Compiles()
    {
        TerminalShaderSource source = new(
            "windows terminal alias sample",
            """
            struct VertexOutput {
                min16float4 position : SV_POSITION;
                min16float2 texCoord : TEXCOORD0;
            };

            Texture2D<float4> terminalFrame : register(t0);
            SamplerState linearSampler : register(s0);

            cbuffer PixelShaderSettings : register(b0) {
                float Time;
                float Scale;
                float2 Resolution;
                float4 Background;
            };

            float4 main(VertexOutput input) : SV_Target {
                float value1f = 1.0f;
                float4 color = terminalFrame.SampleBias(linearSampler, input.texCoord, 0.0f);
                float4 loaded = terminalFrame.Load(int3(input.position.xy, 0));
                float4 grad = terminalFrame.SampleGrad(linearSampler, input.texCoord, float2(0.0f, 0.0f), float2(0.0f, 0.0f));
                float3 rgb = mad(color.rgb, float3(0.5f, 0.5f, 0.5f), loaded.rgb * 0.5f);
                return float4(saturate(rgb + grad.rgb * 0.0f) * value1f, color.a);
            }
            """,
            TerminalShaderLanguage.WindowsTerminalHlsl);

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);

        Assert.True(processor.HasShaders, processor.CompileLog);
        Assert.True(string.IsNullOrWhiteSpace(processor.CompileLog), processor.CompileLog);
    }

    [Fact]
    public void WindowsTerminalHlslShader_WithInputTextureAlias_Compiles()
    {
        TerminalShaderSource source = new(
            "windows terminal input texture sample",
            """
            struct PSInput { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Texture2D<float4> inputTexture : register(t0);
            SamplerState samplerState : register(s0);

            cbuffer PixelShaderSettings : register(b0) {
                float Time;
                float Scale;
                float2 Resolution;
                float4 Background;
            };

            float4 main(PSInput pin) : SV_TARGET {
                return inputTexture.Sample(samplerState, pin.uv);
            }
            """,
            TerminalShaderLanguage.WindowsTerminalHlsl);

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);

        Assert.True(processor.HasShaders, processor.CompileLog);
        Assert.True(string.IsNullOrWhiteSpace(processor.CompileLog), processor.CompileLog);
    }

    [Fact]
    public void WindowsTerminalHlslShader_PreservesIdentifiersEndingInFloatSuffix()
    {
        TerminalShaderSource source = new(
            "windows terminal identifier suffix sample",
            """
            Texture2D shaderTexture : register(t0);
            SamplerState samplerState : register(s0);
            cbuffer PixelShaderSettings : register(b0) {
                float Time;
                float Scale;
                float2 Resolution;
                float4 Background;
            };

            float4 main(float4 pixelPosition : SV_POSITION, float2 texCoord : TEXCOORD0) : SV_TARGET {
                float value1f = 1.0f;
                float4 color = shaderTexture.Sample(samplerState, texCoord);
                return float4(color.rgb * value1f, color.a);
            }
            """,
            TerminalShaderLanguage.WindowsTerminalHlsl);

        string translated = TerminalShaderSourceTranslator.Translate(source);

        Assert.Contains("value1f", translated, StringComparison.Ordinal);
        Assert.DoesNotContain("float value1 = 1.0", translated, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsTerminalHlslShader_IgnoresCommentBracesWhenExtractingMain()
    {
        TerminalShaderSource source = new(
            "windows terminal comment brace sample",
            """
            Texture2D shaderTexture : register(t0);
            SamplerState samplerState : register(s0);
            cbuffer PixelShaderSettings : register(b0) {
                float Time;
                float Scale;
                float2 Resolution;
                float4 Background;
            };

            float4 main(float4 pixelPosition : SV_POSITION, float2 texCoord : TEXCOORD0) : SV_TARGET {
                // A copied shader note with a closing brace: }
                float4 color = shaderTexture.Sample(samplerState, texCoord);
                return float4(color.rgb, color.a);
            }
            """,
            TerminalShaderLanguage.WindowsTerminalHlsl);

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);

        Assert.True(processor.HasShaders, processor.CompileLog);
        Assert.True(string.IsNullOrWhiteSpace(processor.CompileLog), processor.CompileLog);
    }

    [Fact]
    public void WindowsTerminalHlslShader_LeavesNonMatchingFunctionNamesUntouched()
    {
        TerminalShaderSource source = new(
            "windows terminal boundary sample",
            """
            Texture2D shaderTexture : register(t0);
            SamplerState samplerState : register(s0);
            cbuffer PixelShaderSettings : register(b0) {
                float Time;
                float Scale;
                float2 Resolution;
                float4 Background;
            };

            float nonsaturate(float value) {
                return value;
            }

            float4 main(float4 pixelPosition : SV_POSITION, float2 texCoord : TEXCOORD0) : SV_TARGET {
                float4 color = shaderTexture.Sample(samplerState, texCoord);
                float value = nonsaturate(frac(pixelPosition.x / max(Scale, 1.0)));
                return float4(color.rgb * value, color.a);
            }
            """,
            TerminalShaderLanguage.WindowsTerminalHlsl);

        string translated = TerminalShaderSourceTranslator.Translate(source);

        Assert.Contains("float nonsaturate(float value)", translated, StringComparison.Ordinal);
        Assert.DoesNotContain("nonclamp", translated, StringComparison.Ordinal);
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
