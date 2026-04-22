// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Full terminal shader compilation pipeline tests.

using System.Text;
using RoyalTerminal.Shaders;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderCompilationPipelineTests
{
    [Fact]
    public async Task CompileAsync_InvalidPackage_ReturnsValidationDiagnosticsWithoutCallingCompiler()
    {
        TerminalShaderPackage package = new(
            "invalid",
            [new TerminalShaderFile("main.hlsl", "float4 Main() : SV_TARGET { return 1; }")],
            []);
        CapturingCompiler compiler = new();
        TerminalShaderCompilationOptions options = new(TerminalShaderBackendKind.D3D11);

        TerminalShaderCompilationResult result =
            await TerminalShaderCompilationPipeline.CompileAsync(package, compiler, options);

        Assert.False(result.IsSuccess);
        Assert.False(compiler.WasCalled);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADER040");
    }

    [Fact]
    public async Task CompileAsync_ResolvesIncludesBeforeCallingCompiler()
    {
        TerminalShaderPackage package = CreatePackageWithExternalInclude();
        TerminalShaderInMemoryIncludeProvider provider = new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["include/common.hlsl"] = "float4 Common() { return float4(1, 0, 0, 1); }",
            });
        CapturingCompiler compiler = new();
        TerminalShaderCompilationOptions options = new(
            TerminalShaderBackendKind.D3D11,
            TerminalShaderCompilerKind.Dxc,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["ENABLE_TEST"] = "1" },
            "test-package");

        TerminalShaderCompilationResult result =
            await TerminalShaderCompilationPipeline.CompileAsync(package, compiler, options, provider);

        Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        Assert.True(compiler.WasCalled);
        Assert.NotNull(compiler.LastRequest);
        Assert.Equal(TerminalShaderBackendKind.D3D11, compiler.LastRequest.Options.BackendKind);
        Assert.Equal(TerminalShaderCompilerKind.Dxc, compiler.LastRequest.Options.CompilerKind);
        Assert.Contains(compiler.LastRequest.ResolvedFiles, static file => file.VirtualPath == "main.hlsl");
        Assert.Contains(compiler.LastRequest.ResolvedFiles, static file => file.VirtualPath == "include/common.hlsl");
    }

    [Fact]
    public async Task CompileAsync_PropagatesCompilerDiagnostics()
    {
        TerminalShaderPackage package = CreatePackage();
        TerminalShaderDiagnostic compilerError = new(
            TerminalShaderDiagnosticSeverity.Error,
            "DXC0001",
            "Compiler failed.",
            "main.hlsl",
            line: 1,
            column: 1);
        CapturingCompiler compiler = new(new TerminalShaderCompilationResult(
            diagnostics: [compilerError]));
        TerminalShaderCompilationOptions options = new(TerminalShaderBackendKind.D3D11);

        TerminalShaderCompilationResult result =
            await TerminalShaderCompilationPipeline.CompileAsync(package, compiler, options);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "DXC0001");
    }

    [Fact]
    public async Task CompileAsync_BlocksComputePackageForSkiaRuntimeEffectBackend()
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
                    new TerminalShaderDispatch(8, 8),
                    outputs: [new TerminalShaderPassOutput("output", TerminalShaderResourceKind.UavTexture2D)]),
            ]);
        CapturingCompiler compiler = new();
        TerminalShaderCompilationOptions options = new(TerminalShaderBackendKind.SkiaRuntimeEffect);

        TerminalShaderCompilationResult result =
            await TerminalShaderCompilationPipeline.CompileAsync(package, compiler, options);

        Assert.False(result.IsSuccess);
        Assert.False(compiler.WasCalled);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME021");
    }

    [Fact]
    public async Task CachingCompiler_ReusesResultForEquivalentRequest()
    {
        TerminalShaderPackage package = CreatePackage();
        CapturingCompiler innerCompiler = new();
        TerminalShaderCachingCompiler compiler = new(innerCompiler);
        TerminalShaderCompilationOptions options = new(TerminalShaderBackendKind.D3D11);

        TerminalShaderCompilationResult first =
            await TerminalShaderCompilationPipeline.CompileAsync(package, compiler, options);
        TerminalShaderCompilationResult second =
            await TerminalShaderCompilationPipeline.CompileAsync(package, compiler, options);

        Assert.True(first.IsSuccess, FormatDiagnostics(first.Diagnostics));
        Assert.True(second.IsSuccess, FormatDiagnostics(second.Diagnostics));
        Assert.Same(first, second);
        Assert.Equal(1, innerCompiler.CallCount);
        Assert.Equal(1, compiler.Count);
    }

    [Fact]
    public async Task CachingCompiler_InvalidatesWhenDefinesChange()
    {
        TerminalShaderPackage package = CreatePackage();
        CapturingCompiler innerCompiler = new();
        TerminalShaderCachingCompiler compiler = new(innerCompiler);

        await TerminalShaderCompilationPipeline.CompileAsync(
            package,
            compiler,
            new TerminalShaderCompilationOptions(
                TerminalShaderBackendKind.D3D11,
                defines: new Dictionary<string, string>(StringComparer.Ordinal) { ["MODE"] = "0" }));
        await TerminalShaderCompilationPipeline.CompileAsync(
            package,
            compiler,
            new TerminalShaderCompilationOptions(
                TerminalShaderBackendKind.D3D11,
                defines: new Dictionary<string, string>(StringComparer.Ordinal) { ["MODE"] = "1" }));

        Assert.Equal(2, innerCompiler.CallCount);
        Assert.Equal(2, compiler.Count);
    }

    private static TerminalShaderPackage CreatePackageWithExternalInclude()
    {
        return new TerminalShaderPackage(
            "includes",
            [new TerminalShaderFile("main.hlsl", "#include \"include/common.hlsl\"\nfloat4 Main() : SV_TARGET { return Common(); }")],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60,
                    outputs: [new TerminalShaderPassOutput("final")]),
            ],
            options: new TerminalShaderPackageOptions(allowExternalIncludes: true));
    }

    private static TerminalShaderPackage CreatePackage()
    {
        return new TerminalShaderPackage(
            "simple",
            [new TerminalShaderFile("main.hlsl", "float4 Main() : SV_TARGET { return 1; }")],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60,
                    outputs: [new TerminalShaderPassOutput("final")]),
            ]);
    }

    private static string FormatDiagnostics(IReadOnlyList<TerminalShaderDiagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString()));
    }

    private sealed class CapturingCompiler : ITerminalShaderCompiler
    {
        private readonly TerminalShaderCompilationResult? _result;

        public CapturingCompiler(TerminalShaderCompilationResult? result = null)
        {
            _result = result;
        }

        public bool WasCalled { get; private set; }

        public int CallCount { get; private set; }

        public TerminalShaderCompilationRequest? LastRequest { get; private set; }

        public ValueTask<TerminalShaderCompilationResult> CompileAsync(
            TerminalShaderCompilationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WasCalled = true;
            CallCount++;
            LastRequest = request;
            TerminalShaderCompilationResult result = _result ?? new TerminalShaderCompilationResult(
                [
                    new TerminalShaderCompiledPass(
                        request.Package.Passes[0].Name,
                        request.Package.Passes[0].Stage,
                        TerminalShaderCompiledCodeFormat.Dxil,
                        Encoding.UTF8.GetBytes("compiled")),
                ]);
            return ValueTask.FromResult(result);
        }
    }
}
