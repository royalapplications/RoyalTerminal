// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader reflection model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Contains source-side shader reflection data and diagnostics.
/// </summary>
public sealed class TerminalShaderReflectionResult
{
    /// <summary>
    /// Initializes a new reflection result.
    /// </summary>
    /// <param name="reflection">Reflected shader data.</param>
    /// <param name="diagnostics">Reflection diagnostics.</param>
    public TerminalShaderReflectionResult(
        TerminalShaderReflection? reflection = null,
        IReadOnlyList<TerminalShaderDiagnostic>? diagnostics = null)
    {
        Reflection = reflection ?? new TerminalShaderReflection();
        Diagnostics = diagnostics is null ? [] : diagnostics.ToArray();
        IsValid = !Diagnostics.Any(static diagnostic => diagnostic.Severity == TerminalShaderDiagnosticSeverity.Error);
    }

    /// <summary>
    /// Gets whether reflection completed without error diagnostics.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the reflected shader data.
    /// </summary>
    public TerminalShaderReflection Reflection { get; }

    /// <summary>
    /// Gets reflection diagnostics.
    /// </summary>
    public IReadOnlyList<TerminalShaderDiagnostic> Diagnostics { get; }
}
