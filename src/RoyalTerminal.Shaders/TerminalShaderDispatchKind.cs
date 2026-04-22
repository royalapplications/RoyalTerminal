// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Identifies how compute dispatch dimensions should be interpreted.
/// </summary>
public enum TerminalShaderDispatchKind
{
    /// <summary>
    /// Dispatch dimensions are exact thread-group counts.
    /// </summary>
    ExactThreadGroups,

    /// <summary>
    /// Dispatch dimensions describe a thread-group shape used to cover the pass output.
    /// </summary>
    CoverOutput,
}
