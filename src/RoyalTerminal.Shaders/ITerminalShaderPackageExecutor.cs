// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package execution.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Executes full shader packages through a concrete backend runtime.
/// </summary>
public interface ITerminalShaderPackageExecutor
{
    /// <summary>
    /// Gets the backend kind used by this executor.
    /// </summary>
    TerminalShaderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets the backend capabilities used for package and frame validation.
    /// </summary>
    TerminalShaderBackendCapabilities Capabilities { get; }

    /// <summary>
    /// Renders one shader package frame.
    /// </summary>
    /// <param name="package">Shader package to execute.</param>
    /// <param name="frame">Frame request and resolved resources.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rendered frame result.</returns>
    ValueTask<TerminalShaderFrameResult> RenderFrameAsync(
        TerminalShaderPackage package,
        TerminalShaderFrameRequest frame,
        CancellationToken cancellationToken = default);
}
