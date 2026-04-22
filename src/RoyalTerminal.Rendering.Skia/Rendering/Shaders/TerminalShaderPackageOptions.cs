// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader package model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Defines safety and validation limits for a full terminal shader package.
/// </summary>
public sealed class TerminalShaderPackageOptions
{
    /// <summary>
    /// Initializes new shader package options.
    /// </summary>
    /// <param name="maxPasses">Maximum pass count allowed by validation.</param>
    /// <param name="maxFiles">Maximum source file count allowed by validation.</param>
    /// <param name="maxSourceBytes">Maximum aggregate source bytes allowed by validation.</param>
    /// <param name="allowExternalIncludes">Whether package validation allows external include resolution.</param>
    public TerminalShaderPackageOptions(
        int maxPasses = 32,
        int maxFiles = 256,
        int maxSourceBytes = 4 * 1024 * 1024,
        bool allowExternalIncludes = false)
    {
        MaxPasses = Math.Max(1, maxPasses);
        MaxFiles = Math.Max(1, maxFiles);
        MaxSourceBytes = Math.Max(1, maxSourceBytes);
        AllowExternalIncludes = allowExternalIncludes;
    }

    /// <summary>
    /// Gets the maximum pass count allowed by validation.
    /// </summary>
    public int MaxPasses { get; }

    /// <summary>
    /// Gets the maximum source file count allowed by validation.
    /// </summary>
    public int MaxFiles { get; }

    /// <summary>
    /// Gets the maximum aggregate source bytes allowed by validation.
    /// </summary>
    public int MaxSourceBytes { get; }

    /// <summary>
    /// Gets whether validation allows external include resolution.
    /// </summary>
    public bool AllowExternalIncludes { get; }

    /// <summary>
    /// Gets the default shader package options.
    /// </summary>
    public static TerminalShaderPackageOptions Default { get; } = new();
}
