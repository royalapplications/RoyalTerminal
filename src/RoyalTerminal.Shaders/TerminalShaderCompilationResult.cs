// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader compiler model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Contains the result of compiling a full terminal shader package.
/// </summary>
public sealed class TerminalShaderCompilationResult
{
    /// <summary>
    /// Initializes a new shader compilation result.
    /// </summary>
    /// <param name="passes">Compiled passes.</param>
    /// <param name="diagnostics">Compilation diagnostics.</param>
    public TerminalShaderCompilationResult(
        IReadOnlyList<TerminalShaderCompiledPass>? passes = null,
        IReadOnlyList<TerminalShaderDiagnostic>? diagnostics = null)
    {
        Passes = passes is null ? [] : passes.ToArray();
        Diagnostics = diagnostics is null ? [] : diagnostics.ToArray();
        IsSuccess = Passes.Count > 0 &&
            !Diagnostics.Any(static diagnostic => diagnostic.Severity == TerminalShaderDiagnosticSeverity.Error);
    }

    /// <summary>
    /// Gets whether compilation produced at least one pass and no errors.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets compiled passes.
    /// </summary>
    public IReadOnlyList<TerminalShaderCompiledPass> Passes { get; }

    /// <summary>
    /// Gets compilation diagnostics.
    /// </summary>
    public IReadOnlyList<TerminalShaderDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Creates a failed compilation result from diagnostics.
    /// </summary>
    /// <param name="diagnostics">Failure diagnostics.</param>
    /// <returns>A failed compilation result.</returns>
    public static TerminalShaderCompilationResult Failed(IReadOnlyList<TerminalShaderDiagnostic> diagnostics)
    {
        return new TerminalShaderCompilationResult([], diagnostics);
    }
}
