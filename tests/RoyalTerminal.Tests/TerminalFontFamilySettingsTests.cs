// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalFontFamilySettingsTests
{
    [Fact]
    public void NormalizePreservesPriorityDuplicatesAndCommaInFamilyNames()
    {
        TerminalFontFamilySettings settings = new()
        {
            Regular = ["  First  ", null!, " ", "Family, With Comma", "First"],
            Bold = default, Italic = ["Italic"], BoldItalic = ["  Bold Italic "]
        };
        TerminalFontFamilySettings normalized = settings.Normalize();
        Assert.Equal(new[] { "First", "Family, With Comma", "First" }, normalized.Regular);
        Assert.False(normalized.Bold.IsDefault);
        Assert.Empty(normalized.Bold);
        Assert.Equal(new[] { "Italic" }, normalized.Italic);
        Assert.Equal(new[] { "Bold Italic" }, normalized.BoldItalic);
        Assert.Same(normalized, normalized.Normalize());
        Assert.False(normalized.IsEmpty);
        Assert.True(TerminalFontFamilySettings.Default.IsEmpty);
        Assert.Equal("  First  ", settings.Regular[0]);
    }

    [Fact]
    public async Task ProfileSourceGeneratedSerializationRoundTripsEveryStyleAndLegacyFile()
    {
        TerminalSessionProfilesDocument document = new()
        {
            Profiles = [new() { Id = "fonts", DisplayName = "Fonts", Appearance = new()
            {
                FontSource = TerminalFontSource.File, FontFilePath = "/fonts/primary.ttf",
                FontFamilies = new() { Regular = [" One ", "Two"], Bold = ["Bold"], Italic = ["Italic"], BoldItalic = ["Both"] }
            }}]
        };
        string json = TerminalSessionProfileSerializer.ToJson(document);
        Assert.Contains("\"fontSource\": \"File\"", json);
        Assert.DoesNotContain("\"isEmpty\"", json);
        TerminalSessionAppearanceSettings restored = Assert.Single(TerminalSessionProfileSerializer.FromJson(json).Profiles).Appearance;
        Assert.Equal(new[] { "One", "Two" }, restored.FontFamilies.Regular);
        Assert.Equal(new[] { "Bold" }, restored.FontFamilies.Bold);
        Assert.Equal(new[] { "Italic" }, restored.FontFamilies.Italic);
        Assert.Equal(new[] { "Both" }, restored.FontFamilies.BoldItalic);
        Assert.Equal("/fonts/primary.ttf", restored.FontFilePath);
        using MemoryStream stream = new();
        await TerminalSessionProfileSerializer.SaveAsync(document, stream);
        stream.Position = 0;
        TerminalSessionProfilesDocument streamed = await TerminalSessionProfileSerializer.LoadAsync(stream);
        Assert.Equal(json, TerminalSessionProfileSerializer.ToJson(streamed));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"fontFamilies\":null}")]
    [InlineData("{\"fontFamilies\":{\"regular\":[],\"bold\":[]}}")]
    public void LegacyAndEmptyProfileFamilyConfigurationRetainDefaults(string appearance)
    {
        string json = "{\"profiles\":[{\"id\":\"legacy\",\"appearance\":" + appearance + "}]}";
        TerminalSessionProfile profile = Assert.Single(TerminalSessionProfileSerializer.FromJson(json).Profiles);
        Assert.True(profile.Appearance.FontFamilies.IsEmpty);
        Assert.Equal(TerminalFontSource.System, profile.Appearance.FontSource);
        Assert.Equal(new TerminalSessionLayoutSettings(), profile.Layout);
        Assert.Equal(new TerminalSessionBehaviorSettings(), profile.Behavior);
        Assert.Equal(new TerminalSessionLoggingSettings(), profile.Logging);
        Assert.Equal(new TerminalFontRenderingSettings(), profile.Appearance.FontRendering);
        Assert.Equal(new TerminalSessionAppearanceSettings().FontSize, profile.Appearance.FontSize);
    }

    [Fact]
    public void EditorPreservesOrderingWithoutSplittingFamilyNamesAndNotifiesChanges()
    {
        TerminalFontFamilyEditorViewModel editor = new();
        List<string?> changes = [];
        editor.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        editor.Regular = "First\r\n\n Family, With Comma \rLast";
        editor.Bold = "Bold"; editor.Italic = "Italic"; editor.BoldItalic = "Both";
        TerminalFontFamilySettings settings = editor.BuildSettings();
        Assert.Equal(new[] { "First", "Family, With Comma", "Last" }, settings.Regular);
        Assert.Equal(new[] { "Regular", "Bold", "Italic", "BoldItalic" }, changes);
        editor.Load(settings);
        Assert.Equal(settings.Regular.ToArray(), editor.BuildSettings().Regular.ToArray());
        editor.Load(null);
        Assert.True(editor.BuildSettings().IsEmpty);
        editor.Regular = null!;
        Assert.Equal(string.Empty, editor.Regular);
    }

    [Fact]
    public void LoaderSkipsMissingFamiliesRetainsStyleOrderAndLoadsRegularVariants()
    {
        List<SKTypeface> loaded = [];
        Matcher matcher = new((name, style) =>
        {
            if (name == "missing") return null;
            string file = name == "emoji" ? "NotoEmoji-Regular.ttf" : "JetBrainsMono-Regular.ttf";
            SKTypeface face = FontPresentationTestFonts.Load(file);
            loaded.Add(face);
            return face;
        });
        using ConfiguredFontFamilies families = ConfiguredFontFamilies.Load(new()
        {
            Regular = ["missing", "text", "emoji"], Bold = ["missing", "emoji", "text"]
        }, matcher, () => throw new InvalidOperationException("Regular fallback must stay lazy."));
        Assert.Same(loaded[0], families.Collection.GetPrimaryTypeface());
        Assert.Same(loaded[2], families.Collection.GetPrimaryTypeface(TerminalTypefaceStyle.Bold));
        Assert.Contains((loaded[0].FamilyName, TerminalTypefaceStyle.Italic), matcher.Requests);
        using TerminalFontResolver resolver = new(new NoDiscovery());
        Assert.Same(loaded[1], resolver.ResolveTypeface(families.Collection, TerminalTypefaceStyle.Regular, 0x1F600).Typeface);
        Assert.Same(loaded[3], resolver.ResolveTypeface(families.Collection, TerminalTypefaceStyle.Bold, 'A').Typeface);
        resolver.Dispose();
        families.Dispose();
        Assert.All(loaded, face => Assert.Equal(nint.Zero, face.Handle));
    }

    [Fact]
    public void LoaderUsesLegacyFileWhenRegularNamesAreUnavailableAndStylesFallBackToIt()
    {
        Matcher matcher = new((_, _) => null);
        int defaults = 0;
        using ConfiguredFontFamilies families = ConfiguredFontFamilies.Load(new()
        { Regular = ["missing"], Bold = ["missing-bold"] }, matcher, () =>
        {
            defaults++;
            return new GlyphCache("unused", TerminalFontSource.File, FontPath);
        });
        Assert.Equal(1, defaults);
        Assert.Same(families.Collection.GetPrimaryTypeface(), families.Collection.GetPrimaryTypeface(TerminalTypefaceStyle.Bold));
        Assert.Equal(new[] { ("missing", TerminalTypefaceStyle.Regular), ("missing-bold", TerminalTypefaceStyle.Bold) }, matcher.Requests);
        SKTypeface regular = families.Collection.GetPrimaryTypeface();
        families.Dispose();
        families.Dispose();
        Assert.Equal(nint.Zero, regular.Handle);
    }

    [Fact]
    public void LoaderReleasesAllPartiallyLoadedFacesOnFailureAndDeduplicatesOwnership()
    {
        SKTypeface face = FontPresentationTestFonts.Load("JetBrainsMono-Regular.ttf");
        Matcher matcher = new((name, _) => name == "failure" ? throw new InvalidOperationException("fixture") : face);
        Assert.Throws<InvalidOperationException>(() => ConfiguredFontFamilies.Load(new() { Regular = ["first", "first", "failure"] },
            matcher, () => throw new InvalidOperationException("unused")));
        Assert.Equal(nint.Zero, face.Handle);
    }

    [Fact]
    public void NativeFamilyMatcherDoesNotSubstituteAnUnknownFamily()
    {
        using SKFontManager manager = SKFontManager.CreateDefault();
        SkiaConfiguredFontFamilyMatcher matcher = new(manager);
        Assert.Null(matcher.Match("RoyalTerminal-Missing-Family-ED2C8D26", TerminalTypefaceStyle.Regular));
        using SKTypeface primary = SKTypeface.FromFamilyName("monospace");
        using SKTypeface? matched = matcher.Match(primary.FamilyName, TerminalTypefaceStyle.Bold);
        Assert.NotNull(matched);
        Assert.Equal(primary.FamilyName, matched.FamilyName);
    }

    [Fact]
    public void PublicFactoriesOwnConfiguredResourcesAndRetainLegacyFileFallback()
    {
        TerminalFontFamilySettings settings = new() { Regular = ["RoyalTerminal-Missing-Family-ED2C8D26"] };
        using GlyphCache cache = GlyphCache.CreateWithFontFamilies(settings, fontSource: TerminalFontSource.File, fontFilePath: FontPath);
        using SkiaTerminalGlyphCoverageSource coverage = SkiaTerminalGlyphCoverageSource.CreateWithFontFamilies(settings,
            fontSource: TerminalFontSource.File, fontFilePath: FontPath);
        using SkiaTerminalRenderer renderer = SkiaTerminalRenderer.CreateWithFontFamilies(settings,
            fontSource: TerminalFontSource.File, fontFilePath: FontPath);
        Assert.True(coverage.HasSystemGlyph('A'));
        Assert.True(renderer.GlyphCoverageSource.HasSystemGlyph('A'));
        Assert.True(cache.MeasureCellSize(14).Width > 0);
        SKTypeface face = cache.RegularTypeface;
        cache.Dispose();
        Assert.Equal(nint.Zero, face.Handle);
        renderer.Dispose();
        Assert.False(renderer.GlyphCoverageSource.HasSystemGlyph('A'));
        Assert.Throws<ArgumentNullException>(() => GlyphCache.CreateWithFontFamilies(null!));
        Assert.Throws<ArgumentNullException>(() => SkiaTerminalRenderer.CreateWithFontFamilies(null!));
        Assert.Throws<ArgumentNullException>(() => SkiaTerminalGlyphCoverageSource.CreateWithFontFamilies(null!));
    }

    [AvaloniaFact]
    public void SettingsEditorParticipatesInDirtyTrackingProfileSwitchAndRuntimeSuppression()
    {
        TerminalSettingsPanelState state = new();
        state.MarkSaved();
        state.FontFamiliesEditor.Regular = "First\nSecond";
        state.FontFamiliesEditor.BoldItalic = "Both";
        Assert.True(state.IsDirty);
        TerminalSessionProfile saved = Assert.Single(state.BuildDocument().Profiles);
        Assert.Equal(new[] { "First", "Second" }, saved.Appearance.FontFamilies.Regular);
        Assert.Equal(new[] { "Both" }, saved.Appearance.FontFamilies.BoldItalic);
        state.MarkSaved();
        state.UpdateFromRuntime(s => s.FontFamiliesEditor.Load(new() { Italic = ["Italic"] }));
        Assert.False(state.IsDirty);
        Assert.Empty(state.FontFamiliesEditor.Regular);
        state.LoadDocument(new() { Profiles = [new() { Id = "other" }] });
        Assert.True(state.FontFamiliesEditor.BuildSettings().IsEmpty);
    }

    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed)]
    [InlineData(VtProcessorPreference.Native)]
    public void ControlFamiliesRebuildRendererAndCoverageForBothEngines(VtProcessorPreference preference)
    {
        if (preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalControl control = new(new TerminalSessionService(), new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(), new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory([new GhosttyVtProcessorProvider()]), new DefaultPtyFactory())
        { VtProcessorPreference = preference, FontSource = TerminalFontSource.File, FontFilePath = FontPath };
        control.WriteOutput("ready"u8);
        if (preference == VtProcessorPreference.Native) Assert.IsType<GhosttyVtProcessor>(control.ActiveVtProcessor);
        using SkiaTerminalRenderer original = control.Renderer!;
        control.FontFamilies = new() { Regular = ["RoyalTerminal-Missing-Family-ED2C8D26"], Bold = ["missing-bold"] };
        using SkiaTerminalRenderer configured = control.Renderer!;
        Assert.NotSame(original, configured);
        ITerminalGlyphCoverageSink sink = Assert.IsAssignableFrom<ITerminalGlyphCoverageSink>(control.ActiveVtProcessor);
        Assert.Same(configured.GlyphCoverageSource, sink.GlyphCoverageSource);
        Assert.True(configured.GlyphCoverageSource.HasSystemGlyph('A'));
        control.FontFamilies = TerminalFontFamilySettings.Default;
        using SkiaTerminalRenderer restored = control.Renderer!;
        Assert.NotSame(configured, restored);
        Assert.Same(restored.GlyphCoverageSource, sink.GlyphCoverageSource);
    }

    [AvaloniaFact]
    public async Task CompiledSettingsBindingsEditOrderedFamilyLists()
    {
        TerminalSettingsPanelState state = new();
        TerminalSettingsPanel panel = new() { DataContext = state };
        Window window = new() { Width = 760, Height = 740, Content = panel };
        try
        {
            window.Show();
            TabControl tabs = Assert.Single(panel.GetVisualDescendants().OfType<TabControl>());
            for (int index = 0; index < tabs.ItemCount; index++)
            {
                tabs.SelectedIndex = index;
                await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
                Expander? expander = panel.GetVisualDescendants().OfType<Expander>().FirstOrDefault(e => Equals(e.Header, "Ordered font families"));
                if (expander is null) continue;
                expander.IsExpanded = true;
                await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
                TextBox input = Assert.Single(expander.GetVisualDescendants().OfType<TextBox>(), e => e.Name == "RegularFontFamiliesEditor");
                input.Text = "First\nSecond";
                await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
                Assert.Equal(new[] { "First", "Second" }, state.FontFamiliesEditor.BuildSettings().Regular);
                return;
            }
            Assert.Fail("Ordered font-family editor was not available in settings.");
        }
        finally { window.Close(); }
    }

    private static string FontPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "JetBrainsMono-Regular.ttf");
    private sealed class Matcher(Func<string, TerminalTypefaceStyle, SKTypeface?> match) : IConfiguredFontFamilyMatcher
    {
        internal List<(string, TerminalTypefaceStyle)> Requests { get; } = [];
        public SKTypeface? Match(string family, TerminalTypefaceStyle style) { Requests.Add((family, style)); return match(family, style); }
    }
    private sealed class NoDiscovery : ITerminalFontMatcher
    {
        public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languages, int codepoint) => null;
    }
}
