// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Neutral terminal theme model tests.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalThemeTests
{
    [Fact]
    public void TerminalThemeParser_Parse_HandlesNeutralAndCompatibilityPaletteSyntax()
    {
        string text =
            "foreground = #112233\n" +
            "background = #223344\n" +
            "cursor-color = #334455\n" +
            "palette-generation-mode = derived-from-base16-lab\n" +
            "osc-color-report-format = 8-bit\n" +
            "ansi1 = #AA0000\n" +
            "palette[42] = #445566\n" +
            "palette = 43=#778899;44=#99AABB\n";

        TerminalTheme parsed = TerminalThemeParser.Parse(text, TerminalTheme.Dark);

        Assert.Equal(0xFF112233u, parsed.DefaultForeground);
        Assert.Equal(0xFF223344u, parsed.DefaultBackground);
        Assert.Equal(0xFF334455u, parsed.CursorColor);
        Assert.Equal(TerminalPaletteGenerationMode.DerivedFromBase16Lab, parsed.PaletteGenerationMode);
        Assert.Equal(TerminalOscColorReportFormat.Bit8, parsed.OscColorReportFormat);
        Assert.Equal(0xFFAA0000u, parsed.Palette[1]);
        Assert.Equal(0xFF445566u, parsed.Palette[42]);
        Assert.Equal(0xFF778899u, parsed.Palette[43]);
        Assert.Equal(0xFF99AABBu, parsed.Palette[44]);
    }

    [Fact]
    public void TerminalThemeSerializer_ToText_RoundTripsWithOverrides()
    {
        TerminalTheme source = TerminalTheme.Dark
            .WithDefaultForeground(0xFF102030u)
            .WithDefaultBackground(0xFF405060u)
            .WithCursorColor(0xFF708090u)
            .WithOscColorReportFormat(TerminalOscColorReportFormat.Bit8)
            .WithPaletteColor(7, 0xFFAABBCCu, explicitOverride: true)
            .WithPaletteColor(142, 0xFF665544u, explicitOverride: true);

        string text = TerminalThemeSerializer.ToText(source, includeAllPaletteEntries: false);
        TerminalTheme parsed = TerminalThemeParser.Parse(text, TerminalTheme.Dark);

        Assert.Equal(0xFF102030u, parsed.DefaultForeground);
        Assert.Equal(0xFF405060u, parsed.DefaultBackground);
        Assert.Equal(0xFF708090u, parsed.CursorColor);
        Assert.Equal(TerminalOscColorReportFormat.Bit8, parsed.OscColorReportFormat);
        Assert.Equal(0xFFAABBCCu, parsed.Palette[7]);
        Assert.Equal(0xFF665544u, parsed.Palette[142]);
    }

    [Fact]
    public void TerminalPalette_GenerationModes_ProduceDifferentExtendedPalette()
    {
        uint[] base16 =
        [
            Pack(0x00, 0x00, 0x00), Pack(0xCC, 0x24, 0x1D), Pack(0x98, 0x97, 0x1A), Pack(0xD7, 0x99, 0x21),
            Pack(0x45, 0x85, 0x88), Pack(0xB1, 0x62, 0x86), Pack(0x68, 0x9D, 0x6A), Pack(0xA8, 0x99, 0x84),
            Pack(0x92, 0x83, 0x74), Pack(0xFB, 0x49, 0x34), Pack(0xB8, 0xBB, 0x26), Pack(0xFA, 0xBD, 0x2F),
            Pack(0x83, 0xA5, 0x98), Pack(0xD3, 0x86, 0x9B), Pack(0x8E, 0xC0, 0x7C), Pack(0xEB, 0xDB, 0xB2),
        ];

        TerminalPalette canonical = TerminalPalette.FromBase16(base16, TerminalPaletteGenerationMode.Canonical);
        TerminalPalette derived = TerminalPalette.FromBase16(base16, TerminalPaletteGenerationMode.DerivedFromBase16Lab);

        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(base16[i], canonical[i]);
            Assert.Equal(base16[i], derived[i]);
        }

        int extendedDifferences = 0;
        for (int i = 16; i < 256; i++)
        {
            if (canonical[i] != derived[i])
            {
                extendedDifferences++;
            }
        }

        Assert.True(extendedDifferences > 0);
    }

    [Fact]
    public void BasicAndGhosttyProcessors_ApplyTheme_StayColorConsistent_WhenGhosttyAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        TerminalTheme theme = TerminalTheme.Dark
            .WithDefaultForeground(0xFFE0E0E0u)
            .WithDefaultBackground(0xFF101112u)
            .WithPaletteColor(17, 0xFF123456u, explicitOverride: true)
            .WithPaletteColor(202, 0xFFBADA55u, explicitOverride: true);

        TerminalScreen basicScreen = new(80, 24, 0);
        TerminalScreen ghosttyScreen = new(80, 24, 0);

        using BasicVtProcessor basic = new(basicScreen);
        using GhosttyVtProcessor ghostty = new(ghosttyScreen);

        basic.ApplyTheme(theme);
        ghostty.ApplyTheme(theme);

        ReadOnlySpan<byte> payload = "\x1b[38;5;202;48;5;17mA\x1b[0m"u8;
        basic.Process(payload);
        ghostty.Process(payload);

        TerminalCell basicCell = basicScreen.GetViewportRow(0)[0];
        TerminalCell ghosttyCell = ghosttyScreen.GetViewportRow(0)[0];

        Assert.Equal(theme.Palette[202], basicCell.Foreground);
        Assert.Equal(theme.Palette[17], basicCell.Background);
        Assert.Equal(basicCell.Foreground, ghosttyCell.Foreground);
        Assert.Equal(basicCell.Background, ghosttyCell.Background);
        Assert.Equal(basicScreen.DefaultForeground, ghosttyScreen.DefaultForeground);
        Assert.Equal(basicScreen.DefaultBackground, ghosttyScreen.DefaultBackground);
    }

    private static uint Pack(byte red, byte green, byte blue)
    {
        return 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
    }
}
