// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using RoyalTerminal.Terminal.Glyphs;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    private TerminalGlyphGlossary? _glyphGlossary;

    /// <summary>Number of session glyph registrations, shared by both screen buffers.</summary>
    public int RegisteredGlyphCount => _glyphGlossary?.Count ?? 0;

    /// <summary>Gets an immutable glyph registration. Call under the screen lock when shared across threads.</summary>
    public bool TryGetRegisteredGlyph(uint codepoint, [NotNullWhen(true)] out TerminalGlyphRegistration? registration)
    {
        registration = null;
        return _glyphGlossary?.TryGet(codepoint, out registration) == true;
    }

    internal TerminalGlyphGlossary GlyphGlossary => _glyphGlossary ??= new();

    internal void ReplaceGlyphGlossary(TerminalGlyphGlossary glossary)
    {
        _glyphGlossary = glossary.Count == 0 ? null : glossary;
        InvalidateAll();
    }

    internal void ClearRegisteredGlyphs()
    {
        if (_glyphGlossary is null) return;
        _glyphGlossary = null;
        InvalidateAll();
    }
}
