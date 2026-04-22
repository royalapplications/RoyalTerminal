// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Represents a compiled shader package loaded into a runtime backend.
/// </summary>
public sealed class TerminalShaderRuntimeProgram : IDisposable
{
    private readonly Action? _dispose;
    private bool _disposed;

    /// <summary>
    /// Initializes a new runtime shader program.
    /// </summary>
    /// <param name="backendKind">Backend kind.</param>
    /// <param name="compilation">Compilation result used to create the program.</param>
    /// <param name="capabilities">Backend capabilities.</param>
    /// <param name="package">Optional package used to create the program.</param>
    /// <param name="nativeHandle">Optional native program handle.</param>
    /// <param name="dispose">Optional native dispose callback.</param>
    public TerminalShaderRuntimeProgram(
        TerminalShaderBackendKind backendKind,
        TerminalShaderCompilationResult compilation,
        TerminalShaderBackendCapabilities capabilities,
        TerminalShaderPackage? package = null,
        nint nativeHandle = 0,
        Action? dispose = null)
    {
        BackendKind = backendKind;
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Package = package;
        NativeHandle = nativeHandle;
        _dispose = dispose;
    }

    /// <summary>
    /// Gets the backend kind.
    /// </summary>
    public TerminalShaderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets the compilation result used to create the program.
    /// </summary>
    public TerminalShaderCompilationResult Compilation { get; }

    /// <summary>
    /// Gets the package used to create this runtime program, when available.
    /// </summary>
    public TerminalShaderPackage? Package { get; }

    /// <summary>
    /// Gets backend capabilities.
    /// </summary>
    public TerminalShaderBackendCapabilities Capabilities { get; }

    /// <summary>
    /// Gets the optional native program handle.
    /// </summary>
    public nint NativeHandle { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispose?.Invoke();
    }
}
