// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package includes.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Contains the result of resolving a shader package include graph.
/// </summary>
public sealed class TerminalShaderIncludeResolutionResult
{
    /// <summary>
    /// Initializes a new include resolution result.
    /// </summary>
    /// <param name="files">Resolved source files.</param>
    /// <param name="diagnostics">Include diagnostics.</param>
    public TerminalShaderIncludeResolutionResult(
        IReadOnlyList<TerminalShaderFile> files,
        IReadOnlyList<TerminalShaderDiagnostic> diagnostics)
    {
        Files = files is null ? throw new ArgumentNullException(nameof(files)) : files.ToArray();
        Diagnostics = diagnostics is null ? throw new ArgumentNullException(nameof(diagnostics)) : diagnostics.ToArray();
        IsValid = !Diagnostics.Any(static diagnostic => diagnostic.Severity == TerminalShaderDiagnosticSeverity.Error);
    }

    /// <summary>
    /// Gets whether include resolution completed without errors.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets resolved source files.
    /// </summary>
    public IReadOnlyList<TerminalShaderFile> Files { get; }

    /// <summary>
    /// Gets include diagnostics.
    /// </summary>
    public IReadOnlyList<TerminalShaderDiagnostic> Diagnostics { get; }
}
