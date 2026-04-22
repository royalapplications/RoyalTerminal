// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Diagnostic shader runtime used when a requested backend is unavailable.
/// </summary>
public sealed class TerminalShaderUnavailableRuntime : ITerminalShaderRuntime
{
    private readonly string _reason;

    /// <summary>
    /// Initializes a new unavailable runtime.
    /// </summary>
    /// <param name="backendKind">Unavailable backend kind.</param>
    /// <param name="reason">Human-readable unavailability reason.</param>
    public TerminalShaderUnavailableRuntime(TerminalShaderBackendKind backendKind, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Unavailable runtime reason must be non-empty.", nameof(reason));
        }

        _reason = reason.Trim();
        Capabilities = new TerminalShaderBackendCapabilities(
            backendKind,
            supportsPixelShaders: false,
            supportsComputeShaders: false,
            supportsUavResources: false,
            supportsTextureInterop: false,
            maxTextureSize: 1);
    }

    /// <inheritdoc />
    public TerminalShaderBackendCapabilities Capabilities { get; }

    /// <inheritdoc />
    public ValueTask<TerminalShaderRuntimeProgram> CreateProgramAsync(
        TerminalShaderCompilationResult compilation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TerminalShaderCompilationResult failed = TerminalShaderCompilationResult.Failed(
            [
                new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERRUNTIME001",
                    _reason),
            ]);
        TerminalShaderRuntimeProgram program = new(Capabilities.BackendKind, failed, Capabilities);
        return ValueTask.FromResult(program);
    }

    /// <inheritdoc />
    public ValueTask<TerminalShaderFrameResult> RenderFrameAsync(
        TerminalShaderRuntimeProgram program,
        TerminalShaderFrameRequest frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TerminalShaderFrameResult result = TerminalShaderFrameResult.Failed(
            Capabilities.BackendKind,
            [
                new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERRUNTIME001",
                    _reason),
            ]);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
