// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader compiler model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes a full shader package compilation request.
/// </summary>
public sealed class TerminalShaderCompilationRequest
{
    /// <summary>
    /// Initializes a new shader compilation request.
    /// </summary>
    /// <param name="package">Shader package.</param>
    /// <param name="resolvedFiles">Resolved source files including include dependencies.</param>
    /// <param name="options">Compilation options.</param>
    public TerminalShaderCompilationRequest(
        TerminalShaderPackage package,
        IReadOnlyList<TerminalShaderFile> resolvedFiles,
        TerminalShaderCompilationOptions options)
    {
        Package = package ?? throw new ArgumentNullException(nameof(package));
        ResolvedFiles = resolvedFiles is null ? throw new ArgumentNullException(nameof(resolvedFiles)) : resolvedFiles.ToArray();
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Gets the shader package.
    /// </summary>
    public TerminalShaderPackage Package { get; }

    /// <summary>
    /// Gets resolved source files including include dependencies.
    /// </summary>
    public IReadOnlyList<TerminalShaderFile> ResolvedFiles { get; }

    /// <summary>
    /// Gets compilation options.
    /// </summary>
    public TerminalShaderCompilationOptions Options { get; }
}
