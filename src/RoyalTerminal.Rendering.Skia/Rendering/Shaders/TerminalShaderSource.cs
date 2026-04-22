// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Terminal shader source model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Immutable terminal framebuffer shader source.
/// </summary>
public sealed class TerminalShaderSource
{
    /// <summary>
    /// Initializes a new shader source.
    /// </summary>
    /// <param name="name">Human-readable shader name.</param>
    /// <param name="source">Shader source text.</param>
    /// <param name="language">Shader source language.</param>
    /// <param name="requiresContinuousAnimation">
    /// Whether the shader should keep rendering animation frames without terminal output.
    /// </param>
    public TerminalShaderSource(
        string name,
        string source,
        TerminalShaderLanguage language = TerminalShaderLanguage.SkiaRuntimeEffect,
        bool requiresContinuousAnimation = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Shader name must be non-empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Shader source must be non-empty.", nameof(source));
        }

        Name = name.Trim();
        Source = source;
        Language = language;
        RequiresContinuousAnimation = requiresContinuousAnimation;
    }

    /// <summary>
    /// Gets the human-readable shader name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the shader source text.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the source language.
    /// </summary>
    public TerminalShaderLanguage Language { get; }

    /// <summary>
    /// Gets whether this shader should keep the render loop active.
    /// </summary>
    public bool RequiresContinuousAnimation { get; }
}
