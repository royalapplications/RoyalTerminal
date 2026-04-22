// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Full terminal shader runtime tests.

using RoyalTerminal.Shaders;
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
        TerminalShaderFrameRequest frame = new(
            width: 4,
            height: 4,
            time: 0f,
            timeDelta: 0f,
            frame: 0,
            scale: 1f);

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

    [Fact]
    public void RuntimeValidator_DetectsMissingExternalFrameResources()
    {
        TerminalShaderPackage package = new(
            "resources",
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
                    "noise",
                    TerminalShaderResourceKind.Texture2D,
                    TerminalShaderResourceSource.External,
                    TerminalShaderValueType.Texture2D),
            ]);
        TerminalShaderFrameRequest frame = new(128, 64, 0f, 0f, 0, 1f);

        TerminalShaderPackageValidationResult result =
            TerminalShaderRuntimeValidator.ValidateFrameResources(package, frame);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME031");
    }

    [Fact]
    public void RuntimeValidator_DetectsRuntimeResourceKindAndSizeMismatch()
    {
        TerminalShaderPackage package = new(
            "resources",
            [new TerminalShaderFile("main.hlsl", "float4 Main() : SV_TARGET { return 1; }")],
            [
                new TerminalShaderPass(
                    "main",
                    TerminalShaderStage.Pixel,
                    "main.hlsl",
                    "Main",
                    TerminalShaderTargetProfile.PixelShader60,
                    outputs: [new TerminalShaderPassOutput("oversized", widthScale: 2f, heightScale: 1f)]),
            ],
            [
                new TerminalShaderResourceBinding(
                    "noise",
                    TerminalShaderResourceKind.Texture2D,
                    TerminalShaderResourceSource.External,
                    TerminalShaderValueType.Texture2D),
            ]);
        TerminalShaderFrameRequest frame = new(
            128,
            64,
            0f,
            0f,
            0,
            1f,
            [
                new TerminalShaderResourceValue(
                    "noise",
                    TerminalShaderResourceKind.Sampler,
                    width: 256,
                    height: 8),
            ]);
        TerminalShaderBackendCapabilities capabilities = new(
            TerminalShaderBackendKind.D3D11,
            supportsPixelShaders: true,
            supportsComputeShaders: true,
            supportsUavResources: true,
            supportsTextureInterop: true,
            maxTextureSize: 128);

        TerminalShaderPackageValidationResult result =
            TerminalShaderRuntimeValidator.ValidateFrameResources(package, frame, capabilities);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME032");
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME033");
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME034");
    }

    [Fact]
    public async Task RuntimePipeline_CreateFrameRequest_ResolvesExternalResources()
    {
        TerminalShaderPackage package = CreatePackageWithExternalTexture(optional: true);
        TerminalShaderResourceValue texture = new(
            "noise",
            TerminalShaderResourceKind.Texture2D,
            nativeHandle: 42,
            width: 32,
            height: 16);
        FixedResourceProvider provider = new(texture);

        TerminalShaderFrameRequest frame = await TerminalShaderRuntimePipeline.CreateFrameRequestAsync(
            package,
            width: 80,
            height: 24,
            time: 1f,
            timeDelta: 0.016f,
            frame: 12,
            scale: 2f,
            provider);

        TerminalShaderResourceValue resolved = Assert.Single(frame.Resources);
        Assert.Equal("noise", resolved.Name);
        Assert.Equal(42, resolved.NativeHandle);
    }

    [Fact]
    public async Task RuntimePipeline_RenderFrame_ReturnsValidationDiagnosticsBeforeRuntime()
    {
        TerminalShaderPackage package = CreatePackageWithExternalTexture(optional: false);
        FakeShaderRuntime runtime = new(
            new TerminalShaderBackendCapabilities(
                TerminalShaderBackendKind.D3D11,
                supportsPixelShaders: true,
                supportsComputeShaders: true,
                supportsUavResources: true,
                supportsTextureInterop: true,
                maxTextureSize: 4096));
        using TerminalShaderRuntimeProgram program = await runtime.CreateProgramAsync(CreateCompilationResult());
        TerminalShaderFrameRequest frame = new(80, 24, 0f, 0f, 0, 1f);

        TerminalShaderFrameResult result = await TerminalShaderRuntimePipeline.RenderFrameAsync(
            package,
            runtime,
            program,
            frame);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, runtime.RenderFrameCallCount);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERRUNTIME031");
    }

    [Fact]
    public async Task RuntimePipeline_RenderFrame_ForwardsValidFrame()
    {
        TerminalShaderPackage package = CreatePackageWithExternalTexture(optional: false);
        FakeShaderRuntime runtime = new(
            new TerminalShaderBackendCapabilities(
                TerminalShaderBackendKind.D3D11,
                supportsPixelShaders: true,
                supportsComputeShaders: true,
                supportsUavResources: true,
                supportsTextureInterop: true,
                maxTextureSize: 4096));
        using TerminalShaderRuntimeProgram program = await runtime.CreateProgramAsync(CreateCompilationResult());
        TerminalShaderFrameRequest frame = new(
            80,
            24,
            0f,
            0f,
            0,
            1f,
            [
                new TerminalShaderResourceValue(
                    "noise",
                    TerminalShaderResourceKind.Texture2D,
                    nativeHandle: 42,
                    width: 32,
                    height: 16),
            ]);

        TerminalShaderFrameResult result = await TerminalShaderRuntimePipeline.RenderFrameAsync(
            package,
            runtime,
            program,
            frame);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, runtime.RenderFrameCallCount);
        Assert.Equal(80, result.Width);
        Assert.Equal(24, result.Height);
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

    private static TerminalShaderPackage CreatePackageWithExternalTexture(bool optional)
    {
        return new TerminalShaderPackage(
            "resources",
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
                    "noise",
                    TerminalShaderResourceKind.Texture2D,
                    TerminalShaderResourceSource.External,
                    TerminalShaderValueType.Texture2D,
                    optional: optional),
            ]);
    }

    private sealed class FixedResourceProvider : ITerminalShaderResourceProvider
    {
        private readonly TerminalShaderResourceValue _value;

        public FixedResourceProvider(TerminalShaderResourceValue value)
        {
            _value = value;
        }

        public ValueTask<TerminalShaderResourceValue?> TryGetResourceAsync(
            string resourceName,
            CancellationToken cancellationToken = default)
        {
            TerminalShaderResourceValue? value = string.Equals(
                resourceName,
                _value.Name,
                StringComparison.OrdinalIgnoreCase)
                ? _value
                : null;
            return ValueTask.FromResult(value);
        }
    }

    private sealed class FakeShaderRuntime : ITerminalShaderRuntime
    {
        public FakeShaderRuntime(TerminalShaderBackendCapabilities capabilities)
        {
            Capabilities = capabilities;
        }

        public TerminalShaderBackendCapabilities Capabilities { get; }

        public int RenderFrameCallCount { get; private set; }

        public ValueTask<TerminalShaderRuntimeProgram> CreateProgramAsync(
            TerminalShaderCompilationResult compilation,
            CancellationToken cancellationToken = default)
        {
            TerminalShaderRuntimeProgram program = new(Capabilities.BackendKind, compilation, Capabilities);
            return ValueTask.FromResult(program);
        }

        public ValueTask<TerminalShaderFrameResult> RenderFrameAsync(
            TerminalShaderRuntimeProgram program,
            TerminalShaderFrameRequest frame,
            CancellationToken cancellationToken = default)
        {
            RenderFrameCallCount++;
            TerminalShaderFrameResult result = new(
                Capabilities.BackendKind,
                pixelData: new byte[frame.Width * frame.Height * 4],
                width: frame.Width,
                height: frame.Height);
            return ValueTask.FromResult(result);
        }

        public void Dispose()
        {
        }
    }

}
