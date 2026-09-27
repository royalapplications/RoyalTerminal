// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Globalization;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalFontCandidateTests
{
    // Ghostty's CodepointResolver continues discovery after rejecting the
    // presentation of a cmap hit. WT DirectWrite and xterm canvas obtain their
    // platform fallback first; the managed shared renderer retains that fast
    // path, then searches the complete candidate stream when it cannot use it.
    [Fact]
    public void WrongPresentationAndMissingGlyphDoNotHideALaterValidCandidate()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(() => Load("NotoEmoji-Regular.ttf"),
            [() => Load("JetBrainsMono-Regular.ttf"), () => Load("NotoEmoji-Regular.ttf"), () => Load("NotoColorEmoji.ttf"),
                () => throw new InvalidOperationException("Discovery must stop after acceptance.")]);
        TerminalFontResolver resolver = new(matcher);
        TerminalFontResolution result = resolver.ResolveTypeface(primary, "\U0001F600\uFE0F", CultureInfo.InvariantCulture);
        Assert.Same(matcher.Yielded[2], result.Typeface);
        Assert.True(result.UsedFallback);
        Assert.Equal(0, result.ReplacementCodepoint);
        Assert.Equal(3, matcher.Yielded.Count);
        Assert.Equal(1, matcher.Closed);
        Assert.All(matcher.FirstMatches, face => Assert.Equal(nint.Zero, face.Handle));
        Assert.Equal(nint.Zero, matcher.Yielded[0].Handle);
        Assert.Equal(nint.Zero, matcher.Yielded[1].Handle);
        Assert.NotEqual(nint.Zero, result.Typeface.Handle);
        Assert.Same(result.Typeface, resolver.ResolveTypeface(primary, "\U0001F600\uFE0F", CultureInfo.InvariantCulture).Typeface);
        Assert.Equal(1, matcher.Enumerations);
        resolver.Dispose();
        resolver.Dispose();
        Assert.Equal(nint.Zero, result.Typeface.Handle);
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Fact]
    public void FirstMatchAndConfiguredCoverageNeverEnumerateMoreFonts()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(() => Load("NotoColorEmoji.ttf"), [() => throw new InvalidOperationException("Unexpected enumeration.")]);
        using TerminalFontResolver resolver = new(matcher);
        Assert.Same(primary, resolver.ResolveTypeface(primary, 'A').Typeface);
        Assert.True(resolver.ResolveTypeface(primary, 0x1F600).UsedFallback);
        Assert.Equal(0, matcher.Enumerations);
    }

    [Fact]
    public void BorrowedAndPreviouslyCachedCandidatesKeepTheirLifetimeWhenRejected()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using SKTypeface borrowed = Load("NotoEmoji-Regular.ttf");
        SKTypeface accepted = Load("NotoColorEmoji.ttf");
        Matcher matcher = new(() => null, [() => borrowed, () => accepted]);
        TerminalTypefaceCollection collection = new(new(primary, TerminalTypefaceStyle.Regular), new(borrowed, TerminalTypefaceStyle.Bold));
        TerminalFontResolver resolver = new(matcher);
        Assert.Same(accepted, resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, "\U0001F600\uFE0F").Typeface);
        matcher.Factories = [() => accepted, () => primary];
        _ = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, "\U0001F600\uFE0E");
        Assert.NotEqual(nint.Zero, accepted.Handle);
        Assert.NotEqual(nint.Zero, borrowed.Handle);
        resolver.Dispose();
        Assert.Equal(nint.Zero, accepted.Handle);
        Assert.NotEqual(nint.Zero, borrowed.Handle);
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Fact]
    public void CompleteMissIsStickyAndCultureKeysRemainIndependent()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(() => null, [() => Load("NotoEmoji-Regular.ttf")]);
        using TerminalFontResolver resolver = new(matcher);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(primary, "\U0001F600\uFE0F", CultureInfo.InvariantCulture).ReplacementCodepoint);
        int misses = matcher.Enumerations;
        matcher.Factories = [() => Load("NotoColorEmoji.ttf")];
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(primary, "\U0001F600\uFE0F", CultureInfo.InvariantCulture).ReplacementCodepoint);
        Assert.Equal(misses, matcher.Enumerations);
        Assert.Equal(0, resolver.ResolveTypeface(primary, "\U0001F600\uFE0F", CultureInfo.GetCultureInfo("ja-JP")).ReplacementCodepoint);
        Assert.Equal(misses + 1, matcher.Enumerations);
        Assert.Contains("ja-JP", matcher.LastLanguages!);
    }

    [Fact]
    public void CollectionDiscoveryIsRegularOnlyAndReusesAcceptedFacesAcrossScalars()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(() => null, [() => Load("NotoColorEmoji.ttf")]);
        using TerminalFontResolver resolver = new(matcher);
        TerminalTypefaceCollection collection = new TerminalTypefaceCollection(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular)).WithSyntheticStyles();
        TerminalFontResolution first = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.BoldItalic, "#\uFE0F");
        TerminalFontResolution second = resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Italic, "1\uFE0F");
        Assert.Equal(SKFontStyle.Normal.Weight, matcher.LastStyle!.Weight);
        Assert.Equal(SKFontStyleSlant.Upright, matcher.LastStyle.Slant);
        Assert.Same(first.Typeface, second.Typeface);
        Assert.Equal(TerminalFontSynthesis.None, first.Synthesis);
        Assert.Equal(TerminalFontSynthesis.None, second.Synthesis);
        Assert.Equal(1, matcher.Enumerations);
    }

    [Fact]
    public void IteratorFailureClosesResourcesAndReleasesPreviouslyRejectedFaces()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(() => null,
            [() => Load("NotoEmoji-Regular.ttf"), () => throw new InvalidOperationException("Injected iterator failure.")]);
        using TerminalFontResolver resolver = new(matcher);
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveTypeface(primary, "\U0001F600\uFE0F"));
        Assert.Equal(1, matcher.Closed);
        Assert.Equal(nint.Zero, Assert.Single(matcher.Yielded).Handle);
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Fact]
    public void SourceInternalProbeCleanupCannotDisposeAnEarlierAcceptedFace()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        SKTypeface emoji = Load("NotoColorEmoji.ttf");
        Matcher matcher = new(() => null, [() => emoji]);
        TerminalFontResolver resolver = new(matcher);
        Assert.Same(emoji, resolver.ResolveTypeface(primary, 0x1F600).Typeface);
        matcher.Factories = [];
        matcher.DiscardProbe = release => release(emoji);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(primary, 0x10FFFF).ReplacementCodepoint);
        Assert.NotEqual(nint.Zero, emoji.Handle);
        Assert.Same(emoji, resolver.ResolveTypeface(primary, 0x1F600).Typeface);
        resolver.Dispose();
        Assert.Equal(nint.Zero, emoji.Handle);
    }

    [Fact]
    public void WarmSuccessAndMissDoNotAllocateOrReopenDiscovery()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new(() => null, [() => Load("NotoColorEmoji.ttf")]);
        using TerminalFontResolver resolver = new(matcher);
        for (int i = 0; i < 1000; i++) Resolve();
        int enumerations = matcher.Enumerations;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Resolve();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(enumerations, matcher.Enumerations);

        void Resolve()
        {
            _ = resolver.ResolveTypeface(primary, "\U0001F600\uFE0F", CultureInfo.InvariantCulture);
            _ = resolver.ResolveTypeface(primary, 0x10FFFF, CultureInfo.InvariantCulture);
        }
    }

    [Fact]
    public void CoreTextScorePrioritizesMonospaceThenSlantThenWeightThenCoverage()
    {
        uint mono = CoreTextFontCandidates.Score(1, true, true, true, SKFontStyle.Normal);
        uint slant = CoreTextFontCandidates.Score(1, false, true, false, SKFontStyle.Normal);
        uint weight = CoreTextFontCandidates.Score(1, false, false, true, SKFontStyle.Normal);
        uint coverage = CoreTextFontCandidates.Score(65536, false, true, true, SKFontStyle.Normal);
        Assert.True(mono > slant);
        Assert.True(slant > weight);
        Assert.True(weight > coverage);
        Assert.Equal(ushort.MaxValue, coverage);
        Assert.True(CoreTextFontCandidates.Score(100, false, false, false, SKFontStyle.Normal) >
            CoreTextFontCandidates.Score(99, false, false, false, SKFontStyle.Normal));
    }

    [Fact]
    public void CoreTextNameRankingPrefersExactStyleAndThenShorterFuzzyNames()
    {
        uint exact = CoreTextFontCandidates.Score(1, false, true, true, SKFontStyle.Normal, "REGULAR");
        uint traits = CoreTextFontCandidates.Score(65535, false, false, false, SKFontStyle.Normal, "Book");
        Assert.True(exact > traits);
        Assert.True(CoreTextFontCandidates.Score(1, false, false, false, SKFontStyle.Normal, "Regular Book") >
            CoreTextFontCandidates.Score(65535, false, false, false, SKFontStyle.Normal, "Regular Extended Book"));
    }

    [Fact]
    public void CoreTextTraitRefinementUsesFontTablesAndStableVariationTags()
    {
        bool bold = false, italic = false, italSeen = false;
        CoreTextFontCandidates.RefineTableTraits([0, 3], 0, 1, 2, ref bold, ref italic);
        Assert.True(bold);
        Assert.True(italic);
        CoreTextFontCandidates.RefineTableTraits([0], 0, 1, 2, ref bold, ref italic);
        Assert.True(bold); // A truncated table cannot clear existing traits.
        CoreTextFontCandidates.RefineVariationTraits(0x77676874, 600, ref bold, ref italic, ref italSeen);
        Assert.False(bold);
        CoreTextFontCandidates.RefineVariationTraits(0x77676874, 601, ref bold, ref italic, ref italSeen);
        Assert.True(bold);
        CoreTextFontCandidates.RefineVariationTraits(0x736C6E74, -5, ref bold, ref italic, ref italSeen);
        Assert.True(italic);
        CoreTextFontCandidates.RefineVariationTraits(0x6974616C, 0, ref bold, ref italic, ref italSeen);
        Assert.False(italic);
        CoreTextFontCandidates.RefineVariationTraits(0x736C6E74, -12, ref bold, ref italic, ref italSeen);
        Assert.False(italic); // Explicit ital wins regardless of axis ordering.
    }

    [Fact]
    public void WindowsCandidatesRemainAliveAfterEarlyIteratorDisposal()
    {
        if (!OperatingSystem.IsWindows()) return;
        using SKFontManager manager = SKFontManager.CreateDefault();
        SKTypeface? accepted = null;
        using (IEnumerator<SKTypeface> candidates = TerminalFontCandidates.Enumerate(manager, SKFontStyle.Normal, null, 'A').GetEnumerator())
        {
            while (candidates.MoveNext())
            {
                SKTypeface face = candidates.Current;
                if (face.ContainsGlyph('A')) { accepted = face; break; }
                face.Dispose();
            }
        }
        Assert.NotNull(accepted);
        using (accepted) Assert.True(accepted.ContainsGlyph('A'));
    }

    [Fact]
    public void CoreTextNativeCandidatesRetainExactNamesAndDescendingRank()
    {
        if (!OperatingSystem.IsMacOS()) return;
        List<NamedFontCandidate>? names = CoreTextFontCandidates.Find(SKFontStyle.Normal, 'A');
        Assert.NotNull(names);
        Assert.NotEmpty(names);
        Assert.All(names, name => { Assert.NotEmpty(name.Family); Assert.NotEmpty(name.PostScriptName); });
        for (int i = 1; i < names.Count; i++) Assert.True(names[i - 1].Score >= names[i].Score);
        using SKFontManager manager = SKFontManager.CreateDefault();
        using IEnumerator<SKTypeface> candidates = TerminalFontCandidates.Enumerate(manager, SKFontStyle.Normal, null, 'A').GetEnumerator();
        Assert.True(candidates.MoveNext());
        using SKTypeface face = candidates.Current;
        Assert.True(face.ContainsGlyph('A'));
        Assert.Contains(names, name => name.PostScriptName == face.PostScriptName);
    }

    [Fact]
    public void FontconfigNativeCandidatesRetainRealCollectionPathsAndCharacterCoverage()
    {
        if (!OperatingSystem.IsLinux()) return;
        List<FontFileCandidate>? files = FontconfigFontCandidates.Find(SKFontStyle.Normal, ["en"], 'A');
        Assert.NotNull(files);
        Assert.NotEmpty(files);
        foreach (FontFileCandidate file in files)
        {
            using SKTypeface? face = TerminalFontCandidates.LoadFile(file.Path, file.Index);
            Assert.NotNull(face);
            Assert.True(face.ContainsGlyph('A'));
        }
    }

    [Fact]
    public void CollectionHeaderRejectsTruncationAndSupportsEveryTtcFace()
    {
        Assert.Equal(1, TerminalFontCandidates.CollectionCount(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "JetBrainsMono-Regular.ttf")));
        string path = Path.GetTempFileName();
        try
        {
            byte[] header = new byte[20];
            "ttcf"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 2);
            File.WriteAllBytes(path, header);
            Assert.Equal(2, TerminalFontCandidates.CollectionCount(path));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), uint.MaxValue);
            File.WriteAllBytes(path, header);
            Assert.Equal(0, TerminalFontCandidates.CollectionCount(path));
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.Equal(0, TerminalFontCandidates.CollectionCount(path));
        }
        finally { File.Delete(path); }
        Assert.Equal(0, TerminalFontCandidates.CollectionCount(path));
    }

    private static SKTypeface Load(string file) => FontPresentationTestFonts.Load(file);
    private sealed class Matcher(Func<SKTypeface?> first, Func<SKTypeface>[] factories) : ITerminalFontMatcher, ITerminalFontCandidateMatcher
    {
        internal Func<SKTypeface>[] Factories { get; set; } = factories;
        internal List<SKTypeface> Yielded { get; } = [];
        internal List<SKTypeface> FirstMatches { get; } = [];
        internal int Enumerations { get; private set; }
        internal int Closed { get; private set; }
        internal SKFontStyle? LastStyle { get; private set; }
        internal string[]? LastLanguages { get; private set; }
        internal Action<Action<SKTypeface>>? DiscardProbe { get; set; }
        public SKTypeface? MatchCharacter(string? family, SKFontStyle style, string[]? languages, int codepoint)
        {
            SKTypeface? face = first();
            if (face is not null) FirstMatches.Add(face);
            return face;
        }
        public IEnumerable<SKTypeface> MatchCandidates(SKFontStyle style, string[]? languages, int codepoint, Action<SKTypeface> releaseRejected)
        {
            Enumerations++;
            LastStyle = style;
            LastLanguages = languages;
            try
            {
                DiscardProbe?.Invoke(releaseRejected);
                foreach (Func<SKTypeface> factory in Factories)
                {
                    SKTypeface face = factory();
                    Yielded.Add(face);
                    yield return face;
                }
            }
            finally { Closed++; }
        }
    }
}
