// Licensed under the MIT License.
// GhosttySharp.Avalonia - SkiaSharp-based terminal renderer.

using SkiaSharp;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace GhosttySharp.Avalonia.Rendering;

/// <summary>
/// High-performance SkiaSharp terminal renderer.
/// Renders the cell grid to an SKCanvas with support for:
/// - Dirty-row tracking (only re-renders changed rows)
/// - Background batching (merges adjacent same-color backgrounds)
/// - Text rendering with glyph caching
/// - Cursor rendering with configurable styles
/// - Selection highlighting
/// </summary>
public sealed class SkiaTerminalRenderer : IDisposable
{
    private readonly GlyphCache _glyphCache;
    private float _cellWidth;
    private float _cellHeight;
    private float _fontSize;
    private float _baseline;
    private bool _disposed;

    // Reusable paint objects to avoid per-frame allocation
    private readonly SKPaint _bgPaint;
    private readonly SKPaint _fgPaint;
    private readonly SKPaint _cursorPaint;
    private readonly SKPaint _selectionPaint;

    /// <summary>Cell width in pixels.</summary>
    public float CellWidth => _cellWidth;

    /// <summary>Cell height in pixels.</summary>
    public float CellHeight => _cellHeight;

    /// <summary>The current font size.</summary>
    public float FontSize => _fontSize;

    /// <summary>Cursor column position.</summary>
    public int CursorColumn { get; set; }

    /// <summary>Cursor row position (viewport-relative).</summary>
    public int CursorRow { get; set; }

    /// <summary>Whether the cursor is visible.</summary>
    public bool CursorVisible { get; set; } = true;

    /// <summary>Cursor style.</summary>
    public CursorStyle CursorStyle { get; set; } = CursorStyle.Block;

    /// <summary>Cursor color.</summary>
    public SKColor CursorColor { get; set; } = new(0xFF, 0xFF, 0xFF);

    /// <summary>Selection start (column, row) in viewport coordinates.</summary>
    public (int Column, int Row)? SelectionStart { get; set; }

    /// <summary>Selection end (column, row) in viewport coordinates.</summary>
    public (int Column, int Row)? SelectionEnd { get; set; }

    /// <summary>Selection highlight color.</summary>
    public SKColor SelectionColor { get; set; } = new(0x40, 0x60, 0xA0, 0x80);

    /// <summary>The glyph cache used by this renderer.</summary>
    public GlyphCache GlyphCache => _glyphCache;

    public SkiaTerminalRenderer(string fontFamily = "Consolas", float fontSize = 14f)
    {
        _fontSize = fontSize;
        _glyphCache = new GlyphCache(fontFamily);

        var (w, h) = _glyphCache.MeasureCellSize(fontSize);
        _cellWidth = w;
        _cellHeight = h;

        using var font = _glyphCache.CreateFont(fontSize);
        _baseline = -font.Metrics.Ascent;

        _bgPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        _fgPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _cursorPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        _selectionPaint = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
            Color = SelectionColor,
        };
    }

    /// <summary>
    /// Updates the font size and recalculates cell dimensions.
    /// </summary>
    public void SetFontSize(float fontSize)
    {
        _fontSize = fontSize;
        var (w, h) = _glyphCache.MeasureCellSize(fontSize);
        _cellWidth = w;
        _cellHeight = h;

        using var font = _glyphCache.CreateFont(fontSize);
        _baseline = -font.Metrics.Ascent;

        _glyphCache.Clear();
    }

    /// <summary>
    /// Renders the terminal screen to the given SKCanvas.
    /// Only re-renders rows marked as dirty unless forceFullRedraw is true.
    /// </summary>
    public void Render(SKCanvas canvas, TerminalScreen screen, bool forceFullRedraw = false)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(screen);

        canvas.Save();

        // Render backgrounds first (batched), then text on top
        for (var row = 0; row < screen.ViewportRows; row++)
        {
            var terminalRow = screen.GetViewportRow(row);
            if (!forceFullRedraw && !terminalRow.IsDirty) continue;

            var y = row * _cellHeight;

            // Render background cells
            RenderRowBackground(canvas, terminalRow, y);

            // Render text
            RenderRowText(canvas, terminalRow, y);

            terminalRow.IsDirty = false;
        }

        // Render selection overlay
        RenderSelection(canvas, screen);

        // Render cursor
        if (CursorVisible)
            RenderCursor(canvas);

        canvas.Restore();
    }

    /// <summary>
    /// Renders the full screen without dirty tracking (useful for first paint or resize).
    /// </summary>
    public void RenderFull(SKCanvas canvas, TerminalScreen screen)
    {
        Render(canvas, screen, forceFullRedraw: true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetEffectiveBackground(ref readonly TerminalCell cell)
    {
        var inverse = (cell.Attributes & CellAttributes.Inverse) != 0;
        return inverse ? cell.Foreground : cell.Background;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RenderRowBackground(SKCanvas canvas, TerminalRow row, float y)
    {
        var cells = row.ReadOnlyCells;
        if (cells.IsEmpty) return;

        var batchStart = 0;
        var batchColor = GetEffectiveBackground(in cells[0]);

        for (var col = 1; col <= cells.Length; col++)
        {
            var currentColor = col < cells.Length ? GetEffectiveBackground(in cells[col]) : uint.MaxValue;

            if (currentColor != batchColor)
            {
                // Flush batch
                _bgPaint.Color = new SKColor(batchColor);
                var x = batchStart * _cellWidth;
                var width = (col - batchStart) * _cellWidth;
                canvas.DrawRect(x, y, width, _cellHeight, _bgPaint);

                batchStart = col;
                batchColor = currentColor;
            }
        }
    }

    private void RenderRowText(SKCanvas canvas, TerminalRow row, float y)
    {
        var cells = row.ReadOnlyCells;
        using var font = _glyphCache.CreateFont(_fontSize);

        for (var col = 0; col < cells.Length; col++)
        {
            ref readonly var cell = ref cells[col];
            if (!cell.HasContent) continue;

            var bold = (cell.Attributes & CellAttributes.Bold) != 0;
            var italic = (cell.Attributes & CellAttributes.Italic) != 0;
            var underline = (cell.Attributes & CellAttributes.Underline) != 0;
            var strikethrough = (cell.Attributes & CellAttributes.Strikethrough) != 0;
            var inverse = (cell.Attributes & CellAttributes.Inverse) != 0;

            var fg = new SKColor(inverse ? cell.Background : cell.Foreground);

            _fgPaint.Color = fg;

            if (bold || italic)
            {
                using var styledFont = _glyphCache.CreateFont(_fontSize, bold, italic);
                var text = char.ConvertFromUtf32(cell.Codepoint);
                var x = col * _cellWidth;
                canvas.DrawText(text, x, y + _baseline, styledFont, _fgPaint);
            }
            else
            {
                var text = char.ConvertFromUtf32(cell.Codepoint);
                var x = col * _cellWidth;
                canvas.DrawText(text, x, y + _baseline, font, _fgPaint);
            }

            // Draw underline
            if (underline)
            {
                var x = col * _cellWidth;
                var ulY = y + _cellHeight - 2;
                canvas.DrawLine(x, ulY, x + _cellWidth * cell.Width, ulY, _fgPaint);
            }

            // Draw strikethrough
            if (strikethrough)
            {
                var x = col * _cellWidth;
                var stY = y + _cellHeight / 2;
                canvas.DrawLine(x, stY, x + _cellWidth * cell.Width, stY, _fgPaint);
            }
        }
    }

    private void RenderCursor(SKCanvas canvas)
    {
        var x = CursorColumn * _cellWidth;
        var y = CursorRow * _cellHeight;

        _cursorPaint.Color = CursorColor;

        switch (CursorStyle)
        {
            case CursorStyle.Block:
                _cursorPaint.Style = SKPaintStyle.Fill;
                _cursorPaint.BlendMode = SKBlendMode.Difference;
                canvas.DrawRect(x, y, _cellWidth, _cellHeight, _cursorPaint);
                _cursorPaint.BlendMode = SKBlendMode.SrcOver;
                break;

            case CursorStyle.Underline:
                _cursorPaint.Style = SKPaintStyle.Fill;
                canvas.DrawRect(x, y + _cellHeight - 2, _cellWidth, 2, _cursorPaint);
                break;

            case CursorStyle.Bar:
                _cursorPaint.Style = SKPaintStyle.Fill;
                canvas.DrawRect(x, y, 2, _cellHeight, _cursorPaint);
                break;
        }
    }

    private void RenderSelection(SKCanvas canvas, TerminalScreen screen)
    {
        if (SelectionStart is null || SelectionEnd is null) return;

        var (startCol, startRow) = SelectionStart.Value;
        var (endCol, endRow) = SelectionEnd.Value;

        // Normalize so start <= end
        if (startRow > endRow || (startRow == endRow && startCol > endCol))
        {
            (startCol, startRow, endCol, endRow) = (endCol, endRow, startCol, startRow);
        }

        _selectionPaint.Color = SelectionColor;

        for (var row = startRow; row <= endRow && row < screen.ViewportRows; row++)
        {
            var left = row == startRow ? startCol : 0;
            var right = row == endRow ? endCol : screen.Columns;

            var x = left * _cellWidth;
            var y = row * _cellHeight;
            var width = (right - left) * _cellWidth;

            canvas.DrawRect(x, y, width, _cellHeight, _selectionPaint);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _bgPaint.Dispose();
        _fgPaint.Dispose();
        _cursorPaint.Dispose();
        _selectionPaint.Dispose();
        _glyphCache.Dispose();
    }
}

/// <summary>
/// Cursor display style.
/// </summary>
public enum CursorStyle
{
    Block,
    Underline,
    Bar,
}
