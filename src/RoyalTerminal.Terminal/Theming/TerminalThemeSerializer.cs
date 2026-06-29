// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.Terminal.Theming;

/// <summary>
/// Serializes terminal themes to neutral key-value text.
/// </summary>
public static class TerminalThemeSerializer
{
    /// <summary>
    /// Serializes theme to neutral text format.
    /// </summary>
    public static string ToText(TerminalTheme theme, bool includeAllPaletteEntries = true)
    {
        ArgumentNullException.ThrowIfNull(theme);

        StringBuilder builder = new();
        builder.AppendLine($"foreground = {ToHex(theme.DefaultForeground)}");
        builder.AppendLine($"background = {ToHex(theme.DefaultBackground)}");
        builder.AppendLine($"cursor-color = {ToHex(theme.CursorColor)}");
        builder.AppendLine($"cursor-text-color = {ToHex(theme.CursorTextColor)}");
        builder.AppendLine($"palette-generation-mode = {FormatPaletteMode(theme.PaletteGenerationMode)}");
        builder.AppendLine($"osc-color-report-format = {FormatOscMode(theme.OscColorReportFormat)}");

        if (theme.SelectionForeground is uint selectionFg)
        {
            builder.AppendLine($"selection-foreground = {ToHex(selectionFg)}");
        }

        if (theme.SelectionBackground is uint selectionBg)
        {
            builder.AppendLine($"selection-background = {ToHex(selectionBg)}");
        }

        if (theme.BoldColor is uint boldColor)
        {
            builder.AppendLine($"bold-color = {ToHex(boldColor)}");
        }

        if (includeAllPaletteEntries)
        {
            for (int i = 0; i < 256; i++)
            {
                builder.AppendLine($"palette[{i}] = {ToHex(theme.Palette[i])}");
            }
        }
        else
        {
            IReadOnlyList<int> overrides = theme.Palette.ExplicitOverrideIndexes;
            foreach (int index in overrides)
            {
                builder.AppendLine($"palette[{index}] = {ToHex(theme.Palette[index])}");
            }
        }

        return builder.ToString();
    }

    private static string FormatPaletteMode(TerminalPaletteGenerationMode mode)
    {
        return mode switch
        {
            TerminalPaletteGenerationMode.Canonical => "canonical",
            TerminalPaletteGenerationMode.DerivedFromBase16Lab => "derived-from-base16-lab",
            _ => "canonical",
        };
    }

    private static string FormatOscMode(TerminalOscColorReportFormat format)
    {
        return format switch
        {
            TerminalOscColorReportFormat.Bit8 => "8-bit",
            _ => "16-bit",
        };
    }

    internal static string ToHex(uint argb)
    {
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
