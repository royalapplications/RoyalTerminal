// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.Terminal;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// CoreText grayscale glyph masks with Ghostty's font smoothing/strength policy.
/// Advances and shaping remain owned by the shared renderer. Color glyphs retain
/// their Skia path; macOS bitmap strikes are not affected by font smoothing.
/// </summary>
internal sealed partial class MacFontThickeningCache : IDisposable
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const long MaxBytes = 16 * 1024 * 1024;
    private readonly int _capacity;
    private readonly Dictionary<SKTypeface, Face?> _faces = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GlyphKey, GlyphMask> _glyphs = new();
    private nint _graphicsLibrary;
    private nint _colorSpace;
    private float _size;
    private byte _strength;
    private bool _embolden;
    private long _bytes;
    private bool _disposed;

    internal MacFontThickeningCache(int capacity = 4096) => _capacity = Math.Max(1, capacity);
    internal int Count => _glyphs.Count;
    internal long Bytes => _bytes;

    internal bool TryDraw(SKCanvas canvas, SKTypeface typeface, float size,
        TerminalFontRenderingSettings settings, ReadOnlySpan<ushort> glyphs,
        ReadOnlySpan<SKPoint> positions, float originX, float baselineY, SKPaint paint)
    {
        if (_disposed || !OperatingSystem.IsMacOS() || !settings.Thicken ||
            !float.IsFinite(size) || size <= 0 || glyphs.Length != positions.Length) return false;
        if (_size != size || _strength != settings.ThickenStrength || _embolden != settings.Embolden)
        {
            Clear();
            _size = size;
            _strength = settings.ThickenStrength;
            _embolden = settings.Embolden;
        }
        if (!_faces.TryGetValue(typeface, out Face? face))
        {
            if (_faces.Count >= 64) Clear();
            face = CreateFace(typeface, size, settings);
            _faces.Add(typeface, face);
        }
        if (face is null) return false;
        for (int i = 0; i < glyphs.Length; i++)
        {
            GlyphKey key = new(face, glyphs[i]);
            if (!_glyphs.TryGetValue(key, out GlyphMask mask))
            {
                mask = Rasterize(face.Font, glyphs[i]);
                if (_glyphs.Count >= _capacity || _bytes + mask.Bytes > MaxBytes) ClearGlyphs();
                _glyphs.Add(key, mask);
                _bytes += mask.Bytes;
            }
            float x = originX + positions[i].X;
            float y = baselineY + positions[i].Y;
            if (!mask.Available)
            {
                // Keep a glyph visible if CoreText declines a particular outline.
                using SKTextBlobBuilder builder = new();
                builder.AddPositionedRun(glyphs.Slice(i, 1), face.Fallback, positions.Slice(i, 1));
                using SKTextBlob? blob = builder.Build();
                if (blob is not null) canvas.DrawText(blob, originX, baselineY, paint);
            }
            else if (mask.Image is not null)
            {
                // Alpha-only images are tinted by the same foreground paint as
                // normal text, including dim opacity and cursor/preedit colors.
                canvas.DrawImage(mask.Image, x + mask.Left, y - mask.Top, paint);
            }
        }
        return true;
    }

    private Face? CreateFace(SKTypeface typeface, float size, TerminalFontRenderingSettings settings)
    {
        // Native color glyphs do not use the grayscale smoothing path upstream.
        if (typeface.GetTableSize(0x73626978) > 0 || typeface.GetTableSize(0x434F4C52) > 0 ||
            typeface.GetTableSize(0x43424454) > 0 || typeface.GetTableSize(0x53564720) > 0) return null;
        using SKStreamAsset? stream = typeface.OpenStream(out int collectionIndex);
        if (stream is null) return null;
        using SKData? data = SKData.Create(stream);
        if (data is null || data.Size == 0) return null;
        nint cfData = Native.CFDataCreate(0, data.Data, (nint)data.Size);
        if (cfData == 0) return null;
        nint descriptors = 0;
        nint font = 0;
        try
        {
            descriptors = Native.CTFontManagerCreateFontDescriptorsFromData(cfData);
            if (descriptors == 0 || collectionIndex < 0 || collectionIndex >= Native.CFArrayGetCount(descriptors)) return null;
            nint descriptor = Native.CFArrayGetValueAtIndex(descriptors, collectionIndex);
            font = Native.CTFontCreateWithFontDescriptor(descriptor, size, 0);
            if (font == 0 || Native.CTFontGetGlyphCount(font) != typeface.GlyphCount) return null;
            Face result = new(font, GlyphCache.CreateFont(typeface, size, settings));
            font = 0;
            return result;
        }
        finally
        {
            if (font != 0) Native.CFRelease(font);
            if (descriptors != 0) Native.CFRelease(descriptors);
            Native.CFRelease(cfData);
        }
    }

    private unsafe GlyphMask Rasterize(nint font, ushort glyph)
    {
        CgRect bounds = Native.CTFontGetBoundingRectsForGlyphs(font, 1, &glyph, null, 1);
        if (bounds.Width <= 0 || bounds.Height <= 0) return new(null, 0, 0, 0, true);
        double stroke = _embolden ? _size / 24.0 : 0;
        int padding = 1 + (int)Math.Ceiling(stroke / 2);
        double left = Math.Floor(bounds.X) - padding;
        double bottom = Math.Floor(bounds.Y) - padding;
        double widthValue = Math.Ceiling(bounds.X + bounds.Width) + padding - left;
        double heightValue = Math.Ceiling(bounds.Y + bounds.Height) + padding - bottom;
        if (!double.IsFinite(widthValue) || !double.IsFinite(heightValue) ||
            widthValue > 4096 || heightValue > 4096) return default;
        int width = (int)widthValue, height = (int)heightValue;
        using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
        if (bitmap.GetPixels() == 0) return default;
        bitmap.Erase(SKColors.Transparent);
        if (_colorSpace == 0)
        {
            if (_graphicsLibrary == 0) _graphicsLibrary = NativeLibrary.Load(CoreGraphics);
            nint name = Marshal.ReadIntPtr(NativeLibrary.GetExport(_graphicsLibrary, "kCGColorSpaceLinearGray"));
            _colorSpace = Native.CGColorSpaceCreateWithName(name);
            if (_colorSpace == 0) return default;
        }
        nint context = Native.CGBitmapContextCreate(bitmap.GetPixels(), (nuint)width, (nuint)height,
            8, (nuint)bitmap.RowBytes, _colorSpace, 7 /* kCGImageAlphaOnly */);
        if (context == 0) return default;
        try
        {
            Native.CGContextSetAllowsFontSmoothing(context, 1);
            Native.CGContextSetShouldSmoothFonts(context, 1);
            Native.CGContextSetAllowsFontSubpixelPositioning(context, 1);
            Native.CGContextSetShouldSubpixelPositionFonts(context, 1);
            Native.CGContextSetAllowsFontSubpixelQuantization(context, 0);
            Native.CGContextSetShouldSubpixelQuantizeFonts(context, 0);
            Native.CGContextSetAllowsAntialiasing(context, 1);
            Native.CGContextSetShouldAntialias(context, 1);
            Native.CGContextSetGrayFillColor(context, _strength / 255.0, 1);
            Native.CGContextSetGrayStrokeColor(context, _strength / 255.0, 1);
            if (_embolden)
            {
                Native.CGContextSetTextDrawingMode(context, 2 /* fill + stroke */);
                Native.CGContextSetLineWidth(context, stroke);
            }
            CgPoint position = new(-left, -bottom);
            Native.CTFontDrawGlyphs(font, &glyph, &position, 1, context);
            return new(SKImage.FromBitmap(bitmap), (float)left, (float)(bottom + height),
                (long)bitmap.RowBytes * height, true);
        }
        finally { Native.CGContextRelease(context); }
    }

    internal void Clear()
    {
        ClearGlyphs();
        foreach (Face? face in _faces.Values) face?.Dispose();
        _faces.Clear();
    }

    private void ClearGlyphs()
    {
        foreach (GlyphMask mask in _glyphs.Values) mask.Image?.Dispose();
        _glyphs.Clear();
        _bytes = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
        if (_colorSpace != 0) Native.CGColorSpaceRelease(_colorSpace);
        if (_graphicsLibrary != 0) NativeLibrary.Free(_graphicsLibrary);
        _colorSpace = _graphicsLibrary = 0;
    }

    private sealed class Face(nint font, SKFont fallback) : IDisposable
    {
        internal nint Font { get; } = font;
        internal SKFont Fallback { get; } = fallback;
        public void Dispose() { Fallback.Dispose(); Native.CFRelease(Font); }
    }
    private readonly record struct GlyphKey(Face Face, ushort Glyph);
    private readonly record struct GlyphMask(SKImage? Image, float Left, float Top, long Bytes, bool Available);
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CgPoint(double X, double Y);
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CgRect(double X, double Y, double Width, double Height);

    private static partial class Native
    {
        [LibraryImport(CoreFoundation)] internal static partial nint CFDataCreate(nint allocator, nint bytes, nint length);
        [LibraryImport(CoreFoundation)] internal static partial nint CFArrayGetCount(nint array);
        [LibraryImport(CoreFoundation)] internal static partial nint CFArrayGetValueAtIndex(nint array, nint index);
        [LibraryImport(CoreFoundation)] internal static partial void CFRelease(nint value);
        [LibraryImport(CoreText)] internal static partial nint CTFontManagerCreateFontDescriptorsFromData(nint data);
        [LibraryImport(CoreText)] internal static partial nint CTFontCreateWithFontDescriptor(nint descriptor, double size, nint matrix);
        [LibraryImport(CoreText)] internal static partial nint CTFontGetGlyphCount(nint font);
        [LibraryImport(CoreText)] internal static unsafe partial CgRect CTFontGetBoundingRectsForGlyphs(nint font, uint orientation, ushort* glyphs, CgRect* bounds, nint count);
        [LibraryImport(CoreText)] internal static unsafe partial void CTFontDrawGlyphs(nint font, ushort* glyphs, CgPoint* positions, nuint count, nint context);
        [LibraryImport(CoreGraphics)] internal static partial nint CGColorSpaceCreateWithName(nint name);
        [LibraryImport(CoreGraphics)] internal static partial void CGColorSpaceRelease(nint space);
        [LibraryImport(CoreGraphics)] internal static partial nint CGBitmapContextCreate(nint data, nuint width, nuint height, nuint bits, nuint rowBytes, nint space, uint info);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextRelease(nint context);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetAllowsFontSmoothing(nint context, byte enabled);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetShouldSmoothFonts(nint context, byte enabled);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetAllowsFontSubpixelPositioning(nint context, byte enabled);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetShouldSubpixelPositionFonts(nint context, byte enabled);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetAllowsFontSubpixelQuantization(nint context, byte enabled);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetShouldSubpixelQuantizeFonts(nint context, byte enabled);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetAllowsAntialiasing(nint context, byte enabled);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetShouldAntialias(nint context, byte enabled);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetGrayFillColor(nint context, double gray, double alpha);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetGrayStrokeColor(nint context, double gray, double alpha);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetTextDrawingMode(nint context, uint mode);
        [LibraryImport(CoreGraphics)] internal static partial void CGContextSetLineWidth(nint context, double width);
    }
}
