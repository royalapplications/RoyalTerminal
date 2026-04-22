// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Shader package corpus tests.

using RoyalTerminal.Demo.Services;
using RoyalTerminal.Shaders;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderCorpusTests
{
    public static IEnumerable<object[]> SyntheticPackages()
    {
        yield return [CreateExternalTextureAndCBufferPackage()];
        yield return [CreateIncludeAndSemanticPackage()];
        yield return [CreateComputeUavPackage()];
    }

    [Fact]
    public void DemoPackageCorpus_ValidatesAndReflects()
    {
        int packageCount = 0;
        foreach (TerminalShaderSampleOption option in TerminalShaderSampleCatalog.Options)
        {
            TerminalShaderPackage? package = TerminalShaderSampleCatalog.GetPackage(option.Id);
            if (package is null)
            {
                continue;
            }

            packageCount++;
            TerminalShaderPackageValidationResult validation = TerminalShaderPackageValidator.Validate(package);
            TerminalShaderReflectionResult reflection = TerminalShaderHlslReflectionScanner.ScanPackage(package);

            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Diagnostics));
            Assert.True(reflection.Reflection.EntryPoints.Count >= package.Passes.Count);
            Assert.Contains(reflection.Reflection.Resources, static resource =>
                resource.Kind == TerminalShaderResourceKind.ConstantBuffer);
        }

        Assert.True(packageCount >= 3);
    }

    [Theory]
    [MemberData(nameof(SyntheticPackages))]
    public void SyntheticPackageCorpus_ValidatesAndReflects(TerminalShaderPackage package)
    {
        TerminalShaderPackageValidationResult validation = TerminalShaderPackageValidator.Validate(package);
        TerminalShaderReflectionResult reflection = TerminalShaderHlslReflectionScanner.ScanPackage(package);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Diagnostics));
        Assert.NotEmpty(reflection.Reflection.EntryPoints);
        Assert.NotEmpty(reflection.Reflection.Resources);
    }

    [Fact]
    public async Task IncludeCorpus_ResolvesNestedIncludes()
    {
        TerminalShaderPackage package = CreateIncludeAndSemanticPackage();
        TerminalShaderInMemoryIncludeProvider includes = new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["common/math.hlsl"] = "float4 ApplyTone(float4 color) { return saturate(color); }",
            });
        CapturingCompiler compiler = new();

        TerminalShaderCompilationResult result = await TerminalShaderCompilationPipeline.CompileAsync(
            package,
            compiler,
            new TerminalShaderCompilationOptions(TerminalShaderBackendKind.D3D11),
            includes);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains(compiler.LastResolvedFiles, static file => file.VirtualPath == "common/math.hlsl");
    }

    private static TerminalShaderPackage CreateExternalTextureAndCBufferPackage()
    {
        return new TerminalShaderPackage(
            "corpus-texture-cbuffer",
            [
                new TerminalShaderFile(
                    "main.hlsl",
                    """
                    Texture2D TerminalFramebuffer : register(t0);
                    Texture2D NoiseTexture : register(t1);
                    SamplerState LinearSampler : register(s0);

                    cbuffer TerminalFrame : register(b0)
                    {
                        float2 Resolution;
                        float Time;
                        float TimeDelta;
                        float Scale;
                        float3 Padding0;
                        float4 Background;
                    };

                    cbuffer UserParams : register(b1)
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
                        float4 color = TerminalFramebuffer.Sample(LinearSampler, uv);
                        float4 noise = NoiseTexture.Sample(LinearSampler, uv);
                        return saturate(color * Tint + noise * 0.05);
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
                        new TerminalShaderPassInput("NoiseTexture"),
                    ]),
            ],
            CreateFrameResources(
                [
                    new TerminalShaderResourceBinding(
                        "NoiseTexture",
                        TerminalShaderResourceKind.Texture2D,
                        TerminalShaderResourceSource.External,
                        TerminalShaderValueType.Texture2D,
                        registerIndex: 1),
                    new TerminalShaderResourceBinding(
                        "UserParams",
                        TerminalShaderResourceKind.ConstantBuffer,
                        TerminalShaderResourceSource.External,
                        TerminalShaderValueType.Float4,
                        registerIndex: 1),
                ]));
    }

    private static TerminalShaderPackage CreateIncludeAndSemanticPackage()
    {
        return new TerminalShaderPackage(
            "corpus-includes-semantics",
            [
                new TerminalShaderFile(
                    "main.hlsl",
                    """
                    #include "common/math.hlsl"

                    Texture2D TerminalFramebuffer : register(t0);
                    SamplerState LinearSampler : register(s0);

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
                        return ApplyTone(TerminalFramebuffer.Sample(LinearSampler, uv));
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
            CreateFrameResources(),
            new TerminalShaderPackageOptions(allowExternalIncludes: true));
    }

    private static TerminalShaderPackage CreateComputeUavPackage()
    {
        return new TerminalShaderPackage(
            "corpus-compute-uav",
            [
                new TerminalShaderFile(
                    "compute.hlsl",
                    """
                    RWTexture2D<float4> OutputTexture : register(u0);

                    [numthreads(8, 8, 1)]
                    void Main(uint3 id : SV_DispatchThreadID)
                    {
                        OutputTexture[id.xy] = float4(0.0, 0.0, 1.0, 1.0);
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
                        new TerminalShaderPassOutput("OutputTexture", TerminalShaderResourceKind.UavTexture2D),
                    ]),
            ],
            CreateFrameResources());
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
                "LinearSampler",
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

    private sealed class CapturingCompiler : ITerminalShaderCompiler
    {
        public IReadOnlyList<TerminalShaderFile> LastResolvedFiles { get; private set; } = [];

        public ValueTask<TerminalShaderCompilationResult> CompileAsync(
            TerminalShaderCompilationRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastResolvedFiles = request.ResolvedFiles;
            TerminalShaderCompiledPass pass = new(
                request.Package.Passes[0].Name,
                request.Package.Passes[0].Stage,
                TerminalShaderCompiledCodeFormat.Dxbc,
                new byte[] { 1, 2, 3 },
                TerminalShaderHlslReflectionScanner.ScanPackage(request.Package).Reflection);
            return ValueTask.FromResult(new TerminalShaderCompilationResult([pass]));
        }
    }
}
