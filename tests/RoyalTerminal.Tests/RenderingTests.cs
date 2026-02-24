// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — GlyphCache and SkiaTerminalRenderer tests.

using RoyalTerminal.Avalonia.Rendering;
using System.Globalization;
using System.Text;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Tests for the rendering infrastructure.
/// </summary>
public class RenderingTests
{
    [Fact]
    public void GlyphCache_CanBeCreated()
    {
        using var cache = new GlyphCache("Consolas");
        Assert.NotNull(cache);
    }

    [Fact]
    public void GlyphCache_HasTypeface()
    {
        using var cache = new GlyphCache("Consolas");
        Assert.NotNull(cache.RegularTypeface);
    }

    [Fact]
    public void GlyphCache_Dispose_CanBeCalledMultipleTimes()
    {
        var cache = new GlyphCache("Consolas");
        cache.Dispose();
        cache.Dispose(); // Should not throw
    }

    [Fact]
    public void GlyphCache_MeasureCellSize_ReturnsPositiveDimensions()
    {
        using var cache = new GlyphCache("Consolas");
        (float width, float height) = cache.MeasureCellSize(14f);
        Assert.True(width > 0);
        Assert.True(height > 0);
    }

    [Fact]
    public void HarfBuzzTypefaceCache_GetOrCreate_ReusesSameEntry()
    {
        using var glyphCache = new GlyphCache("Consolas");
        using var typefaceCache = new HarfBuzzTypefaceCache();

        HarfBuzzTypefaceEntry first = typefaceCache.GetOrCreate(glyphCache.RegularTypeface);
        HarfBuzzTypefaceEntry second = typefaceCache.GetOrCreate(glyphCache.RegularTypeface);

        Assert.Same(first, second);
        Assert.Equal(1, typefaceCache.Count);
    }

    [Fact]
    public void HarfBuzzTypefaceCache_Dispose_CanBeCalledMultipleTimes()
    {
        using var glyphCache = new GlyphCache("Consolas");
        var typefaceCache = new HarfBuzzTypefaceCache();

        _ = typefaceCache.GetOrCreate(glyphCache.RegularTypeface);
        typefaceCache.Dispose();
        typefaceCache.Dispose();
    }

    [Fact]
    public void HarfBuzzTypefaceCache_GetOrCreate_AfterDispose_Throws()
    {
        using var glyphCache = new GlyphCache("Consolas");
        var typefaceCache = new HarfBuzzTypefaceCache();
        typefaceCache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => typefaceCache.GetOrCreate(glyphCache.RegularTypeface));
    }

    [Fact]
    public void HarfBuzzTextShaper_Shape_ReturnsStableGlyphOutput()
    {
        using var glyphCache = new GlyphCache("Consolas");
        using var shaper = new HarfBuzzTextShaper();

        var options = new TextShapingOptions(
            FontSize: 14f,
            Culture: CultureInfo.InvariantCulture,
            Direction: TextDirectionMode.LeftToRight,
            EnableLigatures: false);

        ShapedTextRun first = shaper.Shape("RoyalTerminal.GhosttySharp shaping baseline", glyphCache.RegularTypeface, options);
        ShapedTextRun second = shaper.Shape("RoyalTerminal.GhosttySharp shaping baseline", glyphCache.RegularTypeface, options);

        Assert.True(first.GlyphCount > 0);
        Assert.Equal(first.GlyphCount, second.GlyphCount);
        Assert.Equal(first.TotalAdvanceX, second.TotalAdvanceX, 4);

        for (int i = 0; i < first.GlyphCount; i++)
        {
            ShapedGlyph lhs = first.Glyphs.Span[i];
            ShapedGlyph rhs = second.Glyphs.Span[i];

            Assert.Equal(lhs.GlyphId, rhs.GlyphId);
            Assert.Equal(lhs.Cluster, rhs.Cluster);
            Assert.Equal(lhs.AdvanceX, rhs.AdvanceX, 4);
            Assert.Equal(lhs.OffsetX, rhs.OffsetX, 4);
            Assert.Equal(lhs.OffsetY, rhs.OffsetY, 4);
        }
    }

    [Fact]
    public void HarfBuzzTextShaper_Dispose_CanBeCalledMultipleTimes()
    {
        var shaper = new HarfBuzzTextShaper();
        shaper.Dispose();
        shaper.Dispose();
    }

    [Fact]
    public void HarfBuzzTextShaper_Shape_AfterDispose_Throws()
    {
        using var glyphCache = new GlyphCache("Consolas");
        var shaper = new HarfBuzzTextShaper();
        shaper.Dispose();

        var options = new TextShapingOptions(14f, CultureInfo.InvariantCulture);
        Assert.Throws<ObjectDisposedException>(() => shaper.Shape("A", glyphCache.RegularTypeface, options));
    }

    [Fact]
    public void HarfBuzzTextShaper_Shape_InvalidFontSize_Throws()
    {
        using var glyphCache = new GlyphCache("Consolas");
        using var shaper = new HarfBuzzTextShaper();
        var options = new TextShapingOptions(0f, CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() => shaper.Shape("A", glyphCache.RegularTypeface, options));
    }

    [Fact]
    public void TerminalFontResolver_BaseFontMissingGlyph_ResolvesConsistentlyWithSkiaMatchCharacter()
    {
        const int nonCharacterCodepoint = 0x10FFFE;
        Assert.True(Rune.IsValid(nonCharacterCodepoint));
        VerifyResolverMatchesSkiaForCodepoint(nonCharacterCodepoint, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TerminalFontResolver_EmojiFallback_ResolvesConsistentlyWithSkiaMatchCharacter()
    {
        const int emojiCodepoint = 0x1F642; // 🙂
        VerifyResolverMatchesSkiaForCodepoint(emojiCodepoint, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TerminalFontResolver_RegionalIndicator_PrefersEmojiLanguageTag_WhenAvailable()
    {
        const int regionalIndicatorCodepoint = 0x1F1E8; // 🇨
        using var resolver = new TerminalFontResolver();
        using var baseTypeface = CreateMonospaceTypeface();
        using var manager = SKFontManager.CreateDefault();

        TerminalFontResolution resolution = resolver.ResolveTypeface(
            baseTypeface,
            regionalIndicatorCodepoint,
            CultureInfo.InvariantCulture);

        using var expectedEmojiTypeface = manager.MatchCharacter(
            null,
            baseTypeface.FontStyle,
            ["und-Zsye"],
            regionalIndicatorCodepoint);

        if (expectedEmojiTypeface is not null && expectedEmojiTypeface.Handle != baseTypeface.Handle)
        {
            Assert.True(resolution.UsedFallback);
            Assert.True(resolution.Typeface.ContainsGlyph(regionalIndicatorCodepoint));
            Assert.NotEqual(baseTypeface.Handle, resolution.Typeface.Handle);
            return;
        }

        Assert.True(
            resolution.Typeface.Handle == baseTypeface.Handle ||
            resolution.Typeface.ContainsGlyph(regionalIndicatorCodepoint),
            "Resolution should keep base typeface or return a typeface containing the regional indicator glyph.");
    }

    [Fact]
    public void TerminalFontResolver_FlagSequenceText_UsesRegionalIndicatorResolutionPath()
    {
        const string canadaFlag = "\U0001F1E8\U0001F1E6";

        using var resolver = new TerminalFontResolver();
        using var baseTypeface = CreateMonospaceTypeface();

        TerminalFontResolution fromText = resolver.ResolveTypeface(
            baseTypeface,
            canadaFlag,
            CultureInfo.InvariantCulture);

        TerminalFontResolution fromCodepoint = resolver.ResolveTypeface(
            baseTypeface,
            0x1F1E8,
            CultureInfo.InvariantCulture);

        Assert.Equal(fromCodepoint.UsedFallback, fromText.UsedFallback);
        Assert.Equal(fromCodepoint.Typeface.Handle, fromText.Typeface.Handle);
    }

    [Fact]
    public void TerminalFontResolver_KeycapSequence_ForcesEmojiResolutionAttempt()
    {
        const string keycap = "#\uFE0F\u20E3";

        using var resolver = new TerminalFontResolver();
        using var baseTypeface = CreateMonospaceTypeface();

        Assert.True(baseTypeface.ContainsGlyph('#'));

        _ = resolver.ResolveTypeface(
            baseTypeface,
            keycap,
            CultureInfo.InvariantCulture);

        Assert.True(
            resolver.CachedFallbackCount >= 1,
            "Keycap sequence should trigger the emoji-resolution path even when the base font has '#'.");
    }

    [Fact]
    public void TerminalFontResolver_NonEmojiZwjText_DoesNotForceEmojiLookup()
    {
        const string nonEmojiZwj = "a\u200D";

        using var resolver = new TerminalFontResolver();
        using var baseTypeface = CreateMonospaceTypeface();
        Assert.True(baseTypeface.ContainsGlyph('a'));

        TerminalFontResolution resolution = resolver.ResolveTypeface(
            baseTypeface,
            nonEmojiZwj,
            CultureInfo.InvariantCulture);

        Assert.False(resolution.UsedFallback);
        Assert.Equal(baseTypeface.Handle, resolution.Typeface.Handle);
        Assert.Equal(0, resolver.CachedFallbackCount);
    }

    [Fact]
    public void TerminalFontResolver_CjkFallback_ResolvesConsistentlyWithSkiaMatchCharacter()
    {
        const int cjkCodepoint = 0x4E2D; // 中
        VerifyResolverMatchesSkiaForCodepoint(cjkCodepoint, new CultureInfo("zh-CN"));
    }

    [Fact]
    public void TerminalFontResolver_MixedScriptLineFallback_ResolvesEachCodepointConsistently()
    {
        using var resolver = new TerminalFontResolver();
        using var baseTypeface = FindTypefaceMissingGlyph(0x1F642);
        using var manager = SKFontManager.CreateDefault();

        const string text = "A🙂中";
        foreach (Rune rune in text.EnumerateRunes())
        {
            int codepoint = rune.Value;
            TerminalFontResolution resolution =
                resolver.ResolveTypeface(baseTypeface, codepoint, CultureInfo.InvariantCulture);

            if (baseTypeface.ContainsGlyph(codepoint))
            {
                Assert.Same(baseTypeface, resolution.Typeface);
                Assert.False(resolution.UsedFallback);
                continue;
            }

            using var expected = manager.MatchCharacter(
                baseTypeface.FamilyName,
                baseTypeface.FontStyle,
                ["en-US"],
                codepoint);

            if (expected is null)
            {
                Assert.Same(baseTypeface, resolution.Typeface);
                Assert.False(resolution.UsedFallback);
            }
            else
            {
                Assert.True(resolution.UsedFallback);
                Assert.True(resolution.Typeface.ContainsGlyph(codepoint));
            }
        }
    }

    [Fact]
    public void TerminalFontResolver_StyleAwareResolution_CachesSeparatelyByStyle()
    {
        const int nonCharacterCodepoint = 0x10FFFE;
        using var resolver = new TerminalFontResolver();
        using var regular = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
                            ?? SKTypeface.FromFamilyName(null, SKFontStyle.Normal)
                            ?? throw new InvalidOperationException("Unable to create regular typeface.");
        using var boldOrItalic = SKTypeface.FromFamilyName(regular.FamilyName, SKFontStyle.Bold)
                               ?? SKTypeface.FromFamilyName(regular.FamilyName, SKFontStyle.Italic)
                               ?? SKTypeface.FromFamilyName(regular.FamilyName, SKFontStyle.Normal)
                               ?? throw new InvalidOperationException("Unable to create second styled typeface.");

        Assert.False(regular.ContainsGlyph(nonCharacterCodepoint));
        Assert.False(boldOrItalic.ContainsGlyph(nonCharacterCodepoint));

        _ = resolver.ResolveTypeface(regular, nonCharacterCodepoint, CultureInfo.InvariantCulture);
        _ = resolver.ResolveTypeface(boldOrItalic, nonCharacterCodepoint, CultureInfo.InvariantCulture);

        bool styleDiffers = !regular.FontStyle.Equals(boldOrItalic.FontStyle);
        int expectedMinEntries = styleDiffers ? 2 : 1;

        Assert.True(
            resolver.CachedFallbackCount >= expectedMinEntries,
            "Fallback cache entry count should reflect distinct style keys when styles differ.");
    }

    [Fact]
    public void SkiaTerminalRenderer_CanBeCreated()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        Assert.NotNull(renderer);
    }

    [Fact]
    public void SkiaTerminalRenderer_CellDimensions_ArePositive()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        Assert.True(renderer.CellWidth > 0);
        Assert.True(renderer.CellHeight > 0);
    }

    [Fact]
    public void SkiaTerminalRenderer_CursorVisible_DefaultTrue()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        Assert.True(renderer.CursorVisible);
    }

    [Fact]
    public void SkiaTerminalRenderer_CursorVisible_CanBeSet()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.CursorVisible = false;
        Assert.False(renderer.CursorVisible);
    }

    [Fact]
    public void SkiaTerminalRenderer_Selection_InitiallyNull()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        Assert.Null(renderer.SelectionStart);
        Assert.Null(renderer.SelectionEnd);
    }

    [Fact]
    public void SkiaTerminalRenderer_Selection_CanBeSet()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SelectionStart = (0, 0);
        renderer.SelectionEnd = (10, 5);

        Assert.Equal((0, 0), renderer.SelectionStart);
        Assert.Equal((10, 5), renderer.SelectionEnd);
    }

    [Fact]
    public void SkiaTerminalRenderer_SetCellSize_UpdatesCellDimensions()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);

        renderer.SetCellSize(9.5f, 18.25f);

        Assert.Equal(9.5f, renderer.CellWidth);
        Assert.Equal(18.25f, renderer.CellHeight);
    }

    [Fact]
    public void SkiaTerminalRenderer_SetCellSize_InvalidDimensions_Throws()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);

        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.SetCellSize(0f, 12f));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.SetCellSize(12f, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.SetCellSize(-1f, 12f));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.SetCellSize(12f, -1f));
    }

    [Fact]
    public void SkiaTerminalRenderer_Dispose_CanBeCalledMultipleTimes()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.Dispose();
        renderer.Dispose();
    }

    [Fact]
    public void SkiaTerminalRenderer_TextDiagnostics_DisabledByDefault_RemainsZero()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        using var surface = CreateRenderSurface(renderer, columns: 8, rows: 1);
        var screen = CreateAsciiScreen(columns: 8, rows: 1, text: "PHASE4");

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.False(renderer.EnableTextRenderDiagnostics);
        Assert.Equal(0, diagnostics.ShapedRuns);
        Assert.Equal(0, diagnostics.FallbackRuns);
        Assert.Equal(0, diagnostics.FallbackFontHits);
        Assert.Equal(0, diagnostics.GridClampedRuns);
        Assert.Equal(0, diagnostics.SpriteCells);
        Assert.Equal(0, diagnostics.BoxDrawingSpriteCells);
        Assert.Equal(0, diagnostics.BrailleSpriteCells);
        Assert.Equal(0, diagnostics.BlockSpriteCells);
        Assert.Equal(0, diagnostics.ScanLineSpriteCells);
    }

    [Fact]
    public void SkiaTerminalRenderer_UnsafeGridMapping_UsesFallbackRun()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
        };
        renderer.SetCellSize(1f, renderer.CellHeight);

        using var surface = CreateRenderSurface(renderer, columns: 8, rows: 1);
        var screen = CreateAsciiScreen(columns: 8, rows: 1, text: "GRIDSAFE");

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.True(diagnostics.FallbackRuns > 0);
        Assert.Equal(0, diagnostics.GridClampedRuns);
    }

    [Fact]
    public void SkiaTerminalRenderer_ModerateGridMismatch_UsesClampedPlacement()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
        };
        renderer.SetCellSize(renderer.CellWidth * 0.75f, renderer.CellHeight);

        using var surface = CreateRenderSurface(renderer, columns: 12, rows: 1);
        var screen = CreateAsciiScreen(columns: 12, rows: 1, text: "HELLOWORLD12");

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.True(diagnostics.ShapedRuns > 0);
        Assert.True(diagnostics.GridClampedRuns > 0);
        Assert.Equal(0, diagnostics.FallbackRuns);
    }

    [Fact]
    public void SkiaTerminalRenderer_TextDiagnostics_Reset_ClearsCounters()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
        };

        using var surface = CreateRenderSurface(renderer, columns: 8, rows: 1);
        var screen = CreateAsciiScreen(columns: 8, rows: 1, text: "RESET123");

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics beforeReset = renderer.GetTextRenderDiagnostics(reset: true);
        TextRenderDiagnostics afterReset = renderer.GetTextRenderDiagnostics();

        Assert.True(beforeReset.ShapedRuns > 0 || beforeReset.FallbackRuns > 0);
        Assert.Equal(0, afterReset.ShapedRuns);
        Assert.Equal(0, afterReset.FallbackRuns);
        Assert.Equal(0, afterReset.FallbackFontHits);
        Assert.Equal(0, afterReset.GridClampedRuns);
        Assert.Equal(0, afterReset.SpriteCells);
        Assert.Equal(0, afterReset.BoxDrawingSpriteCells);
        Assert.Equal(0, afterReset.BrailleSpriteCells);
        Assert.Equal(0, afterReset.BlockSpriteCells);
        Assert.Equal(0, afterReset.ScanLineSpriteCells);
    }

    [Fact]
    public void SkiaTerminalRenderer_TextShaping_Disabled_UsesUnshapedPath()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
            EnableTextShaping = false,
        };
        using var surface = CreateRenderSurface(renderer, columns: 8, rows: 1);
        var screen = CreateAsciiScreen(columns: 8, rows: 1, text: "NOSHAPE");

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.False(renderer.EnableTextShaping);
        Assert.Equal(0, diagnostics.ShapedRuns);
        Assert.Equal(0, diagnostics.FallbackRuns);
        Assert.Equal(0, diagnostics.FallbackFontHits);
        Assert.Equal(0, diagnostics.GridClampedRuns);
        Assert.Equal(0, diagnostics.SpriteCells);
        Assert.Equal(0, diagnostics.BoxDrawingSpriteCells);
        Assert.Equal(0, diagnostics.BrailleSpriteCells);
        Assert.Equal(0, diagnostics.BlockSpriteCells);
        Assert.Equal(0, diagnostics.ScanLineSpriteCells);
    }

    [Fact]
    public void SkiaTerminalRenderer_SpriteFallback_CountsLineDrawingAndBrailleCells()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
        };

        using var surface = CreateRenderSurface(renderer, columns: 4, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 4, rows: 1, text: "\u2500\u253C\u2801\u28FF");

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.True(diagnostics.SpriteCells >= 4);
        Assert.True(diagnostics.BoxDrawingSpriteCells >= 2);
        Assert.True(diagnostics.BrailleSpriteCells >= 2);
        Assert.Equal(0, diagnostics.BlockSpriteCells);
        Assert.Equal(0, diagnostics.ScanLineSpriteCells);
    }

    [Fact]
    public void SkiaTerminalRenderer_SpriteFallback_CategorizesSpriteTypes()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
        };

        using var surface = CreateRenderSurface(renderer, columns: 4, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 4, rows: 1, text: "\u253C\u28FF\u2588\u23BA");

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.True(diagnostics.SpriteCells >= 4);
        Assert.True(diagnostics.BoxDrawingSpriteCells >= 1);
        Assert.True(diagnostics.BrailleSpriteCells >= 1);
        Assert.True(diagnostics.BlockSpriteCells >= 1);
        Assert.True(diagnostics.ScanLineSpriteCells >= 1);
    }

    [Fact]
    public void SkiaTerminalRenderer_MixedBoxDrawingMatrix_UsesSpriteFallbackAndDrawsPixels()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
        };

        string[] rows =
        [
            "\u250D\u252F\u2511\u250E\u2530\u2512",
            "\u251D\u253F\u2525\u251E\u2542\u2526",
            "\u2515\u2537\u2519\u2516\u2538\u251A",
            "\u257C\u257D\u257E\u257F\u2520\u2528",
        ];

        TerminalScreen screen = CreateScreenFromRows(rows);
        using var surface = CreateRenderSurface(renderer, columns: screen.Columns, rows: screen.ViewportRows);
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        int expectedBoxCells = 0;
        foreach (string row in rows)
        {
            foreach (Rune _ in row.EnumerateRunes())
            {
                expectedBoxCells++;
            }
        }

        Assert.True(diagnostics.SpriteCells >= expectedBoxCells);
        Assert.True(diagnostics.BoxDrawingSpriteCells >= expectedBoxCells);
        Assert.Equal(0, diagnostics.BrailleSpriteCells);
        Assert.Equal(0, diagnostics.BlockSpriteCells);
        Assert.Equal(0, diagnostics.ScanLineSpriteCells);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int nonBackground = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: 0f,
            endX: screen.Columns * renderer.CellWidth,
            startY: 0f,
            endY: screen.ViewportRows * renderer.CellHeight);

        Assert.True(nonBackground > 0, "Mixed box-drawing matrix should render visible sprite pixels.");
    }

    [Fact]
    public void SkiaTerminalRenderer_DoubleLineMatrix_UsesSpriteFallbackAndDrawsPixels()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
        };

        string[] rows =
        [
            "\u2554\u2566\u2557",
            "\u2560\u256C\u2563",
            "\u255A\u2569\u255D",
        ];

        TerminalScreen screen = CreateScreenFromRows(rows);
        using var surface = CreateRenderSurface(renderer, columns: screen.Columns, rows: screen.ViewportRows);
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.True(diagnostics.SpriteCells >= 9);
        Assert.True(diagnostics.BoxDrawingSpriteCells >= 9);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int nonBackground = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: 0f,
            endX: screen.Columns * renderer.CellWidth,
            startY: 0f,
            endY: screen.ViewportRows * renderer.CellHeight);

        Assert.True(nonBackground > 0, "Double-line matrix should render visible sprite pixels.");
    }

    [Fact]
    public void SkiaTerminalRenderer_ShadeBlockSprites_IncreaseFilledPixelsByDensity()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        using var surface = CreateRenderSurface(renderer, columns: 3, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 3, rows: 1, text: "\u2591\u2592\u2593");

        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        float cellWidth = renderer.CellWidth;
        float cellHeight = renderer.CellHeight;
        int lightPixels = CountNonBackgroundPixels(pixels, startX: 0f, cellWidth, cellHeight);
        int mediumPixels = CountNonBackgroundPixels(pixels, startX: cellWidth, cellWidth, cellHeight);
        int darkPixels = CountNonBackgroundPixels(pixels, startX: cellWidth * 2f, cellWidth, cellHeight);

        Assert.True(lightPixels > 0);
        Assert.True(mediumPixels > 0);
        Assert.True(darkPixels > 0);
        Assert.NotEqual(lightPixels, darkPixels);
    }

    [Theory]
    [InlineData(TerminalUnderlineStyle.Single)]
    [InlineData(TerminalUnderlineStyle.Double)]
    [InlineData(TerminalUnderlineStyle.Curly)]
    [InlineData(TerminalUnderlineStyle.Dotted)]
    [InlineData(TerminalUnderlineStyle.Dashed)]
    public void SkiaTerminalRenderer_StyledUnderlines_DrawBottomBandPixels(TerminalUnderlineStyle style)
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };

        using var styledSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        using var controlSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);

        TerminalScreen styledScreen = CreateDecorationScreen(
            attributes: CellAttributes.Underline,
            underlineStyle: style);
        TerminalScreen controlScreen = CreateDecorationScreen();

        styledSurface.Canvas.Clear(SKColors.Black);
        controlSurface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(styledSurface.Canvas, styledScreen);
        renderer.RenderFull(controlSurface.Canvas, controlScreen);

        using SKImage styledSnapshot = styledSurface.Snapshot();
        using SKPixmap styledPixels = styledSnapshot.PeekPixels();
        using SKImage controlSnapshot = controlSurface.Snapshot();
        using SKPixmap controlPixels = controlSnapshot.PeekPixels();

        float bandStart = Math.Max(0f, renderer.CellHeight - 5f);
        int styledCount = CountNonBackgroundPixelsInRegion(
            styledPixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: bandStart,
            endY: renderer.CellHeight);
        int controlCount = CountNonBackgroundPixelsInRegion(
            controlPixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: bandStart,
            endY: renderer.CellHeight);

        Assert.True(styledCount > controlCount, $"{style} underline should draw pixels in the bottom band.");
    }

    [Fact]
    public void SkiaTerminalRenderer_OverlineDecoration_DrawsTopBandPixels()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };

        using var overlineSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        using var controlSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);

        TerminalScreen overlineScreen = CreateDecorationScreen(decorations: CellDecorations.Overline);
        TerminalScreen controlScreen = CreateDecorationScreen();

        overlineSurface.Canvas.Clear(SKColors.Black);
        controlSurface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(overlineSurface.Canvas, overlineScreen);
        renderer.RenderFull(controlSurface.Canvas, controlScreen);

        using SKImage overlineSnapshot = overlineSurface.Snapshot();
        using SKPixmap overlinePixels = overlineSnapshot.PeekPixels();
        using SKImage controlSnapshot = controlSurface.Snapshot();
        using SKPixmap controlPixels = controlSnapshot.PeekPixels();

        int overlineCount = CountNonBackgroundPixelsInRegion(
            overlinePixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: Math.Min(3f, renderer.CellHeight));
        int controlCount = CountNonBackgroundPixelsInRegion(
            controlPixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: Math.Min(3f, renderer.CellHeight));

        Assert.True(overlineCount > controlCount, "Overline should draw pixels in the top band.");
    }

    [Fact]
    public void SkiaTerminalRenderer_LegacyUnderlineAttribute_UsesSingleUnderlineFallback()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };

        using var underlineSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        using var controlSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);

        TerminalScreen underlineScreen = CreateDecorationScreen(attributes: CellAttributes.Underline);
        TerminalScreen controlScreen = CreateDecorationScreen();

        underlineSurface.Canvas.Clear(SKColors.Black);
        controlSurface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(underlineSurface.Canvas, underlineScreen);
        renderer.RenderFull(controlSurface.Canvas, controlScreen);

        using SKImage underlineSnapshot = underlineSurface.Snapshot();
        using SKPixmap underlinePixels = underlineSnapshot.PeekPixels();
        using SKImage controlSnapshot = controlSurface.Snapshot();
        using SKPixmap controlPixels = controlSnapshot.PeekPixels();

        float bandStart = Math.Max(0f, renderer.CellHeight - 5f);
        int underlineCount = CountNonBackgroundPixelsInRegion(
            underlinePixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: bandStart,
            endY: renderer.CellHeight);
        int controlCount = CountNonBackgroundPixelsInRegion(
            controlPixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: bandStart,
            endY: renderer.CellHeight);

        Assert.True(underlineCount > controlCount, "Legacy underline attribute should draw a single underline.");
    }

    private static void VerifyResolverMatchesSkiaForCodepoint(int codepoint, CultureInfo culture)
    {
        using var resolver = new TerminalFontResolver();
        using var baseTypeface = FindTypefaceMissingGlyph(codepoint);
        using var manager = SKFontManager.CreateDefault();

        TerminalFontResolution resolution = resolver.ResolveTypeface(baseTypeface, codepoint, culture);
        using var expected = manager.MatchCharacter(
            baseTypeface.FamilyName,
            baseTypeface.FontStyle,
            [culture.Name],
            codepoint);

        if (expected is null)
        {
            Assert.Same(baseTypeface, resolution.Typeface);
            Assert.False(resolution.UsedFallback);
        }
        else
        {
            Assert.True(resolution.UsedFallback);
            Assert.True(resolution.Typeface.ContainsGlyph(codepoint));
            Assert.NotEqual(baseTypeface.Handle, resolution.Typeface.Handle);
        }
    }

    private static SKTypeface FindTypefaceMissingGlyph(int codepoint)
    {
        string[] candidates =
        [
            "Consolas",
            "Cascadia Mono",
            "Menlo",
            "Monaco",
            "Courier New",
            "DejaVu Sans Mono",
            "Liberation Mono",
            "Noto Sans Mono",
        ];

        foreach (string family in candidates)
        {
            SKTypeface? candidate = SKTypeface.FromFamilyName(family, SKFontStyle.Normal);
            if (candidate is null)
            {
                continue;
            }

            if (!candidate.ContainsGlyph(codepoint))
            {
                return candidate;
            }

            candidate.Dispose();
        }

        SKTypeface fallback = SKTypeface.FromFamilyName("Monospace", SKFontStyle.Normal)
                            ?? SKTypeface.FromFamilyName(null, SKFontStyle.Normal)
                            ?? throw new InvalidOperationException("Unable to create fallback typeface.");
        return fallback;
    }

    private static SKTypeface CreateMonospaceTypeface()
    {
        string[] candidates =
        [
            "Consolas",
            "Cascadia Mono",
            "Menlo",
            "Monaco",
            "Courier New",
            "DejaVu Sans Mono",
            "Liberation Mono",
            "Noto Sans Mono",
        ];

        foreach (string family in candidates)
        {
            SKTypeface? candidate = SKTypeface.FromFamilyName(family, SKFontStyle.Normal);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return SKTypeface.FromFamilyName("Monospace", SKFontStyle.Normal)
            ?? SKTypeface.FromFamilyName(null, SKFontStyle.Normal)
            ?? throw new InvalidOperationException("Unable to create monospace typeface.");
    }

    private static SKSurface CreateRenderSurface(SkiaTerminalRenderer renderer, int columns, int rows)
    {
        int width = Math.Max(1, (int)Math.Ceiling(columns * renderer.CellWidth));
        int height = Math.Max(1, (int)Math.Ceiling(rows * renderer.CellHeight));
        return SKSurface.Create(new SKImageInfo(width, height));
    }

    private static int CountNonBackgroundPixels(SKPixmap pixels, float startX, float cellWidth, float cellHeight)
    {
        int count = 0;
        int minX = Math.Clamp((int)MathF.Floor(startX), 0, pixels.Width);
        int maxX = Math.Clamp((int)MathF.Ceiling(startX + cellWidth), 0, pixels.Width);
        int maxY = Math.Clamp((int)MathF.Ceiling(cellHeight), 0, pixels.Height);
        for (int y = 0; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                SKColor pixel = pixels.GetPixelColor(x, y);
                if (pixel.Red > 0 || pixel.Green > 0 || pixel.Blue > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountNonBackgroundPixelsInRegion(
        SKPixmap pixels,
        float startX,
        float endX,
        float startY,
        float endY)
    {
        int count = 0;
        int minX = Math.Clamp((int)MathF.Floor(startX), 0, pixels.Width);
        int maxX = Math.Clamp((int)MathF.Ceiling(endX), 0, pixels.Width);
        int minY = Math.Clamp((int)MathF.Floor(startY), 0, pixels.Height);
        int maxY = Math.Clamp((int)MathF.Ceiling(endY), 0, pixels.Height);

        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                SKColor pixel = pixels.GetPixelColor(x, y);
                if (pixel.Red > 0 || pixel.Green > 0 || pixel.Blue > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static TerminalScreen CreateDecorationScreen(
        CellAttributes attributes = CellAttributes.None,
        TerminalUnderlineStyle underlineStyle = TerminalUnderlineStyle.None,
        CellDecorations decorations = CellDecorations.None)
    {
        var screen = new TerminalScreen(1, 1);
        TerminalRow row = screen.GetViewportRow(0);
        ref TerminalCell cell = ref row[0];
        cell.Codepoint = ' ';
        cell.Foreground = 0xFFFFFFFF;
        cell.Background = 0xFF000000;
        cell.Attributes = attributes;
        cell.UnderlineStyle = underlineStyle;
        cell.Decorations = decorations;
        cell.Width = 1;
        row.IsDirty = true;
        return screen;
    }

    private static TerminalScreen CreateScreenFromRows(string[] rows)
    {
        int rowCount = rows.Length;
        int columns = 1;
        for (int row = 0; row < rows.Length; row++)
        {
            int runeCount = 0;
            foreach (Rune _ in rows[row].EnumerateRunes())
            {
                runeCount++;
            }

            if (runeCount > columns)
            {
                columns = runeCount;
            }
        }

        var screen = new TerminalScreen(columns, Math.Max(1, rowCount));
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            TerminalRow row = screen.GetViewportRow(rowIndex);
            int col = 0;
            foreach (Rune rune in rows[rowIndex].EnumerateRunes())
            {
                if (col >= columns)
                {
                    break;
                }

                row[col].Codepoint = rune.Value;
                row[col].Foreground = 0xFFFFFFFF;
                row[col].Background = 0xFF000000;
                row[col].Attributes = CellAttributes.None;
                row[col].Width = 1;
                col++;
            }

            row.IsDirty = true;
        }

        return screen;
    }

    private static TerminalScreen CreateAsciiScreen(int columns, int rows, string text)
    {
        var screen = new TerminalScreen(columns, rows);
        TerminalRow row = screen.GetViewportRow(0);

        int col = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (col >= columns)
            {
                break;
            }

            row[col].Codepoint = rune.Value;
            row[col].Foreground = 0xFFFFFFFF;
            row[col].Background = 0xFF000000;
            row[col].Attributes = CellAttributes.None;
            row[col].Width = 1;
            col++;
        }

        row.IsDirty = true;
        return screen;
    }
}
