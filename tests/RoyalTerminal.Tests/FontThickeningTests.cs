// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class FontThickeningTests
{
    // Ghostty CoreText uses smoothing plus a 0..255 gray drawing strength, for
    // terminal and preedit alike. WT's DirectWrite font weight and xterm.js's
    // canvas fontWeight select faces instead; neither is this macOS operation.
    [Fact]
    public void DefaultsAndZeroStrengthRemainDistinctFromSyntheticBold()
    {
        Assert.False(TerminalFontRenderingSettings.Default.Thicken);
        Assert.Equal(255, TerminalFontRenderingSettings.Default.ThickenStrength);
        TerminalFontRenderingSettings settings = new() { Thicken = true, ThickenStrength = 0 };
        Assert.Equal(settings, settings.Normalize());
        Assert.False(settings.Embolden);
        using GlyphCache cache = new("monospace", TerminalFontSource.System, null, fontRenderingSettings: settings);
        using SKFont font = cache.CreateFont(16);
        Assert.False(font.Embolden);
    }

    [AvaloniaFact]
    public async Task ControlsAndProfilesRetainThickeningSettings()
    {
        TerminalSettingsPanelState state = new();
        state.MarkSaved();
        state.Appearance.FontThicken = true;
        state.Appearance.FontThickenStrength = 73;
        Assert.True(state.IsDirty);
        TerminalSessionProfilesDocument document = state.BuildDocument();
        using MemoryStream stream = new();
        await TerminalSessionProfileSerializer.SaveAsync(document, stream);
        stream.Position = 0;
        TerminalSessionProfilesDocument restored = await TerminalSessionProfileSerializer.LoadAsync(stream);
        TerminalFontRenderingSettings settings = Assert.Single(restored.Profiles).Appearance.FontRendering;
        Assert.True(settings.Thicken);
        Assert.Equal(73, settings.ThickenStrength);
        TerminalControl control = new() { FontThicken = settings.Thicken, FontThickenStrength = settings.ThickenStrength };
        Assert.True(control.Renderer!.FontRenderingSettings.Thicken);
        Assert.Equal(73, control.Renderer.FontRenderingSettings.ThickenStrength);
        control.FontThickenStrength = 0;
        Assert.Equal(0, control.Renderer.FontRenderingSettings.ThickenStrength);
        control.FontThicken = false;
        Assert.False(control.Renderer.FontRenderingSettings.Thicken);
    }

    [Fact]
    public void RasterizerIsDisabledUnlessPlatformAndSettingSupportSmoothing()
    {
        using MacFontThickeningCache cache = new();
        using SKTypeface face = SKTypeface.FromFamilyName("monospace");
        using SKBitmap bitmap = new(64, 64);
        using SKCanvas canvas = new(bitmap);
        using SKPaint paint = new() { Color = SKColors.White };
        Assert.False(cache.TryDraw(canvas, face, 24, new(), [face.GetGlyph('A')], [new(0, 0)], 4, 40, paint));
        if (!OperatingSystem.IsMacOS())
            Assert.False(cache.TryDraw(canvas, face, 24, new() { Thicken = true }, [face.GetGlyph('A')], [new(0, 0)], 4, 40, paint));
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        cache.Dispose();
        Assert.False(cache.TryDraw(canvas, face, 24, new() { Thicken = true }, [face.GetGlyph('A')], [new(0, 0)], 4, 40, paint));
    }

    [Fact]
    public void NativeSmoothingStrengthChangesCoverageAndKeepsMasksBounded()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using MacFontThickeningCache cache = new(2);
        using SKTypeface face = SKTypeface.FromFamilyName("Menlo");
        using SKBitmap bitmap = new(96, 64);
        using SKCanvas canvas = new(bitmap);
        using SKPaint paint = new() { Color = SKColors.White };
        TerminalFontRenderingSettings settings = new() { Thicken = true, ThickenStrength = 0 };
        SKColor[] light = Draw('A');
        Assert.Contains(light, pixel => pixel.Alpha != 0);
        settings = settings with { ThickenStrength = 255 };
        SKColor[] strong = Draw('A');
        Assert.Contains(strong, pixel => pixel.Alpha != 0);
        Assert.False(light.AsSpan().SequenceEqual(strong));
        Assert.Equal(1, cache.Count);
        Assert.Equal(strong, Draw('A'));
        foreach (char glyph in "BCDEFG") Draw(glyph);
        Assert.InRange(cache.Count, 1, 2);
        Assert.InRange(cache.Bytes, 1, 16 * 1024 * 1024);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);

        SKColor[] Draw(char character)
        {
            canvas.Clear(SKColors.Transparent);
            Assert.True(cache.TryDraw(canvas, face, 24, settings, [face.GetGlyph(character)], [new(0, 0)], 20, 40, paint));
            return bitmap.Pixels;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RendererAppliesSmoothingToNormalTextAndPreeditWithoutChangingMetrics(bool shaping, bool preedit)
    {
        if (!OperatingSystem.IsMacOS()) return;
        TerminalScreen screen = new(20, 2);
        using BasicVtProcessor processor = new(screen);
        if (!preedit) processor.Process(Encoding.UTF8.GetBytes("ABC -> e\u0301 fi"));
        using SkiaTerminalRenderer renderer = new("Menlo", 20)
        {
            EnableTextShaping = shaping,
            CursorVisible = false,
            CursorColumn = 0,
            CursorRow = 0,
            Preedit = preedit ? new("ABC e\u0301", 2) : null,
        };
        (float width, float height) = (renderer.CellWidth, renderer.CellHeight);
        using SKBitmap bitmap = new((int)Math.Ceiling(width * 20), (int)Math.Ceiling(height * 2));
        using SKCanvas canvas = new(bitmap);
        renderer.RenderFull(canvas, screen);
        SKColor[] ordinary = bitmap.Pixels;
        byte[] terminalBefore = processor.GetBinarySnapshot();
        renderer.FontRenderingSettings = new() { Thicken = true, ThickenStrength = 255 };
        Assert.Equal(width, renderer.CellWidth);
        Assert.Equal(height, renderer.CellHeight);
        renderer.RenderFull(canvas, screen);
        Assert.True(renderer.ThickenedGlyphCount > 0);
        Assert.False(ordinary.AsSpan().SequenceEqual(bitmap.Pixels));
        Assert.Equal(terminalBefore, processor.GetBinarySnapshot());
        renderer.FontRenderingSettings = renderer.FontRenderingSettings with { ThickenStrength = 0 };
        Assert.Equal(0, renderer.ThickenedGlyphCount);
        renderer.RenderFull(canvas, screen);
        Assert.True(renderer.ThickenedGlyphCount > 0);
        renderer.FontRenderingSettings = new();
        Assert.Equal(0, renderer.ThickenedGlyphCount);
    }

    [Fact]
    public void ColorBitmapFontsUseTheExistingColorRenderer()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using MacFontThickeningCache cache = new();
        using SKTypeface face = SKTypeface.FromFamilyName("Apple Color Emoji");
        Assert.True(face.GetTableSize(0x73626978) > 0);
        using SKBitmap bitmap = new(64, 64);
        using SKCanvas canvas = new(bitmap);
        using SKPaint paint = new() { Color = SKColors.White };
        Assert.False(cache.TryDraw(canvas, face, 24, new() { Thicken = true }, [face.GetGlyph(0x1F600)], [new(0, 0)], 0, 30, paint));
        Assert.Equal(0, cache.Count);
    }
}
