// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes a resource binding required by a full shader package.
/// </summary>
public sealed class TerminalShaderResourceBinding
{
    /// <summary>
    /// Initializes a new shader resource binding.
    /// </summary>
    /// <param name="name">Stable package resource name.</param>
    /// <param name="kind">Resource kind.</param>
    /// <param name="source">Resource source.</param>
    /// <param name="valueType">Normalized value type.</param>
    /// <param name="registerIndex">HLSL register index, or -1 when not assigned yet.</param>
    /// <param name="registerSpace">HLSL register space.</param>
    /// <param name="optional">Whether the resource can be absent at runtime.</param>
    public TerminalShaderResourceBinding(
        string name,
        TerminalShaderResourceKind kind,
        TerminalShaderResourceSource source,
        TerminalShaderValueType valueType = TerminalShaderValueType.Unknown,
        int registerIndex = -1,
        int registerSpace = 0,
        bool optional = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Resource binding name must be non-empty.", nameof(name));
        }

        Name = name.Trim();
        Kind = kind;
        Source = source;
        ValueType = valueType;
        RegisterIndex = registerIndex < 0 ? -1 : registerIndex;
        RegisterSpace = Math.Max(0, registerSpace);
        Optional = optional;
    }

    /// <summary>
    /// Gets the stable package resource name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the resource kind.
    /// </summary>
    public TerminalShaderResourceKind Kind { get; }

    /// <summary>
    /// Gets the resource source.
    /// </summary>
    public TerminalShaderResourceSource Source { get; }

    /// <summary>
    /// Gets the normalized value type.
    /// </summary>
    public TerminalShaderValueType ValueType { get; }

    /// <summary>
    /// Gets the HLSL register index, or -1 when the compiler should infer it.
    /// </summary>
    public int RegisterIndex { get; }

    /// <summary>
    /// Gets the HLSL register space.
    /// </summary>
    public int RegisterSpace { get; }

    /// <summary>
    /// Gets whether the resource can be absent at runtime.
    /// </summary>
    public bool Optional { get; }
}
