// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Terminal shader diagnostics.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Identifies the severity of a shader diagnostic.
/// </summary>
public enum TerminalShaderDiagnosticSeverity
{
    /// <summary>
    /// Informational diagnostic.
    /// </summary>
    Info,

    /// <summary>
    /// Warning diagnostic.
    /// </summary>
    Warning,

    /// <summary>
    /// Error diagnostic.
    /// </summary>
    Error,
}
