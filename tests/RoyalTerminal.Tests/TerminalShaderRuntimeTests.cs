// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Full terminal shader runtime tests.

using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderRuntimeTests
{
    [Fact]
    public async Task UnavailableRuntime_CreateProgram_ReturnsDiagnosticProgram()
    {
        using TerminalShaderUnavailableRuntime runtime = new(
            TerminalShaderBackendKind.D3D11,
            "D3D11 runtime is unavailable.");
        TerminalShaderCompilationResult compilation = CreateCompilationResult();

        using TerminalShaderRuntimeProgram program =
            await runtime.CreateProgramAsync(compilation);

        Assert.Equal(TerminalShaderBackendKind.D3D11, program.BackendKind);
        Assert.False(program.Capabilities.SupportsPixelShaders);
        Assert.False(program.Compilation.IsSuccess);
        Assert.Contains(program.Compilation.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME001");
    }

    [Fact]
    public async Task UnavailableRuntime_RenderFrame_ReturnsFailure()
    {
        using TerminalShaderUnavailableRuntime runtime = new(
            TerminalShaderBackendKind.Vulkan,
            "Vulkan runtime is unavailable.");
        using TerminalShaderRuntimeProgram program = await runtime.CreateProgramAsync(CreateCompilationResult());
        TerminalShaderFrameRequest frame = new(CreateFrameContext());

        TerminalShaderFrameResult result = await runtime.RenderFrameAsync(program, frame);

        Assert.False(result.IsSuccess);
        Assert.Equal(TerminalShaderBackendKind.Vulkan, result.BackendKind);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME001");
    }

    [Fact]
    public void RuntimeProgram_DisposeCallbackRunsOnce()
    {
        int disposeCount = 0;
        TerminalShaderRuntimeProgram program = new(
            TerminalShaderBackendKind.D3D11,
            CreateCompilationResult(),
            new TerminalShaderBackendCapabilities(
                TerminalShaderBackendKind.D3D11,
                supportsPixelShaders: true,
                supportsComputeShaders: true,
                supportsUavResources: true,
                supportsTextureInterop: true,
                maxTextureSize: 16384),
            dispose: () => disposeCount++);

        program.Dispose();
        program.Dispose();

        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public void FrameResult_WithPixelData_IsSuccessful()
    {
        TerminalShaderFrameResult result = new(
            TerminalShaderBackendKind.D3D11,
            pixelData: new byte[] { 0, 0, 0, 255 },
            width: 1,
            height: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
    }

    [Fact]
    public void RuntimeValidator_DetectsUnsupportedComputeAndUavResources()
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
            ],
            [
                new TerminalShaderResourceBinding(
                    "scratch",
                    TerminalShaderResourceKind.UavTexture2D,
                    TerminalShaderResourceSource.External,
                    TerminalShaderValueType.Texture2D),
            ]);
        TerminalShaderBackendCapabilities capabilities = new(
            TerminalShaderBackendKind.SkiaRuntimeEffect,
            supportsPixelShaders: true,
            supportsComputeShaders: false,
            supportsUavResources: false,
            supportsTextureInterop: false,
            maxTextureSize: 4096);

        TerminalShaderPackageValidationResult result =
            TerminalShaderRuntimeValidator.ValidateCapabilities(package, capabilities);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME021");
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME022");
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME023");
    }

    private static TerminalShaderCompilationResult CreateCompilationResult()
    {
        return new TerminalShaderCompilationResult(
            [
                new TerminalShaderCompiledPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    TerminalShaderCompiledCodeFormat.Dxil,
                    new byte[] { 1, 2, 3 }),
            ]);
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
