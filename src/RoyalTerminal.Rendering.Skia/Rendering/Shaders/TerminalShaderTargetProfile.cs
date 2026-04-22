// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader package model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Describes the compiler target profile for a shader package pass.
/// </summary>
public sealed class TerminalShaderTargetProfile : IEquatable<TerminalShaderTargetProfile>
{
    /// <summary>
    /// Initializes a new shader target profile.
    /// </summary>
    /// <param name="name">Compiler profile name such as <c>ps_6_0</c> or <c>cs_6_0</c>.</param>
    public TerminalShaderTargetProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Target profile name must be non-empty.", nameof(name));
        }

        Name = name.Trim();
    }

    /// <summary>
    /// Gets the compiler profile name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the default pixel shader model 5 profile.
    /// </summary>
    public static TerminalShaderTargetProfile PixelShader50 { get; } = new("ps_5_0");

    /// <summary>
    /// Gets the default pixel shader model 6 profile.
    /// </summary>
    public static TerminalShaderTargetProfile PixelShader60 { get; } = new("ps_6_0");

    /// <summary>
    /// Gets the default compute shader model 5 profile.
    /// </summary>
    public static TerminalShaderTargetProfile ComputeShader50 { get; } = new("cs_5_0");

    /// <summary>
    /// Gets the default compute shader model 6 profile.
    /// </summary>
    public static TerminalShaderTargetProfile ComputeShader60 { get; } = new("cs_6_0");

    /// <inheritdoc />
    public bool Equals(TerminalShaderTargetProfile? other)
    {
        return other is not null && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is TerminalShaderTargetProfile other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Name;
    }
}
