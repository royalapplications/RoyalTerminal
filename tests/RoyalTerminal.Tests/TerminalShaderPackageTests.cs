// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Full terminal shader package tests.

using RoyalTerminal.Avalonia.Rendering;
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
