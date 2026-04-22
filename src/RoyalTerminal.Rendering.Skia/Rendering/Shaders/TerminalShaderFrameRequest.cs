// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader runtime model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Describes one full shader package frame execution request.
/// </summary>
public sealed class TerminalShaderFrameRequest
{
    /// <summary>
    /// Initializes a new frame request.
    /// </summary>
    /// <param name="frameContext">Terminal shader frame context.</param>
    /// <param name="resources">Runtime resources available to the package.</param>
    public TerminalShaderFrameRequest(
        TerminalShaderFrameContext frameContext,
        IReadOnlyList<TerminalShaderResourceValue>? resources = null)
    {
        FrameContext = frameContext;
        Resources = resources is null ? [] : resources.ToArray();
    }

    /// <summary>
    /// Gets the terminal shader frame context.
    /// </summary>
    public TerminalShaderFrameContext FrameContext { get; }

    /// <summary>
    /// Gets runtime resources available to the package.
    /// </summary>
    public IReadOnlyList<TerminalShaderResourceValue> Resources { get; }
}
