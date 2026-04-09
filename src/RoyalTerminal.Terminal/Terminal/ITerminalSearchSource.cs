// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Optional full-buffer terminal search contract.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Absolute terminal search match resolved against the full terminal buffer.
/// </summary>
public readonly record struct TerminalSearchMatch(int AbsoluteRow, int StartColumn, int EndColumn);

/// <summary>
/// Optional VT processor capability for building full-buffer text search matches.
/// </summary>
public interface ITerminalSearchSource
{
    /// <summary>
    /// Rebuilds the current full-buffer search matches for the given needle.
    /// </summary>
    void PopulateSearchMatches(string needle, List<TerminalSearchMatch> destination);
}
