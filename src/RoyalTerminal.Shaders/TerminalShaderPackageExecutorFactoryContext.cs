// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime registration.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Provides package-executor factory inputs for registered shader runtimes.
/// </summary>
public sealed class TerminalShaderPackageExecutorFactoryContext
{
    /// <summary>
    /// Initializes a package-executor factory context.
    /// </summary>
    /// <param name="options">Compilation options for the selected backend.</param>
    /// <param name="includeProvider">Optional include provider.</param>
    public TerminalShaderPackageExecutorFactoryContext(
        TerminalShaderCompilationOptions options,
        ITerminalShaderIncludeProvider? includeProvider = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        IncludeProvider = includeProvider;
    }

    /// <summary>
    /// Gets the compilation options for the selected backend.
    /// </summary>
    public TerminalShaderCompilationOptions Options { get; }

    /// <summary>
    /// Gets the optional include provider.
    /// </summary>
    public ITerminalShaderIncludeProvider? IncludeProvider { get; }
}
