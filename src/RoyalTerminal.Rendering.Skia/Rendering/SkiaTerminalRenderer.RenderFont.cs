// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class SkiaTerminalRenderer
{
    // Glyph identity stays on the borrowed face. Outline effects are explicit
    // through every rendering path and every cache containing rasterized data.
    private readonly record struct RenderFont(SKTypeface Typeface, TerminalFontSynthesis Synthesis = TerminalFontSynthesis.None)
    {
        internal RenderFont(TerminalFontResolution resolution) : this(resolution.Typeface, resolution.Synthesis) { }
        internal nint Handle => Typeface.Handle;
        internal bool Matches(RenderFont other) => Handle == other.Handle && Synthesis == other.Synthesis;
    }
}
