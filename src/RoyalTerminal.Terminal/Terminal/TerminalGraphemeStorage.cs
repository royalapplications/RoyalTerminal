// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

// Ghostty page.zig grapheme_max_len limits suffixes, excluding the base scalar.
// Raw snapshot codecs retain their wire-format limits; live terminal cells use
// the same bound whether populated by input or restored from a snapshot.
internal static class TerminalGraphemeStorage
{
    internal const int MaximumSuffixCodepoints = 64;
    internal const int MaximumCodepoints = MaximumSuffixCodepoints + 1;

    internal static int SuffixLength(in TerminalCell cell)
    {
        if (cell.Grapheme is not { Length: > 0 } text) return 0;
        int count = -1; // The base scalar is stored inline, not in the allocator.
        foreach (Rune _ in text.EnumerateRunes())
            if (++count == MaximumSuffixCodepoints) break;
        return Math.Max(0, count);
    }
}
