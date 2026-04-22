// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Terminal shader diagnostics.

namespace RoyalTerminal.Avalonia.Rendering;

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
