// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalFontHotPathTests
{
    [Theory]
    [InlineData("U+41=Font")]
    [InlineData("U+41-U+5A,=Font, With Comma")]
    [InlineData("=unused")]
    [InlineData("U+41,=invalid")]
    [InlineData("U+42-U+41=invalid")]
    [InlineData("u+41=invalid")]
    public void AllocationFreeValidatorUsesTheFullParserGrammar(string entry)
    {
        string[] entries = ["", entry];
        bool parsed = TerminalFontCodepointMap.TryParse(entries, out _, out string? parsedError);
        bool valid = TerminalFontCodepointMap.TryValidate(entries, out string? validationError);
        Assert.Equal(parsed, valid);
        Assert.Equal(parsedError, validationError);
    }

    [Fact]
    public void RepeatedNormalizedConfigurationValidationDoesNotAllocateParsedMaps()
    {
        TerminalFontFamilySettings settings = new()
        { Regular = ["Text"], BoldStyle = "Book", CodepointMaps = ["U+41-U+5A,U+61-U+7A=Text", "U+2500-U+257F=Symbols"] };
        for (int i = 0; i < 100; i++) settings.Normalize();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) settings.Normalize();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Same(settings, settings.Normalize());
    }

    [Fact]
    public void AsciiCacheKeepsStylesCulturesSelectorsAndCollectionsIndependent()
    {
        using SKTypeface regular = FontPresentationTestFonts.Load("JetBrainsMono-Regular.ttf");
        using SKTypeface styled = FontPresentationTestFonts.Load("JetBrainsMono-Regular.ttf");
        TerminalTypefaceCollection original = new(new(regular, TerminalTypefaceStyle.Regular), new(styled, TerminalTypefaceStyle.Bold));
        TerminalTypefaceCollection disabled = original.WithDisabledStyles(bold: true);
        using TerminalFontResolver resolver = new(new NoDiscovery());
        CultureInfo[] cultures = [CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("pl-PL"), CultureInfo.InvariantCulture];
        foreach (CultureInfo culture in cultures)
            for (int cp = 0x20; cp <= 0x7E; cp++)
            {
                Assert.Same(regular, resolver.ResolveTypeface(original, TerminalTypefaceStyle.Regular, cp, culture).Typeface);
                Assert.Same(styled, resolver.ResolveTypeface(original, TerminalTypefaceStyle.Bold, cp, culture).Typeface);
                Assert.Same(regular, resolver.ResolveTypeface(disabled, TerminalTypefaceStyle.Bold, cp, culture).Typeface);
            }
        Assert.Equal(0, resolver.ResolveTypeface(original, TerminalTypefaceStyle.Regular, "A\uFE0E").ReplacementCodepoint);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(original, TerminalTypefaceStyle.Regular, "A\uFE0F").ReplacementCodepoint);
        Assert.Equal(0, resolver.ResolveTypeface(original, TerminalTypefaceStyle.Regular, 'A').ReplacementCodepoint);
    }

    [Fact]
    public void WarmAsciiResolutionIsAllocationFreeAcrossAllStyles()
    {
        using SKTypeface face = FontPresentationTestFonts.Load("JetBrainsMono-Regular.ttf");
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular));
        using TerminalFontResolver resolver = new(new NoDiscovery());
        for (int warm = 0; warm < 10; warm++) ResolveAscii();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) ResolveAscii();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);

        void ResolveAscii()
        {
            for (TerminalTypefaceStyle style = TerminalTypefaceStyle.Regular; style <= TerminalTypefaceStyle.BoldItalic; style++)
                for (int cp = 0x20; cp <= 0x7E; cp++) resolver.ResolveTypeface(collection, style, cp, CultureInfo.InvariantCulture);
        }
    }

    [Fact]
    public void AsciiMissesRemainStickyAndDoNotMaskAnotherCulturesDiscovery()
    {
        using SKTypeface primary = FontPresentationTestFonts.Load("NotoEmoji-Regular.ttf");
        SKTypeface discovered = FontPresentationTestFonts.Load("JetBrainsMono-Regular.ttf");
        Assert.False(primary.ContainsGlyph('A'));
        Assert.True(primary.ContainsGlyph(' '));
        ChangingDiscovery matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular));
        TerminalFontResolution missing = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'A', CultureInfo.InvariantCulture);
        Assert.NotEqual(0, missing.ReplacementCodepoint);
        matcher.Available = discovered;
        Assert.Same(discovered, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'A', CultureInfo.GetCultureInfo("pl-PL")).Typeface);
        Assert.Equal(missing, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'A', CultureInfo.InvariantCulture));
        resolver.Dispose();
        Assert.Equal(nint.Zero, discovered.Handle);
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmSpriteDecisionsCachePresentAndMissingMappings(bool available)
    {
        using SKTypeface regular = FontPresentationTestFonts.Load("JetBrainsMono-Regular.ttf");
        using SKTypeface mapped = FontPresentationTestFonts.Load("NotoEmoji-Regular.ttf");
        MapMatcher matcher = new(available ? mapped : null);
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection collection = new TerminalTypefaceCollection(new TerminalTypefaceEntry(regular, TerminalTypefaceStyle.Regular))
            .WithCodepointMappings(new TerminalTypefaceCodepointMapping(0x2611, 0x2611, "Symbol"));
        Assert.Equal(available, resolver.TryGetCodepointOverride(collection, 0x2611, out SKTypeface? found));
        Assert.Same(available ? mapped : null, found);
        for (int i = 0; i < 100; i++) resolver.TryGetCodepointOverride(collection, 0x2611, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) resolver.TryGetCodepointOverride(collection, 0x2611, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(1, matcher.FamilyCalls);
        Assert.False(resolver.TryGetCodepointOverride(collection, 0x2500, out _));
    }

    private sealed class NoDiscovery : ITerminalFontMatcher
    {
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languages, int codepoint) => null;
    }
    private sealed class MapMatcher(SKTypeface? face) : ITerminalFontMatcher, ITerminalFontFamilyMatcher
    {
        public int FamilyCalls { get; private set; }
        public SKTypeface? MatchFamily(string familyName) { FamilyCalls++; return face; }
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languages, int codepoint) => null;
    }
    private sealed class ChangingDiscovery : ITerminalFontMatcher
    {
        public SKTypeface? Available { get; set; }
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languages, int codepoint) => Available;
    }
}
