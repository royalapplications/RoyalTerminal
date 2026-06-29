// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — GlyphCache and SkiaTerminalRenderer tests.

using RoyalTerminal.Avalonia.Rendering;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
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
    public void GlyphCache_MeasureCellSize_UsesRoundedDigitZeroAdvance()
    {
        using var cache = new GlyphCache("Consolas");
        using SKFont font = cache.CreateFont(14f);

        (float width, float height) = cache.MeasureCellSize(14f);
        float expectedWidth = MathF.Max(1f, MathF.Round(font.MeasureText("0"), MidpointRounding.AwayFromZero));
        float expectedHeight = MathF.Max(
            1f,
            MathF.Round(
                font.Metrics.Descent - font.Metrics.Ascent + font.Metrics.Leading,
                MidpointRounding.AwayFromZero));

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
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
    public void SkiaTerminalRenderer_TextHighlightRules_ApplyConfiguredForegroundAndBackground()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SetCellSize(16f, 20f);
        renderer.SetTextHighlightRules(
        [
            new TerminalTextHighlightRule
            {
                Name = "Error",
                Pattern = "ERR",
                Foreground = 0xFF00FF00,
                Background = 0xFFFF0000,
            },
        ]);

        TerminalScreen screen = CreateAsciiScreen(columns: 6, rows: 1, text: "ERROR!");
        using SKSurface surface = CreateRenderSurface(renderer, columns: 6, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int highlightedBackgroundPixels = CountRedDominantPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth * 3,
            startY: 0f,
            endY: renderer.CellHeight);
        int unhighlightedBackgroundPixels = CountRedDominantPixelsInRegion(
            pixels,
            startX: renderer.CellWidth * 3,
            endX: renderer.CellWidth * 6,
            startY: 0f,
            endY: renderer.CellHeight);
        int highlightedForegroundPixels = CountGreenDominantPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth * 3,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(highlightedBackgroundPixels > 100);
        Assert.True(highlightedForegroundPixels > 0);
        Assert.True(unhighlightedBackgroundPixels < highlightedBackgroundPixels / 4);
    }

    [Fact]
    public void SkiaTerminalRenderer_TextHighlightRules_KeepOriginalBackgroundWhenBackgroundUnset()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SetCellSize(16f, 20f);
        renderer.SetTextHighlightRules(
        [
            new TerminalTextHighlightRule
            {
                Name = "Only foreground",
                Pattern = "OK",
                Foreground = 0xFFFF0000,
            },
        ]);

        TerminalScreen screen = CreateAsciiScreen(columns: 2, rows: 1, text: "OK");
        using SKSurface surface = CreateRenderSurface(renderer, columns: 2, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int foregroundPixels = CountRedDominantPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth * 2,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(foregroundPixels > 0);
        Assert.True(foregroundPixels < renderer.CellWidth * renderer.CellHeight);
    }

    [Theory]
    [InlineData(@"\b(ERROR|WARN)\b", "WARN")]
    [InlineData("ERROR|WARN", "WARN")]
    public void SkiaTerminalRenderer_TextHighlightRules_HandleAlternationPatterns(
        string pattern,
        string text)
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SetCellSize(16f, 20f);
        renderer.SetTextHighlightRules(
        [
            new TerminalTextHighlightRule
            {
                Name = "Regex semantics",
                Pattern = pattern,
                Background = 0xFFFF0000,
            },
        ]);

        TerminalScreen screen = CreateAsciiScreen(columns: text.Length, rows: 1, text: text);
        using SKSurface surface = CreateRenderSurface(renderer, columns: text.Length, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int redPixels = CountRedDominantPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth * text.Length,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(redPixels > 100);
    }

    [Fact]
    public void SkiaTerminalRenderer_TextHighlightRules_InvalidRegexIsIgnored()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SetTextHighlightRules(
        [
            new TerminalTextHighlightRule
            {
                Name = "Broken",
                Pattern = "[",
                Background = 0xFFFF0000,
            },
        ]);

        TerminalScreen screen = CreateAsciiScreen(columns: 2, rows: 1, text: "OK");
        using SKSurface surface = CreateRenderSurface(renderer, columns: 2, rows: 1);

        renderer.RenderFull(surface.Canvas, screen);

        Assert.Single(renderer.TextHighlightRules);
    }

    [Fact]
    public void SkiaTerminalRenderer_PreparedTextHighlightRules_ApplyConfiguredBackground()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SetCellSize(16f, 20f);
        SkiaTerminalRenderer.PreparedTerminalTextHighlightRules? preparedRules =
            SkiaTerminalRenderer.PrepareTextHighlightRules(
            [
                new TerminalTextHighlightRule
                {
                    Name = "Prepared",
                    Pattern = "OK",
                    Background = 0xFFFF0000,
                },
            ]);

        Assert.NotNull(preparedRules);
        renderer.SetPreparedTextHighlightRules(preparedRules);

        TerminalScreen screen = CreateAsciiScreen(columns: 2, rows: 1, text: "OK");
        using SKSurface surface = CreateRenderSurface(renderer, columns: 2, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int redPixels = CountRedDominantPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth * 2,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(redPixels > 100);
    }

    [Fact]
    public void TerminalControl_SetPreparedTextHighlightRules_UpdatesAndClearsRuleSnapshot()
    {
        TerminalControl control = new();
        SkiaTerminalRenderer.PreparedTerminalTextHighlightRules? preparedRules =
            SkiaTerminalRenderer.PrepareTextHighlightRules(
            [
                new TerminalTextHighlightRule
                {
                    Name = "Prepared",
                    Pattern = "OK",
                    Background = 0xFFFF0000,
                },
            ]);

        Assert.NotNull(preparedRules);
        control.SetPreparedTextHighlightRules(preparedRules);

        TerminalTextHighlightRule rule = Assert.Single(control.TextHighlightRules!);
        Assert.Equal("Prepared", rule.Name);

        control.SetPreparedTextHighlightRules(null);

        Assert.Null(control.TextHighlightRules);
    }

    [Fact]
    public void SkiaTerminalRenderer_TextHighlightingModeDisabled_SkipsConfiguredRules()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SetCellSize(16f, 20f);
        renderer.TextHighlightingMode = TerminalTextHighlightingMode.Disabled;
        renderer.SetTextHighlightRules(
        [
            new TerminalTextHighlightRule
            {
                Name = "Disabled",
                Pattern = "OK",
                Background = 0xFFFF0000,
            },
        ]);

        TerminalScreen screen = CreateAsciiScreen(columns: 2, rows: 1, text: "OK");
        using SKSurface surface = CreateRenderSurface(renderer, columns: 2, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int redPixels = CountRedDominantPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth * 2,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(redPixels < 10);
    }

    [Fact]
    public void SkiaTerminalRenderer_TextHighlightingModeStatic_InvalidatesCacheWhenRowTextChanges()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SetCellSize(16f, 20f);
        renderer.TextHighlightingMode = TerminalTextHighlightingMode.Static;
        renderer.SetTextHighlightRules(
        [
            new TerminalTextHighlightRule
            {
                Name = "Static cache",
                Pattern = "OK",
                Background = 0xFFFF0000,
            },
        ]);

        TerminalScreen screen = CreateAsciiScreen(columns: 2, rows: 1, text: "OK");
        using SKSurface surface = CreateRenderSurface(renderer, columns: 2, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage firstSnapshot = surface.Snapshot();
        using SKPixmap firstPixels = firstSnapshot.PeekPixels();
        int firstRedPixels = CountRedDominantPixelsInRegion(
            firstPixels,
            startX: 0f,
            endX: renderer.CellWidth * 2,
            startY: 0f,
            endY: renderer.CellHeight);

        TerminalRow row = screen.GetViewportRow(0);
        row[0].Codepoint = 'N';
        row[1].Codepoint = 'O';
        row.IsDirty = true;
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage secondSnapshot = surface.Snapshot();
        using SKPixmap secondPixels = secondSnapshot.PeekPixels();
        int secondRedPixels = CountRedDominantPixelsInRegion(
            secondPixels,
            startX: 0f,
            endX: renderer.CellWidth * 2,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(firstRedPixels > 100);
        Assert.True(secondRedPixels < 10);
    }

    [Fact]
    public void SkiaTerminalRenderer_KittyGraphicsPlacement_RendersImageLayer()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SetCellSize(20f, 20f);

        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: string.Empty);
        screen.ReplaceKittyGraphics(
            images:
            [
                new TerminalKittyImageSource(
                    imageId: 1,
                    widthPx: 1,
                    heightPx: 1,
                    rgbaPixels: [0xFF, 0x00, 0x00, 0xFF]),
            ],
            placements:
            [
                new TerminalKittyImagePlacement(
                    imageId: 1,
                    layer: TerminalKittyImageLayer.AboveText,
                    viewportColumn: 0,
                    viewportRow: 0,
                    xOffsetPx: 0,
                    yOffsetPx: 0,
                    widthPx: 20,
                    heightPx: 20,
                    sourceX: 0,
                    sourceY: 0,
                    sourceWidth: 1,
                    sourceHeight: 1),
            ]);

        using SKSurface surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int imageInk = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: renderer.CellHeight);
        Assert.True(imageInk > 0);
    }

    [Fact]
    public void SkiaTerminalRenderer_KittyGraphicsPlacement_ScalesFromPlacementCellMetrics()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(20f, 20f);

        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: " ");
        screen.ReplaceKittyGraphics(
            images:
            [
                new TerminalKittyImageSource(
                    imageId: 1,
                    widthPx: 1,
                    heightPx: 1,
                    rgbaPixels: [0xFF, 0x00, 0x00, 0xFF]),
            ],
            placements:
            [
                new TerminalKittyImagePlacement(
                    imageId: 1,
                    layer: TerminalKittyImageLayer.AboveText,
                    viewportColumn: 0,
                    viewportRow: 0,
                    xOffsetPx: 0,
                    yOffsetPx: 0,
                    widthPx: 10,
                    heightPx: 10,
                    sourceX: 0,
                    sourceY: 0,
                    sourceWidth: 1,
                    sourceHeight: 1,
                    cellWidthPx: 10,
                    cellHeightPx: 10,
                    scaleMode: TerminalKittyImagePlacementScaleMode.ColumnsAndRows),
            ]);

        using SKSurface surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int scaledInk = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: renderer.CellWidth * 0.6f,
            endX: renderer.CellWidth * 0.9f,
            startY: renderer.CellHeight * 0.6f,
            endY: renderer.CellHeight * 0.9f);
        Assert.True(scaledInk > 0);
    }

    [Fact]
    public void SkiaTerminalRenderer_KittyGraphicsPlacement_DoesNotScaleNaturalPixelPlacement()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(20f, 20f);

        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: " ");
        screen.ReplaceKittyGraphics(
            images:
            [
                new TerminalKittyImageSource(
                    imageId: 1,
                    widthPx: 1,
                    heightPx: 1,
                    rgbaPixels: [0xFF, 0x00, 0x00, 0xFF]),
            ],
            placements:
            [
                new TerminalKittyImagePlacement(
                    imageId: 1,
                    layer: TerminalKittyImageLayer.AboveText,
                    viewportColumn: 0,
                    viewportRow: 0,
                    xOffsetPx: 0,
                    yOffsetPx: 0,
                    widthPx: 10,
                    heightPx: 10,
                    sourceX: 0,
                    sourceY: 0,
                    sourceWidth: 1,
                    sourceHeight: 1,
                    cellWidthPx: 10,
                    cellHeightPx: 10),
            ]);

        Assert.Equal(TerminalKittyImagePlacementScaleMode.None, screen.GetKittyPlacements()[0].ScaleMode);

        using SKSurface surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int unscaledOutsideInk = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: renderer.CellWidth * 0.6f,
            endX: renderer.CellWidth * 0.9f,
            startY: renderer.CellHeight * 0.6f,
            endY: renderer.CellHeight * 0.9f);
        Assert.Equal(0, unscaledOutsideInk);
    }

    [Fact]
    public void SkiaTerminalRenderer_RasterGraphicsPlacement_RendersImageLayer()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SetCellSize(20f, 20f);

        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: string.Empty);
        int imageId = screen.AllocateRasterImageId();
        screen.ReplaceRasterImage(
            new TerminalRasterImageSource(
                imageId,
                TerminalRasterImageProtocol.Sixel,
                widthPx: 1,
                heightPx: 1,
                rgbaPixels: [0x00, 0xFF, 0x00, 0xFF]),
            new TerminalRasterImagePlacement(
                imageId,
                TerminalRasterImageLayer.AboveText,
                anchorColumn: 0,
                anchorRow: 0,
                xOffsetPx: 0,
                yOffsetPx: 0,
                widthPx: 20,
                heightPx: 20,
                sourceX: 0,
                sourceY: 0,
                sourceWidth: 1,
                sourceHeight: 1,
                cellWidthPx: 20,
                cellHeightPx: 20));

        using SKSurface surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int imageInk = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: renderer.CellHeight);
        Assert.True(imageInk > 0);
    }

    [Fact]
    public void SkiaTerminalRenderer_RasterGraphicsPlacement_ScalesFromPlacementCellMetrics()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SetCellSize(20f, 20f);

        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: string.Empty);
        int imageId = screen.AllocateRasterImageId();
        screen.ReplaceRasterImage(
            new TerminalRasterImageSource(
                imageId,
                TerminalRasterImageProtocol.Sixel,
                widthPx: 1,
                heightPx: 1,
                rgbaPixels: [0x00, 0xFF, 0x00, 0xFF]),
            new TerminalRasterImagePlacement(
                imageId,
                TerminalRasterImageLayer.AboveText,
                anchorColumn: 0,
                anchorRow: 0,
                xOffsetPx: 0,
                yOffsetPx: 0,
                widthPx: 10,
                heightPx: 10,
                sourceX: 0,
                sourceY: 0,
                sourceWidth: 1,
                sourceHeight: 1,
                cellWidthPx: 10,
                cellHeightPx: 10));

        using SKSurface surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int scaledInk = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: renderer.CellWidth * 0.6f,
            endX: renderer.CellWidth * 0.9f,
            startY: renderer.CellHeight * 0.6f,
            endY: renderer.CellHeight * 0.9f);
        Assert.True(scaledInk > 0);
    }

    [Fact]
    public void SkiaTerminalRenderer_RasterGraphics_CullsOffscreenAndDeduplicatesBitmapCache()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableImageRenderDiagnostics = true,
        };
        renderer.SetCellSize(10f, 10f);

        TerminalScreen screen = CreateAsciiScreen(columns: 100, rows: 5, text: string.Empty);
        for (int i = 0; i < 100; i++)
        {
            screen.AddRow();
        }

        byte[] repeatedRgba = [0x00, 0xFF, 0x00, 0xFF];
        for (int rowIndex = 0; rowIndex < 100; rowIndex++)
        {
            int imageId = screen.AllocateRasterImageId();
            screen.ReplaceRasterImage(
                new TerminalRasterImageSource(
                    imageId,
                    TerminalRasterImageProtocol.Sixel,
                    widthPx: 1,
                    heightPx: 1,
                    rgbaPixels: repeatedRgba.ToArray()),
                new TerminalRasterImagePlacement(
                    imageId,
                    TerminalRasterImageLayer.AboveText,
                    anchorColumn: 0,
                    anchorRow: rowIndex,
                    xOffsetPx: 0,
                    yOffsetPx: 0,
                    widthPx: 10,
                    heightPx: 10,
                    sourceX: 0,
                    sourceY: 0,
                    sourceWidth: 1,
                    sourceHeight: 1,
                    cellWidthPx: 10,
                    cellHeightPx: 10));
        }

        screen.ScrollOffset = screen.TotalRows - screen.ViewportRows - 50;
        using SKSurface surface = CreateRenderSurface(renderer, columns: 100, rows: 5);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        ImageRenderDiagnostics diagnostics = renderer.GetImageRenderDiagnostics();

        Assert.Equal(100, diagnostics.PlacementsVisited);
        Assert.Equal(5, diagnostics.PlacementsVisible);
        Assert.Equal(5, diagnostics.Draws);
        Assert.Equal(1, diagnostics.CacheMisses);
        Assert.Equal(4, diagnostics.CacheHits);
        Assert.Equal(1, diagnostics.RasterBitmapCacheEntries);
    }

    [Fact]
    public void SkiaTerminalRenderer_KittyGraphics_CullsOffscreenAndDeduplicatesBitmapCache()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableImageRenderDiagnostics = true,
        };
        renderer.SetCellSize(10f, 10f);

        TerminalScreen screen = CreateAsciiScreen(columns: 100, rows: 5, text: string.Empty);
        var images = new TerminalKittyImageSource[100];
        var placements = new TerminalKittyImagePlacement[100];
        byte[] repeatedRgba = [0xFF, 0x00, 0x00, 0xFF];
        for (int i = 0; i < 100; i++)
        {
            int imageId = i + 1;
            images[i] = new TerminalKittyImageSource(
                imageId,
                widthPx: 1,
                heightPx: 1,
                rgbaPixels: repeatedRgba.ToArray());
            placements[i] = new TerminalKittyImagePlacement(
                imageId,
                TerminalKittyImageLayer.AboveText,
                viewportColumn: 0,
                viewportRow: i - 50,
                xOffsetPx: 0,
                yOffsetPx: 0,
                widthPx: 10,
                heightPx: 10,
                sourceX: 0,
                sourceY: 0,
                sourceWidth: 1,
                sourceHeight: 1);
        }

        screen.ReplaceKittyGraphics(images, placements);
        using SKSurface surface = CreateRenderSurface(renderer, columns: 100, rows: 5);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        ImageRenderDiagnostics diagnostics = renderer.GetImageRenderDiagnostics();

        Assert.Equal(100, diagnostics.PlacementsVisited);
        Assert.Equal(5, diagnostics.PlacementsVisible);
        Assert.Equal(5, diagnostics.Draws);
        Assert.Equal(1, diagnostics.CacheMisses);
        Assert.Equal(4, diagnostics.CacheHits);
        Assert.Equal(1, diagnostics.KittyBitmapCacheEntries);
    }

    [Fact]
    public void SkiaTerminalRenderer_BlockHollowCursor_DrawsBorderWithoutFillingCenter()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = true,
            CursorStyle = CursorStyle.BlockHollow,
            CursorColumn = 0,
            CursorRow = 0,
            CursorColor = SKColors.White,
        };

        renderer.SetCellSize(24f, 24f);
        using var hollowSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        using var blockSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: string.Empty);
        hollowSurface.Canvas.Clear(SKColors.Black);
        blockSurface.Canvas.Clear(SKColors.Black);

        renderer.CursorStyle = CursorStyle.BlockHollow;
        renderer.RenderFull(hollowSurface.Canvas, screen);
        renderer.CursorStyle = CursorStyle.Block;
        renderer.RenderFull(blockSurface.Canvas, screen);

        using SKImage hollowSnapshot = hollowSurface.Snapshot();
        using SKPixmap hollowPixels = hollowSnapshot.PeekPixels();
        using SKImage blockSnapshot = blockSurface.Snapshot();
        using SKPixmap blockPixels = blockSnapshot.PeekPixels();

        int hollowBorderInk = CountBrightPixelsInRegion(
            hollowPixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: renderer.CellHeight);
        int hollowCenterInk = CountNonBackgroundPixelsInRegion(
            hollowPixels,
            startX: renderer.CellWidth * 0.25f,
            endX: renderer.CellWidth * 0.75f,
            startY: renderer.CellHeight * 0.25f,
            endY: renderer.CellHeight * 0.75f);
        int hollowCenterBrightInk = CountBrightPixelsInRegion(
            hollowPixels,
            startX: renderer.CellWidth * 0.25f,
            endX: renderer.CellWidth * 0.75f,
            startY: renderer.CellHeight * 0.25f,
            endY: renderer.CellHeight * 0.75f);
        int blockCenterBrightInk = CountBrightPixelsInRegion(
            blockPixels,
            startX: renderer.CellWidth * 0.25f,
            endX: renderer.CellWidth * 0.75f,
            startY: renderer.CellHeight * 0.25f,
            endY: renderer.CellHeight * 0.75f);

        Assert.True(hollowBorderInk > 0);
        Assert.True(hollowCenterInk > 0);
        Assert.Equal(0, hollowCenterBrightInk);
        Assert.True(blockCenterBrightInk > 0);
    }

    [Fact]
    public void SkiaTerminalRenderer_WideTailCursor_RendersOverLeadingWideCell()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = true,
            CursorStyle = CursorStyle.Block,
            CursorColumn = 1,
            CursorRow = 0,
            CursorColor = SKColors.White,
        };

        renderer.SetCellSize(20f, 20f);
        using var surface = CreateRenderSurface(renderer, columns: 3, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 3, rows: 1, text: string.Empty);

        TerminalRow row = screen.GetViewportRow(0);
        row[0].Codepoint = 0;
        row[0].Width = 2;
        row[1].Codepoint = 0;
        row[1].Width = 0;
        row[2].Codepoint = 0;
        row[2].Width = 1;
        row.IsDirty = true;

        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        int firstCellInk = CountBrightPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: renderer.CellHeight);
        int secondCellInk = CountBrightPixelsInRegion(
            pixels,
            startX: renderer.CellWidth,
            endX: renderer.CellWidth * 2f,
            startY: 0f,
            endY: renderer.CellHeight);
        int thirdCellInk = CountBrightPixelsInRegion(
            pixels,
            startX: renderer.CellWidth * 2f,
            endX: renderer.CellWidth * 3f,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(firstCellInk > 0);
        Assert.True(secondCellInk > 0);
        Assert.Equal(0, thirdCellInk);
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
        renderer.SelectionIsRectangle = true;

        Assert.Equal((0, 0), renderer.SelectionStart);
        Assert.Equal((10, 5), renderer.SelectionEnd);
        Assert.True(renderer.SelectionIsRectangle);
    }

    [Fact]
    public void SkiaTerminalRenderer_HighlightLayering_SelectionWinsOverSearch()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
            SelectionColor = new SKColor(0x10, 0x20, 0xE0, 0xFF),
            SearchHighlightColor = new SKColor(0x20, 0xD0, 0x20, 0xFF),
            SearchSelectedHighlightColor = new SKColor(0xE0, 0xA0, 0x20, 0xFF),
        };

        TerminalScreen screen = CreateAsciiScreen(columns: 3, rows: 1, text: string.Empty);
        renderer.SelectionStart = (0, 0);
        renderer.SelectionEnd = (1, 0); // end-exclusive -> selects column 0
        renderer.SetHighlightSpans(
        [
            new TerminalHighlightSpan(0, 0, 0, TerminalHighlightKind.SearchSelected),
            new TerminalHighlightSpan(0, 1, 1, TerminalHighlightKind.SearchMatch),
        ]);

        using var surface = CreateRenderSurface(renderer, columns: 3, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        SKColor selectedCell = pixels.GetPixelColor(
            (int)MathF.Floor(renderer.CellWidth * 0.5f),
            (int)MathF.Floor(renderer.CellHeight * 0.5f));
        SKColor searchCell = pixels.GetPixelColor(
            (int)MathF.Floor(renderer.CellWidth * 1.5f),
            (int)MathF.Floor(renderer.CellHeight * 0.5f));

        Assert.True(selectedCell.Blue > selectedCell.Red);
        Assert.True(searchCell.Green > searchCell.Red);
    }

    [Fact]
    public void SkiaTerminalRenderer_SelectionForegroundColor_OverridesSelectedCellText()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 18f)
        {
            CursorVisible = false,
            SelectionColor = SKColors.Black,
            SelectionForegroundColor = SKColors.Lime,
            SelectionStart = (0, 0),
            SelectionEnd = (1, 0),
        };

        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: "A");
        ref TerminalCell cell = ref screen.GetViewportRow(0)[0];
        cell.Foreground = 0xFFFF0000u;
        cell.Background = 0xFF000000u;
        cell.HasBackground = true;

        using var surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        int greenInk = CountPixelsInCell(
            pixels,
            renderer,
            column: 0,
            pixel => pixel.Green > pixel.Red && pixel.Green > pixel.Blue && pixel.Green > 24);
        int redInk = CountPixelsInCell(
            pixels,
            renderer,
            column: 0,
            pixel => pixel.Red > pixel.Green && pixel.Red > pixel.Blue && pixel.Red > 24);

        Assert.True(greenInk > 0);
        Assert.Equal(0, redInk);
    }

    [Fact]
    public void SkiaTerminalRenderer_RectangularSelection_RestrictsMiddleRowsToColumnBand()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
            SelectionColor = new SKColor(0x10, 0x20, 0xE0, 0xFF),
            SelectionStart = (1, 0),
            SelectionEnd = (3, 1),
            SelectionIsRectangle = true,
        };

        TerminalScreen screen = CreateAsciiScreen(columns: 4, rows: 2, text: string.Empty);
        using var surface = CreateRenderSurface(renderer, columns: 4, rows: 2);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        SKColor outsideBand = pixels.GetPixelColor(
            (int)MathF.Floor(renderer.CellWidth * 0.5f),
            (int)MathF.Floor(renderer.CellHeight * 1.5f));
        SKColor insideBand = pixels.GetPixelColor(
            (int)MathF.Floor(renderer.CellWidth * 1.5f),
            (int)MathF.Floor(renderer.CellHeight * 1.5f));
        SKColor insideBandEnd = pixels.GetPixelColor(
            (int)MathF.Floor(renderer.CellWidth * 2.5f),
            (int)MathF.Floor(renderer.CellHeight * 1.5f));
        SKColor outsideBandEnd = pixels.GetPixelColor(
            (int)MathF.Floor(renderer.CellWidth * 3.5f),
            (int)MathF.Floor(renderer.CellHeight * 1.5f));

        Assert.True(outsideBand.Blue < insideBand.Blue);
        Assert.True(insideBand.Blue > insideBand.Red);
        Assert.True(insideBandEnd.Blue > insideBandEnd.Red);
        Assert.True(outsideBandEnd.Blue < insideBandEnd.Blue);
    }

    [Fact]
    public void SkiaTerminalRenderer_RectangularSelection_NormalizesReversedColumns()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
            SelectionColor = new SKColor(0x10, 0x20, 0xE0, 0xFF),
            SelectionStart = (3, 0),
            SelectionEnd = (1, 1),
            SelectionIsRectangle = true,
        };

        TerminalScreen screen = CreateAsciiScreen(columns: 4, rows: 2, text: string.Empty);
        using var surface = CreateRenderSurface(renderer, columns: 4, rows: 2);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        SKColor outsideBand = pixels.GetPixelColor(
            (int)MathF.Floor(renderer.CellWidth * 0.5f),
            (int)MathF.Floor(renderer.CellHeight * 1.5f));
        SKColor insideBand = pixels.GetPixelColor(
            (int)MathF.Floor(renderer.CellWidth * 1.5f),
            (int)MathF.Floor(renderer.CellHeight * 1.5f));

        Assert.True(outsideBand.Blue < insideBand.Blue);
        Assert.True(insideBand.Blue > insideBand.Red);
    }

    [Fact]
    public void SkiaTerminalRenderer_HyperlinkHover_PromotesUnderline()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
            EnableTextShaping = false,
        };

        using var hoverSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        using var controlSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);

        TerminalScreen hoverScreen = CreateDecorationScreen();
        TerminalScreen controlScreen = CreateDecorationScreen();
        renderer.SetHighlightSpans(
        [
            new TerminalHighlightSpan(0, 0, 0, TerminalHighlightKind.HyperlinkHover),
        ]);
        hoverSurface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(hoverSurface.Canvas, hoverScreen);

        renderer.SetHighlightSpans(Array.Empty<TerminalHighlightSpan>());
        controlSurface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(controlSurface.Canvas, controlScreen);

        using SKImage hoverSnapshot = hoverSurface.Snapshot();
        using SKPixmap hoverPixels = hoverSnapshot.PeekPixels();
        using SKImage controlSnapshot = controlSurface.Snapshot();
        using SKPixmap controlPixels = controlSnapshot.PeekPixels();

        float bandStart = Math.Max(0f, renderer.CellHeight - 5f);
        int hoverUnderlinePixels = CountNonBackgroundPixelsInRegion(
            hoverPixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: bandStart,
            endY: renderer.CellHeight);
        int controlUnderlinePixels = CountNonBackgroundPixelsInRegion(
            controlPixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: bandStart,
            endY: renderer.CellHeight);

        Assert.True(hoverUnderlinePixels > controlUnderlinePixels);
    }

    [Fact]
    public void SkiaTerminalRenderer_ExplicitUnderlineColor_IsRendered()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
            EnableTextShaping = false,
        };

        TerminalScreen coloredScreen = CreateDecorationScreen(
            attributes: CellAttributes.Underline,
            underlineStyle: TerminalUnderlineStyle.Single);
        TerminalScreen controlScreen = CreateDecorationScreen(
            attributes: CellAttributes.Underline,
            underlineStyle: TerminalUnderlineStyle.Single);

        ref TerminalCell cell = ref coloredScreen.GetViewportRow(0)[0];
        cell.UnderlineColor = 0xFFFF0000;
        cell.HasUnderlineColor = true;
        coloredScreen.GetViewportRow(0).IsDirty = true;

        using var coloredSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        using var controlSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        coloredSurface.Canvas.Clear(SKColors.Black);
        controlSurface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(coloredSurface.Canvas, coloredScreen);
        renderer.RenderFull(controlSurface.Canvas, controlScreen);

        using SKImage coloredSnapshot = coloredSurface.Snapshot();
        using SKPixmap coloredPixels = coloredSnapshot.PeekPixels();
        using SKImage controlSnapshot = controlSurface.Snapshot();
        using SKPixmap controlPixels = controlSnapshot.PeekPixels();

        float bandStart = Math.Max(0f, renderer.CellHeight - 5f);
        int coloredRedDominant = CountRedDominantPixelsInRegion(
            coloredPixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: bandStart,
            endY: renderer.CellHeight);
        int controlRedDominant = CountRedDominantPixelsInRegion(
            controlPixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: bandStart,
            endY: renderer.CellHeight);

        Assert.True(coloredRedDominant > controlRedDominant);
    }

    [Fact]
    public void SkiaTerminalRenderer_BackgroundOpacityHeuristics_RespectHasBackground()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
            BackgroundOpacityEnabled = true,
            BackgroundOpacityCells = true,
            BackgroundOpacity = 0.5f,
        };

        TerminalScreen screen = CreateAsciiScreen(columns: 2, rows: 1, text: string.Empty);
        TerminalRow row = screen.GetViewportRow(0);
        row[0].Background = 0xFF0000FF;
        row[0].HasBackground = false;
        row[1].Background = 0xFF0000FF;
        row[1].HasBackground = true;
        row.IsDirty = true;

        using var surface = CreateRenderSurface(renderer, columns: 2, rows: 1);
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        SKColor noBackgroundCell = pixels.GetPixelColor(
            (int)MathF.Floor(renderer.CellWidth * 0.5f),
            (int)MathF.Floor(renderer.CellHeight * 0.5f));
        SKColor explicitBackgroundCell = pixels.GetPixelColor(
            (int)MathF.Floor(renderer.CellWidth * 1.5f),
            (int)MathF.Floor(renderer.CellHeight * 0.5f));

        Assert.True(noBackgroundCell.Blue < 10);
        Assert.InRange(explicitBackgroundCell.Blue, 80, 170);
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
        Assert.Equal(0, diagnostics.PretextRuns);
        Assert.Equal(0, diagnostics.PretextFallbackRuns);
    }

    [Fact]
    public void SkiaTerminalRenderer_ImageDiagnostics_DisabledByDefault_RemainsZero()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f);

        Assert.False(renderer.EnableImageRenderDiagnostics);
        Assert.True(renderer.ImageBitmapCacheBudgetBytes > 0);

        renderer.ImageBitmapCacheBudgetBytes = -1;
        Assert.Equal(0, renderer.ImageBitmapCacheBudgetBytes);

        renderer.ResetImageRenderDiagnostics();
        ImageRenderDiagnostics diagnostics = renderer.GetImageRenderDiagnostics(reset: true);

        Assert.Equal(0, diagnostics.PlacementsVisited);
        Assert.Equal(0, diagnostics.PlacementsVisible);
        Assert.Equal(0, diagnostics.Draws);
        Assert.Equal(0, diagnostics.CacheHits);
        Assert.Equal(0, diagnostics.CacheMisses);
        Assert.Equal(0, diagnostics.CacheEvictions);
        Assert.Equal(0, diagnostics.KittyBitmapCacheEntries);
        Assert.Equal(0, diagnostics.RasterBitmapCacheEntries);
    }

    [Fact]
    public void SkiaTerminalRenderer_TextRuns_SplitAroundVisibleCursorCell()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
            CursorVisible = true,
            CursorStyle = CursorStyle.Bar,
            CursorColumn = 3,
            CursorRow = 0,
        };

        using var surface = CreateRenderSurface(renderer, columns: 6, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 6, rows: 1, text: "ABCDEF");
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        long totalRuns = diagnostics.ShapedRuns + diagnostics.FallbackRuns;
        Assert.True(totalRuns >= 3, $"Expected cursor row runs to split around cursor cell. runs={totalRuns}");
    }

    [Fact]
    public void SkiaTerminalRenderer_UnsafeGridMapping_UsesFallbackRun()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
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
        Assert.Equal(0, afterReset.PretextRuns);
        Assert.Equal(0, afterReset.PretextFallbackRuns);
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
        Assert.Equal(0, diagnostics.PretextRuns);
        Assert.Equal(0, diagnostics.PretextFallbackRuns);
    }

    [Fact]
    public void SkiaTerminalRenderer_PretextPipeline_WhenAvailable_DoesNotUseHarfBuzzRuns()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
            TextRenderPipeline = TerminalTextRenderPipeline.Pretext,
        };

        Assert.Equal(TerminalTextRenderPipeline.Pretext, renderer.TextRenderPipeline);

        if (!renderer.IsPretextTextRenderPipelineAvailable)
        {
            return;
        }

        using var surface = CreateRenderSurface(renderer, columns: 8, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 8, rows: 1, text: "PRETEXT!");

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.True(
            diagnostics.PretextRuns > 0 || diagnostics.PretextFallbackRuns > 0,
            "Pretext pipeline should handle the text run when compiled into the build.");
        Assert.Equal(0, diagnostics.ShapedRuns);
    }

    [Fact]
    public void SkiaTerminalRenderer_PretextPipeline_WhenClamped_DoesNotUseFallbackRuns()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
            TextRenderPipeline = TerminalTextRenderPipeline.Pretext,
        };

        if (!renderer.IsPretextTextRenderPipelineAvailable)
        {
            return;
        }

        renderer.SetCellSize(renderer.CellWidth * 0.75f, renderer.CellHeight);

        using var surface = CreateRenderSurface(renderer, columns: 12, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 12, rows: 1, text: "HELLOWORLD12");

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.True(diagnostics.PretextRuns > 0);
        Assert.True(diagnostics.GridClampedRuns > 0);
        Assert.Equal(0, diagnostics.PretextFallbackRuns);
        Assert.Equal(0, diagnostics.ShapedRuns);
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
    public void SkiaTerminalRenderer_PowerlineSeparators_UseCellSpriteGeometry()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
            EnableTextRenderDiagnostics = true,
        };
        renderer.SetCellSize(18f, 36f);

        // Ghostty routes this geometric Powerline subset through its sprite face, and
        // xterm.js defines these as cell-scaled custom glyphs; keep them cell-owned
        // instead of delegating to arbitrary Nerd Font outlines.
        string text = "\uE0B0\uE0B2\uE0B1\uE0B3\uE0B4\uE0B6\uE0D2\uE0D4";
        using var surface = CreateRenderSurface(renderer, columns: text.Length, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: text.Length, rows: 1, text: text);
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();
        Assert.True(diagnostics.SpriteCells >= text.Length);
        Assert.Equal(0, diagnostics.ShapedRuns);
        Assert.Equal(0, diagnostics.FallbackRuns);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        Assert.True(
            CountBrightPixelsInRegion(pixels, 0f, 1f, 0f, renderer.CellHeight, threshold: 64) >= 34,
            "The right-pointing filled separator should own the full left cell edge.");
        Assert.True(
            CountBrightPixelsInRegion(
                pixels,
                renderer.CellWidth - 1f,
                renderer.CellWidth,
                0f,
                renderer.CellHeight * 0.2f,
                threshold: 64) <= 1,
            "The right-pointing filled separator should not fill the top-right corner.");
        Assert.True(
            CountBrightPixelsInRegion(
                pixels,
                renderer.CellWidth - 1f,
                renderer.CellWidth,
                renderer.CellHeight * 0.45f,
                renderer.CellHeight * 0.55f,
                threshold: 64) > 0,
            "The right-pointing filled separator should reach the right edge at the cell midpoint.");

        float secondCellX = renderer.CellWidth;
        Assert.True(
            CountBrightPixelsInRegion(
                pixels,
                secondCellX + renderer.CellWidth - 1f,
                secondCellX + renderer.CellWidth,
                0f,
                renderer.CellHeight,
                threshold: 64) >= 34,
            "The left-pointing filled separator should own the full right cell edge.");
        Assert.True(
            CountBrightPixelsInRegion(
                pixels,
                secondCellX,
                secondCellX + 1f,
                0f,
                renderer.CellHeight * 0.2f,
                threshold: 64) <= 1,
            "The left-pointing filled separator should not fill the top-left corner.");
        Assert.True(
            CountBrightPixelsInRegion(
                pixels,
                secondCellX,
                secondCellX + 1f,
                renderer.CellHeight * 0.45f,
                renderer.CellHeight * 0.55f,
                threshold: 64) > 0,
            "The left-pointing filled separator should reach the left edge at the cell midpoint.");
    }

    [Fact]
    public void SkiaTerminalRenderer_ReportedNerdFontFiles_RenderSpritesWithCellGeometry()
    {
        string[] fontPaths = GetReportedNerdFontFixturePaths();
        if (fontPaths.Length == 0)
        {
            return;
        }

        foreach (string fontPath in fontPaths)
        {
            using var renderer = new SkiaTerminalRenderer(
                System.IO.Path.GetFileNameWithoutExtension(fontPath),
                14f,
                TerminalFontSource.File,
                fontPath)
            {
                CursorVisible = false,
                EnableTextRenderDiagnostics = true,
            };
            renderer.SetCellSize(18f, 36f);

            string text = "\u256D\u2500\u256E\u28FF\u2588\uE0B0\uE0B2";
            using var surface = CreateRenderSurface(renderer, columns: text.Length, rows: 1);
            TerminalScreen screen = CreateAsciiScreen(columns: text.Length, rows: 1, text: text);
            surface.Canvas.Clear(SKColors.Black);

            renderer.RenderFull(surface.Canvas, screen);

            TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();
            Assert.True(diagnostics.SpriteCells >= text.Length, fontPath);
            Assert.True(diagnostics.BoxDrawingSpriteCells >= 3, fontPath);
            Assert.True(diagnostics.BrailleSpriteCells >= 1, fontPath);
            Assert.True(diagnostics.BlockSpriteCells >= 1, fontPath);
            Assert.Equal(0, diagnostics.FallbackRuns);

            using SKImage snapshot = surface.Snapshot();
            SaveReportedNerdFontFixture(snapshot, fontPath);

            using SKPixmap pixels = snapshot.PeekPixels();
            Assert.Equal(18 * 36, CountBrightPixelsInCell(pixels, renderer, 4, threshold: 64));
            Assert.True(CountBrightPixelsInCell(pixels, renderer, 3, threshold: 64) >= 128, fontPath);
            Assert.True(
                CountBrightPixelsInRegion(
                    pixels,
                    5f * renderer.CellWidth,
                    (5f * renderer.CellWidth) + 1f,
                    0f,
                    renderer.CellHeight,
                    threshold: 64) >= 34,
                fontPath);
            Assert.True(
                CountBrightPixelsInRegion(
                    pixels,
                    (6f * renderer.CellWidth) + renderer.CellWidth - 1f,
                    (6f * renderer.CellWidth) + renderer.CellWidth,
                    0f,
                    renderer.CellHeight,
                    threshold: 64) >= 34,
                fontPath);
        }
    }

    private static void SaveReportedNerdFontFixture(SKImage snapshot, string fontPath)
    {
        string? outputDir = Environment.GetEnvironmentVariable("ROYALTERMINAL_RENDER_FIXTURE_OUTPUT_DIR");
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(outputDir);
        string fileName = $"{System.IO.Path.GetFileNameWithoutExtension(fontPath)}-sprites.png";
        using SKData? data = snapshot.Encode(SKEncodedImageFormat.Png, quality: 100);
        if (data is null)
        {
            return;
        }

        using System.IO.FileStream stream = System.IO.File.Create(System.IO.Path.Combine(outputDir, fileName));
        data.SaveTo(stream);
    }

    [Fact]
    public void SkiaTerminalRenderer_RoundedBoxCorners_ConnectToAdjacentEdges()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(32f, 32f);

        string[] rows =
        [
            "\u256D\u2500\u256E",
            "\u2502 \u2502",
            "\u2570\u2500\u256F",
        ];
        TerminalScreen screen = CreateScreenFromRows(rows);
        using var surface = CreateRenderSurface(renderer, columns: 3, rows: 3);
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        float center = renderer.CellWidth * 0.5f;
        float boundary = renderer.CellWidth;
        float lowerBoundary = renderer.CellHeight;

        int topHorizontalJoin = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: boundary - 1f,
            endX: boundary + 2f,
            startY: center - 2f,
            endY: center + 3f);
        int leftVerticalJoin = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: center - 2f,
            endX: center + 3f,
            startY: lowerBoundary - 1f,
            endY: lowerBoundary + 2f);

        Assert.True(topHorizontalJoin > 0, "Rounded top-left corner should connect to the top horizontal segment.");
        Assert.True(leftVerticalJoin > 0, "Rounded top-left corner should connect to the left vertical segment.");
    }

    [Fact]
    public void SkiaTerminalRenderer_RoundedBoxCorners_UseCompactCellArc()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(18f, 36f);

        using var surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: "\u256D");
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int misplacedOuterQuadrantInk = CountBrightPixelsInRegion(
            pixels,
            startX: 13f,
            endX: 18f,
            startY: 29f,
            endY: 36f,
            threshold: 64);
        int rightEdgeJoin = CountBrightPixelsInRegion(
            pixels,
            startX: 17f,
            endX: 18f,
            startY: 16f,
            endY: 20f,
            threshold: 64);
        int bottomStem = CountBrightPixelsInRegion(
            pixels,
            startX: 8f,
            endX: 11f,
            startY: 32f,
            endY: 36f,
            threshold: 64);

        // Ghostty and xterm.js keep U+256D cell-owned but draw it as a compact
        // stem-plus-curve path; the old full-quadrant Bezier put visible ink here.
        Assert.Equal(0, misplacedOuterQuadrantInk);
        Assert.True(rightEdgeJoin > 0, "Rounded corner should still meet the right edge centerline.");
        Assert.True(bottomStem > 0, "Rounded corner should still meet the bottom edge centerline.");
    }

    [Fact]
    public void SkiaTerminalRenderer_BoxDrawingSprites_SnapToSharedCellLocalGridAtEvenSizes()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(18f, 36f);

        string[] rows =
        [
            "\u256D\u2500\u256E",
            "\u2502 \u2502",
            "\u2570\u2500\u256F",
        ];

        TerminalScreen screen = CreateScreenFromRows(rows);
        using var surface = CreateRenderSurface(renderer, columns: screen.Columns, rows: screen.ViewportRows);
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        int expectedVerticalColumn = 8;
        int shiftedVerticalColumn = 9;
        int expectedHorizontalRow = 17;
        int shiftedHorizontalRow = 18;

        Assert.True(
            CountBrightPixelsInRegion(pixels, expectedVerticalColumn, expectedVerticalColumn + 1f, 33f, 36f) > 0,
            "Rounded top-left corner vertical stem should use the same floor-snapped column as straight vertical lines.");
        Assert.Equal(
            0,
            CountBrightPixelsInRegion(pixels, shiftedVerticalColumn, shiftedVerticalColumn + 1f, 33f, 36f));
        Assert.True(
            CountBrightPixelsInRegion(pixels, expectedVerticalColumn, expectedVerticalColumn + 1f, 36f, 72f) > 0,
            "Straight vertical line below the rounded corner should use the same snapped column.");
        Assert.Equal(
            0,
            CountBrightPixelsInRegion(pixels, shiftedVerticalColumn, shiftedVerticalColumn + 1f, 36f, 72f));

        Assert.True(
            CountBrightPixelsInRegion(pixels, 18f, 36f, expectedHorizontalRow, expectedHorizontalRow + 1f) > 0,
            "Straight horizontal line should use the floor-snapped center row.");
        Assert.Equal(
            0,
            CountBrightPixelsInRegion(pixels, 18f, 36f, shiftedHorizontalRow, shiftedHorizontalRow + 1f));
        Assert.True(
            CountBrightPixelsInRegion(pixels, 15f, 18f, expectedHorizontalRow, expectedHorizontalRow + 1f) > 0,
            "Rounded corner horizontal exit should align with the adjacent horizontal segment row.");
        Assert.Equal(
            0,
            CountBrightPixelsInRegion(pixels, 15f, 18f, shiftedHorizontalRow, shiftedHorizontalRow + 1f));
    }

    [Fact]
    public void SkiaTerminalRenderer_LightBoxLines_DoNotThickenAtBtopCellScale()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(14f, 28f);

        using var surface = CreateRenderSurface(renderer, columns: 2, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 2, rows: 1, text: "\u2500\u2502");
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int horizontalRows = CountBrightRowsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: renderer.CellHeight,
            threshold: 64);
        int verticalColumns = CountBrightColumnsInRegion(
            pixels,
            startX: renderer.CellWidth,
            endX: renderer.CellWidth * 2f,
            startY: 0f,
            endY: renderer.CellHeight,
            threshold: 64);

        // xterm.js keeps light box-drawing strokes at width 1; this prevents
        // btop borders from becoming visibly heavier as cell size increases.
        Assert.Equal(1, horizontalRows);
        Assert.Equal(1, verticalColumns);
    }

    [Fact]
    public void SkiaTerminalRenderer_FullBraillePattern_OccupiesReadableGraphArea()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(32f, 40f);

        using var surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: "\u28FF");
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int ink = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(ink >= 200, $"Braille graph cell should be visibly readable at terminal scale. ink={ink}");
    }

    [Fact]
    public void SkiaTerminalRenderer_FullBraillePattern_UsesSmoothSpriteFontDotGeometry()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(17f, 33f);

        using var surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: "\u28FF");
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int brightInk = CountBrightPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: renderer.CellHeight);
        int edgeInk = CountMidTonePixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: renderer.CellHeight);

        // Ghostty and xterm.js both keep Braille cell-owned. xterm's custom
        // glyphs use rounded dots; this keeps btop-style net graphs smooth
        // while preserving the terminal grid placement.
        Assert.True(brightInk >= 64, $"Braille graph cell should keep visible dot coverage. brightInk={brightInk}");
        Assert.True(brightInk < 17 * 33, $"Braille graph cell should not be filled as solid subcells. brightInk={brightInk}");
        Assert.True(edgeInk >= 8, $"Braille graph cell should have anti-aliased dot edges. edgeInk={edgeInk}");
    }

    [Fact]
    public void SkiaTerminalRenderer_BlackSquareSymbol_UsesTerminalSizedSprite()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(32f, 32f);

        using var surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: "\u25A0");
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int ink = CountBrightPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(ink >= 560, $"Black square graph symbol should not look zoomed out. ink={ink}");
    }

    [Fact]
    public void SkiaTerminalRenderer_FullBlockElement_FillsEntireCell()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(24f, 24f);

        using var surface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 1, rows: 1, text: "\u2588");
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int ink = CountBrightPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(ink >= 560, $"Full block element should fill the terminal cell. ink={ink}");
    }

    [Fact]
    public void SkiaTerminalRenderer_BlockElements_UseIntegerCellFractions()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(17f, 19f);

        string text = "\u2581\u2588\u2589\u2590\u2594\u2595";
        using var surface = CreateRenderSurface(renderer, columns: text.Length, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: text.Length, rows: 1, text: text);
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        Assert.Equal(34, CountBrightPixelsInCell(pixels, renderer, 0));
        Assert.Equal(323, CountBrightPixelsInCell(pixels, renderer, 1));
        Assert.Equal(285, CountBrightPixelsInCell(pixels, renderer, 2));
        Assert.Equal(171, CountBrightPixelsInCell(pixels, renderer, 3));
        Assert.Equal(34, CountBrightPixelsInCell(pixels, renderer, 4));
        Assert.Equal(38, CountBrightPixelsInCell(pixels, renderer, 5));
    }

    [Fact]
    public void SkiaTerminalRenderer_GeometricControlSymbols_UseSpriteFallback()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
        };

        var screen = new TerminalScreen(12, 1);
        TerminalRow row = screen.GetViewportRow(0);
        SetTestCell(row, 0, 0x25CB);
        SetTestCell(row, 1, 0x25CF);
        SetTestCell(row, 2, 0x25EF);
        SetTestCell(row, 3, 0x2B24);
        SetTestCell(row, 4, 0x2610);
        SetTestCell(row, 5, 0x2611);
        SetTestCell(row, 6, 0x2612);
        SetTestCell(row, 7, 0x25A0);
        SetTestCell(row, 8, 0x1F7D7, "\U0001F7D7\uFE0E");
        SetTestCell(row, 9, 0x1F5F9, "\U0001F5F9\uFE0E");
        SetTestCell(row, 10, 0x1F834);
        SetTestCell(row, 11, 0x1F837, "\U0001F837\uFE0E");
        row.IsDirty = true;

        using var surface = CreateRenderSurface(renderer, columns: 12, rows: 1);
        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.True(diagnostics.SpriteCells >= 12);
        Assert.Equal(0, diagnostics.GridClampedRuns);
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
    public void SkiaTerminalRenderer_DoubleLineCorners_ClipInnerRailsWithoutCrossing()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(32f, 32f);

        string[] rows =
        [
            "\u2554\u2557",
            "\u255A\u255D",
        ];

        TerminalScreen screen = CreateScreenFromRows(rows);
        using var surface = CreateRenderSurface(renderer, columns: screen.Columns, rows: screen.ViewportRows);
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int totalInk = CountBrightPixelsInRegion(
            pixels,
            startX: 0f,
            endX: screen.Columns * renderer.CellWidth,
            startY: 0f,
            endY: screen.ViewportRows * renderer.CellHeight);

        Assert.True(totalInk > 0, "Double-line corners should render visible ink.");

        int hDoubleTop = 14;
        int hLightTop = 15;
        int hLightBottom = 16;
        int vDoubleLeft = 14;
        int vLightLeft = 15;
        int vLightRight = 16;

        AssertNoCornerInk(column: 0, row: 0, localX: vLightRight, localY: hLightTop, "top-left inner vertical rail should not cross upward");
        AssertNoCornerInk(column: 0, row: 0, localX: vLightLeft, localY: hLightBottom, "top-left lower horizontal rail should not cross leftward");

        AssertNoCornerInk(column: 1, row: 0, localX: vDoubleLeft, localY: hLightTop, "top-right inner vertical rail should not cross upward");
        AssertNoCornerInk(column: 1, row: 0, localX: vLightLeft, localY: hLightBottom, "top-right lower horizontal rail should not cross rightward");

        AssertNoCornerInk(column: 0, row: 1, localX: vLightRight, localY: hLightTop, "bottom-left inner vertical rail should not cross downward");
        AssertNoCornerInk(column: 0, row: 1, localX: vLightLeft, localY: hDoubleTop, "bottom-left upper horizontal rail should not cross leftward");

        AssertNoCornerInk(column: 1, row: 1, localX: vDoubleLeft, localY: hLightTop, "bottom-right inner vertical rail should not cross downward");
        AssertNoCornerInk(column: 1, row: 1, localX: vLightLeft, localY: hDoubleTop, "bottom-right upper horizontal rail should not cross rightward");

        void AssertNoCornerInk(int column, int row, int localX, int localY, string message)
        {
            float x = (column * renderer.CellWidth) + localX;
            float y = (row * renderer.CellHeight) + localY;
            int ink = CountBrightPixelsInRegion(pixels, x, x + 1f, y, y + 1f);
            Assert.True(ink == 0, $"{message}. ink={ink}");
        }
    }

    [Fact]
    public void SkiaTerminalRenderer_DoubleHorizontalSprite_DrawsTwoParallelBands()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        using var doubleSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);
        using var heavySurface = CreateRenderSurface(renderer, columns: 1, rows: 1);

        TerminalScreen doubleScreen = CreateAsciiScreen(columns: 1, rows: 1, text: "\u2550");
        TerminalScreen heavyScreen = CreateAsciiScreen(columns: 1, rows: 1, text: "\u2501");

        doubleSurface.Canvas.Clear(SKColors.Black);
        heavySurface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(doubleSurface.Canvas, doubleScreen);
        renderer.RenderFull(heavySurface.Canvas, heavyScreen);

        using SKImage doubleSnapshot = doubleSurface.Snapshot();
        using SKPixmap doublePixels = doubleSnapshot.PeekPixels();
        using SKImage heavySnapshot = heavySurface.Snapshot();
        using SKPixmap heavyPixels = heavySnapshot.PeekPixels();

        int doubleRuns = CountInkRunsAlongYAxis(doublePixels, startX: 0f, endX: renderer.CellWidth);
        int heavyRuns = CountInkRunsAlongYAxis(heavyPixels, startX: 0f, endX: renderer.CellWidth);

        Assert.True(doubleRuns >= 2, "Double horizontal sprite should produce at least two horizontal ink bands.");
        Assert.True(doubleRuns > heavyRuns, "Double horizontal sprite should have more horizontal bands than heavy single-line sprite.");
    }

    [Fact]
    public void SkiaTerminalRenderer_DashedHorizontalSprite_ProducesMultipleInkRuns()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
            EnableTextRenderDiagnostics = true,
        };
        renderer.SetCellSize(64f, 64f);
        using var dashedSurface = CreateRenderSurface(renderer, columns: 1, rows: 1);

        TerminalScreen dashedScreen = CreateAsciiScreen(columns: 1, rows: 1, text: "\u2508");

        dashedSurface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(dashedSurface.Canvas, dashedScreen);

        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        using SKImage dashedSnapshot = dashedSurface.Snapshot();
        using SKPixmap dashedPixels = dashedSnapshot.PeekPixels();

        float centerY = renderer.CellHeight * 0.5f;
        int dashedRuns = CountInkRunsAlongXAxis(
            dashedPixels,
            startY: Math.Max(0f, centerY - 4f),
            endY: Math.Min(renderer.CellHeight, centerY + 4f));

        Assert.True(diagnostics.SpriteCells >= 1);
        Assert.True(diagnostics.BoxDrawingSpriteCells >= 1);
        Assert.True(dashedRuns >= 2, $"Dashed horizontal sprite should produce multiple separated x-runs. dashedRuns={dashedRuns}");
    }

    [Fact]
    public void SkiaTerminalRenderer_ArcAndDiagonalSprites_AreDetectedAndDrawn()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
        };

        string glyphs = "\u256D\u256E\u256F\u2570\u2571\u2572\u2573";
        using var surface = CreateRenderSurface(renderer, columns: glyphs.Length, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: glyphs.Length, rows: 1, text: glyphs);
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        Assert.True(diagnostics.SpriteCells >= glyphs.Length);
        Assert.True(diagnostics.BoxDrawingSpriteCells >= glyphs.Length);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int nonBackground = CountNonBackgroundPixelsInRegion(
            pixels,
            startX: 0f,
            endX: renderer.CellWidth * glyphs.Length,
            startY: 0f,
            endY: renderer.CellHeight);

        Assert.True(nonBackground > 0, "Arc/diagonal sprite set should draw visible pixels.");
    }

    [Fact]
    public void SkiaTerminalRenderer_ShadeBlockSprites_FillCellsAndIncreaseIntensity()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            EnableTextRenderDiagnostics = true,
            CursorVisible = false,
        };
        renderer.SetCellSize(16f, 32f);

        using var surface = CreateRenderSurface(renderer, columns: 3, rows: 1);
        TerminalScreen screen = CreateAsciiScreen(columns: 3, rows: 1, text: "\u2591\u2592\u2593");

        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();

        int expectedCellPixels = 16 * 32;
        Assert.Equal(expectedCellPixels, CountNonBackgroundPixelsInCell(pixels, renderer, column: 0));
        Assert.Equal(expectedCellPixels, CountNonBackgroundPixelsInCell(pixels, renderer, column: 1));
        Assert.Equal(expectedCellPixels, CountNonBackgroundPixelsInCell(pixels, renderer, column: 2));

        long lightInk = SumPixelIntensityInCell(pixels, renderer, column: 0);
        long mediumInk = SumPixelIntensityInCell(pixels, renderer, column: 1);
        long darkInk = SumPixelIntensityInCell(pixels, renderer, column: 2);

        Assert.True(diagnostics.BlockSpriteCells >= 3);
        Assert.True(lightInk < mediumInk, $"Expected medium shade ink > light shade ink, got {mediumInk} <= {lightInk}.");
        Assert.True(mediumInk < darkInk, $"Expected dark shade ink > medium shade ink, got {darkInk} <= {mediumInk}.");
        Assert.True(lightInk < darkInk, $"Expected dark shade ink > light shade ink, got {darkInk} <= {lightInk}.");
    }

    [Fact]
    public void SkiaTerminalRenderer_ShadeAndBrailleGraphGlyphs_UseIndependentSpriteGeometry()
    {
        using var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        renderer.SetCellSize(16f, 32f);

        string[] rows =
        [
            "\u2591\u2592\u2593\u2588\u28FF",
        ];

        TerminalScreen screen = CreateScreenFromRows(rows);
        using var surface = CreateRenderSurface(renderer, columns: screen.Columns, rows: screen.ViewportRows);
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        int expectedCellPixels = 16 * 32;

        Assert.Equal(expectedCellPixels, CountNonBackgroundPixelsInCell(pixels, renderer, column: 0));
        Assert.Equal(expectedCellPixels, CountNonBackgroundPixelsInCell(pixels, renderer, column: 1));
        Assert.Equal(expectedCellPixels, CountNonBackgroundPixelsInCell(pixels, renderer, column: 2));
        Assert.Equal(expectedCellPixels, CountBrightPixelsInCell(pixels, renderer, column: 3));

        long lightShadeInk = SumPixelIntensityInCell(pixels, renderer, column: 0);
        long mediumShadeInk = SumPixelIntensityInCell(pixels, renderer, column: 1);
        long darkShadeInk = SumPixelIntensityInCell(pixels, renderer, column: 2);
        long fullBlockInk = SumPixelIntensityInCell(pixels, renderer, column: 3);
        Assert.True(lightShadeInk < mediumShadeInk, $"Expected medium shade ink > light shade ink, got {mediumShadeInk} <= {lightShadeInk}.");
        Assert.True(mediumShadeInk < darkShadeInk, $"Expected dark shade ink > medium shade ink, got {darkShadeInk} <= {mediumShadeInk}.");
        Assert.True(darkShadeInk < fullBlockInk, $"Expected full block ink > dark shade ink, got {fullBlockInk} <= {darkShadeInk}.");

        int brailleInk = CountBrightPixelsInCell(pixels, renderer, column: 4);
        Assert.True(brailleInk >= 64, $"Braille graph cell should keep visible dotted coverage. brailleInk={brailleInk}");
        Assert.True(brailleInk < expectedCellPixels, $"Braille graph cell should not be filled as a solid net graph cell. brailleInk={brailleInk}");
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

    private static string[] GetReportedNerdFontFixturePaths()
    {
        string? configuredDirectory = Environment.GetEnvironmentVariable("ROYALTERMINAL_RENDER_FONT_FIXTURE_DIR");
        string directory = !string.IsNullOrWhiteSpace(configuredDirectory)
            ? configuredDirectory
            : "/private/tmp/royalterminal-render-fonts";

        string[] paths =
        [
            System.IO.Path.Combine(directory, "IosevkaTermNerdFontMono-Regular.ttf"),
            System.IO.Path.Combine(directory, "FiraCodeNerdFontMono-Regular.ttf"),
        ];

        return paths.All(System.IO.File.Exists)
            ? paths
            : [];
    }

    private static SKSurface CreateRenderSurface(SkiaTerminalRenderer renderer, int columns, int rows)
    {
        int width = Math.Max(1, (int)Math.Ceiling(columns * renderer.CellWidth));
        int height = Math.Max(1, (int)Math.Ceiling(rows * renderer.CellHeight));
        return SKSurface.Create(new SKImageInfo(width, height));
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

    private static int CountBrightPixelsInRegion(
        SKPixmap pixels,
        float startX,
        float endX,
        float startY,
        float endY,
        byte threshold = 180)
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
                if (pixel.Red >= threshold || pixel.Green >= threshold || pixel.Blue >= threshold)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountMidTonePixelsInRegion(
        SKPixmap pixels,
        float startX,
        float endX,
        float startY,
        float endY,
        byte lowThreshold = 0,
        byte highThreshold = 180)
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
                if (IsMidToneChannel(pixel.Red, lowThreshold, highThreshold) ||
                    IsMidToneChannel(pixel.Green, lowThreshold, highThreshold) ||
                    IsMidToneChannel(pixel.Blue, lowThreshold, highThreshold))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsMidToneChannel(byte value, byte lowThreshold, byte highThreshold)
    {
        return value > lowThreshold && value < highThreshold;
    }

    private static int CountBrightPixelsInCell(
        SKPixmap pixels,
        SkiaTerminalRenderer renderer,
        int column,
        int row = 0,
        byte threshold = 180)
    {
        float startX = column * renderer.CellWidth;
        float startY = row * renderer.CellHeight;
        return CountBrightPixelsInRegion(
            pixels,
            startX,
            startX + renderer.CellWidth,
            startY,
            startY + renderer.CellHeight,
            threshold);
    }

    private static int CountNonBackgroundPixelsInCell(
        SKPixmap pixels,
        SkiaTerminalRenderer renderer,
        int column,
        int row = 0)
    {
        float startX = column * renderer.CellWidth;
        float startY = row * renderer.CellHeight;
        return CountNonBackgroundPixelsInRegion(
            pixels,
            startX,
            startX + renderer.CellWidth,
            startY,
            startY + renderer.CellHeight);
    }

    private static int CountPixelsInCell(
        SKPixmap pixels,
        SkiaTerminalRenderer renderer,
        int column,
        Func<SKColor, bool> predicate,
        int row = 0)
    {
        int count = 0;
        int minX = Math.Clamp((int)MathF.Floor(column * renderer.CellWidth), 0, pixels.Width);
        int maxX = Math.Clamp((int)MathF.Ceiling((column + 1) * renderer.CellWidth), 0, pixels.Width);
        int minY = Math.Clamp((int)MathF.Floor(row * renderer.CellHeight), 0, pixels.Height);
        int maxY = Math.Clamp((int)MathF.Ceiling((row + 1) * renderer.CellHeight), 0, pixels.Height);

        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                if (predicate(pixels.GetPixelColor(x, y)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static long SumPixelIntensityInCell(
        SKPixmap pixels,
        SkiaTerminalRenderer renderer,
        int column,
        int row = 0)
    {
        float startX = column * renderer.CellWidth;
        float startY = row * renderer.CellHeight;
        return SumPixelIntensityInRegion(
            pixels,
            startX,
            startX + renderer.CellWidth,
            startY,
            startY + renderer.CellHeight);
    }

    private static int CountBrightRowsInRegion(
        SKPixmap pixels,
        float startX,
        float endX,
        float startY,
        float endY,
        byte threshold = 180)
    {
        int rows = 0;
        int minX = Math.Clamp((int)MathF.Floor(startX), 0, pixels.Width);
        int maxX = Math.Clamp((int)MathF.Ceiling(endX), 0, pixels.Width);
        int minY = Math.Clamp((int)MathF.Floor(startY), 0, pixels.Height);
        int maxY = Math.Clamp((int)MathF.Ceiling(endY), 0, pixels.Height);

        for (int y = minY; y < maxY; y++)
        {
            bool hasBrightPixel = false;
            for (int x = minX; x < maxX; x++)
            {
                SKColor pixel = pixels.GetPixelColor(x, y);
                if (pixel.Red >= threshold || pixel.Green >= threshold || pixel.Blue >= threshold)
                {
                    hasBrightPixel = true;
                    break;
                }
            }

            if (hasBrightPixel)
            {
                rows++;
            }
        }

        return rows;
    }

    private static int CountBrightColumnsInRegion(
        SKPixmap pixels,
        float startX,
        float endX,
        float startY,
        float endY,
        byte threshold = 180)
    {
        int columns = 0;
        int minX = Math.Clamp((int)MathF.Floor(startX), 0, pixels.Width);
        int maxX = Math.Clamp((int)MathF.Ceiling(endX), 0, pixels.Width);
        int minY = Math.Clamp((int)MathF.Floor(startY), 0, pixels.Height);
        int maxY = Math.Clamp((int)MathF.Ceiling(endY), 0, pixels.Height);

        for (int x = minX; x < maxX; x++)
        {
            bool hasBrightPixel = false;
            for (int y = minY; y < maxY; y++)
            {
                SKColor pixel = pixels.GetPixelColor(x, y);
                if (pixel.Red >= threshold || pixel.Green >= threshold || pixel.Blue >= threshold)
                {
                    hasBrightPixel = true;
                    break;
                }
            }

            if (hasBrightPixel)
            {
                columns++;
            }
        }

        return columns;
    }

    private static long SumPixelIntensityInRegion(
        SKPixmap pixels,
        float startX,
        float endX,
        float startY,
        float endY)
    {
        long sum = 0;
        int minX = Math.Clamp((int)MathF.Floor(startX), 0, pixels.Width);
        int maxX = Math.Clamp((int)MathF.Ceiling(endX), 0, pixels.Width);
        int minY = Math.Clamp((int)MathF.Floor(startY), 0, pixels.Height);
        int maxY = Math.Clamp((int)MathF.Ceiling(endY), 0, pixels.Height);

        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                SKColor pixel = pixels.GetPixelColor(x, y);
                sum += pixel.Red + pixel.Green + pixel.Blue;
            }
        }

        return sum;
    }

    private static int CountRedDominantPixelsInRegion(
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
                if (pixel.Red > pixel.Green + 20 && pixel.Red > pixel.Blue + 20)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountGreenDominantPixelsInRegion(
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
                if (pixel.Green > pixel.Red + 20 && pixel.Green > pixel.Blue + 20)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountInkRunsAlongYAxis(SKPixmap pixels, float startX, float endX)
    {
        int minX = Math.Clamp((int)MathF.Floor(startX), 0, pixels.Width);
        int maxX = Math.Clamp((int)MathF.Ceiling(endX), 0, pixels.Width);
        bool inRun = false;
        int runs = 0;

        for (int y = 0; y < pixels.Height; y++)
        {
            bool hasInk = false;
            for (int x = minX; x < maxX; x++)
            {
                if (IsInkPixel(pixels.GetPixelColor(x, y)))
                {
                    hasInk = true;
                    break;
                }
            }

            if (hasInk && !inRun)
            {
                runs++;
                inRun = true;
            }
            else if (!hasInk)
            {
                inRun = false;
            }
        }

        return runs;
    }

    private static int CountInkRunsAlongXAxis(SKPixmap pixels, float startY, float endY)
    {
        int minY = Math.Clamp((int)MathF.Floor(startY), 0, pixels.Height);
        int maxY = Math.Clamp((int)MathF.Ceiling(endY), 0, pixels.Height);
        bool inRun = false;
        int runs = 0;

        for (int x = 0; x < pixels.Width; x++)
        {
            bool hasInk = false;
            for (int y = minY; y < maxY; y++)
            {
                if (IsInkPixel(pixels.GetPixelColor(x, y)))
                {
                    hasInk = true;
                    break;
                }
            }

            if (hasInk && !inRun)
            {
                runs++;
                inRun = true;
            }
            else if (!hasInk)
            {
                inRun = false;
            }
        }

        return runs;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInkPixel(SKColor pixel)
    {
        return pixel.Red > 0 || pixel.Green > 0 || pixel.Blue > 0;
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
        cell.HasBackground = true;
        cell.Attributes = attributes;
        cell.UnderlineStyle = underlineStyle;
        cell.UnderlineColor = 0;
        cell.HasUnderlineColor = false;
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
                row[col].HasBackground = true;
                row[col].Attributes = CellAttributes.None;
                row[col].UnderlineColor = 0;
                row[col].HasUnderlineColor = false;
                row[col].Width = 1;
                col++;
            }

            row.IsDirty = true;
        }

        return screen;
    }

    private static void SetTestCell(TerminalRow row, int column, int codepoint, string? grapheme = null)
    {
        row[column].Codepoint = codepoint;
        row[column].Grapheme = grapheme;
        row[column].Foreground = 0xFFFFFFFF;
        row[column].Background = 0xFF000000;
        row[column].HasBackground = true;
        row[column].Attributes = CellAttributes.None;
        row[column].UnderlineColor = 0;
        row[column].HasUnderlineColor = false;
        row[column].Width = 1;
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
            row[col].HasBackground = true;
            row[col].Attributes = CellAttributes.None;
            row[col].UnderlineColor = 0;
            row[col].HasUnderlineColor = false;
            row[col].Width = 1;
            col++;
        }

        row.IsDirty = true;
        return screen;
    }
}
