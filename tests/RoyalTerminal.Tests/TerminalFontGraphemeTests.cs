// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public class TerminalFontGraphemeTests
{
    [Fact]
    public void FindsWholeClusterFontFromAdditionalComponentWithoutCandidateList()
    {
        using SKTypeface primary = LoadFont("JetBrainsMono-Regular.ttf");
        Assert.True(primary.ContainsGlyph('#'));
        Assert.False(primary.ContainsGlyph(0x20E3));
        FixtureFontMatcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);

        TerminalFontResolution result = resolver.ResolveTypeface(primary, "#\u20E3", CultureInfo.InvariantCulture);

        Assert.True(result.UsedFallback);
        Assert.True(result.Typeface.ContainsGlyph('#'));
        Assert.True(result.Typeface.ContainsGlyph(0x20E3));
        Assert.Equal(new[] { (int)'#', 0x20E3 }, matcher.Requests.Select(request => request.Codepoint));
        Assert.True(matcher.Requests[0].EmojiPresentation);
        Assert.False(matcher.Requests[1].EmojiPresentation);
    }

    [Theory]
    [InlineData("#\uFE0F\u20E3")]
    [InlineData("#\u200D\u20E3")]
    [InlineData("#\uFE0E\u20E3")]
    public void PresentationControlsDoNotRequireStandaloneGlyphCoverage(string text)
    {
        using SKTypeface primary = LoadFont("JetBrainsMono-Regular.ttf");
        FixtureFontMatcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);

        TerminalFontResolution result = resolver.ResolveTypeface(primary, text, CultureInfo.InvariantCulture);

        Assert.True(result.UsedFallback);
        Assert.True(result.Typeface.ContainsGlyph(0x20E3));
        Assert.DoesNotContain(matcher.Requests, request => request.Codepoint is 0x200D or 0xFE0E or 0xFE0F);
    }

    [Fact]
    public void SameFirstRuneDoesNotReuseAnotherClustersResolution()
    {
        using SKTypeface primary = LoadFont("JetBrainsMono-Regular.ttf");
        FixtureFontMatcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution keycap = resolver.ResolveTypeface(primary, "#\u20E3", CultureInfo.InvariantCulture);

        TerminalFontResolution plain = resolver.ResolveTypeface(primary, "#\u200D", CultureInfo.InvariantCulture);

        Assert.True(keycap.UsedFallback);
        Assert.False(plain.UsedFallback);
        Assert.Same(primary, plain.Typeface);
    }

    [Fact]
    public void MissingWholeClusterFontPreservesBestEffortNonNullContract()
    {
        using SKTypeface primary = LoadFont("JetBrainsMono-Regular.ttf");
        FixtureFontMatcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);

        TerminalFontResolution result = resolver.ResolveTypeface(primary, "a\U0010FFFE", CultureInfo.InvariantCulture);

        Assert.Same(primary, result.Typeface);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public void WarmClusterFallbackHasNoManagedAllocations()
    {
        using SKTypeface primary = LoadFont("JetBrainsMono-Regular.ttf");
        FixtureFontMatcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        for (int i = 0; i < 20; i++)
        {
            _ = resolver.ResolveTypeface(primary, "#\u20E3", CultureInfo.InvariantCulture);
        }

        int requests = matcher.Requests.Count;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _ = resolver.ResolveTypeface(primary, "#\u20E3", CultureInfo.InvariantCulture);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(requests, matcher.Requests.Count);
    }

    [Fact]
    public void ResolverOwnsFallbackButNeverCallersPrimaryTypeface()
    {
        using SKTypeface primary = LoadFont("JetBrainsMono-Regular.ttf");
        FixtureFontMatcher matcher = new();
        TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution result = resolver.ResolveTypeface(primary, "#\u20E3", CultureInfo.InvariantCulture);

        resolver.Dispose();

        Assert.Equal(nint.Zero, result.Typeface.Handle);
        Assert.NotEqual(nint.Zero, primary.Handle);
        Assert.Throws<ObjectDisposedException>(() => resolver.ResolveTypeface(primary, "#\u20E3"));
    }

    private static SKTypeface LoadFont(string fileName)
        => SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", fileName))
           ?? throw new InvalidOperationException($"Unable to load deterministic font fixture {fileName}.");

    private sealed class FixtureFontMatcher : ITerminalFontMatcher
    {
        public List<(int Codepoint, bool EmojiPresentation)> Requests { get; } = new();

        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
        {
            Requests.Add((codepoint, languageTags?.Contains("und-Zsye", StringComparer.Ordinal) == true));
            return codepoint == 0x20E3 ? LoadFont("NotoEmoji-Regular.ttf") : null;
        }
    }
}
