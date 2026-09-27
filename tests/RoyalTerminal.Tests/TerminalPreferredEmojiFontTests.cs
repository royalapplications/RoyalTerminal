// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalPreferredEmojiFontTests
{
    [Fact]
    public void KnownFamilyIsResolvedOnceAndSharedAcrossCharactersCulturesAndPrimaryFonts()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface otherPrimary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface emoji = Load("NotoColorEmoji.ttf");
        string family = emoji.FamilyName;
        Matcher matcher = new(() => emoji);
        TerminalFontResolver resolver = new(matcher, family);
        TerminalFontResolution first = resolver.ResolveTypeface(primary, "#\uFE0F", CultureInfo.InvariantCulture);
        TerminalFontResolution second = resolver.ResolveTypeface(otherPrimary, "1\uFE0F", CultureInfo.GetCultureInfo("pl-PL"));
        Assert.True(first.UsedFallback);
        Assert.Same(emoji, first.Typeface);
        Assert.Same(emoji, second.Typeface);
        Assert.Equal(new[] { family }, matcher.Families);
        Assert.Equal(0, matcher.CharacterRequests);
        Assert.Equal(2, resolver.CachedFallbackCount);
        resolver.Dispose();
        resolver.Dispose();
        Assert.Equal(nint.Zero, emoji.Handle);
        Assert.NotEqual(nint.Zero, primary.Handle);
        Assert.NotEqual(nint.Zero, otherPrimary.Handle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrSubstitutedFamilyUsesGeneralDiscoveryAndCachesTheMiss(bool substitute)
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface familyProbe = Load("NotoColorEmoji.ttf");
        SKTypeface? rejected = null;
        Matcher matcher = new(() => substitute ? rejected = Load("JetBrainsMono-Regular.ttf") : null,
            () => Load("NotoColorEmoji.ttf"));
        using TerminalFontResolver resolver = new(matcher, familyProbe.FamilyName);
        Assert.True(resolver.ResolveTypeface(primary, "#\uFE0F").UsedFallback);
        Assert.True(resolver.ResolveTypeface(primary, "1\uFE0F").UsedFallback);
        Assert.Single(matcher.Families);
        Assert.Equal(2, matcher.CharacterRequests);
        if (substitute) Assert.Equal(nint.Zero, rejected!.Handle);
    }

    [Fact]
    public void ExactFamilyStillRequiresGlyphCoverageAndFallsBackForUnsupportedCharacters()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface preferred = Load("JetBrainsMono-Regular.ttf");
        Assert.False(preferred.ContainsGlyph(0x1F600));
        Matcher matcher = new(() => preferred, () => Load("NotoColorEmoji.ttf"));
        TerminalFontResolver resolver = new(matcher, preferred.FamilyName);
        TerminalFontResolution result = resolver.ResolveTypeface(primary, 0x1F600);
        Assert.True(result.UsedFallback);
        Assert.True(result.Typeface.ContainsGlyph(0x1F600));
        Assert.Equal(1, matcher.CharacterRequests);
        resolver.Dispose();
        Assert.Equal(nint.Zero, preferred.Handle); // Owned even though never a successful fallback.
        Assert.Equal(nint.Zero, result.Typeface.Handle);
    }

    [Fact]
    public void ExactFamilyWithOnlyMonochromeGlyphsDoesNotSatisfyExplicitEmoji()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface preferred = Load("NotoEmoji-Regular.ttf");
        Matcher matcher = new(() => preferred, () => Load("NotoColorEmoji.ttf"));
        using (TerminalFontResolver resolver = new(matcher, preferred.FamilyName))
        {
            TerminalFontResolution result = resolver.ResolveTypeface(primary, "\U0001F600\uFE0F");
            Assert.True(result.UsedFallback);
            Assert.NotSame(preferred, result.Typeface);
            Assert.Equal(1, matcher.CharacterRequests);
        }
        Assert.Equal(nint.Zero, preferred.Handle);
    }

    [Fact]
    public void RejectingCachedColorFaceForTextDoesNotDisposeItsEarlierEmojiEntry()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface color = Load("NotoColorEmoji.ttf");
        Matcher matcher = new(() => null, () => color);
        TerminalFontResolver resolver = new(matcher);
        Assert.Same(color, resolver.ResolveTypeface(primary, "\U0001F600\uFE0F").Typeface);
        _ = resolver.ResolveTypeface(primary, "\U0001F600\uFE0E");
        Assert.NotEqual(nint.Zero, color.Handle);
        Assert.Same(color, resolver.ResolveTypeface(primary, "\U0001F600\uFE0F").Typeface);
        Assert.True(color.ContainsGlyph(0x1F600));
        resolver.Dispose();
        Assert.Equal(nint.Zero, color.Handle);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("#\uFE0E")]
    [InlineData("\U0001F600\uFE0E")]
    [InlineData("")]
    public void TextPresentationNeverQueriesPreferredEmojiFamily(string text)
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(() => throw new InvalidOperationException("must not query"));
        using TerminalFontResolver resolver = new(matcher, "preferred");
        _ = resolver.ResolveTypeface(primary, text);
        Assert.Empty(matcher.Families);
    }

    [Fact]
    public void NoPlatformPreferenceRetainsGenericFontMatching()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(() => throw new InvalidOperationException("disabled"), () => Load("NotoColorEmoji.ttf"));
        using TerminalFontResolver resolver = new(matcher);
        Assert.True(resolver.ResolveTypeface(primary, "#\uFE0F").UsedFallback);
        Assert.Empty(matcher.Families);
        Assert.Equal(1, matcher.CharacterRequests);
    }

    [Fact]
    public void MatcherWithoutFamilyCapabilityKeepsGeneralFallback()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using TerminalFontResolver resolver = new(new CharacterOnlyMatcher(), "preferred");
        Assert.True(resolver.ResolveTypeface(primary, "#\uFE0F").UsedFallback);
    }

    [Fact]
    public void ConfiguredColorPrimaryIsNeverQueriedOrDisposedAsFallback()
    {
        using SKTypeface primary = Load("NotoColorEmoji.ttf");
        Matcher matcher = new(() => primary);
        TerminalFontResolver resolver = new(matcher, primary.FamilyName);
        TerminalFontResolution result = resolver.ResolveTypeface(primary, "#\uFE0F");
        Assert.Same(primary, result.Typeface);
        Assert.False(result.UsedFallback);
        _ = resolver.ResolveTypeface(primary, "#\uFE0F");
        Assert.Empty(matcher.Families);
        resolver.Dispose();
        Assert.NotEqual(nint.Zero, primary.Handle);
        Assert.True(primary.ContainsGlyph('#'));
    }

    [Fact]
    public void SubstitutionWithCallerPrimaryDoesNotSuppressGeneralEmojiFallback()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(() => primary, () => Load("NotoColorEmoji.ttf"));
        using (TerminalFontResolver resolver = new(matcher, "missing preferred family"))
        {
            TerminalFontResolution result = resolver.ResolveTypeface(primary, "#\uFE0F");
            Assert.True(result.UsedFallback);
            Assert.NotSame(primary, result.Typeface);
            Assert.Equal(1, matcher.CharacterRequests);
        }
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Fact]
    public void UncoveredClusterDoesNotSelectComponentFontWithWrongBasePresentation()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface preferred = Load("NotoColorEmoji.ttf");
        Assert.False(preferred.ContainsGlyph(0x0301));
        Matcher matcher = new(() => preferred);
        using TerminalFontResolver resolver = new(matcher, preferred.FamilyName);
        TerminalFontResolution result = resolver.ResolveTypeface(primary, "#\uFE0F\u0301");
        Assert.Same(primary, result.Typeface);
        Assert.False(result.UsedFallback);
        Assert.Equal(0xFFFD, result.ReplacementCodepoint);
        Assert.Single(matcher.Families);
    }

    [Fact]
    public void WarmPreferredFallbackDoesNotAllocateOrDiscoverAgain()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface preferred = Load("NotoColorEmoji.ttf");
        Matcher matcher = new(() => preferred);
        using TerminalFontResolver resolver = new(matcher, preferred.FamilyName);
        for (int i = 0; i < 100; i++) _ = resolver.ResolveTypeface(primary, "#\uFE0F", CultureInfo.InvariantCulture);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = resolver.ResolveTypeface(primary, "#\uFE0F", CultureInfo.InvariantCulture);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Single(matcher.Families);
        Assert.Equal(0, matcher.CharacterRequests);
    }

    [Fact]
    public async Task ConcurrentFirstUsePerformsOneNamedQuery()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface preferred = Load("NotoColorEmoji.ttf");
        Matcher matcher = new(() => preferred);
        using TerminalFontResolver resolver = new(matcher, preferred.FamilyName);
        Task<TerminalFontResolution>[] queries = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => resolver.ResolveTypeface(primary, "#\uFE0F"))).ToArray();
        TerminalFontResolution[] results = await Task.WhenAll(queries);
        Assert.All(results, result => Assert.Same(preferred, result.Typeface));
        Assert.Single(matcher.Families);
    }

    [Fact]
    public void DefaultResolverUsesInstalledAppleEmojiOnMacOS()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using TerminalFontResolver resolver = new();
        TerminalFontResolution result = resolver.ResolveTypeface(primary, 0x1F600);
        Assert.True(result.UsedFallback);
        Assert.Equal("Apple Color Emoji", result.Typeface.FamilyName);
        Assert.True(result.Typeface.ContainsGlyph(0x1F600));
    }

    private static SKTypeface Load(string name)
        => FontPresentationTestFonts.Load(name);

    private sealed class Matcher(Func<SKTypeface?> family, Func<SKTypeface?>? character = null)
        : ITerminalFontMatcher, ITerminalFontFamilyMatcher
    {
        internal List<string> Families { get; } = [];
        internal int CharacterRequests { get; private set; }
        public SKTypeface? MatchFamily(string familyName) { Families.Add(familyName); return family(); }
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
        { CharacterRequests++; return character?.Invoke(); }
    }

    private sealed class CharacterOnlyMatcher : ITerminalFontMatcher
    {
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
            => Load("NotoColorEmoji.ttf");
    }
}
