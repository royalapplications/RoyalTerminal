// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class SkiaTerminalRenderer
{
    private readonly MacFontThickeningCache _thickenedGlyphs = new();
    internal int ThickenedGlyphCount => _thickenedGlyphs.Count;
    private bool FontThickeningEnabled => _fontRenderingSettings.Thicken && OperatingSystem.IsMacOS();

    private bool TryDrawThickenedGlyphs(SKCanvas canvas, SKTypeface typeface,
        ReadOnlySpan<ushort> glyphs, ReadOnlySpan<SKPoint> points, float x, float y)
        => FontThickeningEnabled && _thickenedGlyphs.TryDraw(canvas, typeface, _fontSize,
            _fontRenderingSettings, glyphs, points, x, y, _fgPaint);

    private bool TryDrawThickenedText(SKCanvas canvas, SKTypeface typeface, SKFont font,
        string text, float x, float y)
    {
        if (!FontThickeningEnabled) return false;
        int count = font.CountGlyphs(text);
        if (count == 0) return true;
        ushort[]? rentedGlyphs = null;
        SKPoint[]? rentedPoints = null;
        Span<ushort> glyphs = count <= MaxStackallocGlyphPoints ? stackalloc ushort[count]
            : (rentedGlyphs = ArrayPool<ushort>.Shared.Rent(count)).AsSpan(0, count);
        Span<SKPoint> points = count <= MaxStackallocGlyphPoints ? stackalloc SKPoint[count]
            : (rentedPoints = ArrayPool<SKPoint>.Shared.Rent(count)).AsSpan(0, count);
        try
        {
            font.GetGlyphs(text, glyphs);
            font.GetGlyphPositions(glyphs, points);
            return TryDrawThickenedGlyphs(canvas, typeface, glyphs, points, x, y);
        }
        finally
        {
            if (rentedGlyphs is not null) ArrayPool<ushort>.Shared.Return(rentedGlyphs);
            if (rentedPoints is not null) ArrayPool<SKPoint>.Shared.Return(rentedPoints);
        }
    }

    private bool TryDrawThickenedShapedRun(SKCanvas canvas, CachedShapedRun run, SKTypeface typeface,
        float originX, float rowY, float baselineY, float runWidth, float xScale,
        bool clampToRunWidth, ReadOnlySpan<float> textGridOffsets, bool useClusterGridFit)
    {
        if (!FontThickeningEnabled) return false;
        SKPoint[]? rentedPoints = null;
        Span<SKPoint> points = run.GlyphCount <= MaxStackallocGlyphPoints ? stackalloc SKPoint[run.GlyphCount]
            : (rentedPoints = ArrayPool<SKPoint>.Shared.Rent(run.GlyphCount)).AsSpan(0, run.GlyphCount);
        canvas.Save();
        try
        {
            FillShapedRunPoints(run, runWidth, xScale, clampToRunWidth, textGridOffsets, useClusterGridFit, points);
            if (run.ClipPadding > 0 || clampToRunWidth || useClusterGridFit || xScale != 1f)
                ClipTextRun(canvas, originX, rowY, runWidth, run.ClipPadding);
            return TryDrawThickenedGlyphs(canvas, typeface, run.GlyphIds, points, originX, baselineY);
        }
        finally
        {
            canvas.Restore();
            if (rentedPoints is not null) ArrayPool<SKPoint>.Shared.Return(rentedPoints);
        }
    }

#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
    private bool TryDrawThickenedPretextRun(SKCanvas canvas, CachedPretextRun run, SKTypeface typeface,
        float originX, float rowY, float baselineY, float runWidth, float xScale)
    {
        if (!FontThickeningEnabled) return false;
        canvas.Save();
        try
        {
            if (run.ClipPadding > 0) ClipTextRun(canvas, originX, rowY, runWidth, run.ClipPadding);
            canvas.Translate(originX, baselineY);
            canvas.Scale(xScale, 1f);
            SKFont font = _textRowFontCache.GetOrCreate(typeface, _fontSize, _fontRenderingSettings);
            return TryDrawThickenedText(canvas, typeface, font, run.Text, 0, 0);
        }
        finally { canvas.Restore(); }
    }
#endif
}
