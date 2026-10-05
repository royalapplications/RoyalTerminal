// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Rendering;

// Raster settings belong to the renderer's terminal dependency, not the
// lower-level text-shaping assembly. Keep the full typeface and float identity.
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
