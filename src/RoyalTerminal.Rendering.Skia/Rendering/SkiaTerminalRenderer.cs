// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - SkiaSharp-based terminal renderer.

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using RoyalTerminal.Terminal;
using SkiaSharp;
#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
using Pretext;
using Pretext.SkiaSharp;
#endif

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
    private const float SymbolGlyphClipPaddingCells = 0.5f;
    private const float DefaultBackgroundOpacity = 0.82f;
    private const long DefaultImageBitmapCacheBudgetBytes = 256L * 1024L * 1024L;
    private const int MaxTextHighlightRowCacheEntries = 32_768;
    private const int InitialTextHighlightBufferCapacity = 256;
    private const int MaxSimpleTextRowBatchGroups = 16;
    private const int MaxCodepointTextCacheEntries = 4096;
    private const int MaxStackallocTextRunChars = 256;
    private const int MaxStackallocGlyphPoints = 256;
    private static readonly TimeSpan s_textHighlightNonBacktrackingRegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan s_textHighlightBacktrackingRegexTimeout = TimeSpan.FromMilliseconds(10);
    private static readonly CultureInfo s_renderCulture = CultureInfo.InvariantCulture;

    private readonly GlyphCache _glyphCache;
    private readonly HarfBuzzTextShaper _textShaper;
    private readonly TerminalFontResolver _fontResolver;
    private readonly ShapedRunCache _shapedRunCache;
#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
    private readonly PretextRunCache _pretextRunCache;
#endif
    private readonly SingleGlyphIdCache _singleGlyphIdCache;
    private readonly TextRowFontCache _textRowFontCache;
    private readonly CellTextBlobCache _cellTextBlobCache;
    private readonly Dictionary<int, string> _codepointTextCache = new();
    private readonly SimpleTextRowBatchGroup[] _simpleTextRowBatchGroups = new SimpleTextRowBatchGroup[MaxSimpleTextRowBatchGroups];
    private ushort[] _simpleTextRowCellGlyphIds = Array.Empty<ushort>();
    private ushort[] _simpleTextRowBatchGlyphIds = Array.Empty<ushort>();
    private byte[] _simpleTextRowGroupIndexes = Array.Empty<byte>();
    private SKPoint[] _simpleTextRowGlyphPositions = Array.Empty<SKPoint>();
    private readonly int[] _simpleTextRowGroupOffsets = new int[MaxSimpleTextRowBatchGroups];
    private readonly int[] _simpleTextRowGroupWriteIndexes = new int[MaxSimpleTextRowBatchGroups];
    private readonly Dictionary<TerminalBitmapCacheKey, TerminalBitmapCacheEntry> _kittyBitmapCache = new();
    private readonly Dictionary<TerminalBitmapCacheKey, TerminalBitmapCacheEntry> _rasterBitmapCache = new();
    private const float CellMetricEpsilon = 0.01f;
    private long _imageBitmapCacheBudgetBytes = DefaultImageBitmapCacheBudgetBytes;
    private long _kittyBitmapCacheBytes;
    private long _rasterBitmapCacheBytes;
    private long _imageRenderFrameId;
    private float _cellWidth;
    private float _cellHeight;
    private float _measuredCellWidth;
    private float _fontSize;
    private float _baseline;
    private TextDirectionMode _textDirectionMode = TextDirectionMode.Auto;
    private TerminalTextRenderPipeline _textRenderPipeline = TerminalTextRenderPipeline.HarfBuzz;
    private bool _enableLigatures;
    private bool _disposed;
    private long _diagnosticShapedRuns;
    private long _diagnosticFallbackRuns;
    private long _diagnosticFallbackFontHits;
    private long _diagnosticGridClampedRuns;
    private long _diagnosticPretextRuns;
    private long _diagnosticPretextFallbackRuns;
    private long _diagnosticSpriteCells;
    private long _diagnosticBoxDrawingSpriteCells;
    private long _diagnosticBrailleSpriteCells;
    private long _diagnosticBlockSpriteCells;
    private long _diagnosticScanLineSpriteCells;
    private long _diagnosticImagePlacementsVisited;
    private long _diagnosticImagePlacementsVisible;
    private long _diagnosticImageDraws;
    private long _diagnosticImageCacheHits;
    private long _diagnosticImageCacheMisses;
    private long _diagnosticImageCacheEvictions;
    private TerminalHighlightSpan[] _highlightSpans = Array.Empty<TerminalHighlightSpan>();
    private TerminalTextHighlightRule[] _textHighlightRules = Array.Empty<TerminalTextHighlightRule>();
    private CompiledTextHighlightRule[] _compiledTextHighlightRules = Array.Empty<CompiledTextHighlightRule>();
    private readonly Dictionary<TerminalRow, TextHighlightRowCacheEntry> _textHighlightRowCache = new(ReferenceEqualityComparer.Instance);
    private char[] _textHighlightRowText = Array.Empty<char>();
    private int[] _textHighlightColumnMap = Array.Empty<int>();
    private int _textHighlightRuleRevision;
    private TerminalTextHighlightingMode _textHighlightingMode = TerminalTextHighlightingMode.Static;

    // Reusable paint objects to avoid per-frame allocation
    private readonly SKPaint _bgPaint;
    private readonly SKPaint _fgPaint;
    private readonly SKPaint _cursorPaint;
    private readonly SKPaint _spritePaint;
    private readonly SKPaint _symbolPaint;
    private readonly SKPath _spritePath;

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

    /// <summary>Search-match highlight color.</summary>
    public SKColor SearchHighlightColor { get; set; } = new(0xA0, 0x90, 0x20, 0x90);

    /// <summary>Search-selected highlight color.</summary>
    public SKColor SearchSelectedHighlightColor { get; set; } = new(0xD0, 0x80, 0x20, 0xB0);

    /// <summary>Optional hyperlink-hover underline color override.</summary>
    public SKColor HyperlinkHoverUnderlineColor { get; set; } = SKColors.Empty;

    /// <summary>
    /// Whether Ghostty-style background-opacity heuristics are enabled.
    /// </summary>
    public bool EnableBackgroundOpacityHeuristics { get; set; } = true;

    /// <summary>
    /// Whether background opacity mode is currently enabled.
    /// </summary>
    public bool BackgroundOpacityEnabled { get; set; }

    /// <summary>
    /// Whether opacity applies to explicitly colored cells.
    /// </summary>
    public bool BackgroundOpacityCells { get; set; }

    /// <summary>
    /// Background opacity factor used when background-opacity mode is enabled.
    /// </summary>
    public float BackgroundOpacity
    {
        get => _backgroundOpacity;
        set => _backgroundOpacity = Math.Clamp(value, 0f, 1f);
    }

    private float _backgroundOpacity = DefaultBackgroundOpacity;

    /// <summary>The glyph cache used by this renderer.</summary>
    public GlyphCache GlyphCache => _glyphCache;

    /// <summary>
    /// Gets the configured text highlight rule snapshots.
    /// </summary>
    public IReadOnlyList<TerminalTextHighlightRule> TextHighlightRules => _textHighlightRules;

    /// <summary>
    /// Gets or sets regex-based text highlighting evaluation mode.
    /// </summary>
    public TerminalTextHighlightingMode TextHighlightingMode
    {
        get => _textHighlightingMode;
        set
        {
            TerminalTextHighlightingMode next = NormalizeTextHighlightingMode(value);
            if (_textHighlightingMode == next)
            {
                return;
            }

            _textHighlightingMode = next;
            _textHighlightRowCache.Clear();
        }
    }

    /// <summary>
    /// Enables collection of text-render diagnostics counters.
    /// Disabled by default.
    /// </summary>
    public bool EnableTextRenderDiagnostics { get; set; }

    /// <summary>
    /// Enables collection of terminal image-render diagnostics counters.
    /// Disabled by default.
    /// </summary>
    public bool EnableImageRenderDiagnostics { get; set; }

    /// <summary>
    /// Maximum retained Skia bitmap cache bytes per terminal image protocol.
    /// Visible images are protected from eviction during the frame that draws them.
    /// </summary>
    public long ImageBitmapCacheBudgetBytes
    {
        get => _imageBitmapCacheBudgetBytes;
        set => _imageBitmapCacheBudgetBytes = Math.Max(0, value);
    }

    /// <summary>
    /// Enables or disables HarfBuzz shaping for terminal text rendering.
    /// When disabled, renderer falls back to cell-anchored text drawing.
    /// </summary>
    public bool EnableTextShaping { get; set; } = true;

    /// <summary>
    /// Gets whether the optional Pretext text render pipeline is compiled into this build.
    /// </summary>
    public bool IsPretextTextRenderPipelineAvailable
    {
        get
        {
#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
            return PretextPipelineInitializer.IsAvailable;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Gets or sets the text render pipeline used when text shaping is enabled.
    /// </summary>
    public TerminalTextRenderPipeline TextRenderPipeline
    {
        get => _textRenderPipeline;
        set
        {
            if (_textRenderPipeline == value)
            {
                return;
            }

            _textRenderPipeline = value;
            ClearTextRenderCaches();
        }
    }

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
            ClearTextRenderCaches();
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
            ClearTextRenderCaches();
        }
    }

    public SkiaTerminalRenderer(
        string fontFamily = "Consolas",
        float fontSize = 14f,
        TerminalFontSource fontSource = TerminalFontSource.System,
        string? fontFilePath = null)
    {
        _fontSize = fontSize;
        _glyphCache = new GlyphCache(fontFamily, fontSource, fontFilePath);
        _textShaper = new HarfBuzzTextShaper();
        _fontResolver = new TerminalFontResolver();
        _shapedRunCache = new ShapedRunCache();
#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
        _pretextRunCache = new PretextRunCache();
#endif
        _singleGlyphIdCache = new SingleGlyphIdCache();
        _textRowFontCache = new TextRowFontCache();
        _cellTextBlobCache = new CellTextBlobCache();

        var (w, h) = _glyphCache.MeasureCellSize(fontSize);
        _measuredCellWidth = w;
        _cellWidth = w;
        _cellHeight = h;

        using var font = _glyphCache.CreateFont(fontSize);
        _baseline = -font.Metrics.Ascent;

        _bgPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        _fgPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _cursorPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        _spritePaint = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
        };
        _symbolPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        _spritePath = new SKPath();
    }

    /// <summary>
    /// Updates the font size and recalculates cell dimensions.
    /// </summary>
    public void SetFontSize(float fontSize)
    {
        _fontSize = fontSize;
        var (w, h) = _glyphCache.MeasureCellSize(fontSize);
        _measuredCellWidth = w;
        _cellWidth = w;
        _cellHeight = h;

        using var font = _glyphCache.CreateFont(fontSize);
        _baseline = ComputeBaseline(font.Metrics, _cellHeight);

        _glyphCache.Clear();
        ClearTextRenderCaches();
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
        ClearTextRenderCaches();
    }

    private void ClearTextRenderCaches()
    {
        _shapedRunCache.Clear();
        _singleGlyphIdCache.Clear();
        _cellTextBlobCache.Clear();
        _textRowFontCache.Clear();
        _codepointTextCache.Clear();
#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
        _pretextRunCache.Clear();
#endif
    }

    /// <summary>
    /// Replaces renderer highlight spans used for search/link overlay rendering.
    /// </summary>
    public void SetHighlightSpans(ReadOnlySpan<TerminalHighlightSpan> spans)
    {
        if (spans.IsEmpty)
        {
            _highlightSpans = Array.Empty<TerminalHighlightSpan>();
            return;
        }

        TerminalHighlightSpan[] copy = new TerminalHighlightSpan[spans.Length];
        spans.CopyTo(copy);
        _highlightSpans = copy;
    }

    /// <summary>
    /// Replaces regex-based text highlight rules used for foreground/background overrides.
    /// Invalid regular expressions are ignored so rendering cannot fail because of a user-authored rule.
    /// </summary>
    public void SetTextHighlightRules(IReadOnlyList<TerminalTextHighlightRule>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            ClearTextHighlightRules();
            return;
        }

        List<TerminalTextHighlightRule> copy = new(rules.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            TerminalTextHighlightRule? rule = rules[i];
            if (rule is not null)
            {
                copy.Add(rule);
            }
        }

        if (copy.Count == 0)
        {
            ClearTextHighlightRules();
            return;
        }

        TerminalTextHighlightRule[] nextRules = copy.ToArray();
        if (AreTextHighlightRulesEqual(_textHighlightRules, nextRules))
        {
            return;
        }

        _textHighlightRules = nextRules;
        _compiledTextHighlightRules = CompileTextHighlightRules(_textHighlightRules);
        unchecked
        {
            _textHighlightRuleRevision++;
        }

        _textHighlightRowCache.Clear();
    }

    private void ClearTextHighlightRules()
    {
        if (_textHighlightRules.Length == 0 && _compiledTextHighlightRules.Length == 0)
        {
            return;
        }

        _textHighlightRules = Array.Empty<TerminalTextHighlightRule>();
        _compiledTextHighlightRules = Array.Empty<CompiledTextHighlightRule>();
        unchecked
        {
            _textHighlightRuleRevision++;
        }

        _textHighlightRowCache.Clear();
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
        ReadOnlySpan<TerminalHighlightSpan> highlights = _highlightSpans;
        ReadOnlySpan<CompiledTextHighlightRule> textHighlightRules = _compiledTextHighlightRules;
        TerminalTextHighlightingMode textHighlightingMode = _textHighlightingMode;
        bool textHighlightingEnabled = textHighlightingMode != TerminalTextHighlightingMode.Disabled &&
            !textHighlightRules.IsEmpty;
        ReadOnlySpan<TerminalKittyImagePlacement> kittyPlacements = screen.GetKittyPlacements();
        ReadOnlySpan<TerminalRasterImagePlacement> rasterPlacements = screen.GetRasterImagePlacements();
        long imageFrameId = unchecked(++_imageRenderFrameId);
        int overlayCapacity = Math.Max(1, screen.Columns);
        bool useFullRowBuffers = TryGetPooledRowBufferCellCount(
            screen.ViewportRows,
            overlayCapacity,
            out int rowBufferCellCount);
        CellOverlayFlags[] overlayBuffer = ArrayPool<CellOverlayFlags>.Shared.Rent(rowBufferCellCount);
        CellTextHighlightOverride[]? textHighlightBuffer = textHighlightingEnabled
            ? ArrayPool<CellTextHighlightOverride>.Shared.Rent(rowBufferCellCount)
            : null;

        try
        {
            RenderRasterLayer(canvas, screen, rasterPlacements, TerminalRasterImageLayer.BelowBackground, imageFrameId);
            RenderKittyLayer(canvas, screen, kittyPlacements, TerminalKittyImageLayer.BelowBackground, imageFrameId);

            // Render backgrounds first (batched)
            for (var row = 0; row < screen.ViewportRows; row++)
            {
                var terminalRow = screen.GetViewportRow(row);
                if (!forceFullRedraw && !terminalRow.IsDirty) continue;

                var y = row * _cellHeight;
                Span<CellOverlayFlags> rowOverlays = GetRowOverlayFlags(
                    overlayBuffer,
                    useFullRowBuffers ? row : 0,
                    terminalRow.Columns,
                    overlayCapacity);
                rowOverlays.Clear();
                PopulateRowOverlayFlags(
                    row,
                    terminalRow.Columns,
                    highlights,
                    rowOverlays);
                Span<CellTextHighlightOverride> rowTextHighlights = GetRowTextHighlightOverrides(
                    textHighlightBuffer,
                    useFullRowBuffers ? row : 0,
                    terminalRow.Columns,
                    overlayCapacity);
                rowTextHighlights.Clear();
                PopulateRowTextHighlightOverrides(
                    terminalRow,
                    screen.Theme.DefaultBackground,
                    textHighlightRules,
                    textHighlightingMode,
                    rowTextHighlights);

                RenderRowBackground(canvas, terminalRow, y, rowOverlays, rowTextHighlights);
            }

            RenderKittyLayer(canvas, screen, kittyPlacements, TerminalKittyImageLayer.BelowText, imageFrameId);
            RenderRasterLayer(canvas, screen, rasterPlacements, TerminalRasterImageLayer.BelowText, imageFrameId);

            // Render text on top of the cell layer.
            for (var row = 0; row < screen.ViewportRows; row++)
            {
                var terminalRow = screen.GetViewportRow(row);
                if (!forceFullRedraw && !terminalRow.IsDirty) continue;

                var y = row * _cellHeight;
                Span<CellOverlayFlags> rowOverlays = GetRowOverlayFlags(
                    overlayBuffer,
                    useFullRowBuffers ? row : 0,
                    terminalRow.Columns,
                    overlayCapacity);
                if (!useFullRowBuffers)
                {
                    rowOverlays.Clear();
                    PopulateRowOverlayFlags(
                        row,
                        terminalRow.Columns,
                        highlights,
                        rowOverlays);
                }

                Span<CellTextHighlightOverride> rowTextHighlights = GetRowTextHighlightOverrides(
                    textHighlightBuffer,
                    useFullRowBuffers ? row : 0,
                    terminalRow.Columns,
                    overlayCapacity);
                if (!useFullRowBuffers)
                {
                    rowTextHighlights.Clear();
                    PopulateRowTextHighlightOverrides(
                        terminalRow,
                        screen.Theme.DefaultBackground,
                        textHighlightRules,
                        textHighlightingMode,
                        rowTextHighlights);
                }

                RenderRowText(canvas, terminalRow, y, row, rowOverlays, rowTextHighlights);
                terminalRow.IsDirty = false;
            }
        }
        finally
        {
            ArrayPool<CellOverlayFlags>.Shared.Return(
                overlayBuffer,
                clearArray: false);
            if (textHighlightBuffer is not null)
            {
                ArrayPool<CellTextHighlightOverride>.Shared.Return(
                    textHighlightBuffer,
                    clearArray: false);
            }
        }

        RenderKittyLayer(canvas, screen, kittyPlacements, TerminalKittyImageLayer.AboveText, imageFrameId);
        RenderRasterLayer(canvas, screen, rasterPlacements, TerminalRasterImageLayer.AboveText, imageFrameId);
        TrimBitmapCache(_kittyBitmapCache, ref _kittyBitmapCacheBytes, imageFrameId);
        TrimBitmapCache(_rasterBitmapCache, ref _rasterBitmapCacheBytes, imageFrameId);

        // Render cursor
        if (CursorVisible)
            RenderCursor(canvas, screen);

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
    private void RenderRowBackground(
        SKCanvas canvas,
        TerminalRow row,
        float y,
        ReadOnlySpan<CellOverlayFlags> rowOverlays,
        ReadOnlySpan<CellTextHighlightOverride> rowTextHighlights)
    {
        var cells = row.ReadOnlyCells;
        if (cells.IsEmpty) return;

        var batchStart = 0;
        var batchColor = ResolveBackgroundColorForCell(
            in cells[0],
            rowOverlays[0],
            GetTextHighlightOverride(rowTextHighlights, 0));

        for (var col = 1; col <= cells.Length; col++)
        {
            bool flush = col == cells.Length;
            uint currentColor = flush
                ? 0
                : ResolveBackgroundColorForCell(
                    in cells[col],
                    rowOverlays[col],
                    GetTextHighlightOverride(rowTextHighlights, col));

            if (flush || currentColor != batchColor)
            {
                // Flush batch
                if ((batchColor >> 24) != 0)
                {
                    _bgPaint.Color = new SKColor(batchColor);
                    var x = batchStart * _cellWidth;
                    var width = (col - batchStart) * _cellWidth;
                    canvas.DrawRect(x, y, width, _cellHeight, _bgPaint);
                }

                if (!flush)
                {
                    batchStart = col;
                    batchColor = currentColor;
                }
            }
        }
    }

    private void RenderRowText(
        SKCanvas canvas,
        TerminalRow row,
        float y,
        int rowIndex,
        ReadOnlySpan<CellOverlayFlags> rowOverlays,
        ReadOnlySpan<CellTextHighlightOverride> rowTextHighlights)
    {
        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        if (cells.IsEmpty)
        {
            return;
        }

        bool splitRunsAroundCursor = CursorVisible && rowIndex == CursorRow;
        int cursorSplitColumn = CursorColumn;
#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
        bool usePretextPipeline = EnableTextShaping &&
            _textRenderPipeline == TerminalTextRenderPipeline.Pretext &&
            PretextPipelineInitializer.TryEnsureInitialized();
        if (usePretextPipeline &&
            !_enableLigatures &&
            _textDirectionMode != TextDirectionMode.RightToLeft &&
            Math.Abs(_cellWidth - _measuredCellWidth) < CellMetricEpsilon &&
            TryDrawSimpleTextRowBatch(
                canvas,
                cells,
                rowOverlays,
                rowTextHighlights,
                y,
                requireAscii: false,
                requirePretextSafe: true,
                recordPretextRuns: true))
        {
            return;
        }
#endif
        if (CanUseSimpleHarfBuzzTextRowBatch() &&
            TryDrawSimpleTextRowBatch(
                canvas,
                cells,
                rowOverlays,
                rowTextHighlights,
                y,
                requireAscii: true,
                requirePretextSafe: false,
                recordPretextRuns: false))
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
                SKColor spriteColor = ResolveForegroundColorForCell(
                    in firstCell,
                    GetTextHighlightOverride(rowTextHighlights, col));
                int spriteRunEnd = col + 1;
                while (spriteRunEnd < cells.Length)
                {
                    if (splitRunsAroundCursor && ShouldSplitRunAroundCursor(col, spriteRunEnd, cursorSplitColumn))
                    {
                        break;
                    }

                    ref readonly TerminalCell spriteCandidate = ref cells[spriteRunEnd];
                    if (!IsRenderableGlyphCell(in spriteCandidate))
                    {
                        break;
                    }

                    if (!TryGetSpriteCodepoint(in spriteCandidate, out _))
                    {
                        break;
                    }

                    SKColor nextColor = ResolveForegroundColorForCell(
                        in spriteCandidate,
                        GetTextHighlightOverride(rowTextHighlights, spriteRunEnd));
                    if (nextColor != spriteColor)
                    {
                        break;
                    }

                    spriteRunEnd++;
                }

                DrawSpriteRun(canvas, cells, col, spriteRunEnd, y, spriteColor);
                DrawRunDecorations(
                    canvas,
                    cells,
                    rowOverlays,
                    col,
                    spriteRunEnd,
                    y,
                    spriteColor);
                col = spriteRunEnd;
                continue;
            }

            bool bold = (firstCell.Attributes & CellAttributes.Bold) != 0;
            bool italic = (firstCell.Attributes & CellAttributes.Italic) != 0;
            SKTypeface primaryTypeface = _glyphCache.GetTypeface(bold, italic);
            SKColor runColor = ResolveForegroundColorForCell(
                in firstCell,
                GetTextHighlightOverride(rowTextHighlights, col));
            SKTypeface runTypeface = ResolveTypefaceForCell(primaryTypeface, in firstCell);
            bool firstIsSymbolGlyph = IsSymbolGlyphClipCandidate(in firstCell);

            int runEnd = col + 1;
            while (runEnd < cells.Length)
            {
                if (firstIsSymbolGlyph)
                {
                    break;
                }

                if (splitRunsAroundCursor && ShouldSplitRunAroundCursor(col, runEnd, cursorSplitColumn))
                {
                    break;
                }

                ref readonly TerminalCell nextCell = ref cells[runEnd];
                if (!IsRenderableGlyphCell(in nextCell))
                {
                    break;
                }

                if (TryGetSpriteCodepoint(in nextCell, out _))
                {
                    break;
                }

                if (IsSymbolGlyphClipCandidate(in nextCell))
                {
                    break;
                }

                bool nextBold = (nextCell.Attributes & CellAttributes.Bold) != 0;
                bool nextItalic = (nextCell.Attributes & CellAttributes.Italic) != 0;
                if (nextBold != bold || nextItalic != italic)
                {
                    break;
                }

                SKColor nextColor = ResolveForegroundColorForCell(
                    in nextCell,
                    GetTextHighlightOverride(rowTextHighlights, runEnd));
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
#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
                if (!usePretextPipeline ||
                    !TryDrawPretextTextRun(canvas, cells, col, runEnd, runTypeface, runColor, y))
                {
                    DrawShapedTextRun(canvas, cells, col, runEnd, runTypeface, runColor, y);
                }
#else
                DrawShapedTextRun(canvas, cells, col, runEnd, runTypeface, runColor, y);
#endif
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

            DrawRunDecorations(
                canvas,
                cells,
                rowOverlays,
                col,
                runEnd,
                y,
                runColor);
            col = runEnd;
        }
    }

    private bool CanUseSimpleHarfBuzzTextRowBatch()
    {
        return EnableTextShaping &&
               !EnableTextRenderDiagnostics &&
               _textRenderPipeline == TerminalTextRenderPipeline.HarfBuzz &&
               !_enableLigatures &&
               _textDirectionMode != TextDirectionMode.RightToLeft &&
               Math.Abs(_cellWidth - _measuredCellWidth) < CellMetricEpsilon;
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
                    if (TryDrawSpecialBoxDrawingSymbol(canvas, x, y, spriteWidth, spriteHeight, codepoint, color))
                    {
                        break;
                    }

                    if (TryGetBoxSegments(codepoint, out BoxSegments segments))
                    {
                        DrawBoxSegments(
                            canvas,
                            x,
                            y,
                            spriteWidth,
                            spriteHeight,
                            segments,
                            color,
                            heavyMeansDouble: IsDoubleBoxDrawingCodepoint(codepoint));
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

                case SpriteCategory.Symbol:
                    DrawGeometricSymbol(canvas, codepoint, x, y, spriteWidth, spriteHeight, color);
                    break;
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void DrawGeometricSymbol(
        SKCanvas canvas,
        int codepoint,
        float x,
        float y,
        float width,
        float height,
        SKColor color)
    {
        _symbolPaint.Color = color;
        float diameter = MathF.Max(1f, MathF.Min(width, height) * 0.74f);
        float radius = diameter * 0.5f;
        float centerX = x + (width * 0.5f);
        float centerY = y + (height * 0.5f);

        switch (codepoint)
        {
            case 0x25A0: // Black square.
                DrawCenteredSquare(canvas, centerX, centerY, MathF.Min(width, height) * 0.54f, fill: true);
                break;

            case 0x25CB: // White circle.
            case 0x25EF: // Large circle.
                _symbolPaint.Style = SKPaintStyle.Stroke;
                _symbolPaint.StrokeWidth = MathF.Max(1f, MathF.Round(MathF.Min(width, height) * 0.08f));
                canvas.DrawCircle(centerX, centerY, radius, _symbolPaint);
                break;

            case 0x25CF: // Black circle.
            case 0x2B24: // Black large circle.
                _symbolPaint.Style = SKPaintStyle.Fill;
                canvas.DrawCircle(centerX, centerY, radius, _symbolPaint);
                break;

            case 0x2610: // Ballot box.
                DrawCenteredSquare(canvas, centerX, centerY, MathF.Min(width, height) * 0.58f, fill: false);
                break;

            case 0x2611: // Ballot box with check.
            case 0x1F5F9: // Ballot box with bold check.
                {
                    float size = MathF.Min(width, height) * 0.58f;
                    DrawCenteredSquare(canvas, centerX, centerY, size, fill: false);
                    DrawCheckMark(canvas, centerX, centerY, size);
                    break;
                }

            case 0x2612: // Ballot box with x.
                {
                    float size = MathF.Min(width, height) * 0.58f;
                    DrawCenteredSquare(canvas, centerX, centerY, size, fill: false);
                    DrawCrossMark(canvas, centerX, centerY, size);
                    break;
                }

            case 0x1F7D7: // Circled square.
                DrawCenteredSquare(canvas, centerX, centerY, MathF.Min(width, height) * 0.38f, fill: true);
                _symbolPaint.Style = SKPaintStyle.Stroke;
                _symbolPaint.StrokeWidth = MathF.Max(1f, MathF.Round(MathF.Min(width, height) * 0.08f));
                canvas.DrawCircle(centerX, centerY, radius, _symbolPaint);
                break;

            case 0x1F834: // Leftwards finger-post arrow.
                DrawFingerPostArrow(canvas, centerX, centerY, MathF.Min(width, height) * 0.70f, horizontal: true);
                break;

            case 0x1F837: // Downwards finger-post arrow.
                DrawFingerPostArrow(canvas, centerX, centerY, MathF.Min(width, height) * 0.70f, horizontal: false);
                break;
        }
    }

    private void DrawCenteredSquare(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float size,
        bool fill)
    {
        float half = MathF.Max(1f, size) * 0.5f;
        _symbolPaint.Style = fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke;
        _symbolPaint.StrokeWidth = MathF.Max(1f, MathF.Round(size * 0.12f));
        _symbolPaint.StrokeCap = SKStrokeCap.Square;
        canvas.DrawRect(
            centerX - half,
            centerY - half,
            half * 2f,
            half * 2f,
            _symbolPaint);
    }

    private void DrawCheckMark(SKCanvas canvas, float centerX, float centerY, float size)
    {
        float half = MathF.Max(1f, size) * 0.5f;
        float left = centerX - (half * 0.58f);
        float midX = centerX - (half * 0.12f);
        float right = centerX + (half * 0.62f);
        float lower = centerY + (half * 0.14f);
        float bottom = centerY + (half * 0.48f);
        float upper = centerY - (half * 0.50f);

        _symbolPaint.Style = SKPaintStyle.Stroke;
        _symbolPaint.StrokeWidth = MathF.Max(1f, MathF.Round(size * 0.16f));
        _symbolPaint.StrokeCap = SKStrokeCap.Round;
        canvas.DrawLine(left, lower, midX, bottom, _symbolPaint);
        canvas.DrawLine(midX, bottom, right, upper, _symbolPaint);
    }

    private void DrawCrossMark(SKCanvas canvas, float centerX, float centerY, float size)
    {
        float inset = MathF.Max(1f, size) * 0.28f;

        _symbolPaint.Style = SKPaintStyle.Stroke;
        _symbolPaint.StrokeWidth = MathF.Max(1f, MathF.Round(size * 0.14f));
        _symbolPaint.StrokeCap = SKStrokeCap.Round;
        canvas.DrawLine(centerX - inset, centerY - inset, centerX + inset, centerY + inset, _symbolPaint);
        canvas.DrawLine(centerX + inset, centerY - inset, centerX - inset, centerY + inset, _symbolPaint);
    }

    private void DrawFingerPostArrow(SKCanvas canvas, float centerX, float centerY, float size, bool horizontal)
    {
        float normalizedSize = MathF.Max(1f, size);
        _symbolPaint.Style = SKPaintStyle.Stroke;
        _symbolPaint.StrokeWidth = MathF.Max(1f, MathF.Round(normalizedSize * 0.12f));
        _symbolPaint.StrokeCap = SKStrokeCap.Square;

        if (horizontal)
        {
            float left = centerX - (normalizedSize * 0.34f);
            float right = centerX + (normalizedSize * 0.24f);
            float top = centerY - (normalizedSize * 0.22f);
            float bottom = centerY + (normalizedSize * 0.22f);
            canvas.DrawRect(left, top, right - left, bottom - top, _symbolPaint);

            _spritePath.Reset();
            _spritePath.MoveTo(left, centerY - (normalizedSize * 0.26f));
            _spritePath.LineTo(centerX - (normalizedSize * 0.52f), centerY);
            _spritePath.LineTo(left, centerY + (normalizedSize * 0.26f));
            _spritePath.Close();
        }
        else
        {
            float left = centerX - (normalizedSize * 0.24f);
            float right = centerX + (normalizedSize * 0.24f);
            float top = centerY - (normalizedSize * 0.34f);
            float bottom = centerY + (normalizedSize * 0.24f);
            canvas.DrawRect(left, top, right - left, bottom - top, _symbolPaint);

            _spritePath.Reset();
            _spritePath.MoveTo(centerX - (normalizedSize * 0.26f), bottom);
            _spritePath.LineTo(centerX, centerY + (normalizedSize * 0.52f));
            _spritePath.LineTo(centerX + (normalizedSize * 0.26f), bottom);
            _spritePath.Close();
        }

        _symbolPaint.Style = SKPaintStyle.Fill;
        canvas.DrawPath(_spritePath, _symbolPaint);
    }

    private bool TryDrawSpecialBoxDrawingSymbol(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        int codepoint,
        SKColor color)
    {
        if (TryDrawDashedBoxLine(canvas, x, y, width, height, codepoint, color))
        {
            return true;
        }

        if (TryDrawArcBoxLine(canvas, x, y, width, height, codepoint, color))
        {
            return true;
        }

        return TryDrawDiagonalBoxLine(canvas, x, y, width, height, codepoint, color);
    }

    private bool TryDrawDashedBoxLine(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        int codepoint,
        SKColor color)
    {
        if (!TryGetDashedBoxLineSpec(codepoint, out BoxLineOrientation orientation, out StrokeWeight weight, out int dashCount))
        {
            return false;
        }

        GetStrokeThicknesses(width, height, out float lightThickness, out float heavyThickness);
        float thickness = weight == StrokeWeight.Heavy ? heavyThickness : lightThickness;

        if (orientation == BoxLineOrientation.Horizontal)
        {
            DrawDashedHorizontalLine(canvas, x, y, width, height, dashCount, thickness, color);
        }
        else
        {
            DrawDashedVerticalLine(canvas, x, y, width, height, dashCount, thickness, color);
        }

        return true;
    }

    private void DrawDashedHorizontalLine(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        int dashCount,
        float thickness,
        SKColor color)
    {
        if (dashCount <= 0 || width <= 0f)
        {
            return;
        }

        int totalPixels = Math.Max(1, (int)MathF.Round(width));
        int gapSegments = Math.Max(0, dashCount - 1);
        int gapPixels = gapSegments > 0 ? Math.Max(1, totalPixels / (dashCount * 3)) : 0;
        int dashPixelsRemaining = totalPixels - (gapSegments * gapPixels);
        if (dashPixelsRemaining < dashCount)
        {
            gapPixels = 0;
            dashPixelsRemaining = totalPixels;
        }

        int dashBasePixels = Math.Max(1, dashPixelsRemaining / dashCount);
        int dashExtraPixels = Math.Max(0, dashPixelsRemaining - (dashBasePixels * dashCount));

        float centerY = y + (height * 0.5f);
        float halfThickness = thickness * 0.5f;

        _spritePaint.Color = color;
        int cursorPixels = 0;
        for (int i = 0; i < dashCount; i++)
        {
            if (cursorPixels >= totalPixels)
            {
                break;
            }

            int segmentPixels = dashBasePixels + (i < dashExtraPixels ? 1 : 0);
            if (segmentPixels > 0)
            {
                float segmentX = x + cursorPixels;
                canvas.DrawRect(segmentX, centerY - halfThickness, segmentPixels, thickness, _spritePaint);
            }

            cursorPixels += segmentPixels + gapPixels;
        }
    }

    private void DrawDashedVerticalLine(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        int dashCount,
        float thickness,
        SKColor color)
    {
        if (dashCount <= 0 || height <= 0f)
        {
            return;
        }

        int totalPixels = Math.Max(1, (int)MathF.Round(height));
        int gapSegments = Math.Max(0, dashCount - 1);
        int gapPixels = gapSegments > 0 ? Math.Max(1, totalPixels / (dashCount * 3)) : 0;
        int dashPixelsRemaining = totalPixels - (gapSegments * gapPixels);
        if (dashPixelsRemaining < dashCount)
        {
            gapPixels = 0;
            dashPixelsRemaining = totalPixels;
        }

        int dashBasePixels = Math.Max(1, dashPixelsRemaining / dashCount);
        int dashExtraPixels = Math.Max(0, dashPixelsRemaining - (dashBasePixels * dashCount));

        float centerX = x + (width * 0.5f);
        float halfThickness = thickness * 0.5f;

        _spritePaint.Color = color;
        int cursorPixels = 0;
        for (int i = 0; i < dashCount; i++)
        {
            if (cursorPixels >= totalPixels)
            {
                break;
            }

            int segmentPixels = dashBasePixels + (i < dashExtraPixels ? 1 : 0);
            if (segmentPixels > 0)
            {
                float segmentY = y + cursorPixels;
                canvas.DrawRect(centerX - halfThickness, segmentY, thickness, segmentPixels, _spritePaint);
            }

            cursorPixels += segmentPixels + gapPixels;
        }
    }

    private bool TryDrawArcBoxLine(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        int codepoint,
        SKColor color)
    {
        if (!TryGetArcCorner(codepoint, out ArcCorner corner))
        {
            return false;
        }

        DrawArcBoxLine(canvas, x, y, width, height, corner, color);
        return true;
    }

    private void DrawArcBoxLine(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        ArcCorner corner,
        SKColor color)
    {
        float centerX = x + (width * 0.5f);
        float centerY = y + (height * 0.5f);
        GetStrokeThicknesses(width, height, out float lightThickness, out _);

        SKPoint start;
        SKPoint control;
        SKPoint end;
        switch (corner)
        {
            case ArcCorner.DownRight:
                start = new SKPoint(centerX, y + height);
                control = new SKPoint(x + width, y + height);
                end = new SKPoint(x + width, centerY);
                break;

            case ArcCorner.DownLeft:
                start = new SKPoint(centerX, y + height);
                control = new SKPoint(x, y + height);
                end = new SKPoint(x, centerY);
                break;

            case ArcCorner.UpLeft:
                start = new SKPoint(x, centerY);
                control = new SKPoint(x, y);
                end = new SKPoint(centerX, y);
                break;

            case ArcCorner.UpRight:
            default:
                start = new SKPoint(centerX, y);
                control = new SKPoint(x + width, y);
                end = new SKPoint(x + width, centerY);
                break;
        }

        DrawStrokeQuadratic(canvas, start, control, end, lightThickness, color);
    }

    private bool TryDrawDiagonalBoxLine(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        int codepoint,
        SKColor color)
    {
        if (codepoint is < 0x2571 or > 0x2573)
        {
            return false;
        }

        GetStrokeThicknesses(width, height, out float lightThickness, out _);
        switch (codepoint)
        {
            case 0x2571:
                DrawStrokeLine(canvas, new SKPoint(x, y + height), new SKPoint(x + width, y), lightThickness, color);
                break;
            case 0x2572:
                DrawStrokeLine(canvas, new SKPoint(x, y), new SKPoint(x + width, y + height), lightThickness, color);
                break;
            case 0x2573:
                DrawStrokeLine(canvas, new SKPoint(x, y + height), new SKPoint(x + width, y), lightThickness, color);
                DrawStrokeLine(canvas, new SKPoint(x, y), new SKPoint(x + width, y + height), lightThickness, color);
                break;
        }

        return true;
    }

    private void DrawBoxSegments(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        BoxSegments segments,
        SKColor color,
        bool heavyMeansDouble = false)
    {
        float centerX = x + (width * 0.5f);
        float centerY = y + (height * 0.5f);
        GetStrokeThicknesses(width, height, out float lightThickness, out float heavyThickness);

        float leftThickness = segments.Left == StrokeWeight.Heavy ? heavyThickness : lightThickness;
        float rightThickness = segments.Right == StrokeWeight.Heavy ? heavyThickness : lightThickness;
        float upThickness = segments.Up == StrokeWeight.Heavy ? heavyThickness : lightThickness;
        float downThickness = segments.Down == StrokeWeight.Heavy ? heavyThickness : lightThickness;

        DrawHorizontalSegment(
            canvas,
            x,
            centerX + leftThickness,
            centerY,
            segments.Left,
            color,
            lightThickness,
            heavyThickness,
            heavyMeansDouble);
        DrawHorizontalSegment(
            canvas,
            centerX - rightThickness,
            x + width,
            centerY,
            segments.Right,
            color,
            lightThickness,
            heavyThickness,
            heavyMeansDouble);
        DrawVerticalSegment(
            canvas,
            y,
            centerY + upThickness,
            centerX,
            segments.Up,
            color,
            lightThickness,
            heavyThickness,
            heavyMeansDouble);
        DrawVerticalSegment(
            canvas,
            centerY - downThickness,
            y + height,
            centerX,
            segments.Down,
            color,
            lightThickness,
            heavyThickness,
            heavyMeansDouble);
    }

    private void DrawHorizontalSegment(
        SKCanvas canvas,
        float startX,
        float endX,
        float centerY,
        StrokeWeight weight,
        SKColor color,
        float lightThickness,
        float heavyThickness,
        bool heavyMeansDouble)
    {
        if (weight == StrokeWeight.None || endX <= startX)
        {
            return;
        }

        if (heavyMeansDouble && weight == StrokeWeight.Heavy)
        {
            DrawDoubleHorizontalSegment(canvas, startX, endX, centerY, lightThickness, heavyThickness, color);
            return;
        }

        float thickness = weight == StrokeWeight.Heavy ? heavyThickness : lightThickness;
        _spritePaint.Color = color;
        float halfThickness = thickness * 0.5f;
        canvas.DrawRect(startX, centerY - halfThickness, endX - startX, thickness, _spritePaint);
    }

    private void DrawVerticalSegment(
        SKCanvas canvas,
        float startY,
        float endY,
        float centerX,
        StrokeWeight weight,
        SKColor color,
        float lightThickness,
        float heavyThickness,
        bool heavyMeansDouble)
    {
        if (weight == StrokeWeight.None || endY <= startY)
        {
            return;
        }

        if (heavyMeansDouble && weight == StrokeWeight.Heavy)
        {
            DrawDoubleVerticalSegment(canvas, startY, endY, centerX, lightThickness, heavyThickness, color);
            return;
        }

        float thickness = weight == StrokeWeight.Heavy ? heavyThickness : lightThickness;
        _spritePaint.Color = color;
        float halfThickness = thickness * 0.5f;
        canvas.DrawRect(centerX - halfThickness, startY, thickness, endY - startY, _spritePaint);
    }

    private void DrawDoubleHorizontalSegment(
        SKCanvas canvas,
        float startX,
        float endX,
        float centerY,
        float lightThickness,
        float heavyThickness,
        SKColor color)
    {
        float strokeThickness = MathF.Max(1f, MathF.Floor(lightThickness));
        float pairSpan = MathF.Max((strokeThickness * 2f) + 1f, heavyThickness);
        float top = centerY - (pairSpan * 0.5f);
        float bottom = top + pairSpan - strokeThickness;

        _spritePaint.Color = color;
        canvas.DrawRect(startX, top, endX - startX, strokeThickness, _spritePaint);
        canvas.DrawRect(startX, bottom, endX - startX, strokeThickness, _spritePaint);
    }

    private void DrawDoubleVerticalSegment(
        SKCanvas canvas,
        float startY,
        float endY,
        float centerX,
        float lightThickness,
        float heavyThickness,
        SKColor color)
    {
        float strokeThickness = MathF.Max(1f, MathF.Floor(lightThickness));
        float pairSpan = MathF.Max((strokeThickness * 2f) + 1f, heavyThickness);
        float left = centerX - (pairSpan * 0.5f);
        float right = left + pairSpan - strokeThickness;

        _spritePaint.Color = color;
        canvas.DrawRect(left, startY, strokeThickness, endY - startY, _spritePaint);
        canvas.DrawRect(right, startY, strokeThickness, endY - startY, _spritePaint);
    }

    private void DrawStrokeLine(SKCanvas canvas, SKPoint start, SKPoint end, float thickness, SKColor color)
    {
        SKPaintStyle previousStyle = _spritePaint.Style;
        float previousStrokeWidth = _spritePaint.StrokeWidth;
        bool previousIsAntialias = _spritePaint.IsAntialias;
        SKColor previousColor = _spritePaint.Color;
        try
        {
            _spritePaint.Style = SKPaintStyle.Stroke;
            _spritePaint.StrokeWidth = thickness;
            _spritePaint.IsAntialias = false;
            _spritePaint.Color = color;
            canvas.DrawLine(start, end, _spritePaint);
        }
        finally
        {
            _spritePaint.Style = previousStyle;
            _spritePaint.StrokeWidth = previousStrokeWidth;
            _spritePaint.IsAntialias = previousIsAntialias;
            _spritePaint.Color = previousColor;
        }
    }

    private void DrawStrokeQuadratic(
        SKCanvas canvas,
        SKPoint start,
        SKPoint control,
        SKPoint end,
        float thickness,
        SKColor color)
    {
        SKPaintStyle previousStyle = _spritePaint.Style;
        float previousStrokeWidth = _spritePaint.StrokeWidth;
        bool previousIsAntialias = _spritePaint.IsAntialias;
        SKColor previousColor = _spritePaint.Color;
        try
        {
            _spritePaint.Style = SKPaintStyle.Stroke;
            _spritePaint.StrokeWidth = thickness;
            _spritePaint.IsAntialias = false;
            _spritePaint.Color = color;
            _spritePath.Rewind();
            _spritePath.MoveTo(start);
            _spritePath.QuadTo(control, end);
            canvas.DrawPath(_spritePath, _spritePaint);
        }
        finally
        {
            _spritePaint.Style = previousStyle;
            _spritePaint.StrokeWidth = previousStrokeWidth;
            _spritePaint.IsAntialias = previousIsAntialias;
            _spritePaint.Color = previousColor;
        }
    }

    private static void GetStrokeThicknesses(
        float width,
        float height,
        out float lightThickness,
        out float heavyThickness)
    {
        float minDimension = MathF.Max(1f, MathF.Min(width, height));
        lightThickness = MathF.Max(1f, MathF.Round(minDimension * 0.12f));
        heavyThickness = MathF.Max(lightThickness + 1f, MathF.Round(lightThickness * 1.75f));
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

        if (TryGetBoxSegments(codepoint, out _) || IsSpecialBoxDrawingCodepoint(codepoint))
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

        if (IsGeometricSymbolCodepoint(codepoint))
        {
            category = SpriteCategory.Symbol;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGeometricSymbolCodepoint(int codepoint)
    {
        return codepoint is
            0x25A0 or
            0x25CB or
            0x25CF or
            0x25EF or
            0x2610 or
            0x2611 or
            0x2612 or
            0x2B24 or
            0x1F834 or
            0x1F837 or
            0x1F5F9 or
            0x1F7D7;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDoubleBoxDrawingCodepoint(int codepoint)
    {
        return codepoint >= 0x2550 && codepoint <= 0x256C;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSpecialBoxDrawingCodepoint(int codepoint)
    {
        if (TryGetDashedBoxLineSpec(codepoint, out _, out _, out _))
        {
            return true;
        }

        return codepoint switch
        {
            >= 0x256D and <= 0x2570 => true,
            >= 0x2571 and <= 0x2573 => true,
            _ => false,
        };
    }

    private static bool TryGetCellCodepoint(ref readonly TerminalCell cell, out int codepoint)
    {
        codepoint = 0;
        if (!string.IsNullOrEmpty(cell.Grapheme))
        {
            if (Rune.DecodeFromUtf16(cell.Grapheme.AsSpan(), out Rune rune, out int charsConsumed) != OperationStatus.Done ||
                !ContainsOnlyTextPresentationSelectors(cell.Grapheme.AsSpan(charsConsumed)))
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

    private static bool ContainsOnlyTextPresentationSelectors(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(text, out Rune rune, out int charsConsumed) != OperationStatus.Done ||
                rune.Value != 0xFE0E)
            {
                return false;
            }

            text = text[charsConsumed..];
        }

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

    private static bool TryGetDashedBoxLineSpec(
        int codepoint,
        out BoxLineOrientation orientation,
        out StrokeWeight weight,
        out int dashCount)
    {
        (orientation, weight, dashCount) = codepoint switch
        {
            0x2504 => (BoxLineOrientation.Horizontal, StrokeWeight.Light, 3),
            0x2505 => (BoxLineOrientation.Horizontal, StrokeWeight.Heavy, 3),
            0x2506 => (BoxLineOrientation.Vertical, StrokeWeight.Light, 3),
            0x2507 => (BoxLineOrientation.Vertical, StrokeWeight.Heavy, 3),
            0x2508 => (BoxLineOrientation.Horizontal, StrokeWeight.Light, 4),
            0x2509 => (BoxLineOrientation.Horizontal, StrokeWeight.Heavy, 4),
            0x250A => (BoxLineOrientation.Vertical, StrokeWeight.Light, 4),
            0x250B => (BoxLineOrientation.Vertical, StrokeWeight.Heavy, 4),
            0x254C => (BoxLineOrientation.Horizontal, StrokeWeight.Light, 2),
            0x254D => (BoxLineOrientation.Horizontal, StrokeWeight.Heavy, 2),
            0x254E => (BoxLineOrientation.Vertical, StrokeWeight.Light, 2),
            0x254F => (BoxLineOrientation.Vertical, StrokeWeight.Heavy, 2),
            _ => (default, default, 0),
        };

        return dashCount > 0;
    }

    private static bool TryGetArcCorner(int codepoint, out ArcCorner corner)
    {
        corner = codepoint switch
        {
            0x256D => ArcCorner.DownRight,
            0x256E => ArcCorner.DownLeft,
            0x256F => ArcCorner.UpLeft,
            0x2570 => ArcCorner.UpRight,
            _ => default,
        };

        return codepoint >= 0x256D && codepoint <= 0x2570;
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

            // Unicode box-drawing "double" variants encoded with Heavy markers.
            // The draw path can reinterpret Heavy as true double strokes for this range.
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldSplitRunAroundCursor(int runStartColumn, int runEndCandidate, int cursorColumn)
    {
        if (cursorColumn < 0)
        {
            return false;
        }

        // Force [before cursor], [cursor cell], [after cursor] run boundaries.
        return (runStartColumn < cursorColumn && runEndCandidate == cursorColumn) ||
               (runStartColumn == cursorColumn && runEndCandidate == cursorColumn + 1);
    }

    private bool TryDrawPretextTextRun(
        SKCanvas canvas,
        ReadOnlySpan<TerminalCell> cells,
        int startCol,
        int endCol,
        SKTypeface runTypeface,
        SKColor runColor,
        float y)
    {
#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
        if (ShouldBypassPretextForShaping(cells, startCol, endCol))
        {
            return false;
        }

        try
        {
            return DrawPretextTextRun(canvas, cells, startCol, endCol, runTypeface, runColor, y);
        }
        catch (InvalidOperationException)
        {
            RecordPretextFallbackRun();
            return false;
        }
        catch (ArgumentException)
        {
            RecordPretextFallbackRun();
            return false;
        }
#else
        return false;
#endif
    }

    private bool ShouldBypassPretextForShaping(ReadOnlySpan<TerminalCell> cells, int startCol, int endCol)
    {
        if (_enableLigatures || _textDirectionMode == TextDirectionMode.RightToLeft)
        {
            return true;
        }

        for (int col = startCol; col < endCol; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (RequiresHarfBuzzShaping(in cell))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresHarfBuzzShaping(ref readonly TerminalCell cell)
    {
        if (!string.IsNullOrEmpty(cell.Grapheme) ||
            cell.Codepoint <= 0 ||
            cell.Codepoint > char.MaxValue)
        {
            return true;
        }

        if ((uint)(cell.Codepoint - 0x20) <= 0x5Eu)
        {
            return false;
        }

        UnicodeCategory category = char.GetUnicodeCategory((char)cell.Codepoint);
        if (category is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark or
            UnicodeCategory.Format)
        {
            return true;
        }

        return IsComplexScriptCodepoint(cell.Codepoint);
    }

    private static bool IsComplexScriptCodepoint(int codepoint)
    {
        return codepoint is
            (>= 0x0590 and <= 0x08FF) or // Hebrew, Arabic, Syriac, Thaana and extended Arabic blocks.
            (>= 0x0900 and <= 0x0D7F) or // Indic scripts.
            (>= 0x0E00 and <= 0x0EFF) or // Thai and Lao.
            (>= 0x0F00 and <= 0x0FFF) or // Tibetan.
            (>= 0x1000 and <= 0x109F) or // Myanmar.
            (>= 0x1780 and <= 0x17FF) or // Khmer.
            (>= 0xFB1D and <= 0xFDFF) or // Hebrew and Arabic presentation forms.
            (>= 0xFE70 and <= 0xFEFF); // Arabic presentation forms-B.
    }

    private bool TryDrawSimpleTextRowBatch(
        SKCanvas canvas,
        ReadOnlySpan<TerminalCell> cells,
        ReadOnlySpan<CellOverlayFlags> rowOverlays,
        ReadOnlySpan<CellTextHighlightOverride> rowTextHighlights,
        float y,
        bool requireAscii,
        bool requirePretextSafe,
        bool recordPretextRuns)
    {
        EnsureSimpleTextRowBatchCapacity(cells.Length);
        int groupCount = 0;
        int glyphCount = 0;

        Array.Fill(_simpleTextRowGroupIndexes, byte.MaxValue, 0, cells.Length);
        for (int i = 0; i < MaxSimpleTextRowBatchGroups; i++)
        {
            _simpleTextRowBatchGroups[i] = default;
        }

        for (int col = 0; col < cells.Length; col++)
        {
            if ((uint)col < (uint)rowOverlays.Length &&
                rowOverlays[col] != CellOverlayFlags.None)
            {
                return false;
            }

            if ((uint)col < (uint)rowTextHighlights.Length &&
                rowTextHighlights[col].HasAnyColor)
            {
                return false;
            }

            ref readonly TerminalCell cell = ref cells[col];
            if (!cell.HasContent || cell.Width == 0 || IsCellHidden(in cell))
            {
                continue;
            }

            if (GetEffectiveUnderlineStyle(in cell) != TerminalUnderlineStyle.None ||
                (cell.Attributes & CellAttributes.Strikethrough) != 0 ||
                (cell.Decorations & CellDecorations.Overline) != 0 ||
                !IsSimpleTextRowGlyphCell(in cell, requireAscii) ||
                (requirePretextSafe && RequiresHarfBuzzShaping(in cell)))
            {
                return false;
            }

            bool bold = (cell.Attributes & CellAttributes.Bold) != 0;
            bool italic = (cell.Attributes & CellAttributes.Italic) != 0;
            SKTypeface primaryTypeface = _glyphCache.GetTypeface(bold, italic);
            SKTypeface typeface = ResolveTypefaceForCell(primaryTypeface, in cell);
            if (!_singleGlyphIdCache.TryGetOrCreate(
                new SingleGlyphIdCacheKey(
                    cell.Codepoint,
                    typeface.Handle),
                typeface,
                out ushort glyphId))
            {
                return false;
            }

            SKColor color = GetEffectiveForeground(in cell);
            int groupIndex = FindSimpleTextRowBatchGroup(
                _simpleTextRowBatchGroups,
                groupCount,
                color,
                typeface.Handle);

            if (groupIndex < 0)
            {
                if (groupCount >= MaxSimpleTextRowBatchGroups)
                {
                    return false;
                }

                groupIndex = groupCount++;
                _simpleTextRowBatchGroups[groupIndex] = new SimpleTextRowBatchGroup
                {
                    Color = color,
                    TypefaceHandle = typeface.Handle,
                    Typeface = typeface,
                };
            }

            _simpleTextRowCellGlyphIds[col] = glyphId;
            _simpleTextRowGroupIndexes[col] = (byte)groupIndex;
            _simpleTextRowBatchGroups[groupIndex].Count++;
            glyphCount++;
        }

        if (glyphCount <= 0 || groupCount <= 0)
        {
            return true;
        }

        int offset = 0;
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            _simpleTextRowGroupOffsets[groupIndex] = offset;
            _simpleTextRowGroupWriteIndexes[groupIndex] = 0;
            offset += _simpleTextRowBatchGroups[groupIndex].Count;
        }

        for (int col = 0; col < cells.Length; col++)
        {
            byte groupIndex = _simpleTextRowGroupIndexes[col];
            if (groupIndex == byte.MaxValue)
            {
                continue;
            }

            int writeIndex = _simpleTextRowGroupOffsets[groupIndex] +
                _simpleTextRowGroupWriteIndexes[groupIndex]++;
            _simpleTextRowBatchGlyphIds[writeIndex] = _simpleTextRowCellGlyphIds[col];
            _simpleTextRowGlyphPositions[writeIndex] = new SKPoint(col * _cellWidth, 0f);
        }

        float baselineY = y + _baseline;
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            ref SimpleTextRowBatchGroup group = ref _simpleTextRowBatchGroups[groupIndex];
            int count = group.Count;
            if (count <= 0)
            {
                continue;
            }

            SKFont font = _textRowFontCache.GetOrCreate(group.Typeface, _fontSize);
            using SKTextBlobBuilder builder = new();
            int groupOffset = _simpleTextRowGroupOffsets[groupIndex];
            builder.AddPositionedRun(
                _simpleTextRowBatchGlyphIds.AsSpan(groupOffset, count),
                font,
                _simpleTextRowGlyphPositions.AsSpan(groupOffset, count));

            using SKTextBlob? textBlob = builder.Build();
            if (textBlob is null)
            {
                continue;
            }

            _fgPaint.Color = group.Color;
            canvas.DrawText(textBlob, 0f, baselineY, _fgPaint);
            if (recordPretextRuns)
            {
                RecordPretextRun();
            }
        }

        return true;
    }

    private void EnsureSimpleTextRowBatchCapacity(int cellCount)
    {
        if (_simpleTextRowCellGlyphIds.Length < cellCount)
        {
            _simpleTextRowCellGlyphIds = new ushort[cellCount];
        }

        if (_simpleTextRowGroupIndexes.Length < cellCount)
        {
            _simpleTextRowGroupIndexes = new byte[cellCount];
        }

        if (_simpleTextRowBatchGlyphIds.Length < cellCount)
        {
            _simpleTextRowBatchGlyphIds = new ushort[cellCount];
        }

        if (_simpleTextRowGlyphPositions.Length < cellCount)
        {
            _simpleTextRowGlyphPositions = new SKPoint[cellCount];
        }
    }

    private static int FindSimpleTextRowBatchGroup(
        ReadOnlySpan<SimpleTextRowBatchGroup> groups,
        int groupCount,
        SKColor color,
        nint typefaceHandle)
    {
        for (int i = 0; i < groupCount; i++)
        {
            if (groups[i].Color == color &&
                groups[i].TypefaceHandle == typefaceHandle)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSimpleTextRowGlyphCell(ref readonly TerminalCell cell, bool requireAscii)
    {
        if (cell.Width != 1 ||
            !cell.HasContent ||
            IsCellHidden(in cell) ||
            !string.IsNullOrEmpty(cell.Grapheme) ||
            cell.Codepoint <= 0 ||
            cell.Codepoint > char.MaxValue ||
            char.IsSurrogate((char)cell.Codepoint) ||
            TryGetSpriteCodepoint(in cell, out _))
        {
            return false;
        }

        if (requireAscii && (uint)(cell.Codepoint - 0x20) > 0x5Eu)
        {
            return false;
        }

        if (IsAsciiLetterOrDigit(cell.Codepoint))
        {
            return true;
        }

        return !IsSymbolGlyphClipCandidate(cell.Codepoint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiLetterOrDigit(int codepoint)
    {
        return (uint)(codepoint - '0') <= 9u ||
               (uint)((codepoint | 0x20) - 'a') <= 25u;
    }

#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
    private CachedPretextRun GetOrCreateCachedPretextRun(
        ReadOnlySpan<char> runText,
        ulong textHash,
        SKTypeface runTypeface)
    {
        PretextRunCacheKey cacheKey = new(
            textHash,
            runText.Length,
            runTypeface.Handle,
            BitConverter.SingleToInt32Bits(_fontSize),
            _textDirectionMode,
            _enableLigatures);

        if (_pretextRunCache.TryGet(cacheKey, runText, out CachedPretextRun cachedRun))
        {
            return cachedRun;
        }

        string text = new(runText);
        string font = CreatePretextFontDescriptor(runTypeface);
        PreparedTextWithSegments prepared = PretextLayout.PrepareWithSegments(
            text,
            font,
            new PrepareOptions(WhiteSpaceMode.PreWrap, WordBreakMode.KeepAll));
        float naturalWidth = MeasurePretextNaturalWidth(prepared);
        ushort[] glyphIds = CreatePretextNaturalGlyphIds(runTypeface, text);
        cachedRun = new CachedPretextRun(
            text,
            naturalWidth,
            GetGlyphClipPadding(text.AsSpan()),
            glyphIds,
            CreatePretextNaturalTextBlob(runTypeface, glyphIds));
        _pretextRunCache.Store(cacheKey, cachedRun);
        return cachedRun;
    }

    private bool DrawPretextTextRun(
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
            return true;
        }

        float runWidth = ComputeRunWidth(cells, startCol, endCol);
        if (runWidth <= 0f)
        {
            return true;
        }

        char[]? rentedChars = null;
        Span<char> runChars = utf16Length <= MaxStackallocTextRunChars
            ? stackalloc char[utf16Length]
            : (rentedChars = ArrayPool<char>.Shared.Rent(utf16Length)).AsSpan(0, utf16Length);
        try
        {
            int charCount = 0;
            ulong textHash = FnvOffsetBasis;

            for (int col = startCol; col < endCol; col++)
            {
                ref readonly TerminalCell cell = ref cells[col];
                Span<char> writeTarget = runChars[charCount..];
                int charsWritten;

                if (!string.IsNullOrEmpty(cell.Grapheme))
                {
                    ReadOnlySpan<char> grapheme = cell.Grapheme.AsSpan();
                    grapheme.CopyTo(writeTarget);
                    charsWritten = grapheme.Length;
                    for (int i = 0; i < grapheme.Length; i++)
                    {
                        textHash ^= grapheme[i];
                        textHash *= FnvPrime;
                    }
                }
                else
                {
                    Rune rune = new(cell.Codepoint);
                    charsWritten = rune.EncodeToUtf16(writeTarget);
                    for (int i = 0; i < charsWritten; i++)
                    {
                        textHash ^= writeTarget[i];
                        textHash *= FnvPrime;
                    }
                }

                charCount += charsWritten;
            }

            ReadOnlySpan<char> runText = runChars[..charCount];
            CachedPretextRun cachedRun = GetOrCreateCachedPretextRun(runText, textHash, runTypeface);

            GridPlacementMode placement;
            float xScale;
            if (CanUseNaturalSingleCellPretextPlacement(cells, startCol, endCol) &&
                cachedRun.NaturalWidth > 0f)
            {
                placement = GridPlacementMode.Natural;
                xScale = 1f;
            }
            else
            {
                placement = DeterminePretextGridPlacement(cachedRun.NaturalWidth, runWidth, out xScale);
            }

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
                RecordPretextFallbackRun();
                return true;
            }

            bool clampToRunWidth = placement == GridPlacementMode.Clamped;
            DrawCachedPretextRun(
                canvas,
                cachedRun,
                runTypeface,
                runColor,
                startCol * _cellWidth,
                y,
                y + _baseline,
                runWidth,
                xScale);

            RecordPretextRun();
            if (clampToRunWidth)
            {
                RecordGridClampedRun();
            }

            return true;
        }
        finally
        {
            if (rentedChars is not null)
            {
                ArrayPool<char>.Shared.Return(rentedChars);
            }
        }
    }

    private static bool CanUseNaturalSingleCellPretextPlacement(
        ReadOnlySpan<TerminalCell> cells,
        int startCol,
        int endCol)
    {
        if (endCol != startCol + 1)
        {
            return false;
        }

        ref readonly TerminalCell cell = ref cells[startCol];
        return cell.Width == 1 &&
               cell.Codepoint is >= char.MinValue and <= char.MaxValue &&
               string.IsNullOrEmpty(cell.Grapheme) &&
               !TryGetSpriteCodepoint(in cell, out _) &&
               !IsSymbolGlyphClipCandidate(in cell);
    }

    private string CreatePretextFontDescriptor(SKTypeface typeface)
    {
        SKFontStyle fontStyle = typeface.FontStyle;
        string slant = fontStyle.Slant is SKFontStyleSlant.Italic or SKFontStyleSlant.Oblique
            ? "italic "
            : string.Empty;
        string familyName = string.IsNullOrWhiteSpace(typeface.FamilyName)
            ? "monospace"
            : typeface.FamilyName.Replace("\"", string.Empty, StringComparison.Ordinal);

        return string.Create(
            s_renderCulture,
            $"{slant}{(int)fontStyle.Weight} {_fontSize}px \"{familyName}\"");
    }

    private GridPlacementMode DeterminePretextGridPlacement(float naturalWidth, float runWidth, out float xScale)
    {
        xScale = 1f;
        if (runWidth <= 0f || naturalWidth <= 0f)
        {
            return GridPlacementMode.UnsafeFallback;
        }

        float scale = runWidth / naturalWidth;
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            return GridPlacementMode.UnsafeFallback;
        }

        if (scale < GridScaleFallbackMin || scale > GridScaleFallbackMax)
        {
            return GridPlacementMode.UnsafeFallback;
        }

        float delta = Math.Abs(runWidth - naturalWidth);
        float tolerance = Math.Max(GridClampTolerancePx, runWidth * GridClampToleranceRatio);
        if (delta <= tolerance)
        {
            return GridPlacementMode.Natural;
        }

        xScale = scale;
        return GridPlacementMode.Clamped;
    }

    private void DrawCachedPretextRun(
        SKCanvas canvas,
        CachedPretextRun run,
        SKTypeface typeface,
        SKColor color,
        float originX,
        float rowY,
        float baselineY,
        float runWidth,
        float xScale)
    {
        if (run.Text.Length <= 0)
        {
            return;
        }

        float clipPadding = run.ClipPadding;
        _fgPaint.Color = color;

        bool needsClip = clipPadding > 0f;
        bool needsTransform = xScale != 1f;
        if (!needsClip && !needsTransform)
        {
            if (run.NaturalTextBlob is { } directTextBlob)
            {
                canvas.DrawText(directTextBlob, originX, baselineY, _fgPaint);
            }
            else
            {
                using SKFont directFont = GlyphCache.CreateFont(typeface, _fontSize);
                canvas.DrawText(run.Text, originX, baselineY, directFont, _fgPaint);
            }

            return;
        }

        canvas.Save();
        if (needsClip)
        {
            canvas.ClipRect(
                new SKRect(originX - clipPadding, rowY, originX + runWidth + clipPadding, rowY + _cellHeight),
                SKClipOperation.Intersect,
                antialias: false);
        }

        if (xScale != 1f)
        {
            canvas.Translate(originX, baselineY);
            canvas.Scale(xScale, 1f);
            originX = 0f;
            baselineY = 0f;
        }

        if (run.NaturalTextBlob is { } naturalTextBlob)
        {
            canvas.DrawText(naturalTextBlob, originX, baselineY, _fgPaint);
        }
        else
        {
            using SKFont font = GlyphCache.CreateFont(typeface, _fontSize);
            canvas.DrawText(run.Text, originX, baselineY, font, _fgPaint);
        }

        canvas.Restore();
    }

    private static float MeasurePretextNaturalWidth(PreparedTextWithSegments prepared)
    {
        IReadOnlyList<double> widths = prepared.Widths;
        IReadOnlyList<SegmentBreakKind> kinds = prepared.Kinds;
        double lineWidth = 0d;
        double maxLineWidth = 0d;

        for (int i = 0; i < widths.Count; i++)
        {
            if (kinds[i] == SegmentBreakKind.HardBreak)
            {
                maxLineWidth = Math.Max(maxLineWidth, lineWidth);
                lineWidth = 0d;
                continue;
            }

            lineWidth += widths[i];
        }

        return (float)Math.Max(maxLineWidth, lineWidth);
    }

    private static ushort[] CreatePretextNaturalGlyphIds(SKTypeface typeface, string text)
    {
        if (text.Length <= 0)
        {
            return [];
        }

        return typeface.GetGlyphs(text);
    }

    private SKTextBlob? CreatePretextNaturalTextBlob(SKTypeface typeface, ReadOnlySpan<ushort> glyphIds)
    {
        if (glyphIds.Length <= 0)
        {
            return null;
        }

        SKFont font = _textRowFontCache.GetOrCreate(typeface, _fontSize);
        using SKTextBlobBuilder builder = new();
        builder.AddRun(glyphIds, font, SKPoint.Empty);
        return builder.Build();
    }
#endif

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

        char[]? rentedChars = null;
        Span<char> runChars = utf16Length <= MaxStackallocTextRunChars
            ? stackalloc char[utf16Length]
            : (rentedChars = ArrayPool<char>.Shared.Rent(utf16Length)).AsSpan(0, utf16Length);
        try
        {
            int charCount = 0;
            ulong textHash = FnvOffsetBasis;

            for (int col = startCol; col < endCol; col++)
            {
                ref readonly TerminalCell cell = ref cells[col];
                Span<char> writeTarget = runChars[charCount..];
                int charsWritten;

                if (!string.IsNullOrEmpty(cell.Grapheme))
                {
                    ReadOnlySpan<char> grapheme = cell.Grapheme.AsSpan();
                    grapheme.CopyTo(writeTarget);
                    charsWritten = grapheme.Length;
                    for (int i = 0; i < grapheme.Length; i++)
                    {
                        textHash ^= grapheme[i];
                        textHash *= FnvPrime;
                    }
                }
                else
                {
                    Rune rune = new(cell.Codepoint);
                    charsWritten = rune.EncodeToUtf16(writeTarget);
                    for (int i = 0; i < charsWritten; i++)
                    {
                        textHash ^= writeTarget[i];
                        textHash *= FnvPrime;
                    }
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
                    advanceX,
                    GetGlyphClipPadding(runText),
                    CreateNaturalTextBlob(runTypeface, glyphIds, xOffsets, yOffsets));
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
            if (rentedChars is not null)
            {
                ArrayPool<char>.Shared.Return(rentedChars);
            }
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

        float clipPadding = run.ClipPadding;
        _fgPaint.Color = color;

        if (!clampToRunWidth &&
            xScale == 1f &&
            run.NaturalTextBlob is { } naturalTextBlob)
        {
            if (clipPadding <= 0f)
            {
                canvas.DrawText(naturalTextBlob, originX, baselineY, _fgPaint);
                return;
            }

            canvas.Save();
            ClipTextRun(canvas, originX, rowY, runWidth, clipPadding);
            canvas.DrawText(naturalTextBlob, originX, baselineY, _fgPaint);
            canvas.Restore();
            return;
        }

        int runWidthBits = BitConverter.SingleToInt32Bits(runWidth);
        if (!run.TryGetGridTextBlob(runWidthBits, out SKTextBlob? blob))
        {
            blob = CreateGridTextBlob(typeface, run, runWidth, xScale, clampToRunWidth);
            if (blob is null)
            {
                return;
            }

            run.SetGridTextBlob(runWidthBits, blob);
        }

        canvas.Save();
        ClipTextRun(canvas, originX, rowY, runWidth, clipPadding);
        canvas.DrawText(blob, originX, baselineY, _fgPaint);
        canvas.Restore();
    }

    private SKTextBlob? CreateGridTextBlob(
        SKTypeface typeface,
        CachedShapedRun run,
        float runWidth,
        float xScale,
        bool clampToRunWidth)
    {
        SKPoint[]? rentedPoints = null;
        Span<SKPoint> points = run.GlyphCount <= MaxStackallocGlyphPoints
            ? stackalloc SKPoint[run.GlyphCount]
            : (rentedPoints = ArrayPool<SKPoint>.Shared.Rent(run.GlyphCount)).AsSpan(0, run.GlyphCount);
        try
        {
            float clipPadding = run.ClipPadding;
            for (int i = 0; i < run.GlyphCount; i++)
            {
                float x = run.XOffsets[i];
                if (xScale != 1f)
                {
                    x *= xScale;
                }

                if (clampToRunWidth)
                {
                    x = Math.Clamp(x, -clipPadding, runWidth + clipPadding);
                }

                points[i] = new SKPoint(x, run.YOffsets[i]);
            }

            SKFont font = _textRowFontCache.GetOrCreate(typeface, _fontSize);
            using SKTextBlobBuilder builder = new();
            builder.AddPositionedRun(run.GlyphIds.AsSpan(), font, points);
            return builder.Build();
        }
        finally
        {
            if (rentedPoints is not null)
            {
                ArrayPool<SKPoint>.Shared.Return(rentedPoints);
            }
        }
    }

    private void ClipTextRun(SKCanvas canvas, float originX, float rowY, float runWidth, float clipPadding)
    {
        canvas.ClipRect(
            new SKRect(originX - clipPadding, rowY, originX + runWidth + clipPadding, rowY + _cellHeight),
            SKClipOperation.Intersect,
            antialias: false);
    }

    private SKTextBlob? CreateNaturalTextBlob(
        SKTypeface typeface,
        ReadOnlySpan<ushort> glyphIds,
        ReadOnlySpan<float> xOffsets,
        ReadOnlySpan<float> yOffsets)
    {
        if (glyphIds.IsEmpty)
        {
            return null;
        }

        SKPoint[]? rentedPoints = null;
        Span<SKPoint> points = glyphIds.Length <= MaxStackallocGlyphPoints
            ? stackalloc SKPoint[glyphIds.Length]
            : (rentedPoints = ArrayPool<SKPoint>.Shared.Rent(glyphIds.Length)).AsSpan(0, glyphIds.Length);
        try
        {
            for (int i = 0; i < glyphIds.Length; i++)
            {
                points[i] = new SKPoint(xOffsets[i], yOffsets[i]);
            }

            SKFont font = _textRowFontCache.GetOrCreate(typeface, _fontSize);
            using SKTextBlobBuilder builder = new();
            builder.AddPositionedRun(glyphIds, font, points);
            return builder.Build();
        }
        finally
        {
            if (rentedPoints is not null)
            {
                ArrayPool<SKPoint>.Shared.Return(rentedPoints);
            }
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
        SKFont font = _textRowFontCache.GetOrCreate(typeface, _fontSize);
        _fgPaint.Color = color;
        float clipPadding = GetGlyphClipPadding(cells, startCol, endCol);

        canvas.Save();
        float originX = startCol * _cellWidth;
        canvas.ClipRect(
            new SKRect(originX - clipPadding, y, originX + runWidth + clipPadding, y + _cellHeight),
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
                    ? GetCodepointText(cell.Codepoint)
                    : cell.Grapheme;
                SKTextBlob? blob = GetOrCreateCellTextBlob(typeface, font, text);
                if (blob is not null)
                {
                    canvas.DrawText(blob, x, y + _baseline, _fgPaint);
                }
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private string GetCodepointText(int codepoint)
    {
        if (_codepointTextCache.TryGetValue(codepoint, out string? text))
        {
            return text;
        }

        if (_codepointTextCache.Count >= MaxCodepointTextCacheEntries)
        {
            _codepointTextCache.Clear();
        }

        text = char.ConvertFromUtf32(codepoint);
        _codepointTextCache[codepoint] = text;
        return text;
    }

    private SKTextBlob? GetOrCreateCellTextBlob(SKTypeface typeface, SKFont font, string text)
    {
        CellTextBlobCacheKey key = new(
            ComputeTextHash(text.AsSpan()),
            text.Length,
            typeface.Handle,
            BitConverter.SingleToInt32Bits(_fontSize));
        return _cellTextBlobCache.GetOrCreate(key, text, font);
    }

    private static ulong ComputeTextHash(ReadOnlySpan<char> text)
    {
        ulong textHash = FnvOffsetBasis;
        for (int i = 0; i < text.Length; i++)
        {
            textHash ^= text[i];
            textHash *= FnvPrime;
        }

        return textHash;
    }

    private float GetGlyphClipPadding(ReadOnlySpan<TerminalCell> cells, int startCol, int endCol)
    {
        for (int col = startCol; col < endCol; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (IsSymbolGlyphClipCandidate(in cell))
            {
                return _cellWidth * SymbolGlyphClipPaddingCells;
            }
        }

        return 0f;
    }

    private float GetGlyphClipPadding(ReadOnlySpan<char> text)
    {
        return ContainsSymbolGlyphClipCandidate(text)
            ? _cellWidth * SymbolGlyphClipPaddingCells
            : 0f;
    }

    private static bool IsSymbolGlyphClipCandidate(ref readonly TerminalCell cell)
    {
        return string.IsNullOrEmpty(cell.Grapheme)
            ? IsSymbolGlyphClipCandidate(cell.Codepoint)
            : ContainsSymbolGlyphClipCandidate(cell.Grapheme.AsSpan());
    }

    private static bool ContainsSymbolGlyphClipCandidate(ReadOnlySpan<char> text)
    {
        ReadOnlySpan<char> remaining = text;
        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(remaining, out Rune rune, out int charsConsumed);
            if (status != OperationStatus.Done)
            {
                return false;
            }

            if (IsSymbolGlyphClipCandidate(rune.Value))
            {
                return true;
            }

            remaining = remaining[charsConsumed..];
        }

        return false;
    }

    private static bool IsSymbolGlyphClipCandidate(int codepoint)
    {
        if (!Rune.IsValid(codepoint))
        {
            return false;
        }

        UnicodeCategory category = Rune.GetUnicodeCategory(new Rune(codepoint));
        return category is UnicodeCategory.MathSymbol or UnicodeCategory.OtherSymbol;
    }

    private void DrawRunDecorations(
        SKCanvas canvas,
        ReadOnlySpan<TerminalCell> cells,
        ReadOnlySpan<CellOverlayFlags> rowOverlays,
        int startCol,
        int endCol,
        float y,
        SKColor color)
    {
        for (int col = startCol; col < endCol; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (IsCellHidden(in cell) || cell.Width == 0)
            {
                continue;
            }

            TerminalUnderlineStyle underlineStyle = GetEffectiveUnderlineStyle(in cell);
            bool isHyperlinkHover = (rowOverlays[col] & CellOverlayFlags.HyperlinkHover) != 0;
            if (isHyperlinkHover)
            {
                underlineStyle = underlineStyle switch
                {
                    TerminalUnderlineStyle.None => TerminalUnderlineStyle.Single,
                    TerminalUnderlineStyle.Single => TerminalUnderlineStyle.Double,
                    _ => underlineStyle,
                };
            }

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
                _fgPaint.Color = ResolveUnderlineColor(in cell, color, isHyperlinkHover);
                DrawStyledUnderline(canvas, x, y, width, underlineStyle);
            }

            if (overline)
            {
                _fgPaint.Color = color;
                float overlineY = y + 1f;
                canvas.DrawLine(x, overlineY, x + width, overlineY, _fgPaint);
            }

            if (strikethrough)
            {
                _fgPaint.Color = color;
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
    private static SKColor ResolveForegroundColorForCell(
        ref readonly TerminalCell cell,
        CellTextHighlightOverride textHighlight)
    {
        return textHighlight.HasForeground
            ? new SKColor(textHighlight.Foreground)
            : GetEffectiveForeground(in cell);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CellTextHighlightOverride GetTextHighlightOverride(
        ReadOnlySpan<CellTextHighlightOverride> rowTextHighlights,
        int column)
    {
        return (uint)column < (uint)rowTextHighlights.Length
            ? rowTextHighlights[column]
            : default;
    }

    private static bool TryGetPooledRowBufferCellCount(int rows, int rowCapacity, out int cellCount)
    {
        if (rows <= 0)
        {
            cellCount = Math.Max(1, rowCapacity);
            return true;
        }

        if (rowCapacity <= int.MaxValue / rows)
        {
            cellCount = Math.Max(1, rowCapacity * rows);
            return true;
        }

        cellCount = Math.Max(1, rowCapacity);
        return false;
    }

    private static Span<CellOverlayFlags> GetRowOverlayFlags(
        CellOverlayFlags[] overlayBuffer,
        int row,
        int columnCount,
        int rowCapacity)
    {
        if (columnCount <= 0)
        {
            return Span<CellOverlayFlags>.Empty;
        }

        int start = row * rowCapacity;
        if ((uint)start >= (uint)overlayBuffer.Length)
        {
            return Span<CellOverlayFlags>.Empty;
        }

        int available = Math.Min(columnCount, overlayBuffer.Length - start);
        return overlayBuffer.AsSpan(start, available);
    }

    private static Span<CellTextHighlightOverride> GetRowTextHighlightOverrides(
        CellTextHighlightOverride[]? textHighlightBuffer,
        int row,
        int columnCount,
        int rowCapacity)
    {
        if (textHighlightBuffer is null || columnCount <= 0)
        {
            return Span<CellTextHighlightOverride>.Empty;
        }

        int start = row * rowCapacity;
        if ((uint)start >= (uint)textHighlightBuffer.Length)
        {
            return Span<CellTextHighlightOverride>.Empty;
        }

        int available = Math.Min(columnCount, textHighlightBuffer.Length - start);
        return textHighlightBuffer.AsSpan(start, available);
    }

    private void PopulateRowTextHighlightOverrides(
        TerminalRow row,
        uint defaultBackground,
        ReadOnlySpan<CompiledTextHighlightRule> rules,
        TerminalTextHighlightingMode mode,
        Span<CellTextHighlightOverride> rowTextHighlights)
    {
        if (mode == TerminalTextHighlightingMode.Disabled || rules.IsEmpty || rowTextHighlights.IsEmpty)
        {
            return;
        }

        bool darkTheme = IsPerceivedDarkColor(defaultBackground);
        if (mode == TerminalTextHighlightingMode.Static)
        {
            ulong rowTextHash = ComputeTextHighlightRowTextHash(row);
            if (TryCopyCachedTextHighlightOverrides(
                    row,
                    rowTextHash,
                    darkTheme,
                    rowTextHighlights))
            {
                return;
            }

            PopulateUncachedRowTextHighlightOverrides(row, darkTheme, rules, rowTextHighlights);
            StoreCachedTextHighlightOverrides(row, rowTextHash, darkTheme, rowTextHighlights);
            return;
        }

        PopulateUncachedRowTextHighlightOverrides(row, darkTheme, rules, rowTextHighlights);
    }

    private void PopulateUncachedRowTextHighlightOverrides(
        TerminalRow row,
        bool darkTheme,
        ReadOnlySpan<CompiledTextHighlightRule> rules,
        Span<CellTextHighlightOverride> rowTextHighlights)
    {
        if (!TryBuildTextHighlightRowTextColumnMap(row, out int rowTextLength))
        {
            return;
        }

        ReadOnlySpan<char> rowText = _textHighlightRowText.AsSpan(0, rowTextLength);
        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        for (int i = 0; i < rules.Length; i++)
        {
            CompiledTextHighlightRule rule = rules[i];
            TextHighlightResolvedColors colors = rule.GetColors(darkTheme);
            if (!colors.HasAnyColor)
            {
                continue;
            }

            try
            {
                foreach (ValueMatch match in rule.Regex.EnumerateMatches(rowText))
                {
                    if (match.Length > 0)
                    {
                        ApplyTextHighlightMatch(
                            cells,
                            rowTextHighlights,
                            rowTextLength,
                            match.Index,
                            match.Length,
                            colors);
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // A single expensive user-authored regex should not break terminal rendering.
            }
        }
    }

    private bool TryCopyCachedTextHighlightOverrides(
        TerminalRow row,
        ulong rowTextHash,
        bool darkTheme,
        Span<CellTextHighlightOverride> rowTextHighlights)
    {
        if (!_textHighlightRowCache.TryGetValue(row, out TextHighlightRowCacheEntry? entry) ||
            entry.Columns != rowTextHighlights.Length ||
            entry.RuleRevision != _textHighlightRuleRevision ||
            entry.DarkTheme != darkTheme ||
            entry.RowTextHash != rowTextHash)
        {
            return false;
        }

        if (entry.Overrides.Length > 0)
        {
            entry.Overrides.AsSpan(0, rowTextHighlights.Length).CopyTo(rowTextHighlights);
        }

        return true;
    }

    private void StoreCachedTextHighlightOverrides(
        TerminalRow row,
        ulong rowTextHash,
        bool darkTheme,
        ReadOnlySpan<CellTextHighlightOverride> rowTextHighlights)
    {
        if (!_textHighlightRowCache.TryGetValue(row, out TextHighlightRowCacheEntry? entry))
        {
            if (_textHighlightRowCache.Count >= MaxTextHighlightRowCacheEntries)
            {
                _textHighlightRowCache.Clear();
            }

            entry = new TextHighlightRowCacheEntry();
            _textHighlightRowCache.Add(row, entry);
        }

        if (!HasAnyTextHighlightOverride(rowTextHighlights))
        {
            entry.Overrides = Array.Empty<CellTextHighlightOverride>();
        }
        else
        {
            if (entry.Overrides.Length != rowTextHighlights.Length)
            {
                entry.Overrides = new CellTextHighlightOverride[rowTextHighlights.Length];
            }

            rowTextHighlights.CopyTo(entry.Overrides);
        }

        entry.Columns = rowTextHighlights.Length;
        entry.RuleRevision = _textHighlightRuleRevision;
        entry.DarkTheme = darkTheme;
        entry.RowTextHash = rowTextHash;
    }

    private static bool HasAnyTextHighlightOverride(ReadOnlySpan<CellTextHighlightOverride> rowTextHighlights)
    {
        for (int i = 0; i < rowTextHighlights.Length; i++)
        {
            if (rowTextHighlights[i].HasForeground || rowTextHighlights[i].HasBackground)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyTextHighlightMatch(
        ReadOnlySpan<TerminalCell> cells,
        Span<CellTextHighlightOverride> rowTextHighlights,
        int rowTextLength,
        int matchIndex,
        int matchLength,
        TextHighlightResolvedColors colors)
    {
        int mapEnd = matchIndex + matchLength - 1;
        if ((uint)matchIndex >= (uint)rowTextLength ||
            (uint)mapEnd >= (uint)rowTextLength)
        {
            return;
        }

        int startColumn = _textHighlightColumnMap[matchIndex];
        int endColumn = _textHighlightColumnMap[mapEnd];
        if (endColumn < startColumn)
        {
            return;
        }

        endColumn = ExpandHighlightEndColumnForWideCells(cells, startColumn, endColumn);
        startColumn = Math.Clamp(startColumn, 0, rowTextHighlights.Length - 1);
        endColumn = Math.Clamp(endColumn, 0, rowTextHighlights.Length - 1);
        for (int col = startColumn; col <= endColumn; col++)
        {
            if ((uint)col >= (uint)cells.Length)
            {
                continue;
            }

            ref CellTextHighlightOverride textHighlight = ref rowTextHighlights[col];
            if (colors.HasForeground)
            {
                textHighlight.Foreground = colors.Foreground;
                textHighlight.HasForeground = true;
            }

            if (colors.HasBackground)
            {
                textHighlight.Background = colors.Background;
                textHighlight.HasBackground = true;
            }
        }
    }

    private static int ExpandHighlightEndColumnForWideCells(
        ReadOnlySpan<TerminalCell> cells,
        int startColumn,
        int endColumn)
    {
        int expandedEnd = endColumn;
        int boundedEnd = Math.Min(endColumn, cells.Length - 1);
        for (int col = Math.Max(0, startColumn); col <= boundedEnd; col++)
        {
            int width = cells[col].Width <= 0 ? 1 : cells[col].Width;
            expandedEnd = Math.Max(expandedEnd, col + width - 1);
        }

        return expandedEnd;
    }

    private bool TryBuildTextHighlightRowTextColumnMap(TerminalRow row, out int rowTextLength)
    {
        rowTextLength = 0;

        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        for (int col = 0; col < cells.Length; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (cell.Width == 0 || IsCellHidden(in cell))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                ReadOnlySpan<char> text = cell.Grapheme.AsSpan();
                EnsureTextHighlightBufferCapacity(rowTextLength + text.Length);
                text.CopyTo(_textHighlightRowText.AsSpan(rowTextLength));
                _textHighlightColumnMap.AsSpan(rowTextLength, text.Length).Fill(col);
                rowTextLength += text.Length;
            }
            else if (cell.Codepoint > 0 && Rune.IsValid(cell.Codepoint))
            {
                EnsureTextHighlightBufferCapacity(rowTextLength + 2);
                Rune rune = new(cell.Codepoint);
                int charsWritten = rune.EncodeToUtf16(_textHighlightRowText.AsSpan(rowTextLength, 2));
                _textHighlightColumnMap.AsSpan(rowTextLength, charsWritten).Fill(col);
                rowTextLength += charsWritten;
            }
        }

        return rowTextLength > 0;
    }

    private void EnsureTextHighlightBufferCapacity(int capacity)
    {
        if (_textHighlightRowText.Length >= capacity &&
            _textHighlightColumnMap.Length >= capacity)
        {
            return;
        }

        int nextCapacity = GetTextHighlightBufferCapacity(capacity);
        if (_textHighlightRowText.Length < capacity)
        {
            Array.Resize(ref _textHighlightRowText, nextCapacity);
        }

        if (_textHighlightColumnMap.Length < capacity)
        {
            Array.Resize(ref _textHighlightColumnMap, nextCapacity);
        }
    }

    private int GetTextHighlightBufferCapacity(int requiredCapacity)
    {
        int currentCapacity = Math.Max(_textHighlightRowText.Length, _textHighlightColumnMap.Length);
        int nextCapacity = currentCapacity == 0
            ? InitialTextHighlightBufferCapacity
            : currentCapacity;
        while (nextCapacity < requiredCapacity)
        {
            int doubled = nextCapacity * 2;
            if (doubled <= nextCapacity)
            {
                return requiredCapacity;
            }

            nextCapacity = doubled;
        }

        return nextCapacity;
    }

    private static ulong ComputeTextHighlightRowTextHash(TerminalRow row)
    {
        ulong hash = FnvOffsetBasis;
        AddHashValue(ref hash, row.Columns);

        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        for (int col = 0; col < cells.Length; col++)
        {
            ref readonly TerminalCell cell = ref cells[col];
            if (cell.Width == 0 || IsCellHidden(in cell))
            {
                continue;
            }

            AddHashValue(ref hash, col);
            AddHashValue(ref hash, cell.Width);

            if (!string.IsNullOrEmpty(cell.Grapheme))
            {
                AddHashValue(ref hash, cell.Grapheme.Length);
                for (int i = 0; i < cell.Grapheme.Length; i++)
                {
                    AddHashValue(ref hash, cell.Grapheme[i]);
                }
            }
            else if (cell.Codepoint > 0 && Rune.IsValid(cell.Codepoint))
            {
                AddHashValue(ref hash, cell.Codepoint);
            }
            else
            {
                AddHashValue(ref hash, 0);
            }
        }

        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddHashValue(ref ulong hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= FnvPrime;
        }
    }

    private static CompiledTextHighlightRule[] CompileTextHighlightRules(
        ReadOnlySpan<TerminalTextHighlightRule> rules)
    {
        List<CompiledTextHighlightRule> compiledRules = new(rules.Length);
        for (int i = 0; i < rules.Length; i++)
        {
            TerminalTextHighlightRule rule = rules[i];
            if (!rule.IsEnabled ||
                string.IsNullOrWhiteSpace(rule.Pattern) ||
                !HasAnyConfiguredColor(rule))
            {
                continue;
            }

            try
            {
                Regex regex = CreateTextHighlightRegex(rule.Pattern);
                compiledRules.Add(new CompiledTextHighlightRule(
                    regex,
                    CreateLightTextHighlightColors(rule),
                    CreateDarkTextHighlightColors(rule)));
            }
            catch (ArgumentException)
            {
                // Invalid user regexes remain in the public rule list but do not render.
            }
        }

        return compiledRules.Count == 0 ? Array.Empty<CompiledTextHighlightRule>() : compiledRules.ToArray();
    }

    private static Regex CreateTextHighlightRegex(string pattern)
    {
        RegexOptions options = GetTextHighlightRegexOptions();
        try
        {
            // The non-backtracking engine is linear, so give it enough headroom
            // for cold first-use costs while keeping fallback patterns tightly bounded.
            return new Regex(
                pattern,
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                s_textHighlightNonBacktrackingRegexTimeout);
        }
        catch (NotSupportedException)
        {
            return new Regex(pattern, options, s_textHighlightBacktrackingRegexTimeout);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RegexOptions GetTextHighlightRegexOptions()
    {
        RegexOptions options = RegexOptions.CultureInvariant;
        if (RuntimeFeature.IsDynamicCodeCompiled)
        {
            options |= RegexOptions.Compiled;
        }

        return options;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TerminalTextHighlightingMode NormalizeTextHighlightingMode(TerminalTextHighlightingMode mode)
    {
        return Enum.IsDefined(mode)
            ? mode
            : TerminalTextHighlightingMode.Static;
    }

    private static bool AreTextHighlightRulesEqual(
        ReadOnlySpan<TerminalTextHighlightRule> left,
        ReadOnlySpan<TerminalTextHighlightRule> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasAnyConfiguredColor(TerminalTextHighlightRule rule)
    {
        return rule.Foreground is not null ||
               rule.Background is not null ||
               rule.DarkForeground is not null ||
               rule.DarkBackground is not null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TextHighlightResolvedColors CreateLightTextHighlightColors(TerminalTextHighlightRule rule)
    {
        return new TextHighlightResolvedColors(
            rule.Foreground.GetValueOrDefault(),
            rule.Background.GetValueOrDefault(),
            rule.Foreground.HasValue,
            rule.Background.HasValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TextHighlightResolvedColors CreateDarkTextHighlightColors(TerminalTextHighlightRule rule)
    {
        uint? foreground = rule.DarkForeground ?? rule.Foreground;
        uint? background = rule.DarkBackground ?? rule.Background;
        return new TextHighlightResolvedColors(
            foreground.GetValueOrDefault(),
            background.GetValueOrDefault(),
            foreground.HasValue,
            background.HasValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPerceivedDarkColor(uint argb)
    {
        byte red = (byte)((argb >> 16) & 0xFF);
        byte green = (byte)((argb >> 8) & 0xFF);
        byte blue = (byte)(argb & 0xFF);
        return (red * 299) + (green * 587) + (blue * 114) < 128_000;
    }

    private void PopulateRowOverlayFlags(
        int rowIndex,
        int columnCount,
        ReadOnlySpan<TerminalHighlightSpan> highlights,
        Span<CellOverlayFlags> rowOverlays)
    {
        if (columnCount <= 0)
        {
            return;
        }

        if (SelectionStart is not null && SelectionEnd is not null)
        {
            int startCol = SelectionStart.Value.Column;
            int startRow = SelectionStart.Value.Row;
            int endCol = SelectionEnd.Value.Column;
            int endRow = SelectionEnd.Value.Row;

            if (startRow > endRow || (startRow == endRow && startCol > endCol))
            {
                (startCol, startRow, endCol, endRow) = (endCol, endRow, startCol, startRow);
            }

            if (rowIndex >= startRow && rowIndex <= endRow)
            {
                int left = rowIndex == startRow ? startCol : 0;
                int rightExclusive = rowIndex == endRow ? endCol : columnCount;
                left = Math.Clamp(left, 0, columnCount);
                rightExclusive = Math.Clamp(rightExclusive, 0, columnCount);

                for (int col = left; col < rightExclusive; col++)
                {
                    rowOverlays[col] |= CellOverlayFlags.Selection;
                }
            }
        }

        for (int i = 0; i < highlights.Length; i++)
        {
            TerminalHighlightSpan span = highlights[i];
            if (span.Row != rowIndex)
            {
                continue;
            }

            int start = Math.Clamp(span.StartColumn, 0, columnCount - 1);
            int end = Math.Clamp(span.EndColumn, 0, columnCount - 1);
            if (end < start)
            {
                continue;
            }

            for (int col = start; col <= end; col++)
            {
                switch (span.Kind)
                {
                    case TerminalHighlightKind.SearchMatch:
                        if ((rowOverlays[col] & CellOverlayFlags.SearchSelected) == 0)
                        {
                            rowOverlays[col] |= CellOverlayFlags.SearchMatch;
                        }

                        break;

                    case TerminalHighlightKind.SearchSelected:
                        rowOverlays[col] &= ~CellOverlayFlags.SearchMatch;
                        rowOverlays[col] |= CellOverlayFlags.SearchSelected;
                        break;

                    case TerminalHighlightKind.HyperlinkHover:
                        rowOverlays[col] |= CellOverlayFlags.HyperlinkHover;
                        break;
                }
            }
        }
    }

    private uint ResolveBackgroundColorForCell(
        ref readonly TerminalCell cell,
        CellOverlayFlags overlays,
        CellTextHighlightOverride textHighlight)
    {
        if ((overlays & CellOverlayFlags.Selection) != 0)
        {
            return PackColor(SelectionColor);
        }

        if ((overlays & CellOverlayFlags.SearchSelected) != 0)
        {
            return PackColor(SearchSelectedHighlightColor);
        }

        if ((overlays & CellOverlayFlags.SearchMatch) != 0)
        {
            return PackColor(SearchHighlightColor);
        }

        if (textHighlight.HasBackground)
        {
            return textHighlight.Background;
        }

        uint color = GetEffectiveBackground(in cell);
        byte alpha = ResolveBackgroundAlpha(in cell, overlays);
        return (color & 0x00FFFFFFu) | ((uint)alpha << 24);
    }

    private byte ResolveBackgroundAlpha(
        ref readonly TerminalCell cell,
        CellOverlayFlags overlays)
    {
        if (!EnableBackgroundOpacityHeuristics)
        {
            return 0xFF;
        }

        if ((overlays & (CellOverlayFlags.Selection | CellOverlayFlags.SearchMatch | CellOverlayFlags.SearchSelected)) != 0)
        {
            return 0xFF;
        }

        bool inverse = (cell.Attributes & CellAttributes.Inverse) != 0;
        if (inverse)
        {
            return 0xFF;
        }

        if (BackgroundOpacityEnabled && BackgroundOpacityCells && cell.HasBackground)
        {
            float opacity = Math.Clamp(BackgroundOpacity, 0f, 1f);
            return (byte)Math.Clamp((int)MathF.Round(opacity * 255f), 0, 255);
        }

        return cell.HasBackground ? (byte)0xFF : (byte)0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackColor(SKColor color)
    {
        return ((uint)color.Alpha << 24) |
               ((uint)color.Red << 16) |
               ((uint)color.Green << 8) |
               color.Blue;
    }

    private SKColor ResolveUnderlineColor(
        ref readonly TerminalCell cell,
        SKColor fallbackForeground,
        bool isHyperlinkHover)
    {
        if (cell.HasUnderlineColor)
        {
            return new SKColor(cell.UnderlineColor);
        }

        if (isHyperlinkHover && HyperlinkHoverUnderlineColor != SKColors.Empty)
        {
            return HyperlinkHoverUnderlineColor;
        }

        return fallbackForeground;
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

    private void RenderCursor(SKCanvas canvas, TerminalScreen screen)
    {
        if (!TryResolveCursorCellPlacement(
                screen,
                CursorColumn,
                CursorRow,
                out int renderColumn,
                out int renderWidthCells))
        {
            return;
        }

        float x = renderColumn * _cellWidth;
        float y = CursorRow * _cellHeight;
        float cursorWidth = Math.Max(_cellWidth, _cellWidth * renderWidthCells);

        _cursorPaint.Color = CursorColor;

        switch (CursorStyle)
        {
            case CursorStyle.Block:
                _cursorPaint.Style = SKPaintStyle.Fill;
                _cursorPaint.BlendMode = SKBlendMode.Difference;
                canvas.DrawRect(x, y, cursorWidth, _cellHeight, _cursorPaint);
                _cursorPaint.BlendMode = SKBlendMode.SrcOver;
                break;

            case CursorStyle.BlockHollow:
                _cursorPaint.Style = SKPaintStyle.Stroke;
                _cursorPaint.StrokeWidth = Math.Max(1f, MathF.Round(_cellWidth * 0.08f));
                canvas.DrawRect(x, y, cursorWidth, _cellHeight, _cursorPaint);
                _cursorPaint.StrokeWidth = 0f;
                break;

            case CursorStyle.Underline:
                _cursorPaint.Style = SKPaintStyle.Fill;
                canvas.DrawRect(x, y + _cellHeight - 2, cursorWidth, 2, _cursorPaint);
                break;

            case CursorStyle.Bar:
                _cursorPaint.Style = SKPaintStyle.Fill;
                float barWidth = Math.Max(1f, MathF.Round(_cellWidth * 0.16f));
                canvas.DrawRect(x, y, barWidth, _cellHeight, _cursorPaint);
                break;
        }
    }

    private static bool TryResolveCursorCellPlacement(
        TerminalScreen screen,
        int cursorColumn,
        int cursorRow,
        out int renderColumn,
        out int renderWidthCells)
    {
        renderColumn = cursorColumn;
        renderWidthCells = 1;

        if ((uint)cursorRow >= (uint)screen.ViewportRows || (uint)cursorColumn >= (uint)screen.Columns)
        {
            return false;
        }

        TerminalRow row = screen.GetViewportRow(cursorRow);
        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        if (cells.IsEmpty || (uint)cursorColumn >= (uint)cells.Length)
        {
            return false;
        }

        ref readonly TerminalCell current = ref cells[cursorColumn];
        if (current.Width > 0)
        {
            renderWidthCells = current.Width;
            return true;
        }

        // If cursor is on the tail of a wide cell, render over the leading cell.
        if (cursorColumn > 0)
        {
            ref readonly TerminalCell previous = ref cells[cursorColumn - 1];
            if (previous.Width > 1)
            {
                renderColumn = cursorColumn - 1;
                renderWidthCells = previous.Width;
                return true;
            }
        }

        // Spacer head may appear before a wide cell in some layouts.
        if (cursorColumn + 1 < cells.Length)
        {
            ref readonly TerminalCell next = ref cells[cursorColumn + 1];
            if (next.Width > 1)
            {
                renderColumn = cursorColumn + 1;
                renderWidthCells = next.Width;
                return true;
            }
        }

        return true;
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
                Interlocked.Exchange(ref _diagnosticScanLineSpriteCells, 0),
                Interlocked.Exchange(ref _diagnosticPretextRuns, 0),
                Interlocked.Exchange(ref _diagnosticPretextFallbackRuns, 0));
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
            Volatile.Read(ref _diagnosticScanLineSpriteCells),
            Volatile.Read(ref _diagnosticPretextRuns),
            Volatile.Read(ref _diagnosticPretextFallbackRuns));

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
        Interlocked.Exchange(ref _diagnosticPretextRuns, 0);
        Interlocked.Exchange(ref _diagnosticPretextFallbackRuns, 0);
    }

    /// <summary>
    /// Gets a snapshot of terminal image-render diagnostics.
    /// </summary>
    /// <param name="reset">When true, counters are reset after snapshot capture.</param>
    public ImageRenderDiagnostics GetImageRenderDiagnostics(bool reset = false)
    {
        long placementsVisited = reset
            ? Interlocked.Exchange(ref _diagnosticImagePlacementsVisited, 0)
            : Volatile.Read(ref _diagnosticImagePlacementsVisited);
        long placementsVisible = reset
            ? Interlocked.Exchange(ref _diagnosticImagePlacementsVisible, 0)
            : Volatile.Read(ref _diagnosticImagePlacementsVisible);
        long draws = reset
            ? Interlocked.Exchange(ref _diagnosticImageDraws, 0)
            : Volatile.Read(ref _diagnosticImageDraws);
        long cacheHits = reset
            ? Interlocked.Exchange(ref _diagnosticImageCacheHits, 0)
            : Volatile.Read(ref _diagnosticImageCacheHits);
        long cacheMisses = reset
            ? Interlocked.Exchange(ref _diagnosticImageCacheMisses, 0)
            : Volatile.Read(ref _diagnosticImageCacheMisses);
        long cacheEvictions = reset
            ? Interlocked.Exchange(ref _diagnosticImageCacheEvictions, 0)
            : Volatile.Read(ref _diagnosticImageCacheEvictions);

        return new ImageRenderDiagnostics(
            placementsVisited,
            placementsVisible,
            draws,
            cacheHits,
            cacheMisses,
            cacheEvictions,
            _kittyBitmapCache.Count,
            Volatile.Read(ref _kittyBitmapCacheBytes),
            _rasterBitmapCache.Count,
            Volatile.Read(ref _rasterBitmapCacheBytes));
    }

    /// <summary>
    /// Resets terminal image-render diagnostics counters to zero.
    /// </summary>
    public void ResetImageRenderDiagnostics()
    {
        Interlocked.Exchange(ref _diagnosticImagePlacementsVisited, 0);
        Interlocked.Exchange(ref _diagnosticImagePlacementsVisible, 0);
        Interlocked.Exchange(ref _diagnosticImageDraws, 0);
        Interlocked.Exchange(ref _diagnosticImageCacheHits, 0);
        Interlocked.Exchange(ref _diagnosticImageCacheMisses, 0);
        Interlocked.Exchange(ref _diagnosticImageCacheEvictions, 0);
    }

    private void RenderRasterLayer(
        SKCanvas canvas,
        TerminalScreen screen,
        ReadOnlySpan<TerminalRasterImagePlacement> placements,
        TerminalRasterImageLayer layer,
        long imageFrameId)
    {
        if (placements.IsEmpty)
        {
            return;
        }

        float viewportWidth = screen.Columns * _cellWidth;
        float viewportHeight = screen.ViewportRows * _cellHeight;
        int viewportTopAbsoluteRow = screen.ViewportTopAbsoluteRow;

        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, viewportWidth, viewportHeight));

        try
        {
            for (int i = 0; i < placements.Length; i++)
            {
                TerminalRasterImagePlacement placement = placements[i];
                if (placement.Layer != layer)
                {
                    continue;
                }

                RecordImagePlacementVisited();
                float xScale = GetRasterPlacementScale(placement.CellWidthPx, _cellWidth);
                float yScale = GetRasterPlacementScale(placement.CellHeightPx, _cellHeight);
                float destLeft = (placement.AnchorColumn * _cellWidth) + (placement.XOffsetPx * xScale);
                float destTop = ((placement.AnchorRow - viewportTopAbsoluteRow) * _cellHeight) +
                    (placement.YOffsetPx * yScale);
                SKRect destRect = new(
                    destLeft,
                    destTop,
                    destLeft + (placement.WidthPx * xScale),
                    destTop + (placement.HeightPx * yScale));
                if (!IntersectsViewport(destRect, viewportWidth, viewportHeight))
                {
                    continue;
                }

                RecordImagePlacementVisible();
                if (!screen.TryGetRasterImageSource(placement.ImageId, out TerminalRasterImageSource? source) ||
                    source is null)
                {
                    continue;
                }

                SKRect sourceRect = new(
                    placement.SourceX,
                    placement.SourceY,
                    placement.SourceX + placement.SourceWidth,
                    placement.SourceY + placement.SourceHeight);
                if (!IsRenderableImageRect(sourceRect, destRect))
                {
                    continue;
                }

                SKBitmap bitmap = GetOrCreateRasterBitmap(source, imageFrameId);
                canvas.DrawBitmap(bitmap, sourceRect, destRect);
                RecordImageDraw();
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static float GetRasterPlacementScale(int placementCellSizePx, float rendererCellSize)
    {
        if (placementCellSizePx <= 0 || rendererCellSize <= 0f)
        {
            return 1f;
        }

        float scale = rendererCellSize / placementCellSizePx;
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    private void RenderKittyLayer(
        SKCanvas canvas,
        TerminalScreen screen,
        ReadOnlySpan<TerminalKittyImagePlacement> placements,
        TerminalKittyImageLayer layer,
        long imageFrameId)
    {
        if (placements.IsEmpty)
        {
            return;
        }

        float viewportWidth = screen.Columns * _cellWidth;
        float viewportHeight = screen.ViewportRows * _cellHeight;

        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, viewportWidth, viewportHeight));

        try
        {
            for (int i = 0; i < placements.Length; i++)
            {
                TerminalKittyImagePlacement placement = placements[i];
                if (placement.Layer != layer)
                {
                    continue;
                }

                RecordImagePlacementVisited();
                float destLeft = (placement.ViewportColumn * _cellWidth) + placement.XOffsetPx;
                float destTop = (placement.ViewportRow * _cellHeight) + placement.YOffsetPx;
                SKRect destRect = new(
                    destLeft,
                    destTop,
                    destLeft + placement.WidthPx,
                    destTop + placement.HeightPx);
                if (!IntersectsViewport(destRect, viewportWidth, viewportHeight))
                {
                    continue;
                }

                RecordImagePlacementVisible();
                if (!screen.TryGetKittyImageSource(placement.ImageId, out TerminalKittyImageSource? source) ||
                    source is null)
                {
                    continue;
                }

                SKRect sourceRect = new(
                    placement.SourceX,
                    placement.SourceY,
                    placement.SourceX + placement.SourceWidth,
                    placement.SourceY + placement.SourceHeight);
                if (!IsRenderableImageRect(sourceRect, destRect))
                {
                    continue;
                }

                SKBitmap bitmap = GetOrCreateKittyBitmap(source, imageFrameId);
                canvas.DrawBitmap(bitmap, sourceRect, destRect);
                RecordImageDraw();
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static bool IntersectsViewport(SKRect rect, float viewportWidth, float viewportHeight)
    {
        return rect.Right > 0f &&
            rect.Bottom > 0f &&
            rect.Left < viewportWidth &&
            rect.Top < viewportHeight;
    }

    private static bool IsRenderableImageRect(SKRect sourceRect, SKRect destRect)
    {
        return sourceRect.Width > 0f &&
            sourceRect.Height > 0f &&
            destRect.Width > 0f &&
            destRect.Height > 0f;
    }

    private SKBitmap GetOrCreateKittyBitmap(TerminalKittyImageSource source, long imageFrameId)
    {
        return GetOrCreateImageBitmap(
            source.WidthPx,
            source.HeightPx,
            source.RgbaPixels,
            source.ContentFingerprint,
            _kittyBitmapCache,
            ref _kittyBitmapCacheBytes,
            "Kitty Graphics",
            imageFrameId);
    }

    private SKBitmap GetOrCreateRasterBitmap(TerminalRasterImageSource source, long imageFrameId)
    {
        return GetOrCreateImageBitmap(
            source.WidthPx,
            source.HeightPx,
            source.RgbaPixels,
            source.ContentFingerprint,
            _rasterBitmapCache,
            ref _rasterBitmapCacheBytes,
            "terminal raster graphics",
            imageFrameId);
    }

    private SKBitmap GetOrCreateImageBitmap(
        int widthPx,
        int heightPx,
        byte[] rgbaPixels,
        ulong contentFingerprint,
        Dictionary<TerminalBitmapCacheKey, TerminalBitmapCacheEntry> cache,
        ref long cacheBytes,
        string protocolName,
        long imageFrameId)
    {
        TerminalBitmapCacheKey key = new(widthPx, heightPx, rgbaPixels.Length, contentFingerprint);
        if (cache.TryGetValue(key, out TerminalBitmapCacheEntry? existing))
        {
            existing.LastUsedFrame = imageFrameId;
            RecordImageCacheHit();
            return existing.Bitmap;
        }

        SKBitmap bitmap = new(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        IntPtr pixels = bitmap.GetPixels();
        if (pixels == IntPtr.Zero)
        {
            bitmap.Dispose();
            throw new InvalidOperationException($"Failed to allocate bitmap pixels for {protocolName}.");
        }

        Marshal.Copy(rgbaPixels, 0, pixels, rgbaPixels.Length);
        TerminalBitmapCacheEntry entry = new(bitmap, rgbaPixels.Length, imageFrameId);
        cache[key] = entry;
        cacheBytes += entry.ByteLength;
        RecordImageCacheMiss();
        return bitmap;
    }

    private void TrimBitmapCache(
        Dictionary<TerminalBitmapCacheKey, TerminalBitmapCacheEntry> cache,
        ref long cacheBytes,
        long imageFrameId)
    {
        if (cache.Count == 0 || cacheBytes <= _imageBitmapCacheBudgetBytes)
        {
            return;
        }

        long protectedBytes = 0;
        foreach (TerminalBitmapCacheEntry entry in cache.Values)
        {
            if (entry.LastUsedFrame == imageFrameId)
            {
                protectedBytes += entry.ByteLength;
            }
        }

        long targetBytes = Math.Max(_imageBitmapCacheBudgetBytes, protectedBytes);
        while (cacheBytes > targetBytes)
        {
            TerminalBitmapCacheKey? oldestKey = null;
            long oldestFrame = long.MaxValue;
            foreach ((TerminalBitmapCacheKey key, TerminalBitmapCacheEntry candidate) in cache)
            {
                if (candidate.LastUsedFrame == imageFrameId)
                {
                    continue;
                }

                if (candidate.LastUsedFrame < oldestFrame)
                {
                    oldestFrame = candidate.LastUsedFrame;
                    oldestKey = key;
                }
            }

            if (oldestKey is null)
            {
                return;
            }

            TerminalBitmapCacheEntry entry = cache[oldestKey.Value];
            entry.Dispose();
            cacheBytes -= entry.ByteLength;
            cache.Remove(oldestKey.Value);
            RecordImageCacheEviction();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordImagePlacementVisited()
    {
        if (EnableImageRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticImagePlacementsVisited);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordImagePlacementVisible()
    {
        if (EnableImageRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticImagePlacementsVisible);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordImageDraw()
    {
        if (EnableImageRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticImageDraws);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordImageCacheHit()
    {
        if (EnableImageRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticImageCacheHits);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordImageCacheMiss()
    {
        if (EnableImageRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticImageCacheMisses);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordImageCacheEviction()
    {
        if (EnableImageRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticImageCacheEvictions);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (TerminalBitmapCacheEntry entry in _kittyBitmapCache.Values)
        {
            entry.Dispose();
        }

        foreach (TerminalBitmapCacheEntry entry in _rasterBitmapCache.Values)
        {
            entry.Dispose();
        }

        _kittyBitmapCache.Clear();
        _rasterBitmapCache.Clear();
        _kittyBitmapCacheBytes = 0;
        _rasterBitmapCacheBytes = 0;
        _bgPaint.Dispose();
        _fgPaint.Dispose();
        _cursorPaint.Dispose();
        _spritePaint.Dispose();
        _symbolPaint.Dispose();
        _spritePath.Dispose();
        _textShaper.Dispose();
        _fontResolver.Dispose();
        ClearTextRenderCaches();
        _glyphCache.Dispose();
    }

    private readonly record struct TerminalBitmapCacheKey(
        int WidthPx,
        int HeightPx,
        int ByteLength,
        ulong Fingerprint);

    private sealed class TerminalBitmapCacheEntry : IDisposable
    {
        public TerminalBitmapCacheEntry(
            SKBitmap bitmap,
            int byteLength,
            long lastUsedFrame)
        {
            Bitmap = bitmap;
            ByteLength = byteLength;
            LastUsedFrame = lastUsedFrame;
        }

        public SKBitmap Bitmap { get; }
        public int ByteLength { get; }
        public long LastUsedFrame { get; set; }

        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }

    private readonly record struct CompiledTextHighlightRule(
        Regex Regex,
        TextHighlightResolvedColors LightColors,
        TextHighlightResolvedColors DarkColors)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextHighlightResolvedColors GetColors(bool darkTheme) => darkTheme ? DarkColors : LightColors;
    }

    private readonly record struct TextHighlightResolvedColors(
        uint Foreground,
        uint Background,
        bool HasForeground,
        bool HasBackground)
    {
        public bool HasAnyColor => HasForeground || HasBackground;
    }

    private sealed class TextHighlightRowCacheEntry
    {
        public CellTextHighlightOverride[] Overrides = Array.Empty<CellTextHighlightOverride>();
        public ulong RowTextHash;
        public int Columns;
        public int RuleRevision;
        public bool DarkTheme;
    }

    private struct CellTextHighlightOverride
    {
        public uint Foreground;
        public uint Background;
        public bool HasForeground;
        public bool HasBackground;

        public readonly bool HasAnyColor => HasForeground || HasBackground;
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
    private void RecordPretextRun()
    {
        if (EnableTextRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticPretextRuns);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordPretextFallbackRun()
    {
        if (EnableTextRenderDiagnostics)
        {
            Interlocked.Increment(ref _diagnosticPretextFallbackRuns);
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

    private sealed class SingleGlyphIdCache
    {
        private readonly Dictionary<SingleGlyphIdCacheKey, ushort> _cache = new();
        private readonly int _maxEntries;

        public SingleGlyphIdCache(int maxEntries = 4096)
        {
            _maxEntries = Math.Max(64, maxEntries);
        }

        public bool TryGetOrCreate(SingleGlyphIdCacheKey key, SKTypeface typeface, out ushort glyphId)
        {
            if (_cache.TryGetValue(key, out glyphId))
            {
                return glyphId != 0;
            }

            if (_cache.Count >= _maxEntries)
            {
                ClearCore();
            }

            glyphId = typeface.GetGlyph(key.Codepoint);
            _cache[key] = glyphId;
            return glyphId != 0;
        }

        public void Clear()
        {
            ClearCore();
        }

        private void ClearCore()
        {
            _cache.Clear();
        }
    }

    private readonly record struct SingleGlyphIdCacheKey(
        int Codepoint,
        nint TypefaceHandle);

    private sealed class TextRowFontCache
    {
        private readonly Dictionary<TextRowFontCacheKey, SKFont> _cache = new();

        public SKFont GetOrCreate(SKTypeface typeface, float fontSize)
        {
            TextRowFontCacheKey key = new(typeface.Handle, BitConverter.SingleToInt32Bits(fontSize));
            if (_cache.TryGetValue(key, out SKFont? font))
            {
                return font;
            }

            font = GlyphCache.CreateFont(typeface, fontSize);
            _cache[key] = font;
            return font;
        }

        public void Clear()
        {
            foreach (SKFont font in _cache.Values)
            {
                font.Dispose();
            }

            _cache.Clear();
        }
    }

    private readonly record struct TextRowFontCacheKey(
        nint TypefaceHandle,
        int FontSizeBits);

    private sealed class CellTextBlobCache
    {
        private readonly Dictionary<CellTextBlobCacheKey, CachedCellTextBlob> _cache = new();
        private readonly int _maxEntries;

        public CellTextBlobCache(int maxEntries = 4096)
        {
            _maxEntries = Math.Max(64, maxEntries);
        }

        public SKTextBlob? GetOrCreate(CellTextBlobCacheKey key, string text, SKFont font)
        {
            if (_cache.TryGetValue(key, out CachedCellTextBlob? cachedBlob))
            {
                if (text.AsSpan().SequenceEqual(cachedBlob.Text.AsSpan()))
                {
                    return cachedBlob.TextBlob;
                }

                cachedBlob.Dispose();
                _cache.Remove(key);
            }

            if (_cache.Count >= _maxEntries)
            {
                ClearCore();
            }

            SKTextBlob? textBlob = SKTextBlob.Create(text, font, SKPoint.Empty);
            if (textBlob is null)
            {
                return null;
            }

            cachedBlob = new CachedCellTextBlob(text, textBlob);
            _cache[key] = cachedBlob;
            return textBlob;
        }

        public void Clear()
        {
            ClearCore();
        }

        private void ClearCore()
        {
            foreach (CachedCellTextBlob cachedBlob in _cache.Values)
            {
                cachedBlob.Dispose();
            }

            _cache.Clear();
        }
    }

    private readonly record struct CellTextBlobCacheKey(
        ulong TextHash,
        int TextLength,
        nint TypefaceHandle,
        int FontSizeBits);

    private sealed class CachedCellTextBlob : IDisposable
    {
        public CachedCellTextBlob(string text, SKTextBlob textBlob)
        {
            Text = text;
            TextBlob = textBlob;
        }

        public string Text { get; }

        public SKTextBlob TextBlob { get; }

        public void Dispose()
        {
            TextBlob.Dispose();
        }
    }

    private struct SimpleTextRowBatchGroup
    {
        public SKColor Color;
        public nint TypefaceHandle;
        public SKTypeface Typeface;
        public int Count;
    }

#if ROYALTERMINAL_PRETEXT_TEXT_PIPELINE
    private sealed class PretextRunCache
    {
        private readonly Dictionary<PretextRunCacheKey, CachedPretextRun> _cache = new();
        private readonly int _maxEntries;

        public PretextRunCache(int maxEntries = 4096)
        {
            _maxEntries = Math.Max(64, maxEntries);
        }

        public bool TryGet(PretextRunCacheKey key, ReadOnlySpan<char> text, out CachedPretextRun run)
        {
            if (_cache.TryGetValue(key, out run!) && text.SequenceEqual(run.Text.AsSpan()))
            {
                return true;
            }

            run = null!;
            return false;
        }

        public void Store(PretextRunCacheKey key, CachedPretextRun run)
        {
            if (_cache.Count >= _maxEntries)
            {
                ClearCore();
            }

            StoreCore(key, run);
        }

        public void Clear()
        {
            ClearCore();
        }

        private void StoreCore(PretextRunCacheKey key, CachedPretextRun run)
        {
            if (_cache.TryGetValue(key, out CachedPretextRun? existing) &&
                !ReferenceEquals(existing, run))
            {
                existing.Dispose();
            }

            _cache[key] = run;
        }

        private void ClearCore()
        {
            foreach (CachedPretextRun run in _cache.Values)
            {
                run.Dispose();
            }

            _cache.Clear();
        }
    }

    private readonly record struct PretextRunCacheKey(
        ulong TextHash,
        int TextLength,
        nint TypefaceHandle,
        int FontSizeBits,
        TextDirectionMode Direction,
        bool EnableLigatures);

    private sealed class CachedPretextRun : IDisposable
    {
        public CachedPretextRun(
            string text,
            float naturalWidth,
            float clipPadding,
            ushort[] glyphIds,
            SKTextBlob? naturalTextBlob)
        {
            Text = text;
            NaturalWidth = naturalWidth;
            ClipPadding = clipPadding;
            GlyphIds = glyphIds;
            NaturalTextBlob = naturalTextBlob;
        }

        public string Text { get; }

        public float NaturalWidth { get; }

        public float ClipPadding { get; }

        public ushort[] GlyphIds { get; }

        public SKTextBlob? NaturalTextBlob { get; }

        public void Dispose()
        {
            NaturalTextBlob?.Dispose();
        }
    }

    private static class PretextPipelineInitializer
    {
        private static int s_initialized;
        private static int s_unavailable;

        public static bool IsAvailable => TryEnsureInitialized();

        public static bool TryEnsureInitialized()
        {
            if (Volatile.Read(ref s_initialized) != 0)
            {
                return true;
            }

            if (Volatile.Read(ref s_unavailable) != 0)
            {
                return false;
            }

            try
            {
                PretextLayout.SetTextMeasurerFactory(new SkiaSharpTextMeasurerFactory());
                Volatile.Write(ref s_initialized, 1);
                return true;
            }
            catch (FileNotFoundException)
            {
                Volatile.Write(ref s_unavailable, 1);
                return false;
            }
            catch (FileLoadException)
            {
                Volatile.Write(ref s_unavailable, 1);
                return false;
            }
            catch (TypeLoadException)
            {
                Volatile.Write(ref s_unavailable, 1);
                return false;
            }
            catch (InvalidOperationException)
            {
                Volatile.Write(ref s_unavailable, 1);
                return false;
            }
        }
    }
#endif

    [Flags]
    private enum CellOverlayFlags : byte
    {
        None = 0,
        Selection = 1 << 0,
        SearchMatch = 1 << 1,
        SearchSelected = 1 << 2,
        HyperlinkHover = 1 << 3,
    }

    private enum StrokeWeight : byte
    {
        None = 0,
        Light = 1,
        Heavy = 2,
    }

    private enum BoxLineOrientation : byte
    {
        Horizontal = 0,
        Vertical = 1,
    }

    private enum ArcCorner : byte
    {
        DownRight = 0,
        DownLeft = 1,
        UpLeft = 2,
        UpRight = 3,
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
        Symbol = 4,
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
    BlockHollow,
    Underline,
    Bar,
}
