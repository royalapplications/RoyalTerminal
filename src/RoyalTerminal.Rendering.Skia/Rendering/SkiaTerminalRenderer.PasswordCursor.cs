// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class SkiaTerminalRenderer
{
    private void RenderPasswordCursor(SKCanvas canvas, float x, float y, float width)
    {
        // Ghostty uses the Nerd Font lock (U+F023). Unlike Ghostty, hosts need not
        // bundle Nerd Fonts; a geometric fallback keeps the indicator meaningful.
        SKTypeface typeface = _fontResolver.ResolveTypeface(_glyphCache.RegularTypeface, 0xF023, s_renderCulture).Typeface;
        _cursorPaint.Style = SKPaintStyle.Fill;
        _cursorPaint.BlendMode = SKBlendMode.SrcOver;
        if (_singleGlyphIdCache.TryGetOrCreate(new SingleGlyphIdCacheKey(0xF023, typeface.Handle), typeface, out _))
        {
            SKFont font = _textRowFontCache.GetOrCreate(typeface, _fontSize, _fontRenderingSettings);
            font.MeasureText("\uF023", out SKRect bounds);
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                float scale = Math.Min(width * 0.8f / bounds.Width, _cellHeight * 0.8f / bounds.Height);
                canvas.Save();
                canvas.Translate(x + width / 2, y + _cellHeight / 2);
                canvas.Scale(scale);
                canvas.DrawText("\uF023", -bounds.MidX, -bounds.MidY, font, _cursorPaint);
                canvas.Restore();
                return;
            }
        }

        DrawPasswordLockFallback(canvas, new SKRect(x, y, x + width, y + _cellHeight), _cursorPaint);
    }

    internal static void DrawPasswordLockFallback(SKCanvas canvas, SKRect cell, SKPaint paint)
    {
        float width = cell.Width;
        float height = cell.Height;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = Math.Min(width, height) * 0.1f;
        canvas.DrawRoundRect(new SKRect(cell.Left + width * 0.3f, cell.Top + height * 0.15f,
            cell.Left + width * 0.7f, cell.Top + height * 0.65f), width * 0.2f, height * 0.2f, paint);
        paint.Style = SKPaintStyle.Fill;
        paint.StrokeWidth = 0;
        canvas.DrawRoundRect(new SKRect(cell.Left + width * 0.15f, cell.Top + height * 0.45f,
            cell.Left + width * 0.85f, cell.Top + height * 0.85f), width * 0.05f, height * 0.05f, paint);
    }
}
