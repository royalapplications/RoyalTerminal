// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader compiler model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Defines compiler selection and preprocessor options for full shader packages.
/// </summary>
public sealed class TerminalShaderCompilationOptions
{
    /// <summary>
    /// Initializes new shader compilation options.
    /// </summary>
    /// <param name="backendKind">Target shader execution backend.</param>
    /// <param name="compilerKind">Compiler selection.</param>
    /// <param name="defines">Preprocessor defines.</param>
    /// <param name="debugName">Optional debug name.</param>
    public TerminalShaderCompilationOptions(
        TerminalShaderBackendKind backendKind,
        TerminalShaderCompilerKind compilerKind = TerminalShaderCompilerKind.Auto,
        IReadOnlyDictionary<string, string>? defines = null,
        string? debugName = null)
    {
        BackendKind = backendKind;
        CompilerKind = compilerKind;
        Defines = defines is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(defines, StringComparer.Ordinal);
        DebugName = string.IsNullOrWhiteSpace(debugName) ? null : debugName.Trim();
    }

    /// <summary>
    /// Gets the target shader execution backend.
    /// </summary>
    public TerminalShaderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets the compiler selection.
    /// </summary>
    public TerminalShaderCompilerKind CompilerKind { get; }

    /// <summary>
    /// Gets preprocessor defines.
    /// </summary>
    public IReadOnlyDictionary<string, string> Defines { get; }

    /// <summary>
    /// Gets the optional debug name.
    /// </summary>
    public string? DebugName { get; }
}
