// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Terminal shader package validation.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Contains the result of validating a full terminal shader package.
/// </summary>
public sealed class TerminalShaderPackageValidationResult
{
    /// <summary>
    /// Initializes a new validation result.
    /// </summary>
    /// <param name="diagnostics">Validation diagnostics.</param>
    public TerminalShaderPackageValidationResult(IReadOnlyList<TerminalShaderDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics is null
            ? throw new ArgumentNullException(nameof(diagnostics))
            : diagnostics.ToArray();
        IsValid = !Diagnostics.Any(static diagnostic => diagnostic.Severity == TerminalShaderDiagnosticSeverity.Error);
    }

    /// <summary>
    /// Gets whether validation completed without errors.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets validation diagnostics.
    /// </summary>
    public IReadOnlyList<TerminalShaderDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets a successful validation result.
    /// </summary>
    public static TerminalShaderPackageValidationResult Success { get; } = new([]);
}
