// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal.Glyphs;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    private bool _glyphsSynchronized;
    private uint _nativeGlyphCount;

    // Called only at a presentation boundary, before native render-state update
    // clears dirty flags. During synchronized output, queries keep using native
    // working state while the screen retains its immutable published glossary.
    private void SyncGlyphGlossaryFromNative()
    {
        _terminal.GetGlyphGlossaryInfo(out uint count, out bool dirty);
        if (_glyphsSynchronized && !dirty && count == _nativeGlyphCount) return;
        GhosttyGlyphRegistration[] entries = _terminal.GetGlyphRegistrations();
        TerminalGlyphGlossary glossary = new();
        foreach (GhosttyGlyphRegistration entry in entries) glossary.Register(entry.Codepoint, entry.Registration);
        _screen.ReplaceGlyphGlossary(glossary);
        _nativeGlyphCount = (uint)entries.Length;
        _glyphsSynchronized = true;
    }
}
