// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - SkiaSharp-based terminal renderer.

using SkiaSharp;
using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace RoyalTerminal.Avalonia.Rendering;

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
    private const float DimFactor = 0.55f;
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const float GridScaleFallbackMin = 0.5f;
    private const float GridScaleFallbackMax = 1.6f;
    private const float GridClampToleranceRatio = 0.04f;
    private const float GridClampTolerancePx = 0.25f;
    private static readonly CultureInfo s_renderCulture = CultureInfo.InvariantCulture;

    private readonly GlyphCache _glyphCache;
    private readonly HarfBuzzTextShaper _textShaper;
    private readonly TerminalFontResolver _fontResolver;
    private readonly ShapedRunCache _shapedRunCache;
    private const float CellMetricEpsilon = 0.01f;
    private float _cellWidth;
    private float _cellHeight;
    private float _fontSize;
    private float _baseline;
    private TextDirectionMode _textDirectionMode = TextDirectionMode.Auto;
    private bool _enableLigatures;
    private bool _disposed;
    private long _diagnosticShapedRuns;
    private long _diagnosticFallbackRuns;
    private long _diagnosticFallbackFontHits;
    private long _diagnosticGridClampedRuns;
    private long _diagnosticSpriteCells;
    private long _diagnosticBoxDrawingSpriteCells;
    private long _diagnosticBrailleSpriteCells;
    private long _diagnosticBlockSpriteCells;
    private long _diagnosticScanLineSpriteCells;

    // Reusable paint objects to avoid per-frame allocation
    private readonly SKPaint _bgPaint;
    private readonly SKPaint _fgPaint;
    private readonly SKPaint _cursorPaint;
    private readonly SKPaint _selectionPaint;
    private readonly SKPaint _spritePaint;

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

    /// <summary>
    /// Enables collection of text-render diagnostics counters.
    /// Disabled by default.
    /// </summary>
    public bool EnableTextRenderDiagnostics { get; set; }

    /// <summary>
    /// Enables or disables HarfBuzz shaping for terminal text rendering.
    /// When disabled, renderer falls back to cell-anchored text drawing.
    /// </summary>
    public bool EnableTextShaping { get; set; } = true;

    /// <summary>
    /// Gets or sets text direction mode used for shaping.
    /// Changing this value clears the shaped run cache.
    /// </summary>
    public TextDirectionMode TextDirectionMode
    {
        get => _textDirectionMode;
        set
        {
            if (_textDirectionMode == value)
            {
                return;
            }

            _textDirectionMode = value;
            _shapedRunCache.Clear();
        }
    }

    /// <summary>
    /// Gets or sets whether shaping should enable OpenType ligatures.
    /// Changing this value clears the shaped run cache.
    /// </summary>
    public bool EnableLigatures
    {
        get => _enableLigatures;
        set
        {
            if (_enableLigatures == value)
            {
                return;
            }

            _enableLigatures = value;
            _shapedRunCache.Clear();
        }
    }

    public SkiaTerminalRenderer(string fontFamily = "Consolas", float fontSize = 14f)
    {
        _fontSize = fontSize;
        _glyphCache = new GlyphCache(fontFamily);
        _textShaper = new HarfBuzzTextShaper();
        _fontResolver = new TerminalFontResolver();
        _shapedRunCache = new ShapedRunCache();

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
        _spritePaint = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
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
        _baseline = ComputeBaseline(font.Metrics, _cellHeight);

        _glyphCache.Clear();
        _shapedRunCache.Clear();
    }

    /// <summary>
    /// Overrides the logical cell width/height used for layout.
    /// This is useful when external terminal engines (e.g. Ghostty surfaces)
    /// report exact grid metrics that differ from local font measurement.
    /// </summary>
    public void SetCellSize(float cellWidth, float cellHeight)
    {
        if (cellWidth <= 0 || cellHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellWidth),
                "Cell dimensions must be greater than zero.");
        }

        if (Math.Abs(_cellWidth - cellWidth) < CellMetricEpsilon &&
            Math.Abs(_cellHeight - cellHeight) < CellMetricEpsilon)
        {
            return;
        }

        _cellWidth = cellWidth;
        _cellHeight = cellHeight;

        using var font = _glyphCache.CreateFont(_fontSize);
        _baseline = ComputeBaseline(font.Metrics, _cellHeight);
        _shapedRunCache.Clear();
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
        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        if (cells.IsEmpty)
        {
            return;
        }

        int col = 0;
        while (col < cells.Length)
        {
            ref readonly TerminalCell firstCell = ref cells[col];
            if (!IsRenderableGlyphCell(in firstCell))
            {
                col++;
                continue;
            }

            if (TryGetSpriteCodepoint(in firstCell, out _))
            {
                SKColor spriteColor = GetEffectiveForeground(in firstCell);
                int spriteRunEnd = col + 1;
                while (spriteRunEnd < cells.Length)
                {
                    ref readonly TerminalCell spriteCandidate = ref cells[spriteRunEnd];
                    if (!IsRenderableGlyphCell(in spriteCandidate))
                    {
                        break;
                    }

                    if (!TryGetSpriteCodepoint(in spriteCandidate, out _))
                    {
                        break;
                    }

                    SKColor nextColor = GetEffectiveForeground(in spriteCandidate);
                    if (nextColor != spriteColor)
                    {
                        break;
                    }

                    spriteRunEnd++;
                }

                DrawSpriteRun(canvas, cells, col, spriteRunEnd, y, spriteColor);
                DrawRunDecorations(canvas, cells, col, spriteRunEnd, y, spriteColor);
                col = spriteRunEnd;
                continue;
            }

            bool bold = (firstCell.Attributes & CellAttributes.Bold) != 0;
            bool italic = (firstCell.Attributes & CellAttributes.Italic) != 0;
            SKTypeface primaryTypeface = _glyphCache.GetTypeface(bold, italic);
            SKColor runColor = GetEffectiveForeground(in firstCell);
            SKTypeface runTypeface = ResolveTypefaceForCell(primaryTypeface, in firstCell);

            int runEnd = col + 1;
            while (runEnd < cells.Length)
            {
                ref readonly TerminalCell nextCell = ref cells[runEnd];
                if (!IsRenderableGlyphCell(in nextCell))
                {
                    break;
                }

                if (TryGetSpriteCodepoint(in nextCell, out _))
                {
                    break;
                }

                bool nextBold = (nextCell.Attributes & CellAttributes.Bold) != 0;
                bool nextItalic = (nextCell.Attributes & CellAttributes.Italic) != 0;
                if (nextBold != bold || nextItalic != italic)
                {
                    break;
                }

                SKColor nextColor = GetEffectiveForeground(in nextCell);
                if (nextColor != runColor)
                {
                    break;
                }

                SKTypeface nextTypeface = ResolveTypefaceForCell(primaryTypeface, in nextCell);

                if (nextTypeface.Handle != runTypeface.Handle)
                {
                    break;
                }

                runEnd++;
            }

            if (EnableTextShaping)
            {
                DrawShapedTextRun(canvas, cells, col, runEnd, runTypeface, runColor, y);
            }
            else
            {
                float runWidth = ComputeRunWidth(cells, col, runEnd);
                DrawCellAnchoredFallbackRun(
                    canvas,
                    cells,
                    col,
                    runEnd,
                    runTypeface,
                    runColor,
                    y,
                    runWidth);
            }

            DrawRunDecorations(canvas, cells, col, runEnd, y, runColor);
            col = runEnd;
        }
    }

    private void DrawSpriteRun(
        SKCanvas canvas,
        ReadOnlySpan<TerminalCell> cells,
        int startCol,
        int endCol,
        float y,
        SKColor color)
    {
        for (int col = startCol; col < endCol; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (!TryGetSpriteCodepoint(in cell, out int codepoint, out SpriteCategory category))
            {
                continue;
            }

            int cellWidth = cell.Width <= 0 ? 1 : cell.Width;
            DrawSpriteCell(canvas, codepoint, category, col, cellWidth, y, color);
            RecordSpriteCell(category);
        }
    }

    private void DrawSpriteCell(
        SKCanvas canvas,
        int codepoint,
        SpriteCategory category,
        int column,
        int widthCells,
        float y,
        SKColor color)
    {
        float x = column * _cellWidth;
        float spriteWidth = Math.Max(_cellWidth, _cellWidth * widthCells);
        float spriteHeight = _cellHeight;

        canvas.Save();
        canvas.ClipRect(
            new SKRect(x, y, x + spriteWidth, y + spriteHeight),
            SKClipOperation.Intersect,
            antialias: false);

        try
        {
            switch (category)
            {
                case SpriteCategory.BoxDrawing:
                    if (TryGetBoxSegments(codepoint, out BoxSegments segments))
                    {
                        DrawBoxSegments(canvas, x, y, spriteWidth, spriteHeight, segments, color);
                    }
                    break;

                case SpriteCategory.Braille:
                    DrawBraillePattern(canvas, x, y, spriteWidth, spriteHeight, codepoint - 0x2800, color);
                    break;

                case SpriteCategory.BlockElement:
                    _ = TryDrawBlockElement(canvas, x, y, spriteWidth, spriteHeight, codepoint, color);
                    break;

                case SpriteCategory.ScanLine:
                    if (TryGetScanLineRatio(codepoint, out float scanLineRatio))
                    {
                        DrawScanLine(canvas, x, y, spriteWidth, spriteHeight, scanLineRatio, color);
                    }
                    break;
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void DrawBoxSegments(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        BoxSegments segments,
        SKColor color)
    {
        float centerX = x + (width * 0.5f);
        float centerY = y + (height * 0.5f);
        float minDimension = MathF.Max(1f, MathF.Min(width, height));
        float lightThickness = MathF.Max(1f, MathF.Round(minDimension * 0.12f));
        float heavyThickness = MathF.Max(lightThickness + 1f, MathF.Round(lightThickness * 1.75f));

        float leftThickness = segments.Left == StrokeWeight.Heavy ? heavyThickness : lightThickness;
        float rightThickness = segments.Right == StrokeWeight.Heavy ? heavyThickness : lightThickness;
        float upThickness = segments.Up == StrokeWeight.Heavy ? heavyThickness : lightThickness;
        float downThickness = segments.Down == StrokeWeight.Heavy ? heavyThickness : lightThickness;

        DrawHorizontalSegment(canvas, x, centerX + leftThickness, centerY, leftThickness, segments.Left, color);
        DrawHorizontalSegment(canvas, centerX - rightThickness, x + width, centerY, rightThickness, segments.Right, color);
        DrawVerticalSegment(canvas, y, centerY + upThickness, centerX, upThickness, segments.Up, color);
        DrawVerticalSegment(canvas, centerY - downThickness, y + height, centerX, downThickness, segments.Down, color);
    }

    private void DrawHorizontalSegment(
        SKCanvas canvas,
        float startX,
        float endX,
        float centerY,
        float thickness,
        StrokeWeight weight,
        SKColor color)
    {
        if (weight == StrokeWeight.None || endX <= startX)
        {
            return;
        }

        _spritePaint.Color = color;
        float halfThickness = thickness * 0.5f;
        canvas.DrawRect(startX, centerY - halfThickness, endX - startX, thickness, _spritePaint);
    }

    private void DrawVerticalSegment(
        SKCanvas canvas,
        float startY,
        float endY,
        float centerX,
        float thickness,
        StrokeWeight weight,
        SKColor color)
    {
        if (weight == StrokeWeight.None || endY <= startY)
        {
            return;
        }

        _spritePaint.Color = color;
        float halfThickness = thickness * 0.5f;
        canvas.DrawRect(centerX - halfThickness, startY, thickness, endY - startY, _spritePaint);
    }

    private void DrawBraillePattern(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        int pattern,
        SKColor color)
    {
        if ((pattern & 0xFF) == 0)
        {
            return;
        }

        float insetX = MathF.Max(0f, width * 0.16f);
        float insetY = MathF.Max(0f, height * 0.1f);
        float stepX = MathF.Max(1f, (width - (2f * insetX)) * 0.5f);
        float stepY = MathF.Max(1f, (height - (2f * insetY)) * 0.25f);
        float dotWidth = MathF.Max(1f, stepX * 0.45f);
        float dotHeight = MathF.Max(1f, stepY * 0.45f);

        _spritePaint.Color = color;
        for (int bit = 0; bit < 8; bit++)
        {
            if ((pattern & (1 << bit)) == 0)
            {
                continue;
            }

            (int dotColumn, int dotRow) = bit switch
            {
                0 => (0, 0),
                1 => (0, 1),
                2 => (0, 2),
                3 => (1, 0),
                4 => (1, 1),
                5 => (1, 2),
                6 => (0, 3),
                _ => (1, 3),
            };

            float cx = x + insetX + (dotColumn * stepX) + (stepX * 0.5f);
            float cy = y + insetY + (dotRow * stepY) + (stepY * 0.5f);
            canvas.DrawRect(cx - (dotWidth * 0.5f), cy - (dotHeight * 0.5f), dotWidth, dotHeight, _spritePaint);
        }
    }

    private bool TryDrawBlockElement(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        int codepoint,
        SKColor color)
    {
        _spritePaint.Color = color;

        if (codepoint >= 0x2581 && codepoint <= 0x2588)
        {
            int eighths = codepoint - 0x2580;
            float blockHeight = height * (eighths / 8f);
            float top = y + (height - blockHeight);
            canvas.DrawRect(x, top, width, blockHeight, _spritePaint);
            return true;
        }

        if (codepoint >= 0x2589 && codepoint <= 0x258F)
        {
            int eighths = 0x2590 - codepoint;
            float blockWidth = width * (eighths / 8f);
            canvas.DrawRect(x, y, blockWidth, height, _spritePaint);
            return true;
        }

        switch (codepoint)
        {
            case 0x2580:
                canvas.DrawRect(x, y, width, height * 0.5f, _spritePaint);
                return true;
            case 0x2590:
                canvas.DrawRect(x + (width * 0.5f), y, width * 0.5f, height, _spritePaint);
                return true;
            case 0x2591:
            case 0x2592:
            case 0x2593:
            {
                int fillLevel = codepoint == 0x2591 ? 4 : codepoint == 0x2592 ? 8 : 12;
                DrawShadePattern(canvas, x, y, width, height, fillLevel, color);
                return true;
            }
            case 0x2594:
                canvas.DrawRect(x, y, width, height * 0.125f, _spritePaint);
                return true;
            case 0x2595:
                canvas.DrawRect(x + (width * 0.875f), y, width * 0.125f, height, _spritePaint);
                return true;
        }

        if (TryGetQuadrantMask(codepoint, out QuadrantMask quadrants))
        {
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;

            if ((quadrants & QuadrantMask.UpperLeft) != 0)
            {
                canvas.DrawRect(x, y, halfWidth, halfHeight, _spritePaint);
            }

            if ((quadrants & QuadrantMask.UpperRight) != 0)
            {
                canvas.DrawRect(x + halfWidth, y, halfWidth, halfHeight, _spritePaint);
            }

            if ((quadrants & QuadrantMask.LowerLeft) != 0)
            {
                canvas.DrawRect(x, y + halfHeight, halfWidth, halfHeight, _spritePaint);
            }

            if ((quadrants & QuadrantMask.LowerRight) != 0)
            {
                canvas.DrawRect(x + halfWidth, y + halfHeight, halfWidth, halfHeight, _spritePaint);
            }

            return true;
        }

        return false;
    }

    private void DrawScanLine(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        float ratio,
        SKColor color)
    {
        _spritePaint.Color = color;
        float thickness = MathF.Max(1f, MathF.Round(height * 0.1f));
        float lineY = y + (height * ratio) - (thickness * 0.5f);
        canvas.DrawRect(x, lineY, width, thickness, _spritePaint);
    }

    private void DrawShadePattern(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        int fillLevel,
        SKColor color)
    {
        if (fillLevel <= 0)
        {
            return;
        }

        ReadOnlySpan<int> matrix =
        [
            0, 8, 2, 10,
            12, 4, 14, 6,
            3, 11, 1, 9,
            15, 7, 13, 5,
        ];

        float cellWidth = width / 4f;
        float cellHeight = height / 4f;
        _spritePaint.Color = color;

        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                int threshold = matrix[(row * 4) + col];
                if (threshold >= fillLevel)
                {
                    continue;
                }

                float px = x + (col * cellWidth);
                float py = y + (row * cellHeight);
                canvas.DrawRect(px, py, cellWidth, cellHeight, _spritePaint);
            }
        }
    }

    private static bool TryGetSpriteCodepoint(ref readonly TerminalCell cell, out int codepoint)
    {
        return TryGetSpriteCodepoint(in cell, out codepoint, out _);
    }

    private static bool TryGetSpriteCodepoint(
        ref readonly TerminalCell cell,
        out int codepoint,
        out SpriteCategory category)
    {
        category = default;
        if (!TryGetCellCodepoint(in cell, out codepoint))
        {
            return false;
        }

        if (TryGetBoxSegments(codepoint, out _))
        {
            category = SpriteCategory.BoxDrawing;
            return true;
        }

        if (codepoint >= 0x2800 && codepoint <= 0x28FF)
        {
            category = SpriteCategory.Braille;
            return true;
        }

        if (TryGetScanLineRatio(codepoint, out _))
        {
            category = SpriteCategory.ScanLine;
            return true;
        }

        if (IsBlockElementCodepoint(codepoint))
        {
            category = SpriteCategory.BlockElement;
            return true;
        }

        return false;
    }

    private static bool IsBlockElementCodepoint(int codepoint)
    {
        if ((codepoint >= 0x2581 && codepoint <= 0x2588) ||
            (codepoint >= 0x2589 && codepoint <= 0x258F))
        {
            return true;
        }

        if (codepoint is 0x2580 or 0x2590 or 0x2591 or 0x2592 or 0x2593 or 0x2594 or 0x2595)
        {
            return true;
        }

        return TryGetQuadrantMask(codepoint, out _);
    }

    private static bool TryGetCellCodepoint(ref readonly TerminalCell cell, out int codepoint)
    {
        codepoint = 0;
        if (!string.IsNullOrEmpty(cell.Grapheme))
        {
            if (Rune.DecodeFromUtf16(cell.Grapheme.AsSpan(), out Rune rune, out int charsConsumed) != OperationStatus.Done ||
                charsConsumed != cell.Grapheme.Length)
            {
                return false;
            }

            codepoint = rune.Value;
            return true;
        }

        if (!Rune.IsValid(cell.Codepoint))
        {
            return false;
        }

        codepoint = cell.Codepoint;
        return true;
    }

    private static bool TryGetScanLineRatio(int codepoint, out float ratio)
    {
        ratio = codepoint switch
        {
            0x23BA => 0.125f,
            0x23BB => 0.375f,
            0x23BC => 0.625f,
            0x23BD => 0.875f,
            _ => 0f,
        };

        return ratio > 0f;
    }

    private static bool TryGetBoxSegments(int codepoint, out BoxSegments segments)
    {
        segments = codepoint switch
        {
            0x2500 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.None),
            0x2501 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None),
            0x2504 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.None),
            0x2505 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None),
            0x2508 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.None),
            0x2509 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None),
            0x254C => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.None),
            0x254D => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None),
            0x2502 => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Light),
            0x2503 => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x2506 => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Light),
            0x2507 => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x250A => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Light),
            0x250B => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x254E => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Light),
            0x254F => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy),

            0x250C => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Light),
            0x250D => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Light),
            0x250E => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Heavy),
            0x250F => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Heavy),
            0x2510 => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.None, StrokeWeight.Light),
            0x2511 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None, StrokeWeight.Light),
            0x2512 => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy),
            0x2513 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy),
            0x2514 => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.None),
            0x2515 => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.None),
            0x2516 => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.None),
            0x2517 => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None),
            0x2518 => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.None),
            0x2519 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.None),
            0x251A => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.None),
            0x251B => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.None),

            0x251C => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Light),
            0x251D => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Light),
            0x251E => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Light),
            0x251F => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Heavy),
            0x2520 => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x2521 => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Light),
            0x2522 => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Heavy),
            0x2523 => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x2524 => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Light),
            0x2525 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Light),
            0x2526 => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Light),
            0x2527 => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Heavy),
            0x2528 => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x2529 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Light),
            0x252A => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Heavy),
            0x252B => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy),

            0x252C => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Light),
            0x252D => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Light),
            0x252E => new BoxSegments(StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Light),
            0x252F => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Light),
            0x2530 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Heavy),
            0x2531 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Heavy),
            0x2532 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Heavy),
            0x2533 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Heavy),
            0x2534 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.None),
            0x2535 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.None),
            0x2536 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.None),
            0x2537 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.None),
            0x2538 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.None),
            0x2539 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.None),
            0x253A => new BoxSegments(StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None),
            0x253B => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None),

            0x253C => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Light),
            0x253D => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Light),
            0x253E => new BoxSegments(StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Light),
            0x253F => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Light),
            0x2540 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Light),
            0x2541 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Heavy),
            0x2542 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x2543 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Light),
            0x2544 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Light),
            0x2545 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Heavy),
            0x2546 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Heavy),
            0x2547 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Light),
            0x2548 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Heavy),
            0x2549 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x254A => new BoxSegments(StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x254B => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Heavy),

            // Unicode box-drawing "double" variants mapped to heavy strokes.
            0x2550 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None),
            0x2551 => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x2552 => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Light),
            0x2553 => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Heavy),
            0x2554 => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Heavy),
            0x2555 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None, StrokeWeight.Light),
            0x2556 => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy),
            0x2557 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy),
            0x2558 => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.None),
            0x2559 => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.None),
            0x255A => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None),
            0x255B => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.None),
            0x255C => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.None),
            0x255D => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.None),
            0x255E => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Light),
            0x255F => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x2560 => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x2561 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Light),
            0x2562 => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x2563 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x2564 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Light),
            0x2565 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.Heavy),
            0x2566 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.Heavy),
            0x2567 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.None),
            0x2568 => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.None),
            0x2569 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.None),
            0x256A => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.Light),
            0x256B => new BoxSegments(StrokeWeight.Light, StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.Heavy),
            0x256C => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Heavy, StrokeWeight.Heavy),

            // Half-connection box drawings.
            0x2574 => new BoxSegments(StrokeWeight.Light, StrokeWeight.None, StrokeWeight.None, StrokeWeight.None),
            0x2575 => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.None),
            0x2576 => new BoxSegments(StrokeWeight.None, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.None),
            0x2577 => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.None, StrokeWeight.Light),
            0x2578 => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None, StrokeWeight.None),
            0x2579 => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.None),
            0x257A => new BoxSegments(StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None),
            0x257B => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy),
            0x257C => new BoxSegments(StrokeWeight.Light, StrokeWeight.Heavy, StrokeWeight.None, StrokeWeight.None),
            0x257D => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Light, StrokeWeight.Heavy),
            0x257E => new BoxSegments(StrokeWeight.Heavy, StrokeWeight.Light, StrokeWeight.None, StrokeWeight.None),
            0x257F => new BoxSegments(StrokeWeight.None, StrokeWeight.None, StrokeWeight.Heavy, StrokeWeight.Light),
            _ => default,
        };

        return segments != default;
    }

    private static bool TryGetQuadrantMask(int codepoint, out QuadrantMask mask)
    {
        mask = codepoint switch
        {
            0x2596 => QuadrantMask.LowerLeft,
            0x2597 => QuadrantMask.LowerRight,
            0x2598 => QuadrantMask.UpperLeft,
            0x2599 => QuadrantMask.UpperLeft | QuadrantMask.LowerLeft | QuadrantMask.LowerRight,
            0x259A => QuadrantMask.UpperLeft | QuadrantMask.LowerRight,
            0x259B => QuadrantMask.UpperLeft | QuadrantMask.UpperRight | QuadrantMask.LowerLeft,
            0x259C => QuadrantMask.UpperLeft | QuadrantMask.UpperRight | QuadrantMask.LowerRight,
            0x259D => QuadrantMask.UpperRight,
            0x259E => QuadrantMask.UpperRight | QuadrantMask.LowerLeft,
            0x259F => QuadrantMask.UpperRight | QuadrantMask.LowerLeft | QuadrantMask.LowerRight,
            _ => QuadrantMask.None,
        };

        return mask != QuadrantMask.None;
    }

    private void DrawShapedTextRun(
        SKCanvas canvas,
        ReadOnlySpan<TerminalCell> cells,
        int startCol,
        int endCol,
        SKTypeface runTypeface,
        SKColor runColor,
        float y)
    {
        int utf16Length = 0;
        for (int col = startCol; col < endCol; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                utf16Length += cell.Grapheme.Length;
                continue;
            }

            Rune rune = new(cell.Codepoint);
            utf16Length += rune.Utf16SequenceLength;
        }

        if (utf16Length <= 0)
        {
            return;
        }

        float runWidth = ComputeRunWidth(cells, startCol, endCol);
        if (runWidth <= 0f)
        {
            return;
        }

        char[] rentedChars = ArrayPool<char>.Shared.Rent(utf16Length);
        try
        {
            Span<char> runChars = rentedChars.AsSpan();
            int charCount = 0;
            ulong textHash = FnvOffsetBasis;

            for (int col = startCol; col < endCol; col++)
            {
                ref readonly TerminalCell cell = ref cells[col];
                Span<char> writeTarget = runChars[charCount..];
                ReadOnlySpan<char> written;
                int charsWritten;

                if (!string.IsNullOrEmpty(cell.Grapheme))
                {
                    written = cell.Grapheme.AsSpan();
                    written.CopyTo(writeTarget);
                    charsWritten = written.Length;
                }
                else
                {
                    Rune rune = new(cell.Codepoint);
                    charsWritten = rune.EncodeToUtf16(writeTarget);
                    written = writeTarget[..charsWritten];
                }

                for (int i = 0; i < written.Length; i++)
                {
                    textHash ^= written[i];
                    textHash *= FnvPrime;
                }

                charCount += charsWritten;
            }

            ReadOnlySpan<char> runText = runChars[..charCount];
            TextShapingOptions options = new(
                _fontSize,
                s_renderCulture,
                _textDirectionMode,
                _enableLigatures);

            ShapedRunCacheKey cacheKey = new(
                textHash,
                charCount,
                runTypeface.Handle,
                BitConverter.SingleToInt32Bits(_fontSize),
                BitConverter.SingleToInt32Bits(_cellWidth),
                BitConverter.SingleToInt32Bits(_cellHeight),
                _textDirectionMode,
                _enableLigatures);

            if (!_shapedRunCache.TryGet(cacheKey, runText, out CachedShapedRun cachedRun))
            {
                ShapedTextRun shaped = _textShaper.Shape(runText, runTypeface, options);
                if (shaped.GlyphCount <= 0)
                {
                    DrawCellAnchoredFallbackRun(
                        canvas,
                        cells,
                        startCol,
                        endCol,
                        runTypeface,
                        runColor,
                        y,
                        runWidth);
                    RecordFallbackRun();
                    return;
                }

                ReadOnlySpan<ShapedGlyph> shapedGlyphs = shaped.Glyphs.Span;
                ushort[] glyphIds = new ushort[shapedGlyphs.Length];
                float[] xOffsets = new float[shapedGlyphs.Length];
                float[] yOffsets = new float[shapedGlyphs.Length];
                float advanceX = 0f;

                for (int i = 0; i < shapedGlyphs.Length; i++)
                {
                    ShapedGlyph glyph = shapedGlyphs[i];
                    glyphIds[i] = unchecked((ushort)glyph.GlyphId);
                    xOffsets[i] = advanceX + glyph.OffsetX;
                    yOffsets[i] = glyph.OffsetY;
                    advanceX += glyph.AdvanceX;
                }

                cachedRun = new CachedShapedRun(
                    new string(runText),
                    glyphIds,
                    xOffsets,
                    yOffsets,
                    advanceX);
                _shapedRunCache.Store(cacheKey, cachedRun);
            }

            GridPlacementMode placement = DetermineGridPlacement(cachedRun, runWidth, out float xScale);

            if (placement == GridPlacementMode.UnsafeFallback)
            {
                DrawCellAnchoredFallbackRun(
                    canvas,
                    cells,
                    startCol,
                    endCol,
                    runTypeface,
                    runColor,
                    y,
                    runWidth);
                RecordFallbackRun();
                return;
            }

            bool clampToRunWidth = placement == GridPlacementMode.Clamped;
            DrawCachedShapedRun(
                canvas,
                cachedRun,
                runTypeface,
                runColor,
                startCol * _cellWidth,
                y,
                y + _baseline,
                runWidth,
                xScale,
                clampToRunWidth);

            RecordShapedRun();
            if (clampToRunWidth)
            {
                RecordGridClampedRun();
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rentedChars);
        }
    }

    private void DrawCachedShapedRun(
        SKCanvas canvas,
        CachedShapedRun run,
        SKTypeface typeface,
        SKColor color,
        float originX,
        float rowY,
        float baselineY,
        float runWidth,
        float xScale,
        bool clampToRunWidth)
    {
        if (run.GlyphCount <= 0)
        {
            return;
        }

        SKPoint[] rentedPoints = ArrayPool<SKPoint>.Shared.Rent(run.GlyphCount);
        try
        {
            Span<SKPoint> points = rentedPoints.AsSpan(0, run.GlyphCount);
            for (int i = 0; i < run.GlyphCount; i++)
            {
                float x = run.XOffsets[i];
                if (xScale != 1f)
                {
                    x *= xScale;
                }

                if (clampToRunWidth)
                {
                    x = Math.Clamp(x, 0f, runWidth);
                }

                points[i] = new SKPoint(x, run.YOffsets[i]);
            }

            using SKFont font = GlyphCache.CreateFont(typeface, _fontSize);
            using SKTextBlobBuilder builder = new();
            builder.AddPositionedRun(run.GlyphIds.AsSpan(), font, points);

            using SKTextBlob? blob = builder.Build();
            if (blob is null)
            {
                return;
            }

            _fgPaint.Color = color;

            // Keep shaped draw inside the run cell span to preserve terminal grid boundaries.
            canvas.Save();
            canvas.ClipRect(
                new SKRect(originX, rowY, originX + runWidth, rowY + _cellHeight),
                SKClipOperation.Intersect,
                antialias: false);
            canvas.DrawText(blob, originX, baselineY, _fgPaint);
            canvas.Restore();
        }
        finally
        {
            ArrayPool<SKPoint>.Shared.Return(rentedPoints);
        }
    }

    private void DrawCellAnchoredFallbackRun(
        SKCanvas canvas,
        ReadOnlySpan<TerminalCell> cells,
        int startCol,
        int endCol,
        SKTypeface typeface,
        SKColor color,
        float y,
        float runWidth)
    {
        using SKFont font = GlyphCache.CreateFont(typeface, _fontSize);
        _fgPaint.Color = color;

        canvas.Save();
        float originX = startCol * _cellWidth;
        canvas.ClipRect(
            new SKRect(originX, y, originX + runWidth, y + _cellHeight),
            SKClipOperation.Intersect,
            antialias: false);
        try
        {
            for (int col = startCol; col < endCol; col++)
            {
                ref readonly TerminalCell cell = ref cells[col];
                if (!IsRenderableGlyphCell(in cell))
                {
                    continue;
                }

                float x = col * _cellWidth;
                string text = string.IsNullOrEmpty(cell.Grapheme)
                    ? char.ConvertFromUtf32(cell.Codepoint)
                    : cell.Grapheme;
                canvas.DrawText(text, x, y + _baseline, font, _fgPaint);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void DrawRunDecorations(
        SKCanvas canvas,
        ReadOnlySpan<TerminalCell> cells,
        int startCol,
        int endCol,
        float y,
        SKColor color)
    {
        _fgPaint.Color = color;

        for (int col = startCol; col < endCol; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (IsCellHidden(in cell) || cell.Width == 0)
            {
                continue;
            }

            TerminalUnderlineStyle underlineStyle = GetEffectiveUnderlineStyle(in cell);
            bool overline = (cell.Decorations & CellDecorations.Overline) != 0;
            bool strikethrough = (cell.Attributes & CellAttributes.Strikethrough) != 0;
            if (underlineStyle == TerminalUnderlineStyle.None && !strikethrough && !overline)
            {
                continue;
            }

            float x = col * _cellWidth;
            float width = _cellWidth * cell.Width;

            if (underlineStyle != TerminalUnderlineStyle.None)
            {
                DrawStyledUnderline(canvas, x, y, width, underlineStyle);
            }

            if (overline)
            {
                float overlineY = y + 1f;
                canvas.DrawLine(x, overlineY, x + width, overlineY, _fgPaint);
            }

            if (strikethrough)
            {
                float stY = y + _cellHeight / 2;
                canvas.DrawLine(x, stY, x + width, stY, _fgPaint);
            }
        }
    }

    private void DrawStyledUnderline(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        TerminalUnderlineStyle style)
    {
        if (width <= 0f)
        {
            return;
        }

        float baseY = y + _cellHeight - 2f;
        float endX = x + width;

        switch (style)
        {
            case TerminalUnderlineStyle.Double:
            {
                float secondY = Math.Max(y + 1f, baseY - 2f);
                canvas.DrawLine(x, baseY, endX, baseY, _fgPaint);
                canvas.DrawLine(x, secondY, endX, secondY, _fgPaint);
                break;
            }

            case TerminalUnderlineStyle.Dotted:
            {
                const float step = 2f;
                for (float px = x; px < endX; px += step)
                {
                    float dotWidth = Math.Min(1f, endX - px);
                    canvas.DrawRect(px, baseY, dotWidth, 1f, _fgPaint);
                }
                break;
            }

            case TerminalUnderlineStyle.Dashed:
            {
                const float dashLength = 3f;
                const float gapLength = 2f;
                for (float px = x; px < endX; px += dashLength + gapLength)
                {
                    float segmentEnd = Math.Min(endX, px + dashLength);
                    canvas.DrawLine(px, baseY, segmentEnd, baseY, _fgPaint);
                }
                break;
            }

            case TerminalUnderlineStyle.Curly:
            {
                float amplitude = Math.Max(1f, MathF.Round(_cellHeight * 0.06f));
                float wavelength = Math.Max(4f, MathF.Round(_cellWidth * 0.5f));
                float halfWave = wavelength * 0.5f;
                bool up = true;
                for (float px = x; px < endX; px += halfWave)
                {
                    float next = Math.Min(endX, px + halfWave);
                    float y0 = baseY + (up ? -amplitude : amplitude);
                    float y1 = baseY + (up ? amplitude : -amplitude);
                    canvas.DrawLine(px, y0, next, y1, _fgPaint);
                    up = !up;
                }
                break;
            }

            case TerminalUnderlineStyle.Single:
            default:
                canvas.DrawLine(x, baseY, endX, baseY, _fgPaint);
                break;
        }
    }

    private SKTypeface ResolveTypefaceForCell(SKTypeface primaryTypeface, ref readonly TerminalCell cell)
    {
        TerminalFontResolution resolution = string.IsNullOrEmpty(cell.Grapheme)
            ? _fontResolver.ResolveTypeface(
                primaryTypeface,
                GetCellPrimaryCodepoint(in cell),
                s_renderCulture)
            : _fontResolver.ResolveTypeface(
                primaryTypeface,
                cell.Grapheme.AsSpan(),
                s_renderCulture);

        if (resolution.UsedFallback)
        {
            RecordFallbackFontHit();
        }

        return resolution.Typeface;
    }

    private float ComputeRunWidth(ReadOnlySpan<TerminalCell> cells, int startCol, int endCol)
    {
        float runWidth = 0f;
        for (int col = startCol; col < endCol; col++)
        {
            int width = cells[col].Width <= 0 ? 1 : cells[col].Width;
            runWidth += width * _cellWidth;
        }

        return runWidth;
    }

    private GridPlacementMode DetermineGridPlacement(CachedShapedRun run, float runWidth, out float xScale)
    {
        xScale = 1f;
        if (runWidth <= 0f || run.TotalAdvanceX <= 0f)
        {
            return GridPlacementMode.UnsafeFallback;
        }

        float scale = runWidth / run.TotalAdvanceX;
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            return GridPlacementMode.UnsafeFallback;
        }

        if (scale < GridScaleFallbackMin || scale > GridScaleFallbackMax)
        {
            return GridPlacementMode.UnsafeFallback;
        }

        float delta = Math.Abs(runWidth - run.TotalAdvanceX);
        float tolerance = Math.Max(GridClampTolerancePx, runWidth * GridClampToleranceRatio);
        if (delta <= tolerance)
        {
            return GridPlacementMode.Natural;
        }

        xScale = scale;
        return GridPlacementMode.Clamped;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsRenderableGlyphCell(ref readonly TerminalCell cell)
    {
        return cell.Width != 0 &&
               cell.HasContent &&
               (HasCellGrapheme(in cell) || Rune.IsValid(cell.Codepoint)) &&
               !IsCellHidden(in cell);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasCellGrapheme(ref readonly TerminalCell cell)
    {
        return !string.IsNullOrEmpty(cell.Grapheme);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetCellPrimaryCodepoint(ref readonly TerminalCell cell)
    {
        if (!string.IsNullOrEmpty(cell.Grapheme) &&
            Rune.DecodeFromUtf16(cell.Grapheme.AsSpan(), out Rune rune, out int charsConsumed) == OperationStatus.Done &&
            charsConsumed > 0)
        {
            return rune.Value;
        }

        return cell.Codepoint;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCellHidden(ref readonly TerminalCell cell)
    {
        return (cell.Attributes & CellAttributes.Hidden) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TerminalUnderlineStyle GetEffectiveUnderlineStyle(ref readonly TerminalCell cell)
    {
        if (cell.UnderlineStyle != TerminalUnderlineStyle.None)
        {
            return cell.UnderlineStyle;
        }

        return (cell.Attributes & CellAttributes.Underline) != 0
            ? TerminalUnderlineStyle.Single
            : TerminalUnderlineStyle.None;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SKColor GetEffectiveForeground(ref readonly TerminalCell cell)
    {
        bool inverse = (cell.Attributes & CellAttributes.Inverse) != 0;
        SKColor color = new(inverse ? cell.Background : cell.Foreground);
        if ((cell.Attributes & CellAttributes.Dim) != 0)
        {
            color = ApplyDim(color);
        }

        return color;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SKColor ApplyDim(SKColor color)
    {
        return new SKColor(
            ScaleChannel(color.Red),
            ScaleChannel(color.Green),
            ScaleChannel(color.Blue),
            ScaleChannel(color.Alpha));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ScaleChannel(byte value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value * DimFactor), 0, byte.MaxValue);
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

    /// <summary>
    /// Gets a snapshot of text-render diagnostics.
    /// </summary>
    /// <param name="reset">When true, counters are reset after snapshot capture.</param>
    public TextRenderDiagnostics GetTextRenderDiagnostics(bool reset = false)
    {
        if (reset)
        {
            return new TextRenderDiagnostics(
                Interlocked.Exchange(ref _diagnosticShapedRuns, 0),
                Interlocked.Exchange(ref _diagnosticFallbackRuns, 0),
                Interlocked.Exchange(ref _diagnosticFallbackFontHits, 0),
                Interlocked.Exchange(ref _diagnosticGridClampedRuns, 0),
                Interlocked.Exchange(ref _diagnosticSpriteCells, 0),
                Interlocked.Exchange(ref _diagnosticBoxDrawingSpriteCells, 0),
                Interlocked.Exchange(ref _diagnosticBrailleSpriteCells, 0),
                Interlocked.Exchange(ref _diagnosticBlockSpriteCells, 0),
                Interlocked.Exchange(ref _diagnosticScanLineSpriteCells, 0));
        }

        TextRenderDiagnostics snapshot = new(
            Volatile.Read(ref _diagnosticShapedRuns),
            Volatile.Read(ref _diagnosticFallbackRuns),
            Volatile.Read(ref _diagnosticFallbackFontHits),
            Volatile.Read(ref _diagnosticGridClampedRuns),
            Volatile.Read(ref _diagnosticSpriteCells),
            Volatile.Read(ref _diagnosticBoxDrawingSpriteCells),
            Volatile.Read(ref _diagnosticBrailleSpriteCells),
            Volatile.Read(ref _diagnosticBlockSpriteCells),
            Volatile.Read(ref _diagnosticScanLineSpriteCells));

        return snapshot;
    }

    /// <summary>
    /// Resets text-render diagnostics counters to zero.
    /// </summary>
    public void ResetTextRenderDiagnostics()
    {
        Interlocked.Exchange(ref _diagnosticShapedRuns, 0);
        Interlocked.Exchange(ref _diagnosticFallbackRuns, 0);
        Interlocked.Exchange(ref _diagnosticFallbackFontHits, 0);
        Interlocked.Exchange(ref _diagnosticGridClampedRuns, 0);
        Interlocked.Exchange(ref _diagnosticSpriteCells, 0);
        Interlocked.Exchange(ref _diagnosticBoxDrawingSpriteCells, 0);
        Interlocked.Exchange(ref _diagnosticBrailleSpriteCells, 0);
        Interlocked.Exchange(ref _diagnosticBlockSpriteCells, 0);
        Interlocked.Exchange(ref _diagnosticScanLineSpriteCells, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _bgPaint.Dispose();
        _fgPaint.Dispose();
        _cursorPaint.Dispose();
        _selectionPaint.Dispose();
        _spritePaint.Dispose();
        _textShaper.Dispose();
        _fontResolver.Dispose();
        _shapedRunCache.Clear();
        _glyphCache.Dispose();
    }

    private static float ComputeBaseline(SKFontMetrics metrics, float cellHeight)
    {
        float textHeight = metrics.Descent - metrics.Ascent;
        float topInset = Math.Max(0f, (cellHeight - textHeight) * 0.5f);
        return topInset - metrics.Ascent;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordShapedRun()
    {
        if (EnableTextRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticShapedRuns);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordFallbackRun()
    {
        if (EnableTextRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticFallbackRuns);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordFallbackFontHit()
    {
        if (EnableTextRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticFallbackFontHits);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordGridClampedRun()
    {
        if (EnableTextRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticGridClampedRuns);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordSpriteCell(SpriteCategory category)
    {
        if (!EnableTextRenderDiagnostics)
        {
            return;
        }

        Interlocked.Increment(ref _diagnosticSpriteCells);
        switch (category)
        {
            case SpriteCategory.BoxDrawing:
                Interlocked.Increment(ref _diagnosticBoxDrawingSpriteCells);
                break;
            case SpriteCategory.Braille:
                Interlocked.Increment(ref _diagnosticBrailleSpriteCells);
                break;
            case SpriteCategory.BlockElement:
                Interlocked.Increment(ref _diagnosticBlockSpriteCells);
                break;
            case SpriteCategory.ScanLine:
                Interlocked.Increment(ref _diagnosticScanLineSpriteCells);
                break;
        }
    }

    private enum GridPlacementMode
    {
        Natural,
        Clamped,
        UnsafeFallback,
    }

    private enum StrokeWeight : byte
    {
        None = 0,
        Light = 1,
        Heavy = 2,
    }

    [Flags]
    private enum QuadrantMask : byte
    {
        None = 0,
        UpperLeft = 1 << 0,
        UpperRight = 1 << 1,
        LowerLeft = 1 << 2,
        LowerRight = 1 << 3,
    }

    private enum SpriteCategory : byte
    {
        BoxDrawing = 0,
        Braille = 1,
        BlockElement = 2,
        ScanLine = 3,
    }

    private readonly record struct BoxSegments(
        StrokeWeight Left,
        StrokeWeight Right,
        StrokeWeight Up,
        StrokeWeight Down);
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
