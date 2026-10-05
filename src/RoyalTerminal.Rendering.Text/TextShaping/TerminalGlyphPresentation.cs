// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Cached color-glyph coverage for one borrowed typeface; guarded by its resolver's lock.</summary>
internal sealed class TerminalGlyphPresentation : IDisposable
{
    private readonly SKTypeface _typeface;
    private readonly bool _sbix;
    private readonly bool _layers;
    private readonly bool _svg;
    private readonly bool _png;
    private readonly Dictionary<ushort, bool> _glyphs = new();
    private HarfBuzzTypefaceEntry? _entry;

    internal TerminalGlyphPresentation(SKTypeface typeface)
    {
        _typeface = typeface;
        bool sbix = typeface.GetTableSize(0x73626978) > 0;
        // Ghostty's CoreText backend treats an sbix face as colored. Preserve
        // that rule without loading Apple Color Emoji's large bitmap table.
        _sbix = OperatingSystem.IsMacOS() && sbix;
        _layers = typeface.GetTableSize(0x434F4C52) > 0 && typeface.GetTableSize(0x4350414C) > 0;
        // Ghostty disables FreeType SVG rendering; CoreText supports it.
        _svg = OperatingSystem.IsMacOS() && typeface.GetTableSize(0x53564720) > 0;
        _png = sbix || (typeface.GetTableSize(0x43424C43) > 0 && typeface.GetTableSize(0x43424454) > 0);
    }

    internal bool IsColorGlyph(ushort glyph)
    {
        if (glyph == 0) return false;
        if (_sbix) return true;
        if (!_layers && !_svg && !_png) return false;
        if (_glyphs.TryGetValue(glyph, out bool value)) return value;
        _entry ??= new HarfBuzzTypefaceEntry(_typeface, preferMemoryStream: true);
        nint face = _entry.Face.Handle;
        value = (_layers && (HarfBuzzColorApi.GetLayerCount(face, glyph, 0, 0, 0) != 0 ||
                             HarfBuzzColorApi.HasPaint(face, glyph) != 0)) ||
                (_svg && HasData(HarfBuzzColorApi.ReferenceSvg(face, glyph))) ||
                (_png && HasData(HarfBuzzColorApi.ReferencePng(_entry.Font.Handle, glyph)));
        _glyphs.Add(glyph, value);
        return value;
    }

    private static bool HasData(nint blob)
    {
        if (blob == 0) return false;
        try { return HarfBuzzColorApi.BlobLength(blob) != 0; }
        finally { HarfBuzzColorApi.DestroyBlob(blob); }
    }

    public void Dispose()
    {
        _entry?.Dispose();
        _entry = null;
        _glyphs.Clear();
    }
}

// Public HarfBuzz 8.3.1 hb-ot-color.h APIs exported by the already-pinned
// HarfBuzzSharp native assets but not exposed by its managed Face wrapper.
internal static partial class HarfBuzzColorApi
{
    private const string Library = "libHarfBuzzSharp";

    [LibraryImport(Library, EntryPoint = "hb_ot_color_glyph_get_layers")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint GetLayerCount(nint face, uint glyph, uint start, nint count, nint layers);

    [LibraryImport(Library, EntryPoint = "hb_ot_color_glyph_has_paint")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int HasPaint(nint face, uint glyph);

    [LibraryImport(Library, EntryPoint = "hb_ot_color_glyph_reference_svg")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint ReferenceSvg(nint face, uint glyph);

    [LibraryImport(Library, EntryPoint = "hb_ot_color_glyph_reference_png")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint ReferencePng(nint font, uint glyph);

    [LibraryImport(Library, EntryPoint = "hb_blob_get_length")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint BlobLength(nint blob);

    [LibraryImport(Library, EntryPoint = "hb_blob_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void DestroyBlob(nint blob);
}
