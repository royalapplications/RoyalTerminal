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
    public async Task D3D11Compiler_ReflectsDxbcBindings_WhenExplicitlyEnabled()
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

        TerminalShaderCompilationResult result = await compiler.CompileAsync(request);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        TerminalShaderCompiledPass compiledPass = Assert.Single(result.Passes);
        Assert.Contains(compiledPass.Reflection.Resources, static resource =>
            resource.Name == TerminalShaderBuiltInResourceNames.TerminalFramebuffer &&
            resource.Kind == TerminalShaderResourceKind.Texture2D &&
            resource.RegisterIndex == 0);
        Assert.Contains(compiledPass.Reflection.Resources, static resource =>
            resource.Name == "TerminalSampler" &&
            resource.Kind == TerminalShaderResourceKind.Sampler &&
            resource.RegisterIndex == 0);
        Assert.Contains(compiledPass.Reflection.Resources, static resource =>
            resource.Name == "TerminalFrame" &&
            resource.Kind == TerminalShaderResourceKind.ConstantBuffer &&
            resource.RegisterIndex == 0 &&
            resource.SizeInBytes >= 48);
        Assert.Contains(compiledPass.Reflection.EntryPoints, static entryPoint =>
            entryPoint.Name == "Main" &&
            entryPoint.Stage == TerminalShaderStage.Pixel &&
            entryPoint.Outputs.Any(static semantic => semantic.Name == "SV_Target"));
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
        AssertPixel(result.PixelData.Span, 0, 255, 255, 0, 255);
    }

    [Fact]
    public async Task D3D11GpuConstantBufferTest_RunsWhenExplicitlyEnabled()
    {
        if (!IsD3D11TestEnabled())
        {
            return;
        }

        TerminalShaderPackage package = CreateConstantBufferPackage();
        TerminalShaderFrameResult result = await RenderPackageAsync(
            package,
            [
                new TerminalShaderResourceValue(
                    TerminalShaderBuiltInResourceNames.TerminalFramebuffer,
                    TerminalShaderResourceKind.TerminalFramebuffer,
                    data: CreateSolidPixels(4, 4, 255, 255, 255, 255),
                    width: 4,
                    height: 4),
                new TerminalShaderResourceValue(
                    "TintBuffer",
                    TerminalShaderResourceKind.ConstantBuffer,
                    data: CreateFloat4Buffer(0f, 0.5f, 1f, 1f)),
            ]);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        AssertPixel(result.PixelData.Span, 0, 0, 128, 255, 255, tolerance: 2);
    }

    [Fact]
    public async Task D3D11GpuMultiPassTest_RunsWhenExplicitlyEnabled()
    {
        if (!IsD3D11TestEnabled())
        {
            return;
        }

        TerminalShaderPackage package = CreateMultiPassPackage();
        TerminalShaderFrameResult result = await RenderPackageAsync(
            package,
            [
                new TerminalShaderResourceValue(
                    TerminalShaderBuiltInResourceNames.TerminalFramebuffer,
                    TerminalShaderResourceKind.TerminalFramebuffer,
                    data: CreateSolidPixels(4, 4, 0, 0, 0, 255),
                    width: 4,
                    height: 4),
            ]);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        AssertPixel(result.PixelData.Span, 0, 255, 0, 255, 255);
    }

    [Fact]
    public async Task D3D11GpuComputeUavTest_RunsWhenExplicitlyEnabled()
    {
        if (!IsD3D11TestEnabled())
        {
            return;
        }

        TerminalShaderPackage package = CreateComputePackage();
        TerminalShaderFrameResult result = await RenderPackageAsync(
            package,
            [
                new TerminalShaderResourceValue(
                    TerminalShaderBuiltInResourceNames.TerminalFramebuffer,
                    TerminalShaderResourceKind.TerminalFramebuffer,
                    data: CreateSolidPixels(8, 8, 0, 0, 0, 255),
                    width: 8,
                    height: 8),
            ],
            width: 8,
            height: 8);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        AssertPixel(result.PixelData.Span, 0, 0, 0, 255, 255);
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

    private static TerminalShaderPackage CreateConstantBufferPackage()
    {
        return new TerminalShaderPackage(
            "d3d11-cbuffer",
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

                    cbuffer TintBuffer : register(b1)
                    {
                        float4 Tint;
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
                        return float4(color.rgb * Tint.rgb, color.a * Tint.a);
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
            CreateFrameResources(
                [
                    new TerminalShaderResourceBinding(
                        "TintBuffer",
                        TerminalShaderResourceKind.ConstantBuffer,
                        TerminalShaderResourceSource.External,
                        TerminalShaderValueType.Float4,
                        registerIndex: 1),
                ]));
    }

    private static TerminalShaderPackage CreateMultiPassPackage()
    {
        return new TerminalShaderPackage(
            "d3d11-multipass",
            [
                new TerminalShaderFile(
                    "extract.hlsl",
                    """
                    struct PixelInput
                    {
                        float4 Position : SV_Position;
                        float2 TexCoord : TEXCOORD0;
                    };

                    float4 Main(PixelInput input) : SV_Target
                    {
                        return float4(0.0, 1.0, 0.0, 1.0);
                    }
                    """),
                new TerminalShaderFile(
                    "present.hlsl",
                    """
                    Texture2D MidTexture : register(t0);
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
                        float4 color = MidTexture.Sample(TerminalSampler, uv);
                        return float4(color.g, 0.0, color.g, 1.0);
                    }
                    """),
            ],
            [
                new TerminalShaderPass(
                    "extract",
                    TerminalShaderStage.Pixel,
                    "extract.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader50,
                    outputs:
                    [
                        new TerminalShaderPassOutput("MidTexture"),
                    ]),
                new TerminalShaderPass(
                    "present",
                    TerminalShaderStage.Pixel,
                    "present.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader50,
                    inputs:
                    [
                        new TerminalShaderPassInput("MidTexture"),
                    ]),
            ],
            CreateFrameResources());
    }

    private static TerminalShaderPackage CreateComputePackage()
    {
        return new TerminalShaderPackage(
            "d3d11-compute",
            [
                new TerminalShaderFile(
                    "compute.hlsl",
                    """
                    RWTexture2D<float4> ComputeOutput : register(u0);

                    [numthreads(8, 8, 1)]
                    void Main(uint3 id : SV_DispatchThreadID)
                    {
                        ComputeOutput[id.xy] = float4(0.0, 0.0, 1.0, 1.0);
                    }
                    """),
                new TerminalShaderFile(
                    "present.hlsl",
                    """
                    Texture2D ComputeOutput : register(t0);
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
                        return ComputeOutput.Sample(TerminalSampler, uv);
                    }
                    """),
            ],
            [
                new TerminalShaderPass(
                    "compute",
                    TerminalShaderStage.Compute,
                    "compute.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.ComputeShader50,
                    new TerminalShaderDispatch(8, 8, 1, TerminalShaderDispatchKind.CoverOutput),
                    outputs:
                    [
                        new TerminalShaderPassOutput("ComputeOutput", TerminalShaderResourceKind.UavTexture2D),
                    ]),
                new TerminalShaderPass(
                    "present",
                    TerminalShaderStage.Pixel,
                    "present.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader50,
                    inputs:
                    [
                        new TerminalShaderPassInput("ComputeOutput"),
                    ]),
            ],
            CreateFrameResources());
    }

    private static async ValueTask<TerminalShaderFrameResult> RenderPackageAsync(
        TerminalShaderPackage package,
        IReadOnlyList<TerminalShaderResourceValue> resources,
        int width = 4,
        int height = 4)
    {
        TerminalShaderD3D11Compiler compiler = new();
        TerminalShaderCompilationRequest request = new(
            package,
            package.Files,
            new TerminalShaderCompilationOptions(TerminalShaderBackendKind.D3D11));
        TerminalShaderCompilationResult compilation = await compiler.CompileAsync(request);
        Assert.True(compilation.IsSuccess, string.Join(Environment.NewLine, compilation.Diagnostics));

        using TerminalShaderD3D11Runtime runtime = new();
        using TerminalShaderRuntimeProgram program = await runtime.CreateProgramAsync(package, compilation);
        TerminalShaderFrameRequest frame = new(width, height, 0f, 0f, 0, 1f, resources);
        return await runtime.RenderFrameAsync(program, frame);
    }

    private static IReadOnlyList<TerminalShaderResourceBinding> CreateFrameResources(
        IReadOnlyList<TerminalShaderResourceBinding>? extraResources = null)
    {
        List<TerminalShaderResourceBinding> resources =
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

        if (extraResources is not null)
        {
            resources.AddRange(extraResources);
        }

        return resources;
    }

    private static byte[] CreateRedPixels(int width, int height)
    {
        return CreateSolidPixels(width, height, 255, 0, 0, 255);
    }

    private static byte[] CreateSolidPixels(
        int width,
        int height,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = red;
            pixels[i + 1] = green;
            pixels[i + 2] = blue;
            pixels[i + 3] = alpha;
        }

        return pixels;
    }

    private static byte[] CreateFloat4Buffer(float x, float y, float z, float w)
    {
        byte[] data = new byte[16];
        WriteFloat(data.AsSpan(0, 4), x);
        WriteFloat(data.AsSpan(4, 4), y);
        WriteFloat(data.AsSpan(8, 4), z);
        WriteFloat(data.AsSpan(12, 4), w);
        return data;
    }

    private static void WriteFloat(Span<byte> destination, float value)
    {
        Assert.True(BitConverter.TryWriteBytes(destination, value));
    }

    private static void AssertPixel(
        ReadOnlySpan<byte> pixels,
        int pixelIndex,
        byte red,
        byte green,
        byte blue,
        byte alpha,
        int tolerance = 0)
    {
        int offset = checked(pixelIndex * 4);
        Assert.InRange((int)pixels[offset], red - tolerance, red + tolerance);
        Assert.InRange((int)pixels[offset + 1], green - tolerance, green + tolerance);
        Assert.InRange((int)pixels[offset + 2], blue - tolerance, blue + tolerance);
        Assert.InRange((int)pixels[offset + 3], alpha - tolerance, alpha + tolerance);
    }

    private static bool IsD3D11TestEnabled()
    {
        return OperatingSystem.IsWindows() &&
            string.Equals(Environment.GetEnvironmentVariable("ROYALTERMINAL_TEST_D3D11"), "1", StringComparison.Ordinal);
    }
}
