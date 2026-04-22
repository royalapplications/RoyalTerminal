// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Full terminal shader package tests.

using RoyalTerminal.Shaders;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderPackageTests
{
    [Fact]
    public void Validate_ValidPixelPackage_ReturnsValid()
    {
        TerminalShaderPackage package = CreateValidPixelPackage();

        TerminalShaderPackageValidationResult result = TerminalShaderPackageValidator.Validate(package);

        Assert.True(result.IsValid, FormatDiagnostics(result.Diagnostics));
    }

    [Fact]
    public void Validate_DetectsDuplicateResourceNamesAndRegisters()
    {
        TerminalShaderPackage package = new(
            "duplicates",
            [new TerminalShaderFile("main.hlsl", "float4 Main() : SV_TARGET { return 1; }")],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60),
            ],
            [
                new TerminalShaderResourceBinding(
                    "Frame",
                    TerminalShaderResourceKind.TerminalFramebuffer,
                    TerminalShaderResourceSource.BuiltIn,
                    TerminalShaderValueType.Texture2D,
                    registerIndex: 0),
                new TerminalShaderResourceBinding(
                    "frame",
                    TerminalShaderResourceKind.Texture2D,
                    TerminalShaderResourceSource.External,
                    TerminalShaderValueType.Texture2D,
                    registerIndex: 0),
            ]);

        TerminalShaderPackageValidationResult result = TerminalShaderPackageValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADER020");
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADER021");
    }

    [Fact]
    public void Validate_ComputePassRequiresDispatch()
    {
        TerminalShaderPackage package = new(
            "compute",
            [new TerminalShaderFile("compute.hlsl", "[numthreads(8, 8, 1)] void Main() { }")],
            [
                new TerminalShaderPass(
                    "compute",
                    TerminalShaderStage.Compute,
                    "compute.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.ComputeShader60),
            ]);

        TerminalShaderPackageValidationResult result = TerminalShaderPackageValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADER060");
    }

    [Fact]
    public void Validate_ComputePassWithDispatch_ReturnsValid()
    {
        TerminalShaderPackage package = new(
            "compute",
            [new TerminalShaderFile("compute.hlsl", "[numthreads(8, 8, 1)] void Main() { }")],
            [
                new TerminalShaderPass(
                    "compute",
                    TerminalShaderStage.Compute,
                    "compute.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.ComputeShader60,
                    new TerminalShaderDispatch(8, 8, kind: TerminalShaderDispatchKind.CoverOutput),
                    outputs: [new TerminalShaderPassOutput("compute-output", TerminalShaderResourceKind.UavTexture2D)]),
            ]);

        TerminalShaderPackageValidationResult result = TerminalShaderPackageValidator.Validate(package);

        Assert.True(result.IsValid, FormatDiagnostics(result.Diagnostics));
    }

    [Fact]
    public void Validate_DetectsUnavailablePassInput()
    {
        TerminalShaderPackage package = new(
            "order",
            [new TerminalShaderFile("main.hlsl", "float4 Main() : SV_TARGET { return 1; }")],
            [
                new TerminalShaderPass(
                    "first",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60,
                    inputs: [new TerminalShaderPassInput("future")],
                    outputs: [new TerminalShaderPassOutput("first-output")]),
                new TerminalShaderPass(
                    "second",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60,
                    outputs: [new TerminalShaderPassOutput("future")]),
            ]);

        TerminalShaderPackageValidationResult result = TerminalShaderPackageValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADER071");
    }

    [Fact]
    public void Validate_DetectsPathTraversal()
    {
        TerminalShaderPackage package = new(
            "path traversal",
            [new TerminalShaderFile("effects/../main.hlsl", "float4 Main() : SV_TARGET { return 1; }")],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "effects/../main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60),
            ]);

        TerminalShaderPackageValidationResult result = TerminalShaderPackageValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADER004");
    }

    [Fact]
    public async Task IncludeResolver_ResolvesNestedExternalIncludes()
    {
        TerminalShaderPackage package = new(
            "includes",
            [new TerminalShaderFile("main.hlsl", "#include \"lib/common.hlsl\"\nfloat4 Main() : SV_TARGET { return Common(); }")],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60),
            ],
            options: new TerminalShaderPackageOptions(allowExternalIncludes: true));
        TerminalShaderInMemoryIncludeProvider provider = new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lib/common.hlsl"] = "#include \"math.hlsl\"\nfloat4 Common() { return MathColor(); }",
                ["lib/math.hlsl"] = "float4 MathColor() { return float4(1, 0, 0, 1); }",
            });

        TerminalShaderIncludeResolutionResult result =
            await TerminalShaderIncludeResolver.ResolveAsync(package, provider);

        Assert.True(result.IsValid, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(result.Files, static file => string.Equals(file.VirtualPath, "main.hlsl", StringComparison.Ordinal));
        Assert.Contains(result.Files, static file => string.Equals(file.VirtualPath, "lib/common.hlsl", StringComparison.Ordinal));
        Assert.Contains(result.Files, static file => string.Equals(file.VirtualPath, "lib/math.hlsl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IncludeResolver_DetectsDisabledExternalInclude()
    {
        TerminalShaderPackage package = new(
            "includes",
            [new TerminalShaderFile("main.hlsl", "#include \"lib/common.hlsl\"\nfloat4 Main() : SV_TARGET { return 1; }")],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60),
            ]);

        TerminalShaderIncludeResolutionResult result =
            await TerminalShaderIncludeResolver.ResolveAsync(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADER122");
    }

    [Fact]
    public async Task IncludeResolver_DetectsIncludeCycles()
    {
        TerminalShaderPackage package = new(
            "cycle",
            [
                new TerminalShaderFile("a.hlsl", "#include \"b.hlsl\"\nfloat4 A() { return 1; }"),
                new TerminalShaderFile("b.hlsl", "#include \"a.hlsl\"\nfloat4 B() { return 1; }"),
            ],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "a.hlsl",
                    "A",
                    TerminalShaderTargetProfile.PixelShader60),
            ]);

        TerminalShaderIncludeResolutionResult result =
            await TerminalShaderIncludeResolver.ResolveAsync(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADER120");
    }

    [Fact]
    public async Task IncludeResolver_DetectsProviderPathMismatch()
    {
        TerminalShaderPackage package = new(
            "mismatch",
            [new TerminalShaderFile("main.hlsl", "#include \"common.hlsl\"\nfloat4 Main() : SV_TARGET { return 1; }")],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60),
            ],
            options: new TerminalShaderPackageOptions(allowExternalIncludes: true));

        TerminalShaderIncludeResolutionResult result =
            await TerminalShaderIncludeResolver.ResolveAsync(package, new MismatchedIncludeProvider());

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADER126");
    }

    [Fact]
    public void HlslReflectionScanner_ReflectsResourcesSemanticsAndThreadGroups()
    {
        TerminalShaderPackage package = new(
            "reflection",
            [
                new TerminalShaderFile(
                    "main.hlsl",
                    """
                    cbuffer PixelSettings : register(b2)
                    {
                        float3 Tint;
                        float Intensity;
                    };

                    Texture2D<float4> terminalFrame : register(t0);
                    Texture2D noiseTexture : register(t1, space1);
                    SamplerState linearSampler : register(s0);
                    RWTexture2D<float4> outputTexture : register(u0);

                    struct PSInput
                    {
                        float4 pos : SV_POSITION;
                        float2 uv : TEXCOORD0;
                        float4 color : COLOR1;
                    };

                    float4 Main(PSInput input) : SV_TARGET0
                    {
                        return terminalFrame.Sample(linearSampler, input.uv) * Intensity;
                    }

                    [numthreads(8, 4, 1)]
                    void ComputeMain(uint3 id : SV_DispatchThreadID)
                    {
                        outputTexture[id.xy] = float4(1, 0, 0, 1);
                    }
                    """),
            ],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60),
                new TerminalShaderPass(
                    "compute",
                    TerminalShaderStage.Compute,
                    "main.hlsl",
                    "ComputeMain",
                    TerminalShaderTargetProfile.ComputeShader60,
                    new TerminalShaderDispatch(1, 1),
                    outputs: [new TerminalShaderPassOutput("outputTexture", TerminalShaderResourceKind.UavTexture2D)]),
            ]);

        TerminalShaderReflectionResult result = TerminalShaderHlslReflectionScanner.ScanPackage(package);

        Assert.True(result.IsValid, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(result.Reflection.Resources, static resource =>
            resource.Name == "PixelSettings" &&
            resource.Kind == TerminalShaderResourceKind.ConstantBuffer &&
            resource.RegisterIndex == 2 &&
            resource.SizeInBytes == 16);
        Assert.Contains(result.Reflection.Resources, static resource =>
            resource.Name == "noiseTexture" &&
            resource.Kind == TerminalShaderResourceKind.Texture2D &&
            resource.RegisterIndex == 1 &&
            resource.RegisterSpace == 1);
        Assert.Contains(result.Reflection.Resources, static resource =>
            resource.Name == "outputTexture" &&
            resource.Kind == TerminalShaderResourceKind.UavTexture2D &&
            resource.RegisterIndex == 0);

        TerminalShaderEntryPointReflection pixelEntry =
            Assert.Single(result.Reflection.EntryPoints, static entry => entry.Name == "Main");
        Assert.Contains(pixelEntry.Inputs, static semantic => semantic.Name == "SV_POSITION");
        Assert.Contains(pixelEntry.Inputs, static semantic => semantic.Name == "TEXCOORD" && semantic.SemanticIndex == 0);
        Assert.Contains(pixelEntry.Inputs, static semantic => semantic.Name == "COLOR" && semantic.SemanticIndex == 1);
        Assert.Contains(pixelEntry.Outputs, static semantic => semantic.Name == "SV_TARGET" && semantic.SemanticIndex == 0);

        TerminalShaderEntryPointReflection computeEntry =
            Assert.Single(result.Reflection.EntryPoints, static entry => entry.Name == "ComputeMain");
        Assert.NotNull(computeEntry.ThreadGroupSize);
        Assert.Equal(8, computeEntry.ThreadGroupSize.Value.X);
        Assert.Equal(4, computeEntry.ThreadGroupSize.Value.Y);
        Assert.Equal(1, computeEntry.ThreadGroupSize.Value.Z);
        Assert.Contains(computeEntry.Inputs, static semantic => semantic.Name == "SV_DispatchThreadID");
    }

    [Fact]
    public void HlslReflectionScanner_ComputesBasicConstantBufferPacking()
    {
        TerminalShaderPackage package = new(
            "packing",
            [
                new TerminalShaderFile(
                    "main.hlsl",
                    """
                    cbuffer PackedValues : register(b0)
                    {
                        float3 Color;
                        float Intensity;
                        float2 Offset;
                    };

                    float4 Main() : SV_TARGET { return float4(Color * Intensity, 1); }
                    """),
            ],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60),
            ]);

        TerminalShaderReflectionResult result = TerminalShaderHlslReflectionScanner.ScanPackage(package);

        TerminalShaderResourceReflection cbuffer =
            Assert.Single(result.Reflection.Resources, static resource => resource.Name == "PackedValues");
        Assert.Equal(32, cbuffer.SizeInBytes);
    }

    private static TerminalShaderPackage CreateValidPixelPackage()
    {
        return new TerminalShaderPackage(
            "valid",
            [new TerminalShaderFile("main.hlsl", "float4 Main() : SV_TARGET { return 1; }")],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60,
                    inputs: [new TerminalShaderPassInput("terminalFrame")],
                    outputs: [new TerminalShaderPassOutput("finalFrame")]),
            ],
            [
                new TerminalShaderResourceBinding(
                    "terminalFrame",
                    TerminalShaderResourceKind.TerminalFramebuffer,
                    TerminalShaderResourceSource.BuiltIn,
                    TerminalShaderValueType.Texture2D,
                    registerIndex: 0),
            ]);
    }

    private static string FormatDiagnostics(IReadOnlyList<TerminalShaderDiagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString()));
    }

    private sealed class MismatchedIncludeProvider : ITerminalShaderIncludeProvider
    {
        public ValueTask<TerminalShaderFile?> TryLoadAsync(
            string includePath,
            string? includingFile,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<TerminalShaderFile?>(new TerminalShaderFile(
                "other.hlsl",
                "float4 Other() { return 1; }"));
        }
    }
}
