// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class SkiaTerminalRenderer
{
    internal TerminalPreedit? Preedit { get; set; }

    private bool TryGetPreeditRange(int columns, int row, out TerminalPreeditRange range)
    {
        if (Preedit is not { Count: > 0 } preedit || row != CursorRow || (uint)CursorColumn >= (uint)columns)
        {
            range = default;
            return false;
        }
        range = preedit.Range(CursorColumn, columns - 1);
        return true;
    }

    private void RenderPreedit(SKCanvas canvas, uint foreground, float y, TerminalPreeditRange range)
    {
        TerminalPreedit preedit = Preedit!;
        _fgPaint.Color = new SKColor(foreground);
        _fgPaint.Style = SKPaintStyle.Fill;
        int column = range.Start;
        for (int i = range.Offset; i < range.Limit; i++)
        {
            (string text, int width) = preedit[i];
            SKTypeface typeface = _fontResolver.ResolveTypeface(_glyphCache.RegularTypeface, text.AsSpan(), s_renderCulture).Typeface;
            canvas.Save();
            canvas.Translate(column * _cellWidth, 0);
            DrawShapedTextRun(canvas, preedit.RenderCell(i), 0, 1, typeface, new SKColor(foreground), y);
            canvas.Restore();
            canvas.DrawRect(column * _cellWidth, y + _cellHeight - 1, width * _cellWidth, 1, _fgPaint);
            column += width;
        }
        _cursorPaint.Color = CursorColor;
        _cursorPaint.Style = SKPaintStyle.Fill;
        _cursorPaint.BlendMode = SKBlendMode.SrcOver;
        canvas.DrawRect(range.Caret * _cellWidth, y, _cellWidth, _cellHeight, _cursorPaint);
        // Redraw the caret cluster in cursor text color without consulting the
        // terminal's underlying cell, which does not contain composition text.
        column = range.Start;
        for (int i = range.Offset; i < range.Limit; i++)
        {
            (string text, int width) = preedit[i];
            if (range.Caret >= column && range.Caret < column + width)
            {
                canvas.Save();
                canvas.ClipRect(new SKRect(range.Caret * _cellWidth, y, (range.Caret + 1) * _cellWidth, y + _cellHeight));
                canvas.Translate(column * _cellWidth, 0);
                SKTypeface typeface = _fontResolver.ResolveTypeface(_glyphCache.RegularTypeface, text.AsSpan(), s_renderCulture).Typeface;
                DrawShapedTextRun(canvas, preedit.RenderCell(i), 0, 1, typeface, CursorTextColor, y);
                canvas.Restore();
                break;
            }
            column += width;
        }
    }
}
