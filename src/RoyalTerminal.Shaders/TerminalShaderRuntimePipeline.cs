// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime orchestration.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Orchestrates backend-neutral full shader package frame execution.
/// </summary>
public static class TerminalShaderRuntimePipeline
{
    /// <summary>
    /// Creates a frame request and resolves external package resources from a provider.
    /// </summary>
    /// <param name="package">Shader package that declares the resources.</param>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <param name="height">Framebuffer height in pixels.</param>
    /// <param name="time">Elapsed shader time in seconds.</param>
    /// <param name="timeDelta">Elapsed time since the previous shader frame in seconds.</param>
    /// <param name="frame">Shader frame index.</param>
    /// <param name="scale">Display scale factor.</param>
    /// <param name="resourceProvider">Optional external resource provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A frame request containing resolved external resources.</returns>
    public static async ValueTask<TerminalShaderFrameRequest> CreateFrameRequestAsync(
        TerminalShaderPackage package,
        int width,
        int height,
        float time,
        float timeDelta,
        int frame,
        float scale,
        ITerminalShaderResourceProvider? resourceProvider = null,
        CancellationToken cancellationToken = default)
    {
        return await CreateFrameRequestAsync(
            package,
            width,
            height,
            time,
            timeDelta,
            frame,
            scale,
            builtInResources: null,
            resourceProvider,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a frame request and resolves built-in and external package resources.
    /// </summary>
    /// <param name="package">Shader package that declares the resources.</param>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <param name="height">Framebuffer height in pixels.</param>
    /// <param name="time">Elapsed shader time in seconds.</param>
    /// <param name="timeDelta">Elapsed time since the previous shader frame in seconds.</param>
    /// <param name="frame">Shader frame index.</param>
    /// <param name="scale">Display scale factor.</param>
    /// <param name="builtInResources">Built-in resources supplied by the renderer.</param>
    /// <param name="resourceProvider">Optional external resource provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A frame request containing resolved built-in and external resources.</returns>
    public static async ValueTask<TerminalShaderFrameRequest> CreateFrameRequestAsync(
        TerminalShaderPackage package,
        int width,
        int height,
        float time,
        float timeDelta,
        int frame,
        float scale,
        IReadOnlyList<TerminalShaderResourceValue>? builtInResources,
        ITerminalShaderResourceProvider? resourceProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        List<TerminalShaderResourceValue>? resources = null;
        if (builtInResources is not null && builtInResources.Count > 0)
        {
            resources = new List<TerminalShaderResourceValue>(builtInResources.Count);
            resources.AddRange(builtInResources);
        }

        if (resourceProvider is not null)
        {
            for (int i = 0; i < package.Resources.Count; i++)
            {
                TerminalShaderResourceBinding binding = package.Resources[i];
                if (binding.Source != TerminalShaderResourceSource.External)
                {
                    continue;
                }

                TerminalShaderResourceValue? value = await resourceProvider
                    .TryGetResourceAsync(binding.Name, cancellationToken)
                    .ConfigureAwait(false);
                if (value is not null)
                {
                    resources ??= [];
                    resources.Add(value);
                }
            }
        }

        return new TerminalShaderFrameRequest(
            width,
            height,
            time,
            timeDelta,
            frame,
            scale,
            resources);
    }

    /// <summary>
    /// Renders one frame after validating package, backend, and frame resource requirements.
    /// </summary>
    /// <param name="package">Shader package that owns the runtime program.</param>
    /// <param name="runtime">Runtime backend.</param>
    /// <param name="program">Runtime program.</param>
    /// <param name="frame">Frame request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The frame result, or validation diagnostics when execution is rejected.</returns>
    public static async ValueTask<TerminalShaderFrameResult> RenderFrameAsync(
        TerminalShaderPackage package,
        ITerminalShaderRuntime runtime,
        TerminalShaderRuntimeProgram program,
        TerminalShaderFrameRequest frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(frame);

        if (program.BackendKind != runtime.Capabilities.BackendKind)
        {
            return TerminalShaderFrameResult.Failed(
                runtime.Capabilities.BackendKind,
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERRUNTIME040",
                        $"Runtime backend '{runtime.Capabilities.BackendKind}' cannot execute program for backend '{program.BackendKind}'."),
                ]);
        }

        TerminalShaderPackageValidationResult capabilityValidation =
            TerminalShaderRuntimeValidator.ValidateCapabilities(package, runtime.Capabilities);
        TerminalShaderPackageValidationResult frameValidation =
            TerminalShaderRuntimeValidator.ValidateFrameResources(package, frame, runtime.Capabilities);
        if (capabilityValidation.IsValid && frameValidation.IsValid)
        {
            return await runtime.RenderFrameAsync(program, frame, cancellationToken)
                .ConfigureAwait(false);
        }

        List<TerminalShaderDiagnostic> diagnostics = new(
            capabilityValidation.Diagnostics.Count + frameValidation.Diagnostics.Count);
        diagnostics.AddRange(capabilityValidation.Diagnostics);
        diagnostics.AddRange(frameValidation.Diagnostics);
        return TerminalShaderFrameResult.Failed(runtime.Capabilities.BackendKind, diagnostics);
    }
}
