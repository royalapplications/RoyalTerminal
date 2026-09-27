// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Collection/CodepointResolver/SharedGrid define ordering and sticky
// hit/miss policy. Windows Terminal and xterm.js delegate to DirectWrite/canvas;
// these portable fixtures assert our explicit policy, not platform font order.
public sealed class TerminalTypefaceCollectionTests
{
    [Fact]
    public void ConstructorCopiesEntriesAndRequiresLiveRegularFace()
    {
        using SKTypeface regular = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface other = Load("NotoEmoji-Regular.ttf");
        TerminalTypefaceEntry[] entries = [new(regular, TerminalTypefaceStyle.Regular)];
        TerminalTypefaceCollection collection = new(entries);
        entries[0] = new(other, TerminalTypefaceStyle.Regular);
        Assert.Same(regular, collection.GetPrimaryTypeface());
        Assert.Same(regular, collection.GetPrimaryTypeface(TerminalTypefaceStyle.BoldItalic));
        Assert.Throws<ArgumentException>(() => new TerminalTypefaceCollection());
        Assert.Throws<ArgumentException>(() => new TerminalTypefaceCollection(new TerminalTypefaceEntry(other, TerminalTypefaceStyle.Bold)));
        Assert.Throws<ArgumentNullException>(() => new TerminalTypefaceCollection(null!));
        Assert.Throws<ArgumentNullException>(() => new TerminalTypefaceCollection(new TerminalTypefaceEntry(null!, TerminalTypefaceStyle.Regular)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalTypefaceCollection(new TerminalTypefaceEntry(regular, (TerminalTypefaceStyle)4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => collection.GetPrimaryTypeface((TerminalTypefaceStyle)(-1)));
        other.Dispose();
        Assert.Throws<ObjectDisposedException>(() => new TerminalTypefaceCollection(new TerminalTypefaceEntry(other, TerminalTypefaceStyle.Regular)));
    }

    [Theory]
    [InlineData(TerminalTypefaceStyle.Regular)]
    [InlineData(TerminalTypefaceStyle.Bold)]
    [InlineData(TerminalTypefaceStyle.Italic)]
    [InlineData(TerminalTypefaceStyle.BoldItalic)]
    public void ConfiguredFacesKeepInsertionOrderAndDefaultPresentation(TerminalTypefaceStyle style)
    {
        using SKTypeface regular = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface mono = Load("NotoEmoji-Regular.ttf");
        using SKTypeface color = Load("NotoColorEmoji.ttf");
        Matcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection first = new(new(regular, TerminalTypefaceStyle.Regular), new(mono, style), new(color, style));
        TerminalTypefaceCollection reversed = new(new(regular, TerminalTypefaceStyle.Regular), new(color, style), new(mono, style));
        Assert.Same(mono, resolver.ResolveTypeface(first, style, 0x1F600).Typeface);
        Assert.Same(color, resolver.ResolveTypeface(reversed, style, "😀").Typeface);
        Assert.Same(color, resolver.ResolveTypeface(first, style, "😀\uFE0F".AsSpan()).Typeface);
        Assert.Same(mono, resolver.ResolveTypeface(reversed, style, "😀\uFE0E").Typeface);
        Assert.Empty(matcher.Requests);
    }

    [Theory]
    [InlineData(TerminalTypefaceStyle.Bold)]
    [InlineData(TerminalTypefaceStyle.Italic)]
    [InlineData(TerminalTypefaceStyle.BoldItalic)]
    public void StyledMissUsesConfiguredRegularBeforeDiscovery(TerminalTypefaceStyle style)
    {
        using SKTypeface regular = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface emoji = Load("NotoEmoji-Regular.ttf");
        TerminalTypefaceCollection collection = new(new(regular, TerminalTypefaceStyle.Regular), new(emoji, style));
        Matcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution result = resolver.ResolveTypeface(collection, style, 'A');
        Assert.Same(regular, result.Typeface);
        Assert.True(result.UsedFallback);
        Assert.Equal(0, result.ReplacementCodepoint);
        Assert.Empty(matcher.Requests);
    }

    [Fact]
    public void MissingStyleDoesNotBorrowAnotherStyledCollection()
    {
        using SKTypeface regular = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface emoji = Load("NotoEmoji-Regular.ttf");
        TerminalTypefaceCollection collection = new(new(regular, TerminalTypefaceStyle.Regular), new(emoji, TerminalTypefaceStyle.Bold));
        using TerminalFontResolver resolver = new(new Matcher());
        TerminalFontResolution result = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.BoldItalic, 0x1F600);
        Assert.Same(regular, result.Typeface);
        Assert.Equal(0xFFFD, result.ReplacementCodepoint);
    }

    [Fact]
    public void DiscoveryUsesRegularStyleAndReusesFaceForLaterScalarsAndStyles()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        TerminalTypefaceCollection collection = new(new(primary, TerminalTypefaceStyle.Regular), new(primary, TerminalTypefaceStyle.Bold));
        Matcher matcher = new() { Match = _ => Load("NotoColorEmoji.ttf") };
        TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution first = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, 0x1F600);
        Assert.True(first.UsedFallback);
        Assert.Same(first.Typeface, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Italic, 0x1F601).Typeface);
        Assert.Same(first.Typeface, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 0x1F602).Typeface);
        Request request = Assert.Single(matcher.Requests);
        Assert.Equal((400, 5, SKFontStyleSlant.Upright), (request.Weight, request.Width, request.Slant));
        resolver.Dispose();
        resolver.Dispose();
        Assert.Equal(nint.Zero, first.Typeface.Handle);
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Fact]
    public void DiscoveredFacesMustMatchUnicodeDefaultBeforeAnyFallback()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular));
        Matcher matcher = new() { Match = cp => Load(cp == 0x20E3 ? "NotoEmoji-Regular.ttf" : "NotoColorEmoji.ttf") };
        using TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution mono = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 0x20E3);
        TerminalFontResolution color = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 0x1F600);
        Assert.NotEqual(mono.Typeface.Handle, color.Typeface.Handle);
        Assert.Equal(2, matcher.Requests.Count);
    }

    [Fact]
    public void FinalAnyCanReusePreviouslyAdmittedRegularDiscoveryFace()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular));
        Matcher matcher = new() { Match = cp => cp == 0x20E3 ? Load("NotoEmoji-Regular.ttf") : null };
        using TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution mono = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 0x20E3);
        TerminalFontResolution emoji = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, 0x1F600);
        Assert.Same(mono.Typeface, emoji.Typeface);
        Assert.Equal(0, emoji.ReplacementCodepoint);
        Assert.Contains(matcher.Requests, request => request.Codepoint == 0x1F600);
    }

    [Fact]
    public void NegativeResultsStayCachedAfterCollectionGrowthButNotAcrossCollectionsOrCultures()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        TerminalTypefaceCollection first = new(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular));
        TerminalTypefaceCollection second = new(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular));
        Matcher matcher = new() { Match = cp => cp == 0x1F600 ? Load("NotoColorEmoji.ttf") : null };
        using TerminalFontResolver resolver = new(matcher);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(first, TerminalTypefaceStyle.Regular, 0x1F601, CultureInfo.InvariantCulture).ReplacementCodepoint);
        TerminalFontResolution discovered = resolver.ResolveTypeface(first, TerminalTypefaceStyle.Regular, 0x1F600, CultureInfo.InvariantCulture);
        int calls = matcher.Requests.Count;
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(first, TerminalTypefaceStyle.Regular, 0x1F601, CultureInfo.InvariantCulture).ReplacementCodepoint);
        Assert.Equal(calls, matcher.Requests.Count);
        Assert.Same(discovered.Typeface, resolver.ResolveTypeface(first, TerminalTypefaceStyle.Regular, 0x1F601, CultureInfo.GetCultureInfo("fr-FR")).Typeface);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(second, TerminalTypefaceStyle.Regular, 0x1F601, CultureInfo.InvariantCulture).ReplacementCodepoint);
        Assert.Same(discovered.Typeface, resolver.ResolveTypeface(first, TerminalTypefaceStyle.Regular, 0x1F602, CultureInfo.InvariantCulture).Typeface);
    }

    [Fact]
    public void RejectedBorrowedStyledFaceRemainsAliveAndCannotSatisfyFinalAny()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface mono = Load("NotoEmoji-Regular.ttf");
        TerminalTypefaceCollection collection = new(new(primary, TerminalTypefaceStyle.Regular), new(mono, TerminalTypefaceStyle.Italic));
        Matcher matcher = new() { Match = _ => mono };
        using TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution result = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, 0x1F600);
        Assert.Equal(0xFFFD, result.ReplacementCodepoint);
        resolver.Dispose();
        Assert.NotEqual(nint.Zero, mono.Handle);
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Fact]
    public void AcceptedBorrowedOtherStyleFaceIsNotOwnedByDiscovery()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface color = Load("NotoColorEmoji.ttf");
        TerminalTypefaceCollection collection = new(new(primary, TerminalTypefaceStyle.Regular), new(color, TerminalTypefaceStyle.Italic));
        Matcher matcher = new() { Match = _ => color };
        TerminalFontResolver resolver = new(matcher);
        Assert.Same(color, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, 0x1F600).Typeface);
        resolver.Dispose();
        Assert.NotEqual(nint.Zero, color.Handle);
    }

    [Fact]
    public void ComponentCandidatesAndReplacementUseCollectionWithoutChangingStoredText()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface mono = Load("NotoEmoji-Regular.ttf");
        TerminalTypefaceCollection collection = new(new(primary, TerminalTypefaceStyle.Regular), new(mono, TerminalTypefaceStyle.Regular));
        Matcher matcher = new();
        using TerminalFontResolver resolver = new(matcher);
        Assert.Same(mono, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, "#\u20E3").Typeface);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Italic, "a\U0010FFFE").ReplacementCodepoint);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, "\uD800").ReplacementCodepoint);
        Assert.Same(primary, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, string.Empty).Typeface);
        Assert.Same(primary, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, -1).Typeface);
    }

    [Fact]
    public void SpaceReplacementAndPublicArgumentContracts()
    {
        using SKTypeface mono = Load("NotoEmoji-Regular.ttf");
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(mono, TerminalTypefaceStyle.Regular));
        using TerminalFontResolver resolver = new(new Matcher());
        Assert.Equal(' ', resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, 'A').ReplacementCodepoint);
        Assert.Throws<ArgumentNullException>(() => resolver.ResolveTypeface(null!, TerminalTypefaceStyle.Regular, 65));
        Assert.Throws<ArgumentNullException>(() => resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, (string)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.ResolveTypeface(collection, (TerminalTypefaceStyle)5, 65));
        Assert.Throws<ArgumentNullException>(() => GlyphCache.CreateWithTypefaces(null!));
        Assert.Throws<ArgumentNullException>(() => SkiaTerminalRenderer.CreateWithTypefaces(null!));
        Assert.Throws<ArgumentNullException>(() => SkiaTerminalGlyphCoverageSource.CreateWithTypefaces(null!));
        resolver.Dispose();
        Assert.Throws<ObjectDisposedException>(() => resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 'A'));
        Assert.Throws<ObjectDisposedException>(() => resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, "A"));
    }

    [Fact]
    public void WarmScalarAndClusterQueriesAllocateNothingAndDoNotRediscover()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular));
        Matcher matcher = new() { Match = _ => Load("NotoEmoji-Regular.ttf") };
        using TerminalFontResolver resolver = new(matcher);
        for (int pass = 0; pass < 10; pass++) Measure(resolver, collection);
        int calls = matcher.Requests.Count;
        long allocated = Measure(resolver, collection);
        Assert.Equal(0, allocated);
        Assert.Equal(calls, matcher.Requests.Count);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Measure(TerminalFontResolver resolver, TerminalTypefaceCollection collection)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _ = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Bold, '#', CultureInfo.InvariantCulture);
            _ = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, "#\u20E3", CultureInfo.InvariantCulture);
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public async Task ConcurrentCollectionQueriesShareOneDiscovery()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular));
        Matcher matcher = new() { Match = _ => Load("NotoColorEmoji.ttf") };
        using TerminalFontResolver resolver = new(matcher);
        Task<TerminalFontResolution>[] tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
            resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 0x1F600 + i, CultureInfo.InvariantCulture))).ToArray();
        TerminalFontResolution[] results = await Task.WhenAll(tasks);
        Assert.All(results, value => Assert.Same(results[0].Typeface, value.Typeface));
        Assert.Single(matcher.Requests);
    }

    [Fact]
    public void CacheAndCoverageFactoriesBorrowFacesAndUseRegularMetrics()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface mono = Load("NotoEmoji-Regular.ttf");
        TerminalTypefaceCollection collection = new(new(primary, TerminalTypefaceStyle.Regular), new(mono, TerminalTypefaceStyle.Regular), new(mono, TerminalTypefaceStyle.Bold));
        using GlyphCache cache = GlyphCache.CreateWithTypefaces(collection);
        Assert.Same(collection, cache.TypefaceCollection);
        Assert.Same(primary, cache.RegularTypeface);
        Assert.Same(mono, cache.GetTypeface(true, false));
        Assert.True(cache.MeasureCellSize(20).Width > 0);
        using SkiaTerminalGlyphCoverageSource coverage = SkiaTerminalGlyphCoverageSource.CreateWithTypefaces(collection);
        Assert.True(coverage.HasSystemGlyph('A'));
        Assert.True(coverage.HasSystemGlyph(0x1F600));
        Assert.False(coverage.HasSystemGlyph(0x10FFFF));
        cache.Dispose();
        coverage.Dispose();
        Assert.False(coverage.HasSystemGlyph('A'));
        Assert.NotEqual(nint.Zero, primary.Handle);
        Assert.NotEqual(nint.Zero, mono.Handle);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RendererRegularFallbackMatchesExplicitStyleFace(bool shaping, bool cursor)
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface mono = Load("NotoEmoji-Regular.ttf");
        TerminalTypefaceCollection regularFallback = new(new(primary, TerminalTypefaceStyle.Regular), new(mono, TerminalTypefaceStyle.Regular), new(primary, TerminalTypefaceStyle.Bold));
        TerminalTypefaceCollection explicitBold = new(new(primary, TerminalTypefaceStyle.Regular), new(mono, TerminalTypefaceStyle.Bold));
        using SkiaTerminalRenderer actual = SkiaTerminalRenderer.CreateWithTypefaces(regularFallback, 24);
        using SkiaTerminalRenderer expected = SkiaTerminalRenderer.CreateWithTypefaces(explicitBold, 24);
        actual.EnableTextShaping = expected.EnableTextShaping = shaping;
        actual.CursorVisible = expected.CursorVisible = cursor;
        actual.CursorColumn = expected.CursorColumn = 0;
        TerminalScreen screen = new(4, 1);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("\x1b[1m😀"));
        using SKBitmap left = Render(actual, screen);
        using SKBitmap right = Render(expected, screen);
        Assert.Equal(right.Bytes, left.Bytes);
        Assert.True(actual.GlyphCoverageSource.HasSystemGlyph(0x1F600));
        actual.Dispose();
        Assert.NotEqual(nint.Zero, mono.Handle);
    }

    [Fact]
    public void PreeditUsesAdditionalRegularFaceAndKeepsScreenUnchanged()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface mono = Load("NotoEmoji-Regular.ttf");
        using SkiaTerminalRenderer renderer = SkiaTerminalRenderer.CreateWithTypefaces(new(new(primary, TerminalTypefaceStyle.Regular), new(mono, TerminalTypefaceStyle.Regular)), 24);
        renderer.EnableTextRenderDiagnostics = true;
        renderer.Preedit = new("😀", 1);
        TerminalScreen screen = new(4, 1);
        using BasicVtProcessor processor = new(screen);
        byte[] before = processor.GetBinarySnapshot();
        using SKBitmap bitmap = Render(renderer, screen);
        Assert.True(renderer.GetTextRenderDiagnostics().FallbackFontHits > 0);
        Assert.Equal(before, processor.GetBinarySnapshot());
    }

    private static SKBitmap Render(SkiaTerminalRenderer renderer, TerminalScreen screen)
    {
        SKBitmap bitmap = new((int)Math.Ceiling(renderer.CellWidth * screen.Columns), (int)Math.Ceiling(renderer.CellHeight));
        using SKCanvas canvas = new(bitmap);
        renderer.RenderFull(canvas, screen);
        return bitmap;
    }

    private static SKTypeface Load(string name) => FontPresentationTestFonts.Load(name);
    private readonly record struct Request(int Codepoint, int Weight, int Width, SKFontStyleSlant Slant);
    private sealed class Matcher : ITerminalFontMatcher
    {
        internal Func<int, SKTypeface?>? Match { get; init; }
        internal List<Request> Requests { get; } = [];
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languages, int codepoint)
        {
            Requests.Add(new(codepoint, style.Weight, style.Width, style.Slant));
            return Match?.Invoke(codepoint);
        }
    }
}
