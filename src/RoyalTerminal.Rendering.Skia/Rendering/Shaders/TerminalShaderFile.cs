// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader package model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Represents one source file in a full terminal shader package.
/// </summary>
public sealed class TerminalShaderFile
{
    /// <summary>
    /// Initializes a new shader source file.
    /// </summary>
    /// <param name="virtualPath">Package-relative virtual source path.</param>
    /// <param name="source">Source text.</param>
    public TerminalShaderFile(string virtualPath, string source)
    {
        VirtualPath = TerminalShaderVirtualPath.Normalize(virtualPath);
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    /// Gets the package-relative virtual source path.
    /// </summary>
    public string VirtualPath { get; }

    /// <summary>
    /// Gets the source text.
    /// </summary>
    public string Source { get; }
}
