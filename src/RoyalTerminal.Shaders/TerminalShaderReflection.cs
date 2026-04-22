// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader reflection model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Contains normalized shader reflection data independent of a specific compiler.
/// </summary>
public sealed class TerminalShaderReflection
{
    /// <summary>
    /// Initializes a new shader reflection snapshot.
    /// </summary>
    /// <param name="entryPoints">Reflected entry points.</param>
    /// <param name="resources">Reflected resources.</param>
    public TerminalShaderReflection(
        IReadOnlyList<TerminalShaderEntryPointReflection>? entryPoints = null,
        IReadOnlyList<TerminalShaderResourceReflection>? resources = null)
    {
        EntryPoints = entryPoints is null ? [] : entryPoints.ToArray();
        Resources = resources is null ? [] : resources.ToArray();
    }

    /// <summary>
    /// Gets reflected entry points.
    /// </summary>
    public IReadOnlyList<TerminalShaderEntryPointReflection> EntryPoints { get; }

    /// <summary>
    /// Gets reflected resources.
    /// </summary>
    public IReadOnlyList<TerminalShaderResourceReflection> Resources { get; }
}
