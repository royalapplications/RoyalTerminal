// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader runtime model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Executes compiler-backed full terminal shader packages.
/// </summary>
public interface ITerminalShaderRuntime : IDisposable
{
    /// <summary>
    /// Gets backend capabilities.
    /// </summary>
    TerminalShaderBackendCapabilities Capabilities { get; }

    /// <summary>
    /// Creates a runtime program from compiled shader passes.
    /// </summary>
    /// <param name="compilation">Compilation result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The runtime program.</returns>
    ValueTask<TerminalShaderRuntimeProgram> CreateProgramAsync(
        TerminalShaderCompilationResult compilation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders one frame through a runtime program.
    /// </summary>
    /// <param name="program">Runtime program.</param>
    /// <param name="frame">Frame request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The frame result.</returns>
    ValueTask<TerminalShaderFrameResult> RenderFrameAsync(
        TerminalShaderRuntimeProgram program,
        TerminalShaderFrameRequest frame,
        CancellationToken cancellationToken = default);
}
