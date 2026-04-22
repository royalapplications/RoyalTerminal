// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full shader runtime model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes one full shader package frame execution request without renderer-specific types.
/// </summary>
public sealed class TerminalShaderFrameRequest
{
    /// <summary>
    /// Initializes a new frame request.
    /// </summary>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <param name="height">Framebuffer height in pixels.</param>
    /// <param name="time">Elapsed shader time in seconds.</param>
    /// <param name="timeDelta">Elapsed time since the previous shader frame in seconds.</param>
    /// <param name="frame">Shader frame index.</param>
    /// <param name="scale">Display scale factor.</param>
    /// <param name="resources">Runtime resources available to the package.</param>
    public TerminalShaderFrameRequest(
        int width,
        int height,
        float time,
        float timeDelta,
        int frame,
        float scale,
        IReadOnlyList<TerminalShaderResourceValue>? resources = null)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Time = Math.Max(0f, time);
        TimeDelta = Math.Max(0f, timeDelta);
        Frame = Math.Max(0, frame);
        Scale = scale > 0f && float.IsFinite(scale) ? scale : 1f;
        Resources = resources is null ? [] : resources.ToArray();
    }

    /// <summary>Gets the framebuffer width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the framebuffer height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets elapsed shader time in seconds.</summary>
    public float Time { get; }

    /// <summary>Gets elapsed time since the previous shader frame in seconds.</summary>
    public float TimeDelta { get; }

    /// <summary>Gets the shader frame index.</summary>
    public int Frame { get; }

    /// <summary>Gets the display scale factor.</summary>
    public float Scale { get; }

    /// <summary>
    /// Gets runtime resources available to the package.
    /// </summary>
    public IReadOnlyList<TerminalShaderResourceValue> Resources { get; }
}
