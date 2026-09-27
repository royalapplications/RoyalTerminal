// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalFontStylePolicyTests
{
    [Fact]
    public void StyleNamesNormalizeWithoutChangingAdvertisedNamesOrIndependentDisables()
    {
        TerminalFontFamilySettings settings = new()
        { RegularStyle = " default ", BoldStyle = " false ", ItalicStyle = " Book Oblique ", BoldItalicStyle = null! };
        TerminalFontFamilySettings normalized = settings.Normalize();
        Assert.Equal(string.Empty, normalized.RegularStyle);
        Assert.Equal("false", normalized.BoldStyle);
        Assert.Equal("Book Oblique", normalized.ItalicStyle);
        Assert.Equal(string.Empty, normalized.BoldItalicStyle);
        Assert.Same(normalized, normalized.Normalize());
        Assert.False(normalized.IsEmpty);
        Assert.True(new TerminalFontFamilySettings { RegularStyle = "default" }.Normalize().IsEmpty);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"boldStyle\":null}")]
    [InlineData("{\"boldStyle\":\"default\"}")]
    public void GeneratedJsonPreservesOmittedAndNullStyleDefaults(string fonts)
    {
        string json = "{\"profiles\":[{\"id\":\"old\",\"appearance\":{\"fontFamilies\":" + fonts + "}}]}";
        TerminalFontFamilySettings settings = Assert.Single(TerminalSessionProfileSerializer.FromJson(json).Profiles).Appearance.FontFamilies;
        Assert.True(settings.IsEmpty);
        Assert.Equal(string.Empty, settings.BoldStyle);
        Assert.Equal(string.Empty, settings.ItalicStyle);
    }

    [Fact]
    public void NamedStylesAndDisablesRoundTripInProfiles()
    {
        TerminalFontFamilySettings settings = new()
        { Regular = ["Family"], RegularStyle = "Book", BoldStyle = "Heavy", ItalicStyle = "false", BoldItalicStyle = "Oblique" };
        TerminalSessionProfilesDocument document = new() { Profiles = [new() { Id = "font", Appearance = new() { FontFamilies = settings } }] };
        TerminalFontFamilySettings roundTrip = Assert.Single(TerminalSessionProfileSerializer.FromJson(
            TerminalSessionProfileSerializer.ToJson(document)).Profiles).Appearance.FontFamilies;
        Assert.Equal(settings.RegularStyle, roundTrip.RegularStyle);
        Assert.Equal(settings.BoldStyle, roundTrip.BoldStyle);
        Assert.Equal(settings.ItalicStyle, roundTrip.ItalicStyle);
        Assert.Equal(settings.BoldItalicStyle, roundTrip.BoldItalicStyle);
    }

    [Fact]
    public void AdvertisedNameOverridesLogicalStyleAndMissingNameDoesNotSubstitute()
    {
        using SKFontManager manager = SKFontManager.CreateDefault();
        using SKTypeface primary = SKTypeface.FromFamilyName("monospace");
        using SKFontStyleSet styles = manager.GetFontStyles(primary.FamilyName);
        Assert.True(styles.Count > 0);
        string name = styles.GetStyleName(0);
        using SKTypeface expected = styles.CreateTypeface(0);
        SkiaConfiguredFontFamilyMatcher matcher = new(manager);
        using SKTypeface? actual = matcher.Match(primary.FamilyName, TerminalTypefaceStyle.BoldItalic, name);
        Assert.NotNull(actual);
        Assert.Equal(expected.IsBold, actual.IsBold);
        Assert.Equal(expected.IsItalic, actual.IsItalic);
        Assert.Null(matcher.Match(primary.FamilyName, TerminalTypefaceStyle.Regular, "RoyalTerminal-Missing-Style-9787D17A"));
    }

    [Fact]
    public void LoaderPassesNamedStylesToExplicitAndInheritedFamiliesAndSkipsDisabledLoads()
    {
        Matcher matcher = new((_, _, _) => Load("JetBrainsMono-Regular.ttf"));
        using ConfiguredFontFamilies loaded = ConfiguredFontFamilies.Load(new()
        {
            Regular = ["Text"], RegularStyle = "Book", Bold = ["Never loaded"], BoldStyle = "false",
            ItalicStyle = "Oblique", BoldItalicStyle = "Heavy Oblique",
        }, matcher, () => throw new InvalidOperationException("Unexpected legacy fallback."));
        Assert.Equal(3, matcher.Requests.Count);
        Assert.Equal(("Text", TerminalTypefaceStyle.Regular, "Book"), matcher.Requests[0]);
        Assert.Equal((loaded.Collection.GetPrimaryTypeface().FamilyName, TerminalTypefaceStyle.Italic, "Oblique"), matcher.Requests[1]);
        Assert.Equal("Heavy Oblique", matcher.Requests[2].Name);
        Assert.Same(loaded.Collection.GetPrimaryTypeface(), loaded.Collection.GetPrimaryTypeface(TerminalTypefaceStyle.Bold));
        Assert.Equal(TerminalTypefaceStyle.BoldItalic, loaded.Collection.GetEffectiveStyle(TerminalTypefaceStyle.BoldItalic));
    }

    [Fact]
    public void NamesWithoutConfiguredFamiliesDoNotReloadThePrimaryFile()
    {
        Matcher matcher = new((_, _, _) => throw new InvalidOperationException("A primary file must retain its face."));
        using ConfiguredFontFamilies loaded = ConfiguredFontFamilies.Load(new()
        { RegularStyle = "Unknown", BoldStyle = "Unknown", ItalicStyle = "false", BoldItalicStyle = "Unknown" }, matcher,
            () => new GlyphCache("unused", TerminalFontSource.File, Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "JetBrainsMono-Regular.ttf")));
        Assert.Empty(matcher.Requests);
        Assert.Equal(TerminalTypefaceStyle.Regular, loaded.Collection.GetEffectiveStyle(TerminalTypefaceStyle.Italic));
    }

    [Fact]
    public void LegacyPrimarySystemFamilySupportsNamedRegularAndInheritedStyleSelection()
    {
        Matcher matcher = new((_, _, _) => Load("JetBrainsMono-Regular.ttf"));
        using ConfiguredFontFamilies loaded = ConfiguredFontFamilies.Load(new()
        { RegularStyle = "Book", BoldStyle = "Heavy", ItalicStyle = "false", BoldItalicStyle = "false" }, matcher,
            () => throw new InvalidOperationException("A named primary matched; legacy loading must stay lazy."), "Primary");
        Assert.Equal(2, matcher.Requests.Count);
        Assert.Equal(("Primary", TerminalTypefaceStyle.Regular, "Book"), matcher.Requests[0]);
        Assert.Equal("Heavy", matcher.Requests[1].Name);
        Assert.Equal(loaded.Collection.GetPrimaryTypeface().FamilyName, matcher.Requests[1].Family);
    }

    [Fact]
    public void MissingNamedLegacySystemStyleUsesRegularRatherThanAnotherStyledFace()
    {
        Matcher matcher = new((_, _, _) => null);
        using ConfiguredFontFamilies loaded = ConfiguredFontFamilies.Load(new()
        { BoldStyle = "Not available", ItalicStyle = "false", BoldItalicStyle = "false" }, matcher,
            () => new GlyphCache("unused", TerminalFontSource.File, Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "JetBrainsMono-Regular.ttf")), "Primary");
        Assert.Single(matcher.Requests);
        Assert.Equal("Not available", matcher.Requests[0].Name);
        Assert.Same(loaded.Collection.GetPrimaryTypeface(), loaded.Collection.GetPrimaryTypeface(TerminalTypefaceStyle.Bold));
    }

    [Fact]
    public void MissingNamedFacesFallThroughWithoutDiscardingOtherFamilies()
    {
        Matcher matcher = new((family, style, name) => family == "Missing" || (style != TerminalTypefaceStyle.Regular && name == "Missing style")
            ? null : Load("JetBrainsMono-Regular.ttf"));
        using ConfiguredFontFamilies loaded = ConfiguredFontFamilies.Load(new()
        { Regular = ["Missing", "Text"], RegularStyle = "Book", BoldStyle = "Missing style" }, matcher,
            () => throw new InvalidOperationException("A valid regular face must survive a missing style."));
        Assert.Same(loaded.Collection.GetPrimaryTypeface(), loaded.Collection.GetPrimaryTypeface(TerminalTypefaceStyle.Bold));
        Assert.Contains(matcher.Requests, request => request.Name == "Missing style");
    }

    [Fact]
    public void BorrowedCollectionsPreserveIndependentFlagsAndMappingsAcrossCopies()
    {
        using SKTypeface regular = Load("NotoEmoji-Regular.ttf");
        using SKTypeface bold = Load("NotoColorEmoji.ttf");
        TerminalTypefaceCollection original = new(new(regular, TerminalTypefaceStyle.Regular), new(bold, TerminalTypefaceStyle.Bold),
            new(bold, TerminalTypefaceStyle.BoldItalic));
        TerminalTypefaceCollection disabled = original.WithDisabledStyles(bold: true)
            .WithCodepointMappings(new TerminalTypefaceCodepointMapping('A', 'Z', "Mapped"));
        Assert.Same(bold, original.GetPrimaryTypeface(TerminalTypefaceStyle.Bold));
        Assert.Same(regular, disabled.GetPrimaryTypeface(TerminalTypefaceStyle.Bold));
        Assert.Same(bold, disabled.GetPrimaryTypeface(TerminalTypefaceStyle.BoldItalic));
        Assert.Equal("Mapped", disabled.GetCodepointFamily('A'));
        TerminalTypefaceCollection changed = disabled.WithDisabledStyles(italic: true, boldItalic: true);
        Assert.Same(bold, changed.GetPrimaryTypeface(TerminalTypefaceStyle.Bold));
        Assert.Same(regular, changed.GetPrimaryTypeface(TerminalTypefaceStyle.BoldItalic));
        Assert.Equal("Mapped", changed.GetCodepointFamily('A'));
        Assert.Throws<ArgumentOutOfRangeException>(() => changed.GetEffectiveStyle((TerminalTypefaceStyle)9));
    }

    [Theory]
    [InlineData(TerminalTypefaceStyle.Bold)]
    [InlineData(TerminalTypefaceStyle.Italic)]
    [InlineData(TerminalTypefaceStyle.BoldItalic)]
    public void DisabledStyleUsesRegularForScalarsStringsAndSpans(TerminalTypefaceStyle style)
    {
        using SKTypeface regular = Load("NotoEmoji-Regular.ttf");
        using SKTypeface styled = Load("NotoColorEmoji.ttf");
        TerminalTypefaceCollection original = new(new(regular, TerminalTypefaceStyle.Regular), new(styled, style));
        TerminalTypefaceCollection disabled = original.WithDisabledStyles(bold: true, italic: true, boldItalic: true);
        using TerminalFontResolver resolver = new(new NoDiscovery());
        Assert.Same(styled, resolver.ResolveTypeface(original, style, 0x1F600).Typeface);
        Assert.Same(regular, resolver.ResolveTypeface(disabled, style, 0x1F600).Typeface);
        Assert.Same(regular, resolver.ResolveTypeface(disabled, style, "😀").Typeface);
        Assert.Same(regular, resolver.ResolveTypeface(disabled, style, "😀\uFE0E".AsSpan()).Typeface);
        resolver.Dispose();
        Assert.NotEqual(nint.Zero, regular.Handle);
        Assert.NotEqual(nint.Zero, styled.Handle);
    }

    [AvaloniaFact]
    public void StyleEditorParticipatesInDirtyTrackingAndProfileSwitch()
    {
        TerminalSettingsPanelState state = new();
        state.MarkSaved();
        state.FontFamiliesEditor.RegularStyle = "Book";
        state.FontFamiliesEditor.BoldStyle = "false";
        state.FontFamiliesEditor.ItalicStyle = "Oblique";
        state.FontFamiliesEditor.BoldItalicStyle = "Heavy";
        Assert.True(state.IsDirty);
        TerminalFontFamilySettings value = Assert.Single(state.BuildDocument().Profiles).Appearance.FontFamilies;
        Assert.Equal("Book", value.RegularStyle);
        Assert.Equal("false", value.BoldStyle);
        Assert.Equal("Oblique", value.ItalicStyle);
        Assert.Equal("Heavy", value.BoldItalicStyle);
        state.LoadDocument(new() { Profiles = [new() { Id = "Other" }] });
        Assert.True(state.FontFamiliesEditor.BuildSettings().IsEmpty);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisabledStylesReachShapedUnshapedAndCursorDrawingWithoutChangingCells(bool shaping, bool cursor)
    {
        using SKTypeface regular = Load("NotoEmoji-Regular.ttf");
        using SKTypeface bold = Load("NotoColorEmoji.ttf");
        TerminalTypefaceCollection mixed = new(new(regular, TerminalTypefaceStyle.Regular), new(bold, TerminalTypefaceStyle.Bold));
        TerminalTypefaceCollection plain = new(new TerminalTypefaceEntry(regular, TerminalTypefaceStyle.Regular));
        using SkiaTerminalRenderer actual = SkiaTerminalRenderer.CreateWithTypefaces(mixed.WithDisabledStyles(bold: true), 24);
        using SkiaTerminalRenderer expected = SkiaTerminalRenderer.CreateWithTypefaces(plain, 24);
        actual.EnableTextShaping = expected.EnableTextShaping = shaping;
        actual.CursorVisible = expected.CursorVisible = cursor;
        TerminalScreen screen = new(4, 1);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[1m😀"));
        byte[] before = processor.GetBinarySnapshot();
        using SKBitmap actualBitmap = Draw(actual, screen);
        using SKBitmap expectedBitmap = Draw(expected, screen);
        Assert.Equal(expectedBitmap.Bytes, actualBitmap.Bytes);
        Assert.Equal(before, processor.GetBinarySnapshot());

        static SKBitmap Draw(SkiaTerminalRenderer renderer, TerminalScreen screen)
        {
            SKBitmap bitmap = new((int)Math.Ceiling(renderer.CellWidth * screen.Columns), (int)Math.Ceiling(renderer.CellHeight));
            using SKCanvas canvas = new(bitmap);
            renderer.RenderFull(canvas, screen);
            return bitmap;
        }
    }

    private static SKTypeface Load(string file) => FontPresentationTestFonts.Load(file);
    private sealed class Matcher(Func<string, TerminalTypefaceStyle, string?, SKTypeface?> match) : IConfiguredFontFamilyMatcher
    {
        internal List<(string Family, TerminalTypefaceStyle Style, string? Name)> Requests { get; } = [];
        public SKTypeface? Match(string family, TerminalTypefaceStyle style, string? styleName = null)
        { Requests.Add((family, style, styleName)); return match(family, style, styleName); }
    }
    private sealed class NoDiscovery : ITerminalFontMatcher
    {
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languages, int codepoint) => null;
    }
}
