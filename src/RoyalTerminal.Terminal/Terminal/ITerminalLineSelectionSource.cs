// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>A logical line's absolute buffer range, with an inclusive start and exclusive end column.</summary>
/// <param name="Start">First selected cell.</param>
/// <param name="End">Column after the last selected glyph on its absolute row.</param>
public readonly record struct TerminalLineExtent(TerminalGridPosition Start, TerminalGridPosition End);

/// <summary>Optional logical-line boundaries over the complete accessible buffer.</summary>
public interface ITerminalLineSelectionSource
{
    /// <summary>
    /// Resolves a soft-wrapped logical line, optionally bounded by changes in
    /// cell semantic content, and trims unwritten cells and supplied whitespace.
    /// Empty whitespace uses Ghostty's defaults: NUL, ASCII space and tab.
    /// </summary>
    /// <param name="position">Absolute, top-anchored buffer cell.</param>
    /// <param name="whitespace">Codepoints to trim; a singleton NUL retains written whitespace.</param>
    /// <param name="semanticPromptBoundary">Whether prompt/input/output content changes bound the range.</param>
    /// <param name="extent">Absolute range, or default when no non-whitespace text exists.</param>
    /// <returns>Whether a line was found.</returns>
    /// <remarks>
    /// Queries read current input state, including during presentation holds.
    /// The caller must serialize access with input, scrolling and resizing.
    /// </remarks>
    bool TryGetLineExtent(TerminalGridPosition position, ReadOnlySpan<uint> whitespace,
        bool semanticPromptBoundary, out TerminalLineExtent extent);
}
