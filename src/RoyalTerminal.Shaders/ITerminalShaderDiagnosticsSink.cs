// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader diagnostics.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Receives shader diagnostics from host integrations.
/// </summary>
public interface ITerminalShaderDiagnosticsSink
{
    /// <summary>
    /// Reports one shader diagnostic.
    /// </summary>
    /// <param name="diagnostic">Diagnostic to report.</param>
    void Report(TerminalShaderDiagnostic diagnostic);
}
