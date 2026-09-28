// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Host font coverage, independent of terminal session glyph registrations.</summary>
public interface ITerminalGlyphCoverageSource
{
    /// <summary>
    /// Returns whether the current regular/fallback font configuration contains
    /// a glyph for the Unicode scalar. May be called on the terminal IO thread;
    /// implementations must synchronize resource ownership and font changes.
    /// </summary>
    bool HasSystemGlyph(uint codepoint);
}

/// <summary>Optional processor capability for host-backed glyph coverage queries.</summary>
public interface ITerminalGlyphCoverageSink
{
    /// <summary>
    /// Gets or sets the caller-owned font coverage source. Null preserves
    /// glossary-only responses. The processor never disposes the source.
    /// </summary>
    ITerminalGlyphCoverageSource? GlyphCoverageSource { get; set; }
}
