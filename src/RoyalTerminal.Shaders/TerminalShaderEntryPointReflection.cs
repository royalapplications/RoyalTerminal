// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader reflection model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes one shader entry point discovered by compiler reflection.
/// </summary>
public sealed class TerminalShaderEntryPointReflection
{
    /// <summary>
    /// Initializes a new entry point reflection record.
    /// </summary>
    /// <param name="name">Entry point name.</param>
    /// <param name="stage">Entry point stage.</param>
    /// <param name="threadGroupSize">Optional compute thread-group size.</param>
    /// <param name="inputs">Reflected input semantics.</param>
    /// <param name="outputs">Reflected output semantics.</param>
    public TerminalShaderEntryPointReflection(
        string name,
        TerminalShaderStage stage,
        TerminalShaderDispatch? threadGroupSize = null,
        IReadOnlyList<TerminalShaderSemanticReflection>? inputs = null,
        IReadOnlyList<TerminalShaderSemanticReflection>? outputs = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Entry point name must be non-empty.", nameof(name));
        }

        Name = name.Trim();
        Stage = stage;
        ThreadGroupSize = threadGroupSize;
        Inputs = inputs is null ? [] : inputs.ToArray();
        Outputs = outputs is null ? [] : outputs.ToArray();
    }

    /// <summary>
    /// Gets the entry point name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the entry point stage.
    /// </summary>
    public TerminalShaderStage Stage { get; }

    /// <summary>
    /// Gets the optional compute thread-group size.
    /// </summary>
    public TerminalShaderDispatch? ThreadGroupSize { get; }

    /// <summary>
    /// Gets reflected input semantics.
    /// </summary>
    public IReadOnlyList<TerminalShaderSemanticReflection> Inputs { get; }

    /// <summary>
    /// Gets reflected output semantics.
    /// </summary>
    public IReadOnlyList<TerminalShaderSemanticReflection> Outputs { get; }
}
