// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty font/shaper/run.zig replaces one entire unsupported cell with U+FFFD
// (then space) and strips suffix VS15/VS16 only after resolving presentation.
// WT MapCharacters and xterm.js canvas delegate this to their platform font
// stacks; our shared Skia path deliberately uses Ghostty's explicit policy.
public sealed class TerminalDisplayCellTests
{
    [Theory]
    [InlineData("\U0010FFFE")]
    [InlineData("a\U0010FFFE")]
    [InlineData("a\u0301\U0010FFFE")]
    [InlineData("a\uFE0F\U0010FFFE")]
    public void UnsupportedClusterUsesReplacementAndKeepsSourceWidthAndMetadata(string text)
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using TerminalFontResolver resolver = new(new MissingMatcher());
        TerminalFontResolution font = resolver.ResolveTypeface(primary, text, CultureInfo.InvariantCulture);
        Assert.Equal(0xFFFD, font.ReplacementCodepoint);
        Assert.True(font.Typeface.ContainsGlyph(font.ReplacementCodepoint));
        using TerminalDisplayCellBuffer buffer = new();
        TerminalCell source = new() { Codepoint = 'a', Grapheme = text, Width = 2, Foreground = 0xFF123456,
            Background = 0xFF654321, HyperlinkId = 42, Attributes = CellAttributes.Bold | CellAttributes.Italic,
            UnderlineStyle = TerminalUnderlineStyle.Curly, IsProtected = true };
        buffer.Add(in source, font);
        TerminalCell expected = source;
        expected.Codepoint = 0xFFFD;
        expected.Grapheme = null;
        Assert.Equal(expected, buffer.Cells[0]);
        Assert.Equal(text, source.Grapheme);
        Assert.Equal(2, source.Width);
        Assert.Equal(font, buffer.Fonts[0]);
    }

    [Theory]
    [InlineData("\U0010FFFE")]
    [InlineData("\U0010FFFE\uFE0F")]
    public void MissingReplacementUsesSpaceEvenWithExplicitEmojiPresentation(string text)
    {
        using SKTypeface primary = Load("NotoEmoji-Regular.ttf");
        Assert.False(primary.ContainsGlyph(0xFFFD));
        Assert.True(primary.ContainsGlyph(' '));
        using TerminalFontResolver resolver = new(new MissingMatcher());
        TerminalFontResolution result = resolver.ResolveTypeface(primary, text, CultureInfo.InvariantCulture);
        Assert.Same(primary, result.Typeface);
        Assert.False(result.UsedFallback);
        Assert.Equal(' ', result.ReplacementCodepoint);
    }

    [Fact]
    public void ScalarAndStringMissingGlyphHaveTheSameReplacementDecision()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using TerminalFontResolver resolver = new(new MissingMatcher());
        Assert.Equal(resolver.ResolveTypeface(primary, 0x10FFFE), resolver.ResolveTypeface(primary, "\U0010FFFE"));
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(primary, 0x10FFFE).ReplacementCodepoint);
        Assert.Equal(0, resolver.ResolveTypeface(primary, 'A').ReplacementCodepoint);
    }

    [Fact]
    public void MalformedUtf16UsesOneReplacement()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using TerminalFontResolver resolver = new(new MissingMatcher());
        string malformed = new('\uD800', 1);
        Assert.Equal(0xFFFD, resolver.ResolveTypeface(primary, malformed).ReplacementCodepoint);
    }

    [Fact]
    public void ReplacementCanUseAnotherOwnedFont()
    {
        using SKTypeface primary = Load("NotoEmoji-Regular.ttf");
        using TerminalFontResolver resolver = new(new ReplacementMatcher());
        TerminalFontResolution result = resolver.ResolveTypeface(primary, "\U0010FFFE");
        Assert.True(result.UsedFallback);
        Assert.Equal(0xFFFD, result.ReplacementCodepoint);
        Assert.True(result.Typeface.ContainsGlyph(0xFFFD));
        resolver.Dispose();
        Assert.Equal(nint.Zero, result.Typeface.Handle);
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Fact]
    public void LastResortRangePlaceholdersDoNotSuppressReplacement()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using TerminalFontResolver resolver = new(new LastResortMatcher());
        TerminalFontResolution result = resolver.ResolveTypeface(primary, 0x1F600);
        Assert.Same(primary, result.Typeface);
        Assert.Equal(0xFFFD, result.ReplacementCodepoint);
    }

    [Theory]
    [InlineData("A\uFE0E", "A")]
    [InlineData("#\uFE0F\u20E3", "#\u20E3")]
    [InlineData("A\uFE0E\uFE0F", "A")]
    [InlineData("A\u200D\uFE0F", "A\u200D")]
    [InlineData("\U0001F600\uFE0F\u200D\u2640\uFE0E", "\U0001F600\u200D\u2640")]
    [InlineData("\uFE0F\uFE0E", "\uFE0F")]
    [InlineData("a\u0301", "a\u0301")]
    public void ProjectionStripsOnlySuffixSelectorsAndPreservesOtherShapingControls(string text, string expected)
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using TerminalDisplayCellBuffer buffer = new();
        TerminalCell source = new() { Grapheme = text, Codepoint = char.ConvertToUtf32(text, 0), Width = 2 };
        buffer.Add(in source, new(primary, false));
        TerminalCell display = buffer.Cells[0];
        Assert.Equal(expected, display.Grapheme ?? char.ConvertFromUtf32(display.Codepoint));
        Assert.Equal(text, source.Grapheme);
        Assert.Equal(2, display.Width);
        buffer.Clear();
        Assert.True(buffer.Cells.IsEmpty);
        Assert.True(buffer.Fonts.IsEmpty);
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Fact]
    public void NormalizedLengthsKeepLaterGraphemeClusterBoundaries()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using TerminalDisplayCellBuffer buffer = new();
        buffer.Add(new() { Codepoint = 'A', Grapheme = "A\uFE0E", Width = 1 }, new(primary, false));
        buffer.Add(new() { Codepoint = 'a', Grapheme = "a\u0301", Width = 1 }, new(primary, false));
        Assert.False(SkiaTerminalRenderer.HasMultiClusterGraphemeCell(buffer.Cells, 0, 2, [0, 1, 1]));
        Assert.True(SkiaTerminalRenderer.HasMultiClusterGraphemeCell(buffer.Cells, 0, 2, [0, 1, 2]));
    }

    [Fact]
    public void SelectorCacheBoundsBothEntriesAndRetainedCharacters()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using TerminalDisplayCellBuffer buffer = new();
        TerminalFontResolution font = new(primary, false);
        for (int i = 0; i < 3000; i++)
        {
            TerminalCell cell = new() { Codepoint = 'a', Grapheme = "a" + i.ToString(CultureInfo.InvariantCulture) + "\uFE0F", Width = 1 };
            buffer.Add(in cell, font);
            buffer.Clear();
            Assert.InRange(buffer.CachedEntries, 1, TerminalDisplayCellBuffer.MaxCachedEntries);
            Assert.InRange(buffer.CachedCharacters, 1, TerminalDisplayCellBuffer.MaxCachedCharacters);
        }
        TerminalCell oversized = new() { Codepoint = 'a', Grapheme = new string('a', 70000) + "\uFE0F", Width = 1 };
        int before = buffer.CachedCharacters;
        buffer.Add(in oversized, font);
        Assert.Equal(before, buffer.CachedCharacters);
        buffer.Dispose();
        Assert.Equal(0, buffer.CachedCharacters);
        Assert.Equal(0, buffer.CachedEntries);
        Assert.True(buffer.Cells.IsEmpty);
        Assert.NotEqual(nint.Zero, primary.Handle);
    }

    [Fact]
    public void WarmProjectionDoesNotAllocate()
    {
        using SKTypeface primary = Load("JetBrainsMono-Regular.ttf");
        using TerminalDisplayCellBuffer buffer = new();
        TerminalCell cell = new() { Codepoint = '#', Grapheme = "#\uFE0F\u20E3", Width = 2 };
        TerminalFontResolution font = new(primary, false);
        for (int i = 0; i < 10; i++) _ = Measure(buffer, in cell, font);
        Assert.Equal(0, Measure(buffer, in cell, font));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Measure(TerminalDisplayCellBuffer buffer, in TerminalCell cell, TerminalFontResolution font)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { buffer.Add(in cell, font); buffer.Clear(); }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    internal static SKTypeface Load(string name) => FontPresentationTestFonts.Load(name);
    internal sealed class MissingMatcher : ITerminalFontMatcher
    {
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint) => null;
    }
    private sealed class ReplacementMatcher : ITerminalFontMatcher
    {
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
            => codepoint == 0xFFFD ? Load("JetBrainsMono-Regular.ttf") : null;
    }
    private sealed class LastResortMatcher : ITerminalFontMatcher
    {
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
            => Load("LastResort.ttf");
    }
}
