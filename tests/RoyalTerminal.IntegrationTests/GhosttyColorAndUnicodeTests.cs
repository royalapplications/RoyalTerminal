// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public class GhosttyColorAndUnicodeTests
{
    [GhosttyNativeFact]
    public void ColorUtilities_ParseGenerateAndMeasureUsingGhostty()
    {
        Assert.True(GhosttyColorUtilities.TryParse("#112233", out GhosttyVtNative.GhosttyColorRgb parsed));
        Assert.Equal((byte)0x11, parsed.R);
        Assert.Equal((byte)0x22, parsed.G);
        Assert.Equal((byte)0x33, parsed.B);

        Assert.True(GhosttyColorUtilities.TryParseX11("red", out GhosttyVtNative.GhosttyColorRgb red));
        Assert.Equal(new GhosttyVtNative.GhosttyColorRgb { R = 255 }, red);

        Assert.True(
            GhosttyColorUtilities.TryParsePaletteEntry(
                "42=#102030",
                out byte paletteIndex,
                out GhosttyVtNative.GhosttyColorRgb paletteColor));
        Assert.Equal((byte)42, paletteIndex);
        Assert.Equal(
            new GhosttyVtNative.GhosttyColorRgb { R = 0x10, G = 0x20, B = 0x30 },
            paletteColor);

        GhosttyVtNative.GhosttyColorRgb[] palette = GhosttyColorUtilities.CreateDefaultPalette();
        Assert.Equal(256, palette.Length);
        Assert.Equal(red, palette[196]);

        GhosttyVtNative.GhosttyColorRgb black = default;
        GhosttyVtNative.GhosttyColorRgb white = new() { R = 255, G = 255, B = 255 };
        Assert.Equal(0, GhosttyColorUtilities.GetLuminance(black), precision: 12);
        Assert.Equal(1, GhosttyColorUtilities.GetLuminance(white), precision: 12);
        Assert.Equal(21, GhosttyColorUtilities.GetContrast(black, white), precision: 12);

        GhosttyX11Color[] x11Colors = GhosttyColorUtilities.GetX11Colors();
        Assert.Contains(x11Colors, entry => entry.Name == "red" && entry.Color.Equals(red));
    }

    [GhosttyNativeFact]
    public void ColorSchemeReport_UsesGhosttyMode2031Encoding()
    {
        Assert.Equal(
            "\u001b[?997;1n",
            Encoding.ASCII.GetString(
                GhosttyColorUtilities.EncodeColorSchemeReport(GhosttyColorScheme.Dark)));
        Assert.Equal(
            "\u001b[?997;2n",
            Encoding.ASCII.GetString(
                GhosttyColorUtilities.EncodeColorSchemeReport(GhosttyColorScheme.Light)));
    }

    [GhosttyNativeFact]
    public void UnicodeWidths_MatchGhosttyCodepointAndGraphemeRules()
    {
        Assert.Equal((byte)1, GhosttyUnicode.GetCodepointWidth((uint)'A'));
        Assert.Equal((byte)0, GhosttyUnicode.GetCodepointWidth(0x0301));
        Assert.Equal((byte)2, GhosttyUnicode.GetCodepointWidth(0x4E00));
        Assert.Equal((byte)2, GhosttyUnicode.GetCodepointWidth(0x1F600));

        uint[] family = [0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467, (uint)'A'];
        nuint consumed = GhosttyUnicode.GetGraphemeWidth(family, out byte width);
        Assert.Equal((nuint)5, consumed);
        Assert.Equal((byte)2, width);

        uint[] separateCharacters = [(uint)'A', (uint)'B'];
        consumed = GhosttyUnicode.GetGraphemeWidth(separateCharacters, out width);
        Assert.Equal((nuint)1, consumed);
        Assert.Equal((byte)1, width);
    }
}
