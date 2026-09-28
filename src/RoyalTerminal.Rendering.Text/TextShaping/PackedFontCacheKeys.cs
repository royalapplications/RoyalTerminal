// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

// Ghostty SharedGrid CodepointKey/GlyphKey (#14301/#14300) stores bounded
// identity fields in integers. RoyalTerminal must additionally retain culture,
// full-width Skia handles, exact float bits and every rasterization option.
// WT's atlas keeps glyph maps per font face; xterm.js's atlas keeps colors and
// extended attributes in its key. Neither justifies truncating these identities.
internal readonly record struct CollectionCodepointKey
{
    private readonly ulong _packed;
    private readonly string _cultureName;

    internal CollectionCodepointKey(TerminalTypefaceStyle style, int codepoint, bool? presentation, string cultureName)
    {
        if ((uint)style > 3) throw new ArgumentOutOfRangeException(nameof(style));
        _packed = (uint)codepoint | ((ulong)style << 32) |
            (presentation.HasValue ? 1UL << 34 : 0) | (presentation == true ? 1UL << 35 : 0);
        _cultureName = cultureName;
    }
}
