// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalFontSynthesisTests
{
    // Ghostty Collection.completeStyles: real variants win; combined prefers
    // italicizing real bold, otherwise emboldening the completed italic face.
    // WT/DirectWrite and xterm/canvas likewise retain configured real variants.
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void MissingStyleCompletionUsesIndependentToggles(bool bold, bool italic, bool combined)
    {
        using SKTypeface face = Load();
        TerminalTypefaceCollection source = new(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular));
        TerminalTypefaceCollection completed = source.WithSyntheticStyles(bold, italic, combined);
        Assert.Equal(TerminalFontSynthesis.None, source.GetSynthesis(TerminalTypefaceStyle.Bold));
        Assert.Equal(bold ? TerminalFontSynthesis.Bold : TerminalFontSynthesis.None, completed.GetSynthesis(TerminalTypefaceStyle.Bold));
        Assert.Equal(italic ? TerminalFontSynthesis.Italic : TerminalFontSynthesis.None, completed.GetSynthesis(TerminalTypefaceStyle.Italic));
        Assert.Equal(combined ? TerminalFontSynthesis.Bold | (italic ? TerminalFontSynthesis.Italic : TerminalFontSynthesis.None)
            : TerminalFontSynthesis.None, completed.GetSynthesis(TerminalTypefaceStyle.BoldItalic));
        Assert.Same(face, completed.GetPrimaryTypeface(TerminalTypefaceStyle.BoldItalic));
        Assert.Same(completed, completed.WithSyntheticStyles());
    }

    [Fact]
    public void RealLogicalBoldIsPreferredForCombinedEvenWhenIndividualSynthesisIsOff()
    {
        using SKTypeface regular = Load();
        using SKTypeface namedBold = Load();
        using SKTypeface namedItalic = Load();
        TerminalTypefaceCollection source = new(new(regular, TerminalTypefaceStyle.Regular),
            new(namedBold, TerminalTypefaceStyle.Bold), new(namedItalic, TerminalTypefaceStyle.Italic));
        TerminalTypefaceCollection completed = source.WithSyntheticStyles(false, false, true);
        Assert.Same(namedBold, completed.GetPrimaryTypeface(TerminalTypefaceStyle.BoldItalic));
        Assert.Equal(TerminalFontSynthesis.Italic, completed.GetSynthesis(TerminalTypefaceStyle.BoldItalic));
        Assert.Same(namedItalic, completed.GetPrimaryTypeface(TerminalTypefaceStyle.Italic));
        Assert.Equal(TerminalFontSynthesis.None, completed.GetSynthesis(TerminalTypefaceStyle.Italic));
        TerminalTypefaceCollection disabled = completed.WithDisabledStyles(boldItalic: true)
            .WithCodepointMappings(new TerminalTypefaceCodepointMapping('A', 'A', "Mapped"));
        Assert.Same(regular, disabled.GetPrimaryTypeface(TerminalTypefaceStyle.BoldItalic));
        Assert.Equal(TerminalFontSynthesis.None, disabled.GetSynthesis(TerminalTypefaceStyle.BoldItalic));
        Assert.Equal(TerminalFontSynthesis.Italic, disabled.WithDisabledStyles().GetSynthesis(TerminalTypefaceStyle.BoldItalic));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetSynthesis((TerminalTypefaceStyle)4));
    }

    [Fact]
    public void CombinedUsesRealItalicWhenNoRealBoldExistsAndExplicitCombinedIsNeverChanged()
    {
        using SKTypeface regular = Load();
        using SKTypeface italic = Load();
        TerminalTypefaceCollection source = new(new(regular, TerminalTypefaceStyle.Regular), new(italic, TerminalTypefaceStyle.Italic));
        TerminalTypefaceCollection completed = source.WithSyntheticStyles();
        Assert.Same(italic, completed.GetPrimaryTypeface(TerminalTypefaceStyle.BoldItalic));
        Assert.Equal(TerminalFontSynthesis.Bold, completed.GetSynthesis(TerminalTypefaceStyle.BoldItalic));
        TerminalTypefaceCollection explicitCombined = new(new(regular, TerminalTypefaceStyle.Regular), new(italic, TerminalTypefaceStyle.BoldItalic));
        Assert.Equal(TerminalFontSynthesis.None, explicitCombined.WithSyntheticStyles().GetSynthesis(TerminalTypefaceStyle.BoldItalic));
    }

    [Fact]
    public void CompletionSkipsColorOnlyRegularFacesAndDoesNotInventTextForEmojiOnlyCollections()
    {
        using SKTypeface emoji = FontPresentationTestFonts.Load("NotoColorEmoji.ttf");
        using SKTypeface text = Load();
        TerminalTypefaceCollection source = new(new(emoji, TerminalTypefaceStyle.Regular), new(text, TerminalTypefaceStyle.Regular));
        TerminalTypefaceCollection completed = source.WithSyntheticStyles();
        Assert.Same(emoji, completed.GetPrimaryTypeface());
        Assert.Same(text, completed.GetPrimaryTypeface(TerminalTypefaceStyle.Italic));
        TerminalTypefaceCollection colorOnly = new(new TerminalTypefaceEntry(emoji, TerminalTypefaceStyle.Regular));
        Assert.Same(colorOnly, colorOnly.WithSyntheticStyles());
    }

    [Fact]
    public void ScalarCachesClusterReplacementAndMappingsRetainPerResultEffects()
    {
        using SKTypeface face = Load();
        TerminalTypefaceCollection collection = new TerminalTypefaceCollection(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular))
            .WithSyntheticStyles().WithCodepointMappings(new TerminalTypefaceCodepointMapping('A', 'A', "Mapped"));
        using TerminalFontResolver resolver = new(new MappedFace(face));
        foreach (CultureInfo culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("pl-PL") })
        {
            for (int pass = 0; pass < 2; pass++)
            {
                TerminalFontResolution mapped = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, 'A', culture);
                Assert.Same(face, mapped.Typeface);
                Assert.Equal(TerminalFontSynthesis.None, mapped.Synthesis);
                Assert.Equal(TerminalFontSynthesis.Bold, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, 'B', culture).Synthesis);
                Assert.Equal(TerminalFontSynthesis.Italic, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Italic, "e\u0301".AsSpan(), culture).Synthesis);
                Assert.Equal(TerminalFontSynthesis.None, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'B', culture).Synthesis);
                TerminalFontResolution replacement = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.BoldItalic, 0x10FFFF, culture);
                Assert.Equal(0xFFFD, replacement.ReplacementCodepoint);
                Assert.Equal(TerminalFontSynthesis.Bold | TerminalFontSynthesis.Italic, replacement.Synthesis);
            }
        }
        resolver.Dispose();
        Assert.NotEqual(nint.Zero, face.Handle);
    }

    [Fact]
    public void RegularFallbackAndDiscoveredFacesDoNotAcquireRequestedEffects()
    {
        using SKTypeface face = Load();
        using SKTypeface emoji = FontPresentationTestFonts.Load("NotoColorEmoji.ttf");
        TerminalTypefaceCollection configured = new TerminalTypefaceCollection(new(face, TerminalTypefaceStyle.Regular),
            new(emoji, TerminalTypefaceStyle.Regular)).WithSyntheticStyles();
        using TerminalFontResolver resolver = new(new MappedFace(face));
        Assert.Equal(TerminalFontSynthesis.None, resolver.ResolveTypeface(configured, TerminalTypefaceStyle.Bold, 0x1F600).Synthesis);
        TerminalTypefaceCollection discovered = new TerminalTypefaceCollection(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular))
            .WithSyntheticStyles();
        using TerminalFontResolver discovery = new(new EmojiDiscovery());
        Assert.Equal(TerminalFontSynthesis.None, discovery.ResolveTypeface(discovered, TerminalTypefaceStyle.Italic, 0x1F600).Synthesis);
    }

    [Fact]
    public void FontCreationCombinesGlobalEmboldenWithExplicitSyntheticOutlines()
    {
        using SKTypeface face = Load();
        using SKFont plain = GlyphCache.CreateFont(face, 24, new());
        using SKFont italic = GlyphCache.CreateFont(face, 24, new() { Embolden = true }, TerminalFontSynthesis.Italic);
        using SKFont both = GlyphCache.CreateFont(face, 24, new(), TerminalFontSynthesis.Bold | TerminalFontSynthesis.Italic);
        Assert.False(plain.Embolden);
        Assert.Equal(0, plain.SkewX);
        Assert.True(italic.Embolden);
        Assert.True(both.Embolden);
        Assert.Equal(-0.267949f, both.SkewX);
        Assert.Equal(both.SkewX, italic.SkewX);
        Assert.Throws<ArgumentOutOfRangeException>(() => GlyphCache.CreateFont(face, 24, null, (TerminalFontSynthesis)4));
        using GlyphCache fileCache = new("unused", TerminalFontSource.File, Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "JetBrainsMono-Regular.ttf"));
        using SKFont fileBold = fileCache.CreateFont(24, bold: true);
        Assert.True(fileBold.Embolden);
    }

    [Theory]
    [InlineData(false, false, "Hello", false)]
    [InlineData(true, false, "Hello", false)]
    [InlineData(true, true, "Hello", false)]
    [InlineData(true, false, "fi -> e\u0301", false)]
    [InlineData(false, true, "fi -> e\u0301", false)]
    [InlineData(true, false, "Hello ->", true)]
    [InlineData(true, true, "Hello", true)]
    public void RenderingAndCachedBlobsDistinguishAllFourStyles(bool shaping, bool cursor, string text, bool pretext)
    {
        using SKTypeface face = Load();
        TerminalTypefaceCollection fonts = new TerminalTypefaceCollection(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular))
            .WithSyntheticStyles();
        using SkiaTerminalRenderer renderer = SkiaTerminalRenderer.CreateWithTypefaces(fonts, 24);
        renderer.EnableTextShaping = shaping;
        renderer.CursorVisible = cursor;
        if (pretext)
        {
            if (!renderer.IsPretextTextRenderPipelineAvailable) return;
            renderer.TextRenderPipeline = TerminalTextRenderPipeline.Pretext;
            renderer.EnableLigatures = false;
        }
        TerminalScreen screen = new(24, 1);
        using BasicVtProcessor processor = new(screen);
        byte[] regular = Draw("0");
        byte[] bold = Draw("1"), italic = Draw("3"), combined = Draw("1;3");
        Assert.False(regular.AsSpan().SequenceEqual(bold));
        Assert.False(regular.AsSpan().SequenceEqual(italic));
        Assert.False(bold.AsSpan().SequenceEqual(combined));
        Assert.Equal(regular, Draw("0"));
        Assert.Equal(bold, Draw("1"));
        Assert.Equal(italic, Draw("3"));
        Assert.Equal(combined, Draw("1;3"));

        byte[] Draw(string sgr)
        {
            processor.Process(Encoding.UTF8.GetBytes("\u001b[0m\u001b[2J\u001b[H\u001b[" + sgr + "m" + text));
            byte[] before = processor.GetBinarySnapshot();
            using SKBitmap bitmap = new((int)Math.Ceiling(renderer.CellWidth * screen.Columns), (int)Math.Ceiling(renderer.CellHeight));
            using SKCanvas canvas = new(bitmap);
            renderer.RenderFull(canvas, screen);
            Assert.Equal(before, processor.GetBinarySnapshot());
            return bitmap.Bytes;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedStylesRemainSeparateEvenWhenSharingOneTypeface(bool shaping)
    {
        using SKTypeface face = Load();
        TerminalTypefaceCollection fonts = new TerminalTypefaceCollection(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular)).WithSyntheticStyles();
        using SkiaTerminalRenderer renderer = SkiaTerminalRenderer.CreateWithTypefaces(fonts, 24);
        renderer.EnableTextShaping = shaping;
        renderer.CursorVisible = false;
        TerminalScreen screen = new(4, 1);
        using BasicVtProcessor processor = new(screen);
        processor.Process("H\u001b[1mH\u001b[22;3mH\u001b[1mH"u8);
        using SKBitmap bitmap = new((int)(renderer.CellWidth * 4), (int)renderer.CellHeight);
        using SKCanvas canvas = new(bitmap);
        renderer.RenderFull(canvas, screen);
        for (int column = 1; column < 4; column++)
        {
            bool differs = false;
            for (int y = 0; y < bitmap.Height && !differs; y++)
                for (int x = 0; x < (int)renderer.CellWidth; x++)
                    if (bitmap.GetPixel(x, y) != bitmap.GetPixel(x + column * (int)renderer.CellWidth, y)) { differs = true; break; }
            Assert.True(differs, "Each styled cell must use its own outlines, including the row-batching path.");
        }
    }

    [Fact]
    public void PreeditAndCaretDoNotInheritSyntheticEffectsFromUnderlyingCells()
    {
        using SKTypeface face = Load();
        TerminalTypefaceCollection plain = new(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular));
        using SkiaTerminalRenderer expected = SkiaTerminalRenderer.CreateWithTypefaces(plain, 24);
        using SkiaTerminalRenderer actual = SkiaTerminalRenderer.CreateWithTypefaces(plain.WithSyntheticStyles(), 24);
        expected.Preedit = actual.Preedit = new("ABC e\u0301", 2);
        TerminalScreen screen = new(16, 1);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1;3m        "u8);
        Assert.Equal(Draw(expected), Draw(actual));

        byte[] Draw(SkiaTerminalRenderer renderer)
        {
            using SKBitmap bitmap = new((int)(renderer.CellWidth * screen.Columns), (int)renderer.CellHeight);
            using SKCanvas canvas = new(bitmap);
            renderer.RenderFull(canvas, screen);
            return bitmap.Bytes;
        }
    }

    [Fact]
    public void WarmSyntheticResolutionDoesNotAllocateOrRediscover()
    {
        using SKTypeface face = Load();
        TerminalTypefaceCollection fonts = new TerminalTypefaceCollection(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular)).WithSyntheticStyles();
        using TerminalFontResolver resolver = new(new MappedFace(face));
        for (int i = 0; i < 1000; i++) Resolve();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Resolve();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(0, resolver.CachedFallbackCount);

        void Resolve()
        {
            for (TerminalTypefaceStyle style = TerminalTypefaceStyle.Regular; style <= TerminalTypefaceStyle.BoldItalic; style++)
                _ = resolver.ResolveTypeface(fonts, style, 'H', CultureInfo.InvariantCulture);
        }
    }

    [Fact]
    public void NativeThickeningCachesEachSyntheticOutlineAndReturnsStableMasks()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using SKTypeface face = Load();
        using MacFontThickeningCache cache = new();
        using SKBitmap bitmap = new(96, 96);
        using SKCanvas canvas = new(bitmap);
        using SKPaint paint = new() { Color = SKColors.White };
        byte[] regular = Draw(TerminalFontSynthesis.None);
        byte[] bold = Draw(TerminalFontSynthesis.Bold);
        byte[] italic = Draw(TerminalFontSynthesis.Italic);
        byte[] both = Draw(TerminalFontSynthesis.Bold | TerminalFontSynthesis.Italic);
        Assert.False(regular.AsSpan().SequenceEqual(bold));
        Assert.False(regular.AsSpan().SequenceEqual(italic));
        Assert.False(bold.AsSpan().SequenceEqual(both));
        Assert.Equal(4, cache.Count);
        Assert.Equal(regular, Draw(TerminalFontSynthesis.None));
        Assert.Equal(both, Draw(TerminalFontSynthesis.Bold | TerminalFontSynthesis.Italic));
        cache.Clear();
        Assert.Equal(0, cache.Bytes);

        byte[] Draw(TerminalFontSynthesis effect)
        {
            canvas.Clear(SKColors.Transparent);
            Assert.True(cache.TryDraw(canvas, face, 32, new() { Thicken = true }, [face.GetGlyph('H')], [new(0, 0)], 24, 60, paint, effect));
            return bitmap.Bytes;
        }
    }

    [AvaloniaFact]
    public void ProfileDefaultsEditorDirtyStateAndSerializationPreserveIndependentToggles()
    {
        TerminalFontFamilyEditorViewModel editor = new();
        Assert.True(editor.BuildSettings().IsEmpty);
        editor.SyntheticBold = false;
        editor.SyntheticItalic = false;
        TerminalFontFamilySettings settings = editor.BuildSettings();
        Assert.False(settings.IsEmpty);
        Assert.True(settings.SyntheticBoldItalic);
        TerminalSessionProfilesDocument document = new() { Profiles = [new() { Id = "synthetic", Appearance = new() { FontFamilies = settings } }] };
        TerminalFontFamilySettings restored = Assert.Single(TerminalSessionProfileSerializer.FromJson(
            TerminalSessionProfileSerializer.ToJson(document)).Profiles).Appearance.FontFamilies;
        Assert.False(restored.SyntheticBold);
        Assert.False(restored.SyntheticItalic);
        Assert.True(restored.SyntheticBoldItalic);
        TerminalSettingsPanelState state = new();
        state.LoadDocument(document);
        state.MarkSaved();
        state.FontFamiliesEditor.SyntheticBoldItalic = false;
        Assert.True(state.IsDirty);
        Assert.False(Assert.Single(state.BuildDocument().Profiles).Appearance.FontFamilies.SyntheticBoldItalic);
        state.LoadDocument(new() { Profiles = [new() { Id = "defaults" }] });
        Assert.True(state.FontFamiliesEditor.BuildSettings().IsEmpty);
    }

    private static SKTypeface Load() => FontPresentationTestFonts.Load("JetBrainsMono-Regular.ttf");
    private sealed class MappedFace(SKTypeface face) : ITerminalFontMatcher, ITerminalFontFamilyMatcher
    {
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint) => null;
        public SKTypeface? MatchFamily(string familyName) => face;
    }
    private sealed class EmojiDiscovery : ITerminalFontMatcher
    {
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
            => codepoint == 0x1F600 ? FontPresentationTestFonts.Load("NotoColorEmoji.ttf") : null;
    }
}
