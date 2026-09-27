// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Optional word-boundary queries over a processor's full accessible history.</summary>
public interface ITerminalWordSelectionSource
{
    /// <summary>
    /// Finds a word or delimiter run at an absolute, top-anchored buffer cell,
    /// crossing only soft wraps. The result uses the same absolute coordinates
    /// and an exclusive end column, and is bounded by the host's history policy.
    /// </summary>
    /// <param name="position">Cell in the current buffer, including off-viewport history.</param>
    /// <param name="delimiters">UTF-16 delimiters in addition to Unicode whitespace.</param>
    /// <param name="extent">Resolved word, or default when the cell is empty or out of range.</param>
    /// <returns>Whether a written word or delimiter run was found.</returns>
    /// <remarks>
    /// The caller must serialize this query with input, scrolling and resizing.
    /// Like native Ghostty's selection queries, this reads current terminal state;
    /// synchronized output holds presentation, not these input-state queries.
    /// Returned coordinates are not persistent anchors across buffer mutations.
    /// </remarks>
    bool TryGetWordExtent(TerminalGridPosition position, ReadOnlySpan<char> delimiters,
        out TerminalWordExtent extent);
}
