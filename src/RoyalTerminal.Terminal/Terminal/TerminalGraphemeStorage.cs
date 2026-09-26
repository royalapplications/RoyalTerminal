// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

// Ghostty page.zig grapheme_max_len limits suffixes, excluding the base scalar.
// Raw snapshot codecs retain their wire-format limits; live terminal cells use
// the same bound whether populated by input or restored from a snapshot.
internal static class TerminalGraphemeStorage
{
    internal const int MaximumSuffixCodepoints = 64;
    internal const int MaximumCodepoints = MaximumSuffixCodepoints + 1;
}
