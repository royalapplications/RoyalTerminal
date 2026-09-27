// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalFontCodepointMapTests
{
    [Fact]
    public void ParserPreservesRangesOverlapFamilyPunctuationAndNativeConfigurationBounds()
    {
        Assert.True(TerminalFontCodepointMap.TryParse([
            " U+0041 - U+005A , U+0061-U+007a, = Family, A=B ", "U+0041=Later", "U+1FFFFF=Last", "=unused", ""
        ], out var mappings, out string? error), error);
        Assert.Equal(new TerminalCodepointFontMapping[]
        {
            new(0x41, 0x5A, "Family, A=B"), new(0x61, 0x7A, "Family, A=B"), new(0x41, 0x41, "Later"), new(0x1FFFFF, 0x1FFFFF, "Last")
        }, mappings);
    }

    [Theory]
    [InlineData("U+41")]
    [InlineData("u+41=Font")]
    [InlineData("U+=Font")]
    [InlineData("U+200000=Font")]
    [InlineData("U+FFFFFFFF=Font")]
    [InlineData("U+42-U+41=Font")]
    [InlineData("U+41-42=Font")]
    [InlineData("U+41,=Font")]
    [InlineData("U+41,,U+42=Font")]
    [InlineData("U+41oops=Font")]
    public void MalformedConfigurationIsAtomicAndReportsTheLine(string invalid)
    {
        Assert.False(TerminalFontCodepointMap.TryParse(["U+41=Good", invalid], out var result, out string? error));
        Assert.Empty(result);
        Assert.Contains("line 2", error);
        Assert.Throws<FormatException>(() => new TerminalFontFamilySettings { CodepointMaps = [invalid] }.Normalize());
    }

    [Fact]
    public void ProfileRoundTripAndNormalizationKeepMapOrderAndOldDefaults()
    {
        TerminalFontFamilySettings settings = new() { CodepointMaps = [" U+41=First ", "", "U+41-U+5A=Second"] };
        TerminalSessionProfilesDocument document = new() { Profiles = [new() { Id = "map", Appearance = new() { FontFamilies = settings } }] };
        var restored = Assert.Single(TerminalSessionProfileSerializer.FromJson(TerminalSessionProfileSerializer.ToJson(document)).Profiles);
        Assert.Equal(new[] { "U+41=First", "U+41-U+5A=Second" }, restored.Appearance.FontFamilies.CodepointMaps);
        Assert.False(restored.Appearance.FontFamilies.IsEmpty);
        Assert.Same(restored.Appearance.FontFamilies, restored.Appearance.FontFamilies.Normalize());
    }

    [Fact]
    public void CollectionCopiesMappingsAndRejectsInvalidRanges()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        TerminalTypefaceCollection original = new(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular));
        TerminalTypefaceCodepointMapping[] input = [new('A', 'Z', " First "), new('A', 'A', "Last")];
        TerminalTypefaceCollection mapped = original.WithCodepointMappings(input);
        input[1] = new('B', 'B', "Changed");
        Assert.False(original.HasCodepointMappings);
        Assert.True(mapped.HasCodepointMappings);
        Assert.Equal("Last", mapped.GetCodepointFamily('A'));
        Assert.Equal("First", mapped.GetCodepointFamily('B'));
        Assert.Null(mapped.GetCodepointFamily('0'));
        Assert.False(mapped.WithCodepointMappings().HasCodepointMappings);
        Assert.Throws<ArgumentNullException>(() => original.WithCodepointMappings(null!));
        Assert.Throws<ArgumentNullException>(() => original.WithCodepointMappings(new TerminalTypefaceCodepointMapping(0, 1, null!)));
        Assert.Throws<ArgumentOutOfRangeException>(() => original.WithCodepointMappings(new TerminalTypefaceCodepointMapping(2, 1, "Bad")));
        Assert.Throws<ArgumentOutOfRangeException>(() => original.WithCodepointMappings(new TerminalTypefaceCodepointMapping(-1, 1, "Bad")));
        Assert.Throws<ArgumentOutOfRangeException>(() => original.WithCodepointMappings(new TerminalTypefaceCodepointMapping(0, 0x200000, "Bad")));
    }

    [Fact]
    public void OverrideWinsAcrossStylesAndCachesOneDescriptorAcrossScalars()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface mapped = Load("NotoEmoji-Regular.ttf");
        Matcher matcher = new(_ => mapped);
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection collection = Collection(primary, new(0x1F600, 0x1F601, "emoji"));
        Assert.Empty(matcher.Families);
        foreach (TerminalTypefaceStyle style in Enum.GetValues<TerminalTypefaceStyle>())
        {
            Assert.Same(mapped, resolver.ResolveTypeface(collection, style, 0x1F600).Typeface);
            Assert.Same(mapped, resolver.ResolveTypeface(collection, style, 0x1F601).Typeface);
        }
        Assert.Equal(new[] { "emoji" }, matcher.Families);
        Assert.Equal(0, matcher.Characters);
        resolver.Dispose();
        Assert.Equal(nint.Zero, mapped.Handle);
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Fact]
    public void LastMatchingMissingDescriptorDoesNotTryEarlierRangeAndCachesMisses()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(_ => null);
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection collection = Collection(primary, new('A', 'Z', "earlier"), new('A', 'B', "missing"));
        Assert.Same(primary, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, 'A').Typeface);
        Assert.Same(primary, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Italic, 'B').Typeface);
        Assert.Equal(new[] { "missing" }, matcher.Families);
    }

    [Fact]
    public void MissingMappedGlyphFallsThroughButItsLoadedFaceRemainsReusable()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface mapped = Load("NotoEmoji-Regular.ttf");
        Matcher matcher = new(_ => mapped);
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection collection = Collection(primary, new('A', 'B', "emoji"));
        Assert.Same(primary, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'A').Typeface);
        // A mapped descriptor is a configured regular face: unlike discovered
        // fallback fonts, its default presentation is ANY even outside the map.
        Assert.Same(mapped, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, 0x1F600).Typeface);
        Assert.Single(matcher.Families);
        Assert.Equal(0, matcher.Characters);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MappedAndDiscoveredFacesRetainTheirLoadOrder(bool mappingFirst)
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface mapped = Load("NotoEmoji-Regular.ttf");
        SKTypeface discovered = Load("NotoColorEmoji.ttf");
        Matcher matcher = new(_ => mapped) { Character = _ => discovered };
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection collection = Collection(primary, new('A', 'A', "emoji"));
        if (mappingFirst) resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'A');
        Assert.Same(discovered, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, "😁\uFE0F").Typeface);
        if (!mappingFirst) resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'A');
        Assert.Same(mappingFirst ? mapped : discovered, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, 0x1F600).Typeface);
    }

    [Fact]
    public void MapOnlyConfigurationDoesNotEagerlyLoadMappedFamiliesAndFlowsThroughFactories()
    {
        TerminalFontFamilySettings settings = new() { CodepointMaps = ["U+2611=Not installed", "U+41-U+5A=monospace"] };
        string file = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "JetBrainsMono-Regular.ttf");
        using ConfiguredFontFamilies loaded = ConfiguredFontFamilies.Load(settings, new NeverLoadConfiguredFamily(),
            () => new GlyphCache("unused", TerminalFontSource.File, file));
        Assert.Equal("Not installed", loaded.Collection.GetCodepointFamily(0x2611));
        using GlyphCache cache = GlyphCache.CreateWithFontFamilies(settings, fontSource: TerminalFontSource.File, fontFilePath: file);
        Assert.True(cache.TypefaceCollection.HasCodepointMappings);
        Assert.Equal("monospace", cache.TypefaceCollection.GetCodepointFamily('A'));
        using SkiaTerminalGlyphCoverageSource coverage = SkiaTerminalGlyphCoverageSource.CreateWithFontFamilies(settings,
            fontSource: TerminalFontSource.File, fontFilePath: file);
        using SkiaTerminalRenderer renderer = SkiaTerminalRenderer.CreateWithFontFamilies(settings,
            fontSource: TerminalFontSource.File, fontFilePath: file);
        Assert.True(coverage.HasSystemGlyph('A'));
        Assert.True(renderer.GlyphCoverageSource.HasSystemGlyph('A'));
    }

    [Fact]
    public void WholeGraphemeStillChecksComponentsAndExplicitPresentation()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface mapped = Load("NotoEmoji-Regular.ttf");
        Matcher matcher = new(_ => mapped);
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection collection = Collection(primary, new(0x1F600, 0x1F600, "emoji"));
        Assert.Same(mapped, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, "😀").Typeface);
        Assert.Equal(0, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, "😀\uFE0E").ReplacementCodepoint);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, "😀\uFE0F").ReplacementCodepoint);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, "😀\U0010FFFE").ReplacementCodepoint);
    }

    [Fact]
    public void MappedOwnershipSurvivesRejectedFallbackAndDoesNotDisposeBorrowedFaces()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface mapped = Load("NotoEmoji-Regular.ttf");
        Matcher matcher = new(_ => mapped) { Character = _ => mapped };
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection collection = Collection(primary, new(0x1F600, 0x1F600, "emoji"));
        Assert.Same(mapped, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 0x1F600).Typeface);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, "😀\uFE0F").ReplacementCodepoint);
        Assert.NotEqual(nint.Zero, mapped.Handle);
        // A later collection may borrow a face already present in discovery.
        TerminalTypefaceCollection borrowed = Collection(mapped, new(0x1F601, 0x1F601, "same"));
        Assert.Same(mapped, resolver.ResolveTypeface(borrowed, TerminalTypefaceStyle.Regular, 0x1F601).Typeface);
        resolver.Dispose();
        Assert.NotEqual(nint.Zero, mapped.Handle);
    }

    [Fact]
    public void WarmMappedLookupIsAllocationFreeAndRespectsStickyMisses()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface mapped = Load("NotoEmoji-Regular.ttf");
        Matcher matcher = new(_ => mapped);
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection collection = Collection(primary, new('A', 'A', "emoji"));
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 0x1F600, CultureInfo.InvariantCulture).ReplacementCodepoint);
        resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'A', CultureInfo.InvariantCulture);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 0x1F600, CultureInfo.InvariantCulture).ReplacementCodepoint);
        for (int i = 0; i < 100; i++) resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'A', CultureInfo.InvariantCulture);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'A', CultureInfo.InvariantCulture);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SuccessfulOverrideReplacesBuiltInSymbolsInTextAndCursor(bool shaping, bool cursor)
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface mapped = Load("NotoEmoji-Regular.ttf");
        Assert.True(mapped.ContainsGlyph(0x2611));
        Matcher matcher = new(_ => mapped);
        using SkiaTerminalRenderer renderer = new(new TerminalFontResolver(matcher), Collection(primary, new(0x2611, 0x2611, "symbol")), 24)
        { EnableTextShaping = shaping, CursorVisible = cursor, CursorColumn = 0, EnableTextRenderDiagnostics = true };
        using SkiaTerminalRenderer missing = new(new TerminalFontResolver(new Matcher(_ => null)), Collection(primary, new(0x2611, 0x2611, "missing")), 24)
        { EnableTextShaping = shaping, CursorVisible = cursor, CursorColumn = 0, EnableTextRenderDiagnostics = true };
        TerminalScreen screen = new(3, 1);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("☑"));
        byte[] before = processor.GetBinarySnapshot();
        using SKBitmap actual = Render(renderer, screen);
        using SKBitmap fallback = Render(missing, screen);
        Assert.Equal(0, renderer.GetTextRenderDiagnostics().SpriteCells);
        Assert.True(missing.GetTextRenderDiagnostics().SpriteCells > 0);
        Assert.False(actual.Bytes.AsSpan().SequenceEqual(fallback.Bytes));
        Assert.Equal(before, processor.GetBinarySnapshot());
        Assert.Single(matcher.Families);
    }

    [Fact]
    public void PreeditAndCaretUseMappedFontWithoutChangingCompositionOrScreen()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface mapped = Load("NotoEmoji-Regular.ttf");
        Matcher matcher = new(_ => mapped);
        using SkiaTerminalRenderer renderer = new(new TerminalFontResolver(matcher), Collection(primary, new(0x1F600, 0x1F600, "emoji")), 24);
        TerminalScreen screen = new(4, 1);
        using BasicVtProcessor processor = new(screen);
        byte[] before = processor.GetBinarySnapshot();
        TerminalPreedit preedit = new("😀", 0);
        renderer.Preedit = preedit;
        TerminalCell[] cells = preedit.RenderCells.ToArray();
        using SKBitmap bitmap = Render(renderer, screen);
        Assert.Single(matcher.Families);
        Assert.Equal(0, matcher.Characters);
        Assert.Equal(cells, preedit.RenderCells.ToArray());
        Assert.Equal(before, processor.GetBinarySnapshot());
    }

    [AvaloniaFact]
    public void InvalidEditorMappingBlocksApplySaveAndRetainsLastValidProfileConfiguration()
    {
        TerminalSettingsPanelState state = new();
        state.FontFamiliesEditor.CodepointMaps = "U+41=First\r\nU+42=Second";
        Assert.True(state.ApplyCommand.CanExecute(null));
        int applies = 0, saves = 0;
        state.ApplyRequested += (_, _) => applies++;
        state.SaveRequested += (_, _) => saves++;
        state.FontFamiliesEditor.CodepointMaps = "U+41=First\n\ninvalid";
        Assert.Contains("line 3", state.FontFamiliesEditor.CodepointMapError);
        Assert.False(state.ApplyCommand.CanExecute(null));
        Assert.False(state.SaveCommand.CanExecute(null));
        state.ApplyCommand.Execute(null);
        state.SaveCommand.Execute(null);
        Assert.Equal(0, applies);
        Assert.Equal(0, saves);
        Assert.Equal(new[] { "U+41=First", "U+42=Second" }, Assert.Single(state.BuildDocument().Profiles).Appearance.FontFamilies.CodepointMaps);
        state.FontFamiliesEditor.CodepointMaps = "U+43=Third";
        Assert.False(state.FontFamiliesEditor.HasCodepointMapError);
        state.ApplyCommand.Execute(null);
        Assert.Equal(1, applies);
        Assert.Equal(new[] { "U+43=Third" }, Assert.Single(state.BuildDocument().Profiles).Appearance.FontFamilies.CodepointMaps);
    }

    private static TerminalTypefaceCollection Collection(SKTypeface primary, params TerminalTypefaceCodepointMapping[] maps)
        => new TerminalTypefaceCollection(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular)).WithCodepointMappings(maps);
    private static SKTypeface Load(string file) => FontPresentationTestFonts.Load(file);
    private static SKBitmap Render(SkiaTerminalRenderer renderer, TerminalScreen screen)
    {
        SKBitmap bitmap = new((int)Math.Ceiling(renderer.CellWidth * screen.Columns), (int)Math.Ceiling(renderer.CellHeight));
        using SKCanvas canvas = new(bitmap);
        renderer.RenderFull(canvas, screen);
        return bitmap;
    }
    private sealed class Matcher(Func<string, SKTypeface?> family) : ITerminalFontMatcher, ITerminalFontFamilyMatcher
    {
        public List<string> Families { get; } = [];
        public int Characters { get; private set; }
        public Func<int, SKTypeface?>? Character { get; init; }
        public SKTypeface? MatchFamily(string familyName) { Families.Add(familyName); return family(familyName); }
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
        { Characters++; return Character?.Invoke(codepoint); }
    }
    private sealed class NeverLoadConfiguredFamily : IConfiguredFontFamilyMatcher
    {
        public SKTypeface? Match(string family, TerminalTypefaceStyle style) => throw new InvalidOperationException("Mapped families must load lazily.");
    }
}
