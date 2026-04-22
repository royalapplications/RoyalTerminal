// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader compiler model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Compiles full terminal shader packages into backend-specific shader programs.
/// </summary>
public interface ITerminalShaderCompiler
{
    /// <summary>
    /// Compiles a full shader package.
    /// </summary>
    /// <param name="request">Compilation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The compilation result.</returns>
    ValueTask<TerminalShaderCompilationResult> CompileAsync(
        TerminalShaderCompilationRequest request,
        CancellationToken cancellationToken = default);
}
