// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader package model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Describes one output resource produced by a shader package pass.
/// </summary>
public sealed class TerminalShaderPassOutput
{
    /// <summary>
    /// Initializes a new pass output.
    /// </summary>
    /// <param name="name">Stable output resource name.</param>
    /// <param name="kind">Output resource kind.</param>
    /// <param name="widthScale">Width scale relative to the terminal frame.</param>
    /// <param name="heightScale">Height scale relative to the terminal frame.</param>
    public TerminalShaderPassOutput(
        string name,
        TerminalShaderResourceKind kind = TerminalShaderResourceKind.RenderTarget,
        float widthScale = 1f,
        float heightScale = 1f)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Pass output name must be non-empty.", nameof(name));
        }

        Name = name.Trim();
        Kind = kind;
        WidthScale = widthScale > 0f && float.IsFinite(widthScale) ? widthScale : 1f;
        HeightScale = heightScale > 0f && float.IsFinite(heightScale) ? heightScale : 1f;
    }

    /// <summary>
    /// Gets the stable output resource name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the output resource kind.
    /// </summary>
    public TerminalShaderResourceKind Kind { get; }

    /// <summary>
    /// Gets the width scale relative to the terminal frame.
    /// </summary>
    public float WidthScale { get; }

    /// <summary>
    /// Gets the height scale relative to the terminal frame.
    /// </summary>
    public float HeightScale { get; }
}
