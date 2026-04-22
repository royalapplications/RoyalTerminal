// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime registration.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes one compiler/runtime package-executor registration.
/// </summary>
public sealed class TerminalShaderPackageExecutorRegistration
{
    private readonly Func<TerminalShaderPackageExecutorFactoryContext, ITerminalShaderPackageExecutor?> _factory;

    /// <summary>
    /// Initializes a package-executor registration.
    /// </summary>
    /// <param name="backendKind">Runtime backend kind.</param>
    /// <param name="compilerKind">Compiler used by the registration.</param>
    /// <param name="displayName">Human-readable registration name.</param>
    /// <param name="isAvailable">Whether the registration can create executors on the current host.</param>
    /// <param name="factory">Factory used to create package executors.</param>
    public TerminalShaderPackageExecutorRegistration(
        TerminalShaderBackendKind backendKind,
        TerminalShaderCompilerKind compilerKind,
        string displayName,
        bool isAvailable,
        Func<TerminalShaderPackageExecutorFactoryContext, ITerminalShaderPackageExecutor?> factory)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Shader runtime registration display name must be non-empty.", nameof(displayName));
        }

        BackendKind = backendKind;
        CompilerKind = compilerKind;
        DisplayName = displayName.Trim();
        IsAvailable = isAvailable;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Gets the runtime backend kind.
    /// </summary>
    public TerminalShaderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets the compiler kind used by the registration.
    /// </summary>
    public TerminalShaderCompilerKind CompilerKind { get; }

    /// <summary>
    /// Gets a human-readable registration name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets whether this registration can create executors on the current host.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// Creates a package executor.
    /// </summary>
    /// <param name="context">Factory context.</param>
    /// <returns>A package executor, or <see langword="null"/> when creation failed without throwing.</returns>
    public ITerminalShaderPackageExecutor? TryCreate(TerminalShaderPackageExecutorFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _factory(context);
    }
}
