// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using HarfBuzzSharp;
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
        _preeditDisplayCells.Clear();
        try
        {
            foreach (ref readonly TerminalCell cell in Preedit!.RenderCells)
                _preeditDisplayCells.Add(in cell, ResolveFontForCell(in cell));
            RenderPreeditDisplay(canvas, foreground, y, range, _preeditDisplayCells.Cells, _preeditDisplayCells.Fonts);
        }
        finally { _preeditDisplayCells.Clear(); }
    }

    private void RenderPreeditDisplay(SKCanvas canvas, uint foreground, float y, TerminalPreeditRange range,
        ReadOnlySpan<TerminalCell> cells, ReadOnlySpan<TerminalFontResolution> fonts)
    {
        int column = range.Start;
        for (int i = 0; i < range.Offset; i++) column -= cells[i].Width;
        int caretOffset = 0, caretLength = 0, caretColumn = 0;
        SKTypeface? caretTypeface = null;
        SKRect textClip = new(range.Start * _cellWidth, y, (range.End + 1) * _cellWidth, y + _cellHeight);
        for (int offset = 0; offset < range.Limit;)
        {
            SKTypeface typeface = fonts[offset].Typeface;
            Script script = GetPreeditScript(in cells[offset]);
            int limit = offset + 1;
            int width = cells[offset].Width;
            while (limit < cells.Length)
            {
                Script nextScript = GetPreeditScript(in cells[limit]);
                if (!IsNeutralPreeditScript(script) && !IsNeutralPreeditScript(nextScript) && script != nextScript)
                    break;
                if (fonts[limit].Typeface.Handle != typeface.Handle)
                    break;
                if (IsNeutralPreeditScript(script)) script = nextScript;
                width += cells[limit++].Width;
            }

            // Ghostty uses an isolated scalar overlay; WT's TSF preview and
            // xterm.js's composition DOM retain surrounding text. Shape complete
            // compatible runs here so joining/ligatures survive the IME caret.
            // Retain offscreen neighbors in the shaping context as well. A
            // horizontally clipped preview must not acquire new word endings.
            if (limit > range.Offset)
                DrawPreeditRun(canvas, cells[offset..limit], column, typeface, new SKColor(foreground), y, textClip);
            if (limit > range.Offset && range.Caret <= range.End &&
                range.Caret >= column && range.Caret < column + width)
            {
                caretOffset = offset;
                caretLength = limit - offset;
                caretColumn = column;
                caretTypeface = typeface;
            }
            column += width;
            offset = limit;
        }

        _fgPaint.Color = new SKColor(foreground);
        _fgPaint.Style = SKPaintStyle.Fill;
        if (range.End >= range.Start)
            canvas.DrawRect(range.Start * _cellWidth, y + _cellHeight - 1,
                (range.End - range.Start + 1) * _cellWidth, 1, _fgPaint);
        _cursorPaint.Color = CursorColor;
        _cursorPaint.Style = SKPaintStyle.Fill;
        _cursorPaint.BlendMode = SKBlendMode.SrcOver;
        canvas.DrawRect(range.Caret * _cellWidth, y, _cellWidth, _cellHeight, _cursorPaint);
        // Recolor the same shaped context under a one-cell clip. Reshaping only
        // the caret cluster would replace joined forms and split ligatures.
        if (caretTypeface is not null)
        {
            canvas.Save();
            try
            {
                canvas.ClipRect(new SKRect(range.Caret * _cellWidth, y, (range.Caret + 1) * _cellWidth, y + _cellHeight));
                DrawPreeditRun(canvas, cells.Slice(caretOffset, caretLength), caretColumn, caretTypeface, CursorTextColor, y, textClip);
            }
            finally { canvas.Restore(); }
        }
    }

    private void DrawPreeditRun(SKCanvas canvas, ReadOnlySpan<TerminalCell> cells, int column,
        SKTypeface typeface, SKColor color, float y, SKRect clip)
    {
        canvas.Save();
        try
        {
            canvas.ClipRect(clip);
            canvas.Translate(column * _cellWidth, 0);
            DrawShapedTextRun(canvas, cells, 0, cells.Length, typeface, color, y);
        }
        finally { canvas.Restore(); }
    }

    private static Script GetPreeditScript(ref readonly TerminalCell cell)
    {
        if (string.IsNullOrEmpty(cell.Grapheme) && Rune.IsValid(cell.Codepoint))
            return UnicodeFunctions.Default.GetScript(cell.Codepoint);
        foreach (Rune rune in cell.Grapheme.AsSpan().EnumerateRunes())
        {
            Script script = UnicodeFunctions.Default.GetScript(rune.Value);
            if (!IsNeutralPreeditScript(script)) return script;
        }
        return Script.Common;
    }

    private static bool IsNeutralPreeditScript(Script script)
        => script == Script.Common || script == Script.Inherited || script == Script.Unknown;
}
