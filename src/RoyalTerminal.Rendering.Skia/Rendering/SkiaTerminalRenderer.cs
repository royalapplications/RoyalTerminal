// Licensed under the MIT License.
// GhosttySharp.Avalonia - SkiaSharp-based terminal renderer.

using SkiaSharp;
using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

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

            bool underline = (cell.Attributes & CellAttributes.Underline) != 0;
            bool strikethrough = (cell.Attributes & CellAttributes.Strikethrough) != 0;
            if (!underline && !strikethrough)
            {
                continue;
            }

            float x = col * _cellWidth;
            float width = _cellWidth * cell.Width;

            if (underline)
            {
                float ulY = y + _cellHeight - 2;
                canvas.DrawLine(x, ulY, x + width, ulY, _fgPaint);
            }

            if (strikethrough)
            {
                float stY = y + _cellHeight / 2;
                canvas.DrawLine(x, stY, x + width, stY, _fgPaint);
            }
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
                Interlocked.Exchange(ref _diagnosticGridClampedRuns, 0));
        }

        TextRenderDiagnostics snapshot = new(
            Volatile.Read(ref _diagnosticShapedRuns),
            Volatile.Read(ref _diagnosticFallbackRuns),
            Volatile.Read(ref _diagnosticFallbackFontHits),
            Volatile.Read(ref _diagnosticGridClampedRuns));

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
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _bgPaint.Dispose();
        _fgPaint.Dispose();
        _cursorPaint.Dispose();
        _selectionPaint.Dispose();
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

    private enum GridPlacementMode
    {
        Natural,
        Clamped,
        UnsafeFallback,
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
