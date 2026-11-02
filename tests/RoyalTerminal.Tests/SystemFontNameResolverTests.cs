// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SystemFontNameResolverTests
{
    [Theory]
    [InlineData("Source Code Pro Light", "Source Code Pro", "Light")]
    [InlineData("Cascadia Code SemiLight", "Cascadia Code", "SemiLight")]
    [InlineData("Cascadia Mono ExtraLight Italic", "Cascadia Mono", "ExtraLight Italic")]
    [InlineData("  Source Code Pro Light  ", "Source Code Pro", "Light")]
    public void AliasesSelectOnlyAnAdvertisedStyle(string alias, string family, string advertised)
    {
        Matcher matcher = new((name, _, style) => name == family && style == advertised ? Mono() : null);
        using SKTypeface? face = new SystemFontNameResolver(matcher).Match(alias, TerminalTypefaceStyle.Regular);
        Assert.NotNull(face);
        Assert.True(face.IsFixedPitch);
        Assert.Equal((family, TerminalTypefaceStyle.Regular, advertised), matcher.Requests[^1]);
    }

    [Fact]
    public void RealFamilyEndingInStyleWordWinsOverAlias()
    {
        Matcher matcher = new((name, _, style) => name == "Example Light" && style is null ? Mono() : null);
        using SKTypeface? face = new SystemFontNameResolver(matcher).Match("Example Light", TerminalTypefaceStyle.Regular);
        Assert.NotNull(face);
        Assert.Single(matcher.Requests);
    }

    [Fact]
    public void LongestMatchingFamilyAndMultiwordStyleWin()
    {
        Matcher matcher = new((name, _, style) =>
            (name == "Example Mono" && style == "Light Italic") || (name == "Example" && style == "Mono Light Italic") ? Mono() : null);
        using SKTypeface? face = new SystemFontNameResolver(matcher).Match("Example Mono Light Italic", TerminalTypefaceStyle.Regular);
        Assert.NotNull(face);
        Assert.DoesNotContain(matcher.Requests, request => request.Family == "Example");
    }

    [Fact]
    public void UnknownSuffixDoesNotDropTheRequestedWeightOrSelectAnUnrelatedFace()
    {
        Matcher matcher = new((name, _, style) => name == "Source Code Pro" && style is null ? Mono() : null);
        Assert.Null(new SystemFontNameResolver(matcher).Match("Source Code Pro MissingStyle", TerminalTypefaceStyle.Regular));
        Assert.DoesNotContain(matcher.Requests, request => request.Family == "Source Code Pro" && request.Name is null);
    }

    [Theory]
    [InlineData(TerminalTypefaceStyle.Bold)]
    [InlineData(TerminalTypefaceStyle.Italic)]
    public void FaceAliasOverridesLogicalCategoryLikeAnExplicitStyleName(TerminalTypefaceStyle style)
    {
        Matcher matcher = new((name, _, advertised) => name == "Example" && advertised == "Light" ? Mono() : null);
        using SKTypeface? face = new SystemFontNameResolver(matcher).Match("Example Light", style);
        Assert.NotNull(face);
    }

    [Fact]
    public void ExplicitStyleOverridesAliasAndMissingExplicitStyleDoesNotSubstitute()
    {
        List<SKTypeface> loaded = [];
        Matcher matcher = new((name, _, style) =>
        {
            if (name != "Example" || style is not ("Light" or "Heavy")) return null;
            SKTypeface face = Mono();
            loaded.Add(face);
            return face;
        });
        SystemFontNameResolver resolver = new(matcher);
        using SKTypeface? actual = resolver.Match("Example Light", TerminalTypefaceStyle.Bold, "Heavy");
        Assert.NotNull(actual);
        Assert.Same(loaded[1], actual);
        Assert.Equal(nint.Zero, loaded[0].Handle);
        Assert.Null(resolver.Match("Example Light", TerminalTypefaceStyle.Bold, "Missing"));
        Assert.Equal(nint.Zero, loaded[^1].Handle);
    }

    [Fact]
    public void AliasFaceIsReleasedWhenAnExplicitStyleLookupThrows()
    {
        SKTypeface? loaded = null;
        Matcher matcher = new((name, _, style) =>
        {
            if (name != "Example") return null;
            if (style == "Light") return loaded = Mono();
            throw new InvalidOperationException("Style lookup failed.");
        });
        Assert.Throws<InvalidOperationException>(() => new SystemFontNameResolver(matcher).Match(
            "Example Light", TerminalTypefaceStyle.Regular, "Heavy"));
        Assert.NotNull(loaded);
        Assert.Equal(nint.Zero, loaded.Handle);
    }

    [Fact]
    public void MissingStyleInARealFamilyDoesNotReinterpretItsNameAsAnAlias()
    {
        Matcher matcher = new((name, style, advertised) =>
            name == "Example Light" && style == TerminalTypefaceStyle.Regular && advertised is null ? Mono() : null);
        Assert.Null(new SystemFontNameResolver(matcher).Match("Example Light", TerminalTypefaceStyle.Bold, "Missing"));
        Assert.DoesNotContain(matcher.Requests, request => request.Family == "Example");
    }

    [Fact]
    public void FallbackPrefersConsolasAndDoesNotEnumerateOtherFonts()
    {
        Matcher matcher = new((name, _, _) => name == "Consolas" ? Mono() : null);
        using SKTypeface fallback = new SystemFontNameResolver(matcher).CreateMonospaceFallback(UnexpectedFamilies());
        Assert.True(fallback.IsFixedPitch);
        Assert.Single(matcher.Requests);
    }

    [Fact]
    public void FallbackRejectsProportionalFacesAndFindsAnInstalledMonospaceFont()
    {
        List<SKTypeface> rejected = [];
        Matcher matcher = new((name, _, _) =>
        {
            if (name == "Other Mono") return Mono();
            if (name is not ("Consolas" or "Other Proportional")) return null;
            SKTypeface face = FontPresentationTestFonts.Load("Proportional.ttf");
            Assert.False(face.IsFixedPitch);
            rejected.Add(face);
            return face;
        });
        using SKTypeface fallback = new SystemFontNameResolver(matcher).CreateMonospaceFallback(["Other Proportional", "Other Mono"]);
        Assert.True(fallback.IsFixedPitch);
        Assert.All(rejected, face => Assert.Equal(nint.Zero, face.Handle));
    }

    [Fact]
    public void FallbackRejectsFixedPitchEmojiFontsWithoutBasicTextCoverage()
    {
        SKTypeface? rejected = null;
        Matcher matcher = new((name, _, _) => name switch
        {
            "Consolas" => rejected = FontPresentationTestFonts.Load("NotoEmoji-Regular.ttf"),
            "Menlo" => Mono(),
            _ => null,
        });
        using SKTypeface fallback = new SystemFontNameResolver(matcher).CreateMonospaceFallback([]);
        Assert.NotNull(rejected);
        Assert.Equal(nint.Zero, rejected.Handle);
        using SKFont font = new(fallback);
        Assert.True(font.ContainsGlyph('M'));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FallbackRejectsLastResortEvenWithMonospacedAsciiCoverage(bool preferred)
    {
        SKTypeface rejected = FontPresentationTestFonts.Load("LastResortMono.ttf");
        Assert.True(rejected.IsFixedPitch);
        Assert.True(TerminalFontCoverage.IsLastResort(rejected));
        using (SKFont font = new(rejected))
            foreach (char character in " 0MW") Assert.True(font.ContainsGlyph(character));
        string rejectedFamily = preferred ? "Consolas" : "Placeholder Mono";
        Matcher matcher = new((name, _, _) => name == rejectedFamily ? rejected : name == "Other Mono" ? Mono() : null);
        using SKTypeface fallback = new SystemFontNameResolver(matcher).CreateMonospaceFallback(preferred ? ["Other Mono"] : [rejectedFamily, "Other Mono"]);
        Assert.Equal(nint.Zero, rejected.Handle);
        Assert.False(TerminalFontCoverage.IsLastResort(fallback));
        Assert.Equal("Other Mono", matcher.Requests[^1].Family);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Proportional.ttf")]
    [InlineData("LastResortMono.ttf")]
    public void NoUsableSystemMonospaceFontUsesAnOwnedBundledFace(string? unusableFont)
    {
        SKTypeface? rejected = null;
        Matcher matcher = new((name, _, _) => name == "Unusable" && unusableFont is not null
            ? rejected = FontPresentationTestFonts.Load(unusableFont) : null);
        SystemFontNameResolver resolver = new(matcher);
        using SKTypeface fallback = resolver.CreateMonospaceFallback(["Unusable"]);
        using SKTypeface second = resolver.CreateMonospaceFallback([]);
        using SKTypeface expected = Mono();
        Assert.Equal(expected.FamilyName, fallback.FamilyName);
        Assert.Equal((expected.FontWeight, expected.FontWidth, expected.FontSlant),
            (fallback.FontWeight, fallback.FontWidth, fallback.FontSlant));
        Assert.True(fallback.IsFixedPitch);
        Assert.False(TerminalFontCoverage.IsLastResort(fallback));
        if (rejected is not null) Assert.Equal(nint.Zero, rejected.Handle);
        Assert.NotSame(fallback, second);
        second.Dispose();
        Assert.Equal(nint.Zero, second.Handle);
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(fallback, TerminalTypefaceStyle.Regular));
        using SkiaTerminalRenderer renderer = SkiaTerminalRenderer.CreateWithTypefaces(collection, fontSize: 16);
        Assert.True(renderer.CellWidth > 0);
        Assert.True(renderer.CellHeight > 0);
        foreach (char character in " 0MW") Assert.True(renderer.GlyphCoverageSource.HasSystemGlyph(character));
        TerminalScreen screen = new(1, 1);
        screen.GetRow(0)[0].Codepoint = 'M';
        renderer.CursorVisible = false;
        using SKBitmap bitmap = new((int)renderer.CellWidth, (int)renderer.CellHeight);
        using SKCanvas canvas = new(bitmap);
        renderer.RenderFull(canvas, screen);
        Assert.Contains(bitmap.Pixels, pixel => pixel != bitmap.GetPixel(0, bitmap.Height - 1));
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConstructedControlAcceptsAHostFontFileInEitherPropertyOrder(bool sourceFirst)
    {
        TerminalControl control = new();
        try
        {
            Assert.NotNull(control.Renderer);
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "JetBrainsMono-Regular.ttf");
            if (sourceFirst) control.FontSource = TerminalFontSource.File;
            control.FontFilePath = path;
            if (!sourceFirst) control.FontSource = TerminalFontSource.File;
            using SkiaTerminalRenderer expected = new("unused", control.Renderer!.FontSize, TerminalFontSource.File, path);
            Assert.Equal((expected.CellWidth, expected.CellHeight), (control.Renderer.CellWidth, control.Renderer.CellHeight));
            Assert.True(control.Renderer.GlyphCoverageSource.HasSystemGlyph('M'));
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
            control.Renderer?.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ConstructedControlAcceptsACallerOwnedTypefaceCollection()
    {
        using SKTypeface face = Mono();
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular));
        TerminalControl control = new();
        try
        {
            control.TypefaceCollection = collection;
            using SkiaTerminalRenderer expected = SkiaTerminalRenderer.CreateWithTypefaces(collection, fontSize: control.Renderer!.FontSize);
            Assert.Equal((expected.CellWidth, expected.CellHeight), (control.Renderer!.CellWidth, control.Renderer.CellHeight));
            Assert.True(control.Renderer.GlyphCoverageSource.HasSystemGlyph('M'));
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
            control.Renderer?.Dispose();
        }
        Assert.NotEqual(nint.Zero, face.Handle);
    }

    [Theory]
    [InlineData("Source Code Pro", "Light", 300)]
    [InlineData("Cascadia Code", "Light", 300)]
    [InlineData("Cascadia Mono", "SemiLight", 350)]
    public void NativeMatcherAndPublicCacheResolveInstalledStyleAliases(string family, string style, int weight)
    {
        using SKFontManager manager = SKFontManager.CreateDefault();
        using SKFontStyleSet styles = manager.GetFontStyles(family);
        if (styles.Count == 0) return; // Deterministic alias coverage above does not require installed fonts.
        SkiaConfiguredFontFamilyMatcher matcher = new(manager);
        using SKTypeface? expected = matcher.Match(family, TerminalTypefaceStyle.Regular, style);
        Assert.NotNull(expected);
        using GlyphCache cache = new(family + " " + style);
        Assert.Equal(expected.FamilyName, cache.RegularTypeface.FamilyName);
        Assert.Equal(weight, cache.RegularTypeface.FontWeight);
        Assert.True(cache.RegularTypeface.IsFixedPitch);
        using GlyphCache configured = GlyphCache.CreateWithFontFamilies(new() { Regular = [family + " " + style] });
        Assert.Equal(weight, configured.RegularTypeface.FontWeight);
        Assert.Equal(cache.MeasureCellSize(16), configured.MeasureCellSize(16));
    }

    [Theory]
    [InlineData("Cascadia Code", false)]
    [InlineData("Cascadia Code", true)]
    [InlineData("Source Code Pro", false)]
    [InlineData("Source Code Pro", true)]
    public void RenderedAliasMatchesAnExplicitLightStyle(string family, bool shaping)
    {
        using SKFontManager manager = SKFontManager.CreateDefault();
        using SKFontStyleSet styles = manager.GetFontStyles(family);
        if (styles.Count == 0) return;
        using SkiaTerminalRenderer alias = new(family + " Light", 16) { CursorVisible = false, EnableTextShaping = shaping };
        using SkiaTerminalRenderer explicitStyle = SkiaTerminalRenderer.CreateWithFontFamilies(
            new() { Regular = [family], RegularStyle = "Light" }, fontSize: 16);
        explicitStyle.CursorVisible = false;
        explicitStyle.EnableTextShaping = shaping;
        TerminalScreen screen = new(10, 1);
        const string text = "MiW.012345";
        for (int i = 0; i < text.Length; i++)
        {
            TerminalCell cell = TerminalCell.Empty();
            cell.Codepoint = text[i];
            screen.GetRow(0)[i] = cell;
        }
        Assert.Equal((explicitStyle.CellWidth, explicitStyle.CellHeight), (alias.CellWidth, alias.CellHeight));
        using SKBitmap actual = new((int)alias.CellWidth * 10, (int)alias.CellHeight);
        using SKBitmap expected = new(actual.Width, actual.Height);
        using SKCanvas actualCanvas = new(actual);
        using SKCanvas expectedCanvas = new(expected);
        alias.RenderFull(actualCanvas, screen);
        explicitStyle.RenderFull(expectedCanvas, screen);
        Assert.Equal(expected.Pixels, actual.Pixels);
        Assert.Contains(actual.Pixels, pixel => pixel != actual.GetPixel(0, actual.Height - 1));
    }

    [Fact]
    public void PublicFallbackKeepsStylesCoverageAndRendererOnTheSelectedMonospaceFamily()
    {
        const string missing = "RoyalTerminal-Missing-Font-2416-0F128A";
        using GlyphCache cache = new(missing);
        Assert.True(cache.RegularTypeface.IsFixedPitch);
        foreach ((bool bold, bool italic) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            SKTypeface face = cache.GetTypeface(bold, italic);
            Assert.Equal(cache.RegularTypeface.FamilyName, face.FamilyName);
            Assert.True(face.IsFixedPitch);
        }
        using GlyphCache fileFallback = new(missing, TerminalFontSource.File, "missing-2416.ttf");
        using GlyphCache configured = GlyphCache.CreateWithFontFamilies(new() { Regular = [missing] }, missing);
        Assert.Equal(cache.RegularTypeface.FamilyName, fileFallback.RegularTypeface.FamilyName);
        Assert.Equal(cache.RegularTypeface.FamilyName, configured.RegularTypeface.FamilyName);
        using SkiaTerminalRenderer renderer = new(missing, 16);
        Assert.Equal(cache.MeasureCellSize(16), (renderer.CellWidth, renderer.CellHeight));
        Assert.True(renderer.GlyphCoverageSource.HasSystemGlyph('A'));
        Assert.True(renderer.GlyphCoverageSource.HasSystemGlyph('W'));
        if (OperatingSystem.IsWindows()) Assert.Equal("Consolas", cache.RegularTypeface.FamilyName);
    }

    private static SKTypeface Mono() => FontPresentationTestFonts.Load("JetBrainsMono-Regular.ttf");

    private static IEnumerable<string> UnexpectedFamilies()
    {
        throw new InvalidOperationException("Font enumeration should remain lazy when Consolas is available.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private sealed class Matcher(Func<string, TerminalTypefaceStyle, string?, SKTypeface?> match) : IConfiguredFontFamilyMatcher
    {
        internal List<(string Family, TerminalTypefaceStyle Style, string? Name)> Requests { get; } = [];
        public SKTypeface? Match(string family, TerminalTypefaceStyle style, string? styleName = null)
        {
            Requests.Add((family, style, styleName));
            return match(family, style, styleName);
        }
    }
}
