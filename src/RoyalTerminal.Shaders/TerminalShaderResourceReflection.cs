// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader reflection model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes one shader resource discovered by compiler reflection.
/// </summary>
public sealed class TerminalShaderResourceReflection
{
    /// <summary>
    /// Initializes a new resource reflection record.
    /// </summary>
    /// <param name="name">Resource name.</param>
    /// <param name="kind">Resource kind.</param>
    /// <param name="valueType">Resource value type.</param>
    /// <param name="registerIndex">Register index.</param>
    /// <param name="registerSpace">Register space.</param>
    /// <param name="sizeInBytes">Optional resource size in bytes.</param>
    public TerminalShaderResourceReflection(
        string name,
        TerminalShaderResourceKind kind,
        TerminalShaderValueType valueType = TerminalShaderValueType.Unknown,
        int registerIndex = -1,
        int registerSpace = 0,
        int? sizeInBytes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Resource name must be non-empty.", nameof(name));
        }

        Name = name.Trim();
        Kind = kind;
        ValueType = valueType;
        RegisterIndex = registerIndex < 0 ? -1 : registerIndex;
        RegisterSpace = Math.Max(0, registerSpace);
        SizeInBytes = sizeInBytes is > 0 ? sizeInBytes : null;
    }

    /// <summary>
    /// Gets the resource name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the resource kind.
    /// </summary>
    public TerminalShaderResourceKind Kind { get; }

    /// <summary>
    /// Gets the resource value type.
    /// </summary>
    public TerminalShaderValueType ValueType { get; }

    /// <summary>
    /// Gets the register index, or -1 when unavailable.
    /// </summary>
    public int RegisterIndex { get; }

    /// <summary>
    /// Gets the register space.
    /// </summary>
    public int RegisterSpace { get; }

    /// <summary>
    /// Gets the optional resource size in bytes.
    /// </summary>
    public int? SizeInBytes { get; }
}
