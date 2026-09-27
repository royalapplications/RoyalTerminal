// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

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

internal readonly record struct RasterFontCacheKey
{
    private readonly nint _typefaceHandle;
    private readonly ulong _packed;

    // Settings are normalized by the caller. Reject out-of-range values before
    // packing rather than aliasing an existing valid cache entry.
    internal RasterFontCacheKey(nint typefaceHandle, int fontSizeBits,
        TerminalFontRenderingSettings settings, TerminalFontSynthesis synthesis)
    {
        if ((uint)settings.Edging > 2) throw new ArgumentOutOfRangeException(nameof(settings));
        if ((uint)settings.Hinting > 3) throw new ArgumentOutOfRangeException(nameof(settings));
        if ((uint)synthesis > 3) throw new ArgumentOutOfRangeException(nameof(synthesis));
        uint flags = (settings.SubpixelPositioning ? 1U : 0) |
            ((uint)settings.Edging << 1) | ((uint)settings.Hinting << 3) |
            (settings.BaselineSnap ? 1U << 5 : 0) | (settings.EmbeddedBitmaps ? 1U << 6 : 0) |
            (settings.Embolden ? 1U << 7 : 0) | (settings.ForceAutoHinting ? 1U << 8 : 0) |
            (settings.LinearMetrics ? 1U << 9 : 0) | ((uint)synthesis << 10);
        _typefaceHandle = typefaceHandle;
        _packed = (uint)fontSizeBits | ((ulong)flags << 32);
    }
}
