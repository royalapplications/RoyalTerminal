// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader reflection model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes one shader semantic input or output discovered by compiler reflection.
/// </summary>
public sealed class TerminalShaderSemanticReflection
{
    /// <summary>
    /// Initializes a new semantic reflection record.
    /// </summary>
    /// <param name="name">Semantic name.</param>
    /// <param name="index">Semantic index.</param>
    /// <param name="valueType">Semantic value type.</param>
    public TerminalShaderSemanticReflection(
        string name,
        int index = 0,
        TerminalShaderValueType valueType = TerminalShaderValueType.Unknown)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Semantic name must be non-empty.", nameof(name));
        }

        Name = name.Trim();
        SemanticIndex = Math.Max(0, index);
        ValueType = valueType;
    }

    /// <summary>
    /// Gets the semantic name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the semantic index.
    /// </summary>
    public int SemanticIndex { get; }

    /// <summary>
    /// Gets the semantic value type.
    /// </summary>
    public TerminalShaderValueType ValueType { get; }
}
