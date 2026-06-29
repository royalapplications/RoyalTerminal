// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;

namespace RoyalTerminal.Terminal.Theming;

/// <summary>
/// Parses text-based terminal theme configuration.
/// </summary>
public static class TerminalThemeParser
{
    /// <summary>
    /// Parses a theme text payload.
    /// </summary>
    public static TerminalTheme Parse(string text, TerminalTheme? baseTheme = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        TerminalTheme seed = baseTheme ?? TerminalTheme.Dark;

        uint defaultFg = seed.DefaultForeground;
        uint defaultBg = seed.DefaultBackground;
        uint cursor = seed.CursorColor;
        uint? cursorText = null;
        uint? selectionFg = seed.SelectionForeground;
        uint? selectionBg = seed.SelectionBackground;
        uint? boldColor = seed.BoldColor;
        TerminalPaletteGenerationMode mode = seed.PaletteGenerationMode;
        TerminalOscColorReportFormat reportFormat = seed.OscColorReportFormat;

        Dictionary<int, uint> paletteOverrides = new();
        uint[] ansiBase16 = new uint[16];
        for (int i = 0; i < 16; i++)
        {
            ansiBase16[i] = seed.Palette[i];
        }

        bool ansiChanged = false;

        string[] lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            string key = line[..separator].Trim().ToLowerInvariant();
            string value = line[(separator + 1)..].Trim();

            if (TryParseAnsiKey(key, out int ansiIndex))
            {
                if (TryParseColor(value, out uint ansiColor))
                {
                    ansiBase16[ansiIndex] = ansiColor;
                    ansiChanged = true;
                }

                continue;
            }

            if (TryParsePaletteColorKey(key, out int paletteKeyIndex))
            {
                if (TryParseColor(value, out uint paletteColor))
                {
                    paletteOverrides[paletteKeyIndex] = paletteColor;
                }

                continue;
            }

            switch (key)
            {
                case "foreground":
                    if (TryParseColor(value, out uint fg)) defaultFg = fg;
                    break;

                case "background":
                    if (TryParseColor(value, out uint bg)) defaultBg = bg;
                    break;

                case "cursor":
                case "cursor-color":
                    if (TryParseColor(value, out uint cursorColor)) cursor = cursorColor;
                    break;

                case "cursor-text":
                case "cursor-text-color":
                    if (TryParseColor(value, out uint cursorTextColor)) cursorText = cursorTextColor;
                    break;

                case "selection-foreground":
                    if (TryParseColor(value, out uint parsedSelectionFg)) selectionFg = parsedSelectionFg;
                    break;

                case "selection-background":
                    if (TryParseColor(value, out uint parsedSelectionBg)) selectionBg = parsedSelectionBg;
                    break;

                case "bold-color":
                    if (TryParseColor(value, out uint parsedBoldColor)) boldColor = parsedBoldColor;
                    break;

                case "palette-generation-mode":
                    if (TryParsePaletteGenerationMode(value, out TerminalPaletteGenerationMode parsedMode))
                    {
                        mode = parsedMode;
                    }

                    break;

                case "osc-color-report-format":
                    if (TryParseOscReportFormat(value, out TerminalOscColorReportFormat parsedFormat))
                    {
                        reportFormat = parsedFormat;
                    }

                    break;

                case "palette":
                    foreach ((int index, uint color) in ParsePaletteEntries(value))
                    {
                        paletteOverrides[index] = color;
                    }

                    break;
            }
        }

        TerminalPalette palette = seed.Palette;
        if (ansiChanged || mode != seed.PaletteGenerationMode)
        {
            HashSet<int> explicitAnsi = new();
            if (ansiChanged)
            {
                for (int i = 0; i < 16; i++)
                {
                    if (ansiBase16[i] != seed.Palette[i])
                    {
                        explicitAnsi.Add(i);
                    }
                }
            }

            palette = TerminalPalette.FromBase16(ansiBase16.AsSpan(), mode, explicitAnsi);
        }

        foreach ((int index, uint color) in paletteOverrides)
        {
            palette = palette.WithColor(index, color, explicitOverride: true);
        }

        return new TerminalTheme(
            defaultFg,
            defaultBg,
            cursor,
            palette,
            mode,
            reportFormat,
            selectionFg,
            selectionBg,
            boldColor,
            cursorText);
    }

    private static bool TryParsePaletteGenerationMode(string value, out TerminalPaletteGenerationMode mode)
    {
        string normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "canonical":
            case "xterm":
                mode = TerminalPaletteGenerationMode.Canonical;
                return true;

            case "derived":
            case "derivedfrombase16lab":
            case "derived-from-base16-lab":
            case "lab":
                mode = TerminalPaletteGenerationMode.DerivedFromBase16Lab;
                return true;

            default:
                mode = default;
                return false;
        }
    }

    private static bool TryParseOscReportFormat(string value, out TerminalOscColorReportFormat format)
    {
        string normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "16":
            case "16bit":
            case "16-bit":
            case "bit16":
                format = TerminalOscColorReportFormat.Bit16;
                return true;

            case "8":
            case "8bit":
            case "8-bit":
            case "bit8":
                format = TerminalOscColorReportFormat.Bit8;
                return true;

            default:
                format = default;
                return false;
        }
    }

    private static bool TryParseAnsiKey(string key, out int index)
    {
        index = -1;
        if (!key.StartsWith("ansi", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(key.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            return false;
        }

        return (uint)index < 16;
    }

    private static bool TryParsePaletteColorKey(string key, out int index)
    {
        index = -1;

        if (key.StartsWith("palette[", StringComparison.Ordinal) && key.EndsWith(']'))
        {
            ReadOnlySpan<char> indexSpan = key.AsSpan("palette[".Length, key.Length - "palette[".Length - 1);
            return int.TryParse(indexSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && (uint)index < 256;
        }

        if (key.StartsWith("color", StringComparison.Ordinal) &&
            int.TryParse(key.AsSpan("color".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) &&
            (uint)index < 256)
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<(int Index, uint Color)> ParsePaletteEntries(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        char[] separators = [',', ';'];
        string[] entries = value.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string entry in entries)
        {
            int separator = entry.IndexOf('=');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                continue;
            }

            string indexPart = entry[..separator].Trim();
            string colorPart = entry[(separator + 1)..].Trim();
            if (!int.TryParse(indexPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || (uint)index >= 256)
            {
                continue;
            }

            if (!TryParseColor(colorPart, out uint color))
            {
                continue;
            }

            yield return (index, color);
        }
    }

    /// <summary>
    /// Tries to parse a color token in #RGB/#RRGGBB/#RRRRGGGGBBBB or rgb:R/G/B form.
    /// </summary>
    public static bool TryParseColor(string value, out uint color)
    {
        color = 0;
        string token = value.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        if (token.StartsWith('#'))
        {
            return TryParseHashColor(token.AsSpan(1), out color);
        }

        if (token.StartsWith("rgb:", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgbColor(token.AsSpan(4), out color);
        }

        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            color = (argb & 0x00FFFFFFu) | 0xFF000000u;
            return true;
        }

        if (uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint packed))
        {
            color = (packed & 0x00FFFFFFu) | 0xFF000000u;
            return true;
        }

        return false;
    }

    private static bool TryParseHashColor(ReadOnlySpan<char> value, out uint color)
    {
        color = 0;
        if (value.Length == 3)
        {
            if (!TryParseHexByte(value[0], value[0], out byte r) ||
                !TryParseHexByte(value[1], value[1], out byte g) ||
                !TryParseHexByte(value[2], value[2], out byte b))
            {
                return false;
            }

            color = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            return true;
        }

        if (value.Length == 6)
        {
            if (!TryParseHexByte(value[0], value[1], out byte r) ||
                !TryParseHexByte(value[2], value[3], out byte g) ||
                !TryParseHexByte(value[4], value[5], out byte b))
            {
                return false;
            }

            color = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            return true;
        }

        if (value.Length == 12)
        {
            if (!ushort.TryParse(value[..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort r16) ||
                !ushort.TryParse(value.Slice(4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort g16) ||
                !ushort.TryParse(value.Slice(8, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort b16))
            {
                return false;
            }

            byte r = (byte)(r16 / 257);
            byte g = (byte)(g16 / 257);
            byte b = (byte)(b16 / 257);
            color = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            return true;
        }

        return false;
    }

    private static bool TryParseRgbColor(ReadOnlySpan<char> value, out uint color)
    {
        color = 0;
        int first = value.IndexOf('/');
        if (first <= 0)
        {
            return false;
        }

        int second = value[(first + 1)..].IndexOf('/');
        if (second <= 0)
        {
            return false;
        }

        second += first + 1;

        ReadOnlySpan<char> rPart = value[..first];
        ReadOnlySpan<char> gPart = value.Slice(first + 1, second - first - 1);
        ReadOnlySpan<char> bPart = value[(second + 1)..];

        if (!TryParseRgbComponent(rPart, out byte r) ||
            !TryParseRgbComponent(gPart, out byte g) ||
            !TryParseRgbComponent(bPart, out byte b))
        {
            return false;
        }

        color = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        return true;
    }

    private static bool TryParseRgbComponent(ReadOnlySpan<char> value, out byte component)
    {
        component = 0;
        if (value.Length == 0 || value.Length > 4)
        {
            return false;
        }

        if (!ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort parsed))
        {
            return false;
        }

        if (value.Length == 1)
        {
            component = (byte)(parsed * 17);
            return true;
        }

        if (value.Length == 2)
        {
            component = (byte)parsed;
            return true;
        }

        if (value.Length == 3)
        {
            component = (byte)Math.Round((parsed / 4095.0) * 255.0, MidpointRounding.AwayFromZero);
            return true;
        }

        component = (byte)(parsed / 257);
        return true;
    }

    private static bool TryParseHexByte(char high, char low, out byte value)
    {
        value = 0;
        Span<char> token = stackalloc char[2];
        token[0] = high;
        token[1] = low;
        if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return true;
    }
}
