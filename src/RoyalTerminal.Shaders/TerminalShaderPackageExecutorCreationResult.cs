// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime registration.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Contains the result of resolving a shader package executor from registered runtimes.
/// </summary>
public sealed class TerminalShaderPackageExecutorCreationResult
{
    /// <summary>
    /// Initializes a package-executor creation result.
    /// </summary>
    /// <param name="backendKind">Resolved backend kind.</param>
    /// <param name="executor">Created executor.</param>
    /// <param name="diagnostics">Creation diagnostics.</param>
    public TerminalShaderPackageExecutorCreationResult(
        TerminalShaderBackendKind backendKind,
        ITerminalShaderPackageExecutor? executor,
        IReadOnlyList<TerminalShaderDiagnostic>? diagnostics = null)
    {
        BackendKind = backendKind;
        Executor = executor;
        Diagnostics = diagnostics is null ? [] : diagnostics.ToArray();
        IsSuccess = executor is not null &&
            !Diagnostics.Any(static diagnostic => diagnostic.Severity == TerminalShaderDiagnosticSeverity.Error);
    }

    /// <summary>
    /// Gets whether an executor was created without error diagnostics.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the resolved backend kind.
    /// </summary>
    public TerminalShaderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets the created executor.
    /// </summary>
    public ITerminalShaderPackageExecutor? Executor { get; }

    /// <summary>
    /// Gets creation diagnostics.
    /// </summary>
    public IReadOnlyList<TerminalShaderDiagnostic> Diagnostics { get; }
}
