// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App — Theme catalog with preset and generated mode-specific themes.

using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Avalonia.App.Services;

internal readonly record struct TerminalThemePreset(string Id, string DisplayName);

public readonly record struct TerminalThemeApplyRequest(TerminalTheme Theme, string ThemeName);

internal interface ITerminalThemeCatalog
{
    IReadOnlyList<TerminalThemePreset> Presets { get; }

    TerminalThemePreset GetPreset(string presetId);

    TerminalThemePreset GetDefaultPreset(TerminalRenderMode mode);

    TerminalTheme CreatePresetTheme(string presetId, TerminalRenderMode mode);

    TerminalTheme CreateGeneratedTheme(TerminalRenderMode mode, int generation);
}

internal sealed class TerminalThemeCatalog : ITerminalThemeCatalog
{
    public const string LightPresetId = "solarized-light";

    private static readonly IReadOnlyList<TerminalThemePreset> s_presets =
    [
        new TerminalThemePreset("catppuccin-mocha", "Catppuccin Mocha"),
        new TerminalThemePreset("tokyo-night", "Tokyo Night"),
        new TerminalThemePreset("gruvbox-dark", "Gruvbox Dark"),
        new TerminalThemePreset("nord", "Nord"),
        new TerminalThemePreset("ayu-mirage", "Ayu Mirage"),
        new TerminalThemePreset(LightPresetId, "Solarized Light"),
    ];

    private static readonly Dictionary<string, string> s_presetThemeText = new(StringComparer.Ordinal)
    {
        ["catppuccin-mocha"] = """
            foreground = #cdd6f4
            background = #1e1e2e
            cursor = #f5e0dc
            color0 = #45475a
            color1 = #f38ba8
            color2 = #a6e3a1
            color3 = #f9e2af
            color4 = #89b4fa
            color5 = #f5c2e7
            color6 = #94e2d5
            color7 = #bac2de
            color8 = #585b70
            color9 = #f38ba8
            color10 = #a6e3a1
            color11 = #f9e2af
            color12 = #89b4fa
            color13 = #f5c2e7
            color14 = #94e2d5
            color15 = #a6adc8
            """,
        ["tokyo-night"] = """
            foreground = #a9b1d6
            background = #1a1b26
            cursor = #c0caf5
            color0 = #15161e
            color1 = #f7768e
            color2 = #9ece6a
            color3 = #e0af68
            color4 = #7aa2f7
            color5 = #bb9af7
            color6 = #7dcfff
            color7 = #a9b1d6
            color8 = #414868
            color9 = #f7768e
            color10 = #9ece6a
            color11 = #e0af68
            color12 = #7aa2f7
            color13 = #bb9af7
            color14 = #7dcfff
            color15 = #c0caf5
            """,
        ["gruvbox-dark"] = """
            foreground = #ebdbb2
            background = #282828
            cursor = #fe8019
            color0 = #282828
            color1 = #cc241d
            color2 = #98971a
            color3 = #d79921
            color4 = #458588
            color5 = #b16286
            color6 = #689d6a
            color7 = #a89984
            color8 = #928374
            color9 = #fb4934
            color10 = #b8bb26
            color11 = #fabd2f
            color12 = #83a598
            color13 = #d3869b
            color14 = #8ec07c
            color15 = #ebdbb2
            """,
        ["nord"] = """
            foreground = #d8dee9
            background = #2e3440
            cursor = #88c0d0
            color0 = #3b4252
            color1 = #bf616a
            color2 = #a3be8c
            color3 = #ebcb8b
            color4 = #81a1c1
            color5 = #b48ead
            color6 = #88c0d0
            color7 = #e5e9f0
            color8 = #4c566a
            color9 = #bf616a
            color10 = #a3be8c
            color11 = #ebcb8b
            color12 = #81a1c1
            color13 = #b48ead
            color14 = #8fbcbb
            color15 = #eceff4
            """,
        ["ayu-mirage"] = """
            foreground = #cccac2
            background = #1f2430
            cursor = #ffcc66
            color0 = #1f2430
            color1 = #f28779
            color2 = #a6cc70
            color3 = #ffcc66
            color4 = #5ccfe6
            color5 = #d4bfff
            color6 = #95e6cb
            color7 = #cccac2
            color8 = #707a8c
            color9 = #f28779
            color10 = #a6cc70
            color11 = #ffd580
            color12 = #73d0ff
            color13 = #dfbfff
            color14 = #95e6cb
            color15 = #ffffff
            """,
        [LightPresetId] = """
            foreground = #657b83
            background = #fdf6e3
            cursor = #586e75
            color0 = #073642
            color1 = #dc322f
            color2 = #859900
            color3 = #b58900
            color4 = #268bd2
            color5 = #d33682
            color6 = #2aa198
            color7 = #eee8d5
            color8 = #002b36
            color9 = #cb4b16
            color10 = #586e75
            color11 = #657b83
            color12 = #839496
            color13 = #6c71c4
            color14 = #93a1a1
            color15 = #fdf6e3
            """,
    };

    private static readonly Dictionary<TerminalRenderMode, string> s_modeDefaults = new()
    {
        [TerminalRenderMode.NativeVt] = "gruvbox-dark",
        [TerminalRenderMode.ManagedVt] = "nord",
        [TerminalRenderMode.RenderedAuto] = "ayu-mirage",
    };

    public IReadOnlyList<TerminalThemePreset> Presets => s_presets;

    public TerminalThemePreset GetPreset(string presetId)
    {
        for (int i = 0; i < s_presets.Count; i++)
        {
            TerminalThemePreset preset = s_presets[i];
            if (string.Equals(preset.Id, presetId, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return GetDefaultPreset(TerminalRenderMode.RenderedAuto);
    }

    public TerminalThemePreset GetDefaultPreset(TerminalRenderMode mode)
    {
        if (s_modeDefaults.TryGetValue(mode, out string? presetId))
        {
            return GetPreset(presetId);
        }

        return GetPreset("catppuccin-mocha");
    }

    public TerminalTheme CreatePresetTheme(string presetId, TerminalRenderMode mode)
    {
        TerminalThemePreset preset = GetPreset(presetId);
        if (!s_presetThemeText.TryGetValue(preset.Id, out string? payload))
        {
            payload = s_presetThemeText["catppuccin-mocha"];
        }

        TerminalTheme parsed = TerminalThemeParser.Parse(payload, TerminalTheme.Dark);
        return AdaptForMode(parsed, mode);
    }

    public TerminalTheme CreateGeneratedTheme(TerminalRenderMode mode, int generation)
    {
        int iteration = Math.Max(1, generation);
        double baseHue = mode switch
        {
            TerminalRenderMode.NativeVt => 120.0,
            TerminalRenderMode.ManagedVt => 40.0,
            _ => 330.0,
        };

        double hue = NormalizeHue(baseHue + (iteration * 37.0));
        uint[] base16 = BuildBase16(hue);
        TerminalTheme theme = TerminalTheme.FromBase16(
            base16.AsSpan(),
            defaultForeground: base16[7],
            defaultBackground: base16[0],
            cursorColor: base16[14],
            mode: TerminalPaletteGenerationMode.Canonical,
            oscReportFormat: TerminalOscColorReportFormat.Bit16,
            selectionForeground: base16[15],
            selectionBackground: Blend(base16[0], base16[4], 0.55));

        return AdaptForMode(theme, mode);
    }

    private static TerminalTheme AdaptForMode(TerminalTheme theme, TerminalRenderMode mode)
    {
        TerminalPaletteGenerationMode targetGenerationMode = TerminalPaletteGenerationMode.Canonical;

        if (theme.PaletteGenerationMode != targetGenerationMode)
        {
            uint[] base16 = new uint[16];
            for (int i = 0; i < 16; i++)
            {
                base16[i] = theme.Palette[i];
            }

            theme = theme.RegeneratePalette(base16.AsSpan(), targetGenerationMode);
        }

        return theme.WithOscColorReportFormat(TerminalOscColorReportFormat.Bit16);
    }

    private static uint[] BuildBase16(double hue)
    {
        return
        [
            HslToArgb(hue, 0.25, 0.11),                 // black
            HslToArgb(hue + 350.0, 0.72, 0.58),         // red
            HslToArgb(hue + 120.0, 0.62, 0.52),         // green
            HslToArgb(hue + 55.0, 0.75, 0.58),          // yellow
            HslToArgb(hue + 220.0, 0.75, 0.62),         // blue
            HslToArgb(hue + 300.0, 0.62, 0.64),         // magenta
            HslToArgb(hue + 180.0, 0.60, 0.58),         // cyan
            HslToArgb(hue + 185.0, 0.20, 0.82),         // white
            HslToArgb(hue, 0.22, 0.28),                 // bright black
            HslToArgb(hue + 350.0, 0.78, 0.68),         // bright red
            HslToArgb(hue + 120.0, 0.70, 0.64),         // bright green
            HslToArgb(hue + 55.0, 0.85, 0.70),          // bright yellow
            HslToArgb(hue + 220.0, 0.82, 0.73),         // bright blue
            HslToArgb(hue + 300.0, 0.70, 0.75),         // bright magenta
            HslToArgb(hue + 180.0, 0.70, 0.70),         // bright cyan
            HslToArgb(hue + 185.0, 0.18, 0.93),         // bright white
        ];
    }

    private static uint Blend(uint from, uint to, double amount)
    {
        double t = Math.Clamp(amount, 0.0, 1.0);
        byte r1 = (byte)((from >> 16) & 0xFF);
        byte g1 = (byte)((from >> 8) & 0xFF);
        byte b1 = (byte)(from & 0xFF);
        byte r2 = (byte)((to >> 16) & 0xFF);
        byte g2 = (byte)((to >> 8) & 0xFF);
        byte b2 = (byte)(to & 0xFF);

        byte r = (byte)Math.Clamp((int)Math.Round(r1 + ((r2 - r1) * t), MidpointRounding.AwayFromZero), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(g1 + ((g2 - g1) * t), MidpointRounding.AwayFromZero), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(b1 + ((b2 - b1) * t), MidpointRounding.AwayFromZero), 0, 255);
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private static uint HslToArgb(double hue, double saturation, double lightness)
    {
        double h = NormalizeHue(hue) / 360.0;
        double s = Math.Clamp(saturation, 0.0, 1.0);
        double l = Math.Clamp(lightness, 0.0, 1.0);

        double r;
        double g;
        double b;

        if (s <= 0.0)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1.0 + s) : l + s - (l * s);
            double p = (2.0 * l) - q;
            r = HueToChannel(p, q, h + (1.0 / 3.0));
            g = HueToChannel(p, q, h);
            b = HueToChannel(p, q, h - (1.0 / 3.0));
        }

        byte r8 = (byte)Math.Clamp((int)Math.Round(r * 255.0, MidpointRounding.AwayFromZero), 0, 255);
        byte g8 = (byte)Math.Clamp((int)Math.Round(g * 255.0, MidpointRounding.AwayFromZero), 0, 255);
        byte b8 = (byte)Math.Clamp((int)Math.Round(b * 255.0, MidpointRounding.AwayFromZero), 0, 255);
        return 0xFF000000u | ((uint)r8 << 16) | ((uint)g8 << 8) | b8;
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0.0)
        {
            t += 1.0;
        }
        else if (t > 1.0)
        {
            t -= 1.0;
        }

        if (t < (1.0 / 6.0))
        {
            return p + ((q - p) * 6.0 * t);
        }

        if (t < 0.5)
        {
            return q;
        }

        if (t < (2.0 / 3.0))
        {
            return p + ((q - p) * ((2.0 / 3.0) - t) * 6.0);
        }

        return p;
    }

    private static double NormalizeHue(double hue)
    {
        double normalized = hue % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }
}
