// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes compute dispatch sizing for a shader package pass.
/// </summary>
public readonly record struct TerminalShaderDispatch
{
    /// <summary>
    /// Initializes a new compute dispatch descriptor.
    /// </summary>
    /// <param name="x">X dimension.</param>
    /// <param name="y">Y dimension.</param>
    /// <param name="z">Z dimension.</param>
    /// <param name="kind">Dispatch interpretation.</param>
    public TerminalShaderDispatch(
        int x,
        int y,
        int z = 1,
        TerminalShaderDispatchKind kind = TerminalShaderDispatchKind.ExactThreadGroups)
    {
        X = Math.Max(1, x);
        Y = Math.Max(1, y);
        Z = Math.Max(1, z);
        Kind = kind;
    }

    /// <summary>
    /// Gets the X dispatch dimension.
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Gets the Y dispatch dimension.
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// Gets the Z dispatch dimension.
    /// </summary>
    public int Z { get; }

    /// <summary>
    /// Gets the dispatch interpretation.
    /// </summary>
    public TerminalShaderDispatchKind Kind { get; }
}
