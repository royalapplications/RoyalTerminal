// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader compiler model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Contains compiled code and reflection data for one shader pass.
/// </summary>
public sealed class TerminalShaderCompiledPass
{
    /// <summary>
    /// Initializes a new compiled shader pass.
    /// </summary>
    /// <param name="passName">Package pass name.</param>
    /// <param name="stage">Shader stage.</param>
    /// <param name="format">Compiled code format.</param>
    /// <param name="code">Compiled code bytes or encoded source bytes.</param>
    /// <param name="reflection">Normalized reflection data.</param>
    public TerminalShaderCompiledPass(
        string passName,
        TerminalShaderStage stage,
        TerminalShaderCompiledCodeFormat format,
        ReadOnlyMemory<byte> code,
        TerminalShaderReflection? reflection = null)
    {
        if (string.IsNullOrWhiteSpace(passName))
        {
            throw new ArgumentException("Compiled pass name must be non-empty.", nameof(passName));
        }

        PassName = passName.Trim();
        Stage = stage;
        Format = format;
        Code = code.ToArray();
        Reflection = reflection ?? new TerminalShaderReflection();
    }

    /// <summary>
    /// Gets the package pass name.
    /// </summary>
    public string PassName { get; }

    /// <summary>
    /// Gets the shader stage.
    /// </summary>
    public TerminalShaderStage Stage { get; }

    /// <summary>
    /// Gets the compiled code format.
    /// </summary>
    public TerminalShaderCompiledCodeFormat Format { get; }

    /// <summary>
    /// Gets the compiled code bytes or encoded source bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Code { get; }

    /// <summary>
    /// Gets normalized reflection data.
    /// </summary>
    public TerminalShaderReflection Reflection { get; }
}
