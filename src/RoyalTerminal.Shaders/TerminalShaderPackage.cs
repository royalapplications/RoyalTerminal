// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes a full compiler-backed terminal shader package.
/// </summary>
public sealed class TerminalShaderPackage
{
    /// <summary>
    /// Initializes a new full shader package.
    /// </summary>
    /// <param name="name">Human-readable package name.</param>
    /// <param name="files">Package source files.</param>
    /// <param name="passes">Passes executed by the package.</param>
    /// <param name="resources">Declared package resources.</param>
    /// <param name="options">Package validation and safety options.</param>
    public TerminalShaderPackage(
        string name,
        IReadOnlyList<TerminalShaderFile> files,
        IReadOnlyList<TerminalShaderPass> passes,
        IReadOnlyList<TerminalShaderResourceBinding>? resources = null,
        TerminalShaderPackageOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Shader package name must be non-empty.", nameof(name));
        }

        Name = name.Trim();
        Files = files is null ? throw new ArgumentNullException(nameof(files)) : files.ToArray();
        Passes = passes is null ? throw new ArgumentNullException(nameof(passes)) : passes.ToArray();
        Resources = resources is null ? [] : resources.ToArray();
        Options = options ?? TerminalShaderPackageOptions.Default;
    }

    /// <summary>
    /// Gets the human-readable package name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets package source files.
    /// </summary>
    public IReadOnlyList<TerminalShaderFile> Files { get; }

    /// <summary>
    /// Gets passes executed by the package.
    /// </summary>
    public IReadOnlyList<TerminalShaderPass> Passes { get; }

    /// <summary>
    /// Gets declared package resources.
    /// </summary>
    public IReadOnlyList<TerminalShaderResourceBinding> Resources { get; }

    /// <summary>
    /// Gets package validation and safety options.
    /// </summary>
    public TerminalShaderPackageOptions Options { get; }
}
