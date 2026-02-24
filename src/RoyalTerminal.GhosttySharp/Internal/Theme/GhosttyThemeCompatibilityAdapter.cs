// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.GhosttySharp.Internal.Theme;

/// <summary>
/// Internal compatibility adapter that maps neutral <see cref="TerminalTheme"/>
/// to Ghostty config payloads.
/// </summary>
internal static class GhosttyThemeCompatibilityAdapter
{
    public static GhosttyConfig CreateConfigForTheme(
        TerminalTheme theme,
        Func<GhosttyConfig>? baseConfigFactory = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        GhosttyConfig baseConfig = baseConfigFactory?.Invoke() ?? CreateDefaultBaseConfig();
        try
        {
            GhosttyConfig merged = baseConfig.Clone();
            try
            {
                string overrideText = BuildGhosttyThemeOverrides(theme);
                string tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"royalterminal-theme-{Guid.NewGuid():N}.ghostty");

                try
                {
                    File.WriteAllText(tempPath, overrideText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    merged.LoadFile(tempPath);
                    merged.Finalize_();
                }
                finally
                {
                    TryDeleteTempFile(tempPath);
                }

                return merged;
            }
            catch
            {
                merged.Dispose();
                throw;
            }
        }
        finally
        {
            baseConfig.Dispose();
        }
    }

    public static bool TryReadTheme(nint configHandle, out TerminalTheme theme)
    {
        theme = TerminalTheme.Dark;
        if (configHandle == nint.Zero)
        {
            return false;
        }

        GhosttyConfig config = new(configHandle, ownsHandle: false);
        try
        {
            uint[] paletteEntries = TerminalTheme.Dark.Palette.ToArray();

            if (config.TryGet("palette", out GhosttyConfigPalette paletteStruct))
            {
                unsafe
                {
                    for (int i = 0; i < 256; i++)
                    {
                        int offset = i * 3;
                        byte r = paletteStruct.Colors[offset];
                        byte g = paletteStruct.Colors[offset + 1];
                        byte b = paletteStruct.Colors[offset + 2];
                        paletteEntries[i] = PackArgb(r, g, b);
                    }
                }
            }

            TerminalPalette palette = new(paletteEntries);

            uint foreground = config.TryGet("foreground", out GhosttyConfigColor fg)
                ? PackArgb(fg.R, fg.G, fg.B)
                : TerminalTheme.Dark.DefaultForeground;
            uint background = config.TryGet("background", out GhosttyConfigColor bg)
                ? PackArgb(bg.R, bg.G, bg.B)
                : TerminalTheme.Dark.DefaultBackground;
            uint cursor = config.TryGet("cursor-color", out GhosttyConfigColor cursorColor)
                ? PackArgb(cursorColor.R, cursorColor.G, cursorColor.B)
                : foreground;

            uint? selectionForeground = config.TryGet("selection-foreground", out GhosttyConfigColor selectionFg)
                ? PackArgb(selectionFg.R, selectionFg.G, selectionFg.B)
                : null;
            uint? selectionBackground = config.TryGet("selection-background", out GhosttyConfigColor selectionBg)
                ? PackArgb(selectionBg.R, selectionBg.G, selectionBg.B)
                : null;
            uint? boldColor = config.TryGet("bold-color", out GhosttyConfigColor bold)
                ? PackArgb(bold.R, bold.G, bold.B)
                : null;

            TerminalOscColorReportFormat reportFormat = TerminalOscColorReportFormat.Bit16;
            byte[] reportKey = Encoding.UTF8.GetBytes("osc-color-report-format");
            if (config.TryGetString(reportKey, out string? reportValue) && reportValue is not null)
            {
                string normalized = reportValue.Trim().ToLowerInvariant();
                if (normalized.Contains('8'))
                {
                    reportFormat = TerminalOscColorReportFormat.Bit8;
                }
            }

            theme = new TerminalTheme(
                foreground,
                background,
                cursor,
                palette,
                TerminalPaletteGenerationMode.Canonical,
                reportFormat,
                selectionForeground,
                selectionBackground,
                boldColor);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static GhosttyConfig CreateDefaultBaseConfig()
    {
        GhosttyConfig config = new();
        config.Finalize_();
        return config;
    }

    private static string BuildGhosttyThemeOverrides(TerminalTheme theme)
    {
        StringBuilder builder = new();
        builder.AppendLine($"foreground = {ToHex(theme.DefaultForeground)}");
        builder.AppendLine($"background = {ToHex(theme.DefaultBackground)}");
        builder.AppendLine($"cursor-color = {ToHex(theme.CursorColor)}");

        if (theme.SelectionForeground is uint selectionForeground)
        {
            builder.AppendLine($"selection-foreground = {ToHex(selectionForeground)}");
        }

        if (theme.SelectionBackground is uint selectionBackground)
        {
            builder.AppendLine($"selection-background = {ToHex(selectionBackground)}");
        }

        if (theme.BoldColor is uint boldColor)
        {
            builder.AppendLine($"bold-color = {ToHex(boldColor)}");
        }

        builder.AppendLine($"osc-color-report-format = {(theme.OscColorReportFormat == TerminalOscColorReportFormat.Bit8 ? "8-bit" : "16-bit")}");

        for (int i = 0; i < 256; i++)
        {
            builder.AppendLine($"palette = {i}={ToHex(theme.Palette[i])}");
        }

        return builder.ToString();
    }

    private static uint PackArgb(byte r, byte g, byte b)
    {
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private static string ToHex(uint color)
    {
        byte r = (byte)((color >> 16) & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)(color & 0xFF);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temp cleanup errors.
        }
    }
}
