// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class SkiaTerminalRenderer
{
    private void DrawDisplayTextRun(SKCanvas canvas, ReadOnlySpan<TerminalCell> cells, int column,
        SKTypeface typeface, SKColor color, float y, bool usePretextPipeline)
    {
        // All lengths, hashes, cluster/grid offsets, fallback blobs and font
        // thickening use the same display projection. Source cells remain the
        // authority for copy/search, decorations, selection and snapshots.
        canvas.Save();
        try
        {
            canvas.Translate(column * _cellWidth, 0);
            if (EnableTextShaping)
            {
                if (!usePretextPipeline || !TryDrawPretextTextRun(canvas, cells, 0, cells.Length, typeface, color, y))
                    DrawShapedTextRun(canvas, cells, 0, cells.Length, typeface, color, y);
            }
            else
            {
                DrawCellAnchoredFallbackRun(canvas, cells, 0, cells.Length, typeface, color, y,
                    ComputeRunWidth(cells, 0, cells.Length));
            }
        }
        finally { canvas.Restore(); }
    }

    private void DrawCursorDisplayCell(SKCanvas canvas, in TerminalCell cell, int column, float y)
    {
        if (TryGetSpriteCodepoint(in cell, out int sprite, out SpriteCategory category))
        {
            DrawSpriteCell(canvas, sprite, category, column, Math.Max(1, (int)cell.Width), y, CursorTextColor);
            return;
        }
        TerminalFontResolution font = ResolveFontForCell(in cell);
        _displayCells.Clear();
        _displayCells.Add(in cell, font);
        canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(column * _cellWidth, y,
                (column + Math.Max(1, (int)cell.Width)) * _cellWidth, y + _cellHeight));
            DrawDisplayTextRun(canvas, _displayCells.Cells, column, font.Typeface, CursorTextColor, y,
                CanUsePretextTextPipeline());
        }
        finally
        {
            canvas.Restore();
            _displayCells.Clear();
        }
    }
}
