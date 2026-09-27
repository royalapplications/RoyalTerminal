// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalFontPresentationTests
{
    [Theory]
    [InlineData("\U0001F600")]
    [InlineData("\U0001F600\uFE0E")]
    public void ConfiguredMonochromeEmojiFaceTakesPrecedenceWithoutExplicitColor(string text)
    {
        using SKTypeface primary = Load("NotoEmoji-Regular.ttf");
        Matcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution result = resolver.ResolveTypeface(primary, text, CultureInfo.InvariantCulture);
        Assert.Same(primary, result.Typeface);
        Assert.False(result.UsedFallback);
        Assert.Empty(matcher.Requests);
        Assert.Same(primary, resolver.ResolveTypeface(primary, 0x1F600).Typeface);
    }

    [Theory]
    [InlineData("\U0001F600\uFE0F", true)]
    [InlineData("\U0001F600\uFE0E", false)]
    public void AdjacentSelectorOverridesConfiguredFacePresentation(string text, bool emoji)
    {
        using SKTypeface primary = Load(emoji ? "NotoEmoji-Regular.ttf" : "NotoColorEmoji.ttf");
        Matcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution result = resolver.ResolveTypeface(primary, text, CultureInfo.InvariantCulture);
        Assert.True(result.UsedFallback);
        using TerminalGlyphPresentation presentation = new(result.Typeface);
        Assert.Equal(emoji, presentation.IsColorGlyph(result.Typeface.GetGlyph(0x1F600)));
        Assert.Single(matcher.Requests);
        Assert.Equal(emoji, matcher.Requests[0].Emoji);
    }

    [Theory]
    [InlineData("#\uFE0E\uFE0F", false)]
    [InlineData("#\uFE0F\uFE0E", true)]
    [InlineData("#\u200D\uFE0F", false)]
    [InlineData("#\u0301\uFE0F", false)]
    public void OnlySelectorImmediatelyFollowingBaseChangesPresentation(string text, bool emoji)
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution result = resolver.ResolveTypeface(primary, text, CultureInfo.InvariantCulture);
        Assert.Equal(emoji, result.UsedFallback);
        Assert.Equal(emoji ? 1 : 0, matcher.Requests.Count);
    }

    [Theory]
    [InlineData(0x231A, true)] // Watch: outside the previous supplementary-block heuristic.
    [InlineData(0x1F321, false)] // Thermometer: inside that block, but defaults to text.
    public void MissingBaseUsesExactUnicodeDefault(int codepoint, bool emoji)
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Assert.False(primary.ContainsGlyph(codepoint));
        Matcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution result = resolver.ResolveTypeface(primary, codepoint, CultureInfo.InvariantCulture);
        Assert.True(result.UsedFallback);
        Assert.Single(matcher.Requests);
        Assert.Equal(emoji, matcher.Requests[0].Emoji);
    }

    [Fact]
    public void ExplicitTextAndEmojiUseIndependentFallbackCacheEntries()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution text = resolver.ResolveTypeface(primary, "\U0001F600\uFE0E", CultureInfo.InvariantCulture);
        TerminalFontResolution emoji = resolver.ResolveTypeface(primary, "\U0001F600\uFE0F", CultureInfo.InvariantCulture);
        Assert.True(text.UsedFallback);
        Assert.True(emoji.UsedFallback);
        Assert.NotEqual(text.Typeface.Handle, emoji.Typeface.Handle);
        Assert.Same(text.Typeface, resolver.ResolveTypeface(primary, "\U0001F600\uFE0E", CultureInfo.InvariantCulture).Typeface);
        Assert.Same(emoji.Typeface, resolver.ResolveTypeface(primary, 0x1F600, CultureInfo.InvariantCulture).Typeface);
        Assert.Equal(2, matcher.Requests.Count);
        Assert.Equal(2, resolver.CachedFallbackCount);
    }

    [Fact]
    public void AdditionalComponentDoesNotInheritExplicitBasePresentation()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(returnColor: false);
        using TerminalFontResolver resolver = new(matcher);
        _ = resolver.ResolveTypeface(primary, "#\uFE0F\u20E3", CultureInfo.InvariantCulture);
        Assert.Equal(new[] { ((int)'#', true), (0x20E3, false) }, matcher.Requests);
    }

    [Fact]
    public void WarmPresentationChecksDoNotAllocate()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        for (int i = 0; i < 100; i++) _ = resolver.ResolveTypeface(primary, "\U0001F600\uFE0F", CultureInfo.InvariantCulture);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = resolver.ResolveTypeface(primary, "\U0001F600\uFE0F", CultureInfo.InvariantCulture);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Single(matcher.Requests);
    }

    [Fact]
    public void ColorDetectionUsesGlyphDataNotEmojiFamilyName()
    {
        using SKTypeface mono = Load("NotoEmoji-Regular.ttf");
        using SKTypeface color = Load("NotoColorEmoji.ttf");
        using TerminalGlyphPresentation monoPresentation = new(mono);
        using TerminalGlyphPresentation colorPresentation = new(color);
        Assert.False(monoPresentation.IsColorGlyph(mono.GetGlyph(0x1F600)));
        Assert.True(colorPresentation.IsColorGlyph(color.GetGlyph(0x1F600)));
        Assert.False(colorPresentation.IsColorGlyph(0));
        Assert.False(colorPresentation.IsColorGlyph(color.GetGlyph(' ')));
    }

    [Fact]
    public void ColorInteropExportsAreAvailableAndEmptyBlobsAreOwned()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using HarfBuzzTypefaceEntry entry = new(primary, preferMemoryStream: true);
        uint glyph = primary.GetGlyph('A');
        Assert.Equal(0u, HarfBuzzColorApi.GetLayerCount(entry.Face.Handle, glyph, 0, 0, 0));
        Assert.Equal(0, HarfBuzzColorApi.HasPaint(entry.Face.Handle, glyph));
        AssertEmpty(HarfBuzzColorApi.ReferenceSvg(entry.Face.Handle, glyph));
        AssertEmpty(HarfBuzzColorApi.ReferencePng(entry.Font.Handle, glyph));
        static void AssertEmpty(nint blob)
        {
            Assert.NotEqual(nint.Zero, blob);
            try { Assert.Equal(0u, HarfBuzzColorApi.BlobLength(blob)); }
            finally { HarfBuzzColorApi.DestroyBlob(blob); }
        }
    }

    [Fact]
    public void MemoryStreamAndTableCallbackFacesAgreeAndDoNotOwnCallerTypeface()
    {
        using SKTypeface primary = Load("NotoColorEmoji.ttf");
        using (HarfBuzzTypefaceEntry tables = new(primary))
        using (HarfBuzzTypefaceEntry stream = new(primary, preferMemoryStream: true))
        {
            Assert.True(tables.Font.TryGetNominalGlyph(0x1F600, out uint tableGlyph));
            Assert.True(stream.Font.TryGetNominalGlyph(0x1F600, out uint streamGlyph));
            Assert.Equal(tableGlyph, streamGlyph);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.Equal(1u, HarfBuzzColorApi.GetLayerCount(tables.Face.Handle, tableGlyph, 0, 0, 0));
            Assert.Equal(1u, HarfBuzzColorApi.GetLayerCount(stream.Face.Handle, streamGlyph, 0, 0, 0));
        }
        Assert.NotEqual(nint.Zero, primary.Handle);
        Assert.True(primary.ContainsGlyph(0x1F600));
    }

    private static SKTypeface Load(string name)
        => FontPresentationTestFonts.Load(name);

    [Fact]
    public void BitmapGlyphApiReadsPinnedNotoColorDataWithoutRequiringCoreTextToLoadIt()
    {
        using HarfBuzzSharp.Blob data = HarfBuzzSharp.Blob.FromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "NotoColorEmoji.ttf"));
        using HarfBuzzSharp.Face face = new(data, 0);
        using HarfBuzzSharp.Font font = new(face);
        font.SetFunctionsOpenType();
        Assert.True(font.TryGetNominalGlyph(0x1F600, out uint glyph));
        nint blob = HarfBuzzColorApi.ReferencePng(font.Handle, glyph);
        try { Assert.True(HarfBuzzColorApi.BlobLength(blob) > 0); }
        finally { HarfBuzzColorApi.DestroyBlob(blob); }
    }

    private sealed class Matcher(bool returnColor = true) : ITerminalFontMatcher
    {
        internal List<(int Codepoint, bool Emoji)> Requests { get; } = [];
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
        {
            bool emoji = languageTags?.Contains("und-Zsye", StringComparer.Ordinal) == true;
            Requests.Add((codepoint, emoji));
            SKTypeface face = Load(emoji && returnColor ? "NotoColorEmoji.ttf" : "NotoEmoji-Regular.ttf");
            return face;
        }
    }
}
