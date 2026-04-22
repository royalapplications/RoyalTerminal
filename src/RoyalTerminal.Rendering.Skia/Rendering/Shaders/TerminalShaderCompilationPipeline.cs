// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader compiler model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Orchestrates validation, include resolution, and compiler invocation for full shader packages.
/// </summary>
public static class TerminalShaderCompilationPipeline
{
    /// <summary>
    /// Validates, resolves includes, and compiles a shader package.
    /// </summary>
    /// <param name="package">Shader package.</param>
    /// <param name="compiler">Shader compiler.</param>
    /// <param name="options">Compilation options.</param>
    /// <param name="includeProvider">Optional include provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The compilation result.</returns>
    public static async ValueTask<TerminalShaderCompilationResult> CompileAsync(
        TerminalShaderPackage package,
        ITerminalShaderCompiler compiler,
        TerminalShaderCompilationOptions options,
        ITerminalShaderIncludeProvider? includeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(options);

        TerminalShaderPackageValidationResult validation = TerminalShaderPackageValidator.Validate(package);
        if (!validation.IsValid)
        {
            return TerminalShaderCompilationResult.Failed(validation.Diagnostics);
        }

        TerminalShaderBackendCapabilities compileCapabilities = GetCompileCapabilities(options.BackendKind);
        TerminalShaderPackageValidationResult capabilityValidation =
            TerminalShaderRuntimeValidator.ValidateCapabilities(package, compileCapabilities);
        if (!capabilityValidation.IsValid)
        {
            return TerminalShaderCompilationResult.Failed(capabilityValidation.Diagnostics);
        }

        TerminalShaderIncludeResolutionResult includeResult =
            await TerminalShaderIncludeResolver.ResolveAsync(package, includeProvider, cancellationToken)
                .ConfigureAwait(false);
        if (!includeResult.IsValid)
        {
            return TerminalShaderCompilationResult.Failed(includeResult.Diagnostics);
        }

        TerminalShaderCompilationRequest request = new(package, includeResult.Files, options);
        TerminalShaderCompilationResult result = await compiler
            .CompileAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    private static TerminalShaderBackendCapabilities GetCompileCapabilities(TerminalShaderBackendKind backendKind)
    {
        return backendKind switch
        {
            TerminalShaderBackendKind.SkiaRuntimeEffect => new TerminalShaderBackendCapabilities(
                backendKind,
                supportsPixelShaders: true,
                supportsComputeShaders: false,
                supportsUavResources: false,
                supportsTextureInterop: false,
                maxTextureSize: 32768),
            TerminalShaderBackendKind.D3D11 or TerminalShaderBackendKind.D3D12 or
                TerminalShaderBackendKind.Vulkan or TerminalShaderBackendKind.Metal => new TerminalShaderBackendCapabilities(
                    backendKind,
                    supportsPixelShaders: true,
                    supportsComputeShaders: true,
                    supportsUavResources: true,
                    supportsTextureInterop: true,
                    maxTextureSize: 16384),
            _ => new TerminalShaderBackendCapabilities(
                backendKind,
                supportsPixelShaders: false,
                supportsComputeShaders: false,
                supportsUavResources: false,
                supportsTextureInterop: false,
                maxTextureSize: 1),
        };
    }
}
