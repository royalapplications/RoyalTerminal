// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - DXC shader compiler backend tests.

using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderDxcCliCompilerTests
{
    [Fact]
    public async Task CompileAsync_MissingExecutable_ReturnsUnavailableDiagnostic()
    {
        TerminalShaderPackage package = CreateSimplePixelPackage();
        TerminalShaderDxcCliCompiler compiler = new(
            "royalterminal-missing-dxc-executable",
            TimeSpan.FromSeconds(1));
        TerminalShaderCompilationOptions options = new(
            TerminalShaderBackendKind.D3D11,
            TerminalShaderCompilerKind.Dxc);

        TerminalShaderCompilationResult result =
            await TerminalShaderCompilationPipeline.CompileAsync(package, compiler, options);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERDXC001");
    }

    [Fact]
    public async Task CompileAsync_WhenDxcIsAvailable_CompilesPixelShader()
    {
        if (!await TerminalShaderDxcCliCompiler.IsAvailableAsync())
        {
            return;
        }

        TerminalShaderPackage package = CreateSimplePixelPackage();
        TerminalShaderDxcCliCompiler compiler = new(timeout: TimeSpan.FromSeconds(10));
        TerminalShaderCompilationOptions options = new(
            TerminalShaderBackendKind.D3D11,
            TerminalShaderCompilerKind.Dxc);

        TerminalShaderCompilationResult result =
            await TerminalShaderCompilationPipeline.CompileAsync(package, compiler, options);

        Assert.True(result.IsSuccess, FormatDiagnostics(result.Diagnostics));
        TerminalShaderCompiledPass pass = Assert.Single(result.Passes);
        Assert.Equal("main", pass.PassName);
        Assert.Equal(TerminalShaderCompiledCodeFormat.Dxil, pass.Format);
        Assert.True(pass.Code.Length > 0);
        Assert.Contains(pass.Reflection.EntryPoints, static entry => entry.Name == "Main");
    }

    private static TerminalShaderPackage CreateSimplePixelPackage()
    {
        return new TerminalShaderPackage(
            "dxc-simple",
            [
                new TerminalShaderFile(
                    "main.hlsl",
                    """
                    struct PSInput
                    {
                        float4 pos : SV_POSITION;
                        float2 uv : TEXCOORD0;
                    };

                    float4 Main(PSInput input) : SV_TARGET
                    {
                        return float4(input.uv, 0.0, 1.0);
                    }
                    """),
            ],
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
}
