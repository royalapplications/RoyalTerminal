// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Direct3D 11 shader backend tests.

using RoyalTerminal.Shaders;
using RoyalTerminal.Shaders.D3D11;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderD3D11Tests
{
    [Fact]
    public async Task D3D11Compiler_NonWindows_ReturnsUnavailableDiagnostic()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        TerminalShaderD3D11Compiler compiler = new();
        TerminalShaderCompilationRequest request = new(
            CreatePackage(),
            CreatePackage().Files,
            new TerminalShaderCompilationOptions(TerminalShaderBackendKind.D3D11));

        TerminalShaderCompilationResult result = await compiler.CompileAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERD3DCOMPILER000");
    }

    [Fact]
    public void D3D11Runtime_NonWindows_IsNotSupported()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False(TerminalShaderD3D11Runtime.IsSupported);
    }

    [Fact]
    public async Task D3D11GpuSmokeTest_RunsWhenExplicitlyEnabled()
    {
        if (!OperatingSystem.IsWindows() ||
            !string.Equals(Environment.GetEnvironmentVariable("ROYALTERMINAL_TEST_D3D11"), "1", StringComparison.Ordinal))
        {
            return;
        }

        TerminalShaderPackage package = CreatePackage();
        TerminalShaderD3D11Compiler compiler = new();
        TerminalShaderCompilationRequest request = new(
            package,
            package.Files,
            new TerminalShaderCompilationOptions(TerminalShaderBackendKind.D3D11));
        TerminalShaderCompilationResult compilation = await compiler.CompileAsync(request);
        Assert.True(compilation.IsSuccess, string.Join(Environment.NewLine, compilation.Diagnostics));

        using TerminalShaderD3D11Runtime runtime = new();
        using TerminalShaderRuntimeProgram program = await runtime.CreateProgramAsync(package, compilation);
        TerminalShaderFrameRequest frame = new(
            4,
            4,
            0f,
            0f,
            0,
            1f,
            [
                new TerminalShaderResourceValue(
                    TerminalShaderBuiltInResourceNames.TerminalFramebuffer,
                    TerminalShaderResourceKind.TerminalFramebuffer,
                    data: CreateRedPixels(4, 4),
                    width: 4,
                    height: 4),
            ]);

        TerminalShaderFrameResult result = await runtime.RenderFrameAsync(program, frame);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
        Assert.Equal(4 * 4 * 4, result.PixelData.Length);
    }

    private static TerminalShaderPackage CreatePackage()
    {
        return new TerminalShaderPackage(
            "d3d11-smoke",
            [
                new TerminalShaderFile(
                    "main.hlsl",
                    """
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

                    float4 Main(PixelInput input) : SV_Target
                    {
                        float2 uv = input.Position.xy / max(Resolution, 1.0);
                        float4 color = TerminalFramebuffer.Sample(TerminalSampler, uv);
                        return float4(color.r, 1.0 - color.g, color.b, color.a);
                    }
                    """),
            ],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader50,
                    inputs:
                    [
                        new TerminalShaderPassInput(TerminalShaderBuiltInResourceNames.TerminalFramebuffer),
                    ]),
            ],
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
                    registerIndex: 0),
            ]);
    }

    private static byte[] CreateRedPixels(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 3] = 255;
        }

        return pixels;
    }
}
