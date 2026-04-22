// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Shader runtime registration tests.

using RoyalTerminal.Shaders;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderRuntimeRegistryTests
{
    [Fact]
    public void TryCreate_ExplicitBackend_CreatesRegisteredExecutor()
    {
        TerminalShaderPackageExecutorRegistry registry = new();
        FakePackageExecutor executor = new(TerminalShaderBackendKind.D3D11);
        registry.Register(new TerminalShaderPackageExecutorRegistration(
            TerminalShaderBackendKind.D3D11,
            TerminalShaderCompilerKind.D3DCompiler,
            "test d3d11",
            isAvailable: true,
            context =>
            {
                Assert.Equal(TerminalShaderBackendKind.D3D11, context.Options.BackendKind);
                Assert.Equal(TerminalShaderCompilerKind.D3DCompiler, context.Options.CompilerKind);
                return executor;
            }));

        TerminalShaderPackageExecutorCreationResult result = registry.TryCreate(
            TerminalShaderBackendPreference.D3D11,
            new TerminalShaderCompilationOptions(TerminalShaderBackendKind.D3D11));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Same(executor, result.Executor);
        Assert.Equal(TerminalShaderBackendKind.D3D11, result.BackendKind);
    }

    [Fact]
    public void TryCreate_AutoPreference_FallsBackToAvailableRegistration()
    {
        TerminalShaderBackendKind platformDefault =
            TerminalShaderBackendSelector.SelectBackend(TerminalShaderBackendPreference.Auto);
        TerminalShaderBackendKind fallback = platformDefault == TerminalShaderBackendKind.D3D11
            ? TerminalShaderBackendKind.Vulkan
            : TerminalShaderBackendKind.D3D11;
        TerminalShaderPackageExecutorRegistry registry = new();
        registry.Register(new TerminalShaderPackageExecutorRegistration(
            platformDefault,
            TerminalShaderCompilerKind.Auto,
            "unavailable default",
            isAvailable: false,
            static _ => new FakePackageExecutor(TerminalShaderBackendKind.Metal)));
        registry.Register(new TerminalShaderPackageExecutorRegistration(
            fallback,
            TerminalShaderCompilerKind.Slang,
            "available fallback",
            isAvailable: true,
            _ => new FakePackageExecutor(fallback)));

        TerminalShaderPackageExecutorCreationResult result = registry.TryCreate(TerminalShaderBackendPreference.Auto);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(fallback, result.BackendKind);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERREGISTRY002");
    }

    [Fact]
    public void TryCreate_CompilerMismatch_DoesNotCreateExecutor()
    {
        TerminalShaderPackageExecutorRegistry registry = new();
        registry.Register(new TerminalShaderPackageExecutorRegistration(
            TerminalShaderBackendKind.D3D11,
            TerminalShaderCompilerKind.D3DCompiler,
            "test d3d11",
            isAvailable: true,
            static _ => new FakePackageExecutor(TerminalShaderBackendKind.D3D11)));

        TerminalShaderPackageExecutorCreationResult result = registry.TryCreate(
            TerminalShaderBackendPreference.D3D11,
            new TerminalShaderCompilationOptions(
                TerminalShaderBackendKind.D3D11,
                TerminalShaderCompilerKind.Slang));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Executor);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERREGISTRY003");
    }

    [Fact]
    public void TryCreate_FactoryFailure_ReturnsDiagnostic()
    {
        TerminalShaderPackageExecutorRegistry registry = new();
        registry.Register(new TerminalShaderPackageExecutorRegistration(
            TerminalShaderBackendKind.D3D11,
            TerminalShaderCompilerKind.D3DCompiler,
            "test d3d11",
            isAvailable: true,
            static _ => throw new InvalidOperationException("native device unavailable")));

        TerminalShaderPackageExecutorCreationResult result = registry.TryCreate(TerminalShaderBackendPreference.D3D11);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Executor);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "RTSHADERREGISTRY004");
    }

    private sealed class FakePackageExecutor : ITerminalShaderPackageExecutor
    {
        public FakePackageExecutor(TerminalShaderBackendKind backendKind)
        {
            BackendKind = backendKind;
            Capabilities = new TerminalShaderBackendCapabilities(
                backendKind,
                supportsPixelShaders: true,
                supportsComputeShaders: true,
                supportsUavResources: true,
                supportsTextureInterop: true,
                maxTextureSize: 4096);
        }

        public TerminalShaderBackendKind BackendKind { get; }

        public TerminalShaderBackendCapabilities Capabilities { get; }

        public ValueTask<TerminalShaderFrameResult> RenderFrameAsync(
            TerminalShaderPackage package,
            TerminalShaderFrameRequest frame,
            CancellationToken cancellationToken = default)
        {
            _ = package;
            _ = cancellationToken;
            return ValueTask.FromResult(new TerminalShaderFrameResult(
                BackendKind,
                pixelData: new byte[frame.Width * frame.Height * 4],
                width: frame.Width,
                height: frame.Height));
        }

        public void Dispose()
        {
        }
    }
}
