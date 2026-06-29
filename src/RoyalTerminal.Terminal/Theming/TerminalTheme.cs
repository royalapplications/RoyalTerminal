// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Theming;

/// <summary>
/// Immutable terminal theme model shared by all rendering/VT pipelines.
/// </summary>
public sealed class TerminalTheme
{
    /// <summary>
    /// Default dark theme.
    /// </summary>
    public static TerminalTheme Dark { get; } = CreateDark();

    /// <summary>
    /// Default light theme.
    /// </summary>
    public static TerminalTheme Light { get; } = CreateLight();

    /// <summary>
    /// Default foreground color (ARGB).
    /// </summary>
    public uint DefaultForeground { get; }

    /// <summary>
    /// Default background color (ARGB).
    /// </summary>
    public uint DefaultBackground { get; }

    /// <summary>
    /// Cursor color (ARGB).
    /// </summary>
    public uint CursorColor { get; }

    /// <summary>
    /// Cursor text color (ARGB).
    /// </summary>
    public uint CursorTextColor { get; }

    /// <summary>
    /// Optional selection foreground color (ARGB).
    /// </summary>
    public uint? SelectionForeground { get; }

    /// <summary>
    /// Optional selection background color (ARGB).
    /// </summary>
    public uint? SelectionBackground { get; }

    /// <summary>
    /// Optional explicit bold color (ARGB).
    /// </summary>
    public uint? BoldColor { get; }

    /// <summary>
    /// Full 256-color palette.
    /// </summary>
    public TerminalPalette Palette { get; }

    /// <summary>
    /// Palette generation mode.
    /// </summary>
    public TerminalPaletteGenerationMode PaletteGenerationMode { get; }

    /// <summary>
    /// OSC color report format.
    /// </summary>
    public TerminalOscColorReportFormat OscColorReportFormat { get; }

    /// <summary>
    /// Creates a new immutable theme.
    /// </summary>
    public TerminalTheme(
        uint defaultForeground,
        uint defaultBackground,
        uint cursorColor,
        TerminalPalette palette,
        TerminalPaletteGenerationMode paletteGenerationMode = TerminalPaletteGenerationMode.Canonical,
        TerminalOscColorReportFormat oscColorReportFormat = TerminalOscColorReportFormat.Bit16,
        uint? selectionForeground = null,
        uint? selectionBackground = null,
        uint? boldColor = null,
        uint? cursorTextColor = null)
    {
        Palette = palette ?? throw new ArgumentNullException(nameof(palette));
        DefaultForeground = EnsureOpaque(defaultForeground);
        DefaultBackground = EnsureOpaque(defaultBackground);
        CursorColor = EnsureOpaque(cursorColor);
        CursorTextColor = EnsureOpaque(cursorTextColor ?? defaultBackground);
        SelectionForeground = selectionForeground is null ? null : EnsureOpaque(selectionForeground.Value);
        SelectionBackground = selectionBackground is null ? null : EnsureOpaque(selectionBackground.Value);
        BoldColor = boldColor is null ? null : EnsureOpaque(boldColor.Value);
        PaletteGenerationMode = paletteGenerationMode;
        OscColorReportFormat = oscColorReportFormat;
    }

    /// <summary>
    /// Creates a theme from ANSI base colors.
    /// </summary>
    public static TerminalTheme FromBase16(
        ReadOnlySpan<uint> base16,
        uint defaultForeground,
        uint defaultBackground,
        uint? cursorColor = null,
        TerminalPaletteGenerationMode mode = TerminalPaletteGenerationMode.Canonical,
        TerminalOscColorReportFormat oscReportFormat = TerminalOscColorReportFormat.Bit16,
        uint? selectionForeground = null,
        uint? selectionBackground = null,
        uint? boldColor = null,
        uint? cursorTextColor = null)
    {
        TerminalPalette palette = TerminalPalette.FromBase16(base16, mode);
        return new TerminalTheme(
            defaultForeground,
            defaultBackground,
            cursorColor ?? defaultForeground,
            palette,
            mode,
            oscReportFormat,
            selectionForeground,
            selectionBackground,
            boldColor,
            cursorTextColor);
    }

    /// <summary>
    /// Returns a copy with a different default foreground.
    /// </summary>
    public TerminalTheme WithDefaultForeground(uint value)
    {
        return new TerminalTheme(
            value,
            DefaultBackground,
            CursorColor,
            Palette,
            PaletteGenerationMode,
            OscColorReportFormat,
            SelectionForeground,
            SelectionBackground,
            BoldColor,
            CursorTextColor);
    }

    /// <summary>
    /// Returns a copy with a different default background.
    /// </summary>
    public TerminalTheme WithDefaultBackground(uint value)
    {
        return new TerminalTheme(
            DefaultForeground,
            value,
            CursorColor,
            Palette,
            PaletteGenerationMode,
            OscColorReportFormat,
            SelectionForeground,
            SelectionBackground,
            BoldColor,
            CursorTextColor);
    }

    /// <summary>
    /// Returns a copy with a different cursor color.
    /// </summary>
    public TerminalTheme WithCursorColor(uint value)
    {
        return new TerminalTheme(
            DefaultForeground,
            DefaultBackground,
            value,
            Palette,
            PaletteGenerationMode,
            OscColorReportFormat,
            SelectionForeground,
            SelectionBackground,
            BoldColor,
            CursorTextColor);
    }

    /// <summary>
    /// Returns a copy with a different cursor text color.
    /// </summary>
    public TerminalTheme WithCursorTextColor(uint value)
    {
        return new TerminalTheme(
            DefaultForeground,
            DefaultBackground,
            CursorColor,
            Palette,
            PaletteGenerationMode,
            OscColorReportFormat,
            SelectionForeground,
            SelectionBackground,
            BoldColor,
            value);
    }

    /// <summary>
    /// Returns a copy with new selection colors.
    /// </summary>
    public TerminalTheme WithSelectionColors(uint? foreground, uint? background)
    {
        return new TerminalTheme(
            DefaultForeground,
            DefaultBackground,
            CursorColor,
            Palette,
            PaletteGenerationMode,
            OscColorReportFormat,
            foreground,
            background,
            BoldColor,
            CursorTextColor);
    }

    /// <summary>
    /// Returns a copy with a different bold color.
    /// </summary>
    public TerminalTheme WithBoldColor(uint? boldColor)
    {
        return new TerminalTheme(
            DefaultForeground,
            DefaultBackground,
            CursorColor,
            Palette,
            PaletteGenerationMode,
            OscColorReportFormat,
            SelectionForeground,
            SelectionBackground,
            boldColor,
            CursorTextColor);
    }

    /// <summary>
    /// Returns a copy with a different OSC report format.
    /// </summary>
    public TerminalTheme WithOscColorReportFormat(TerminalOscColorReportFormat reportFormat)
    {
        return new TerminalTheme(
            DefaultForeground,
            DefaultBackground,
            CursorColor,
            Palette,
            PaletteGenerationMode,
            reportFormat,
            SelectionForeground,
            SelectionBackground,
            BoldColor,
            CursorTextColor);
    }

    /// <summary>
    /// Returns a copy with a different palette.
    /// </summary>
    public TerminalTheme WithPalette(TerminalPalette palette)
    {
        return new TerminalTheme(
            DefaultForeground,
            DefaultBackground,
            CursorColor,
            palette,
            PaletteGenerationMode,
            OscColorReportFormat,
            SelectionForeground,
            SelectionBackground,
            BoldColor,
            CursorTextColor);
    }

    /// <summary>
    /// Returns a copy with one palette entry updated.
    /// </summary>
    public TerminalTheme WithPaletteColor(int index, uint color, bool explicitOverride = true)
    {
        return WithPalette(Palette.WithColor(index, color, explicitOverride));
    }

    /// <summary>
    /// Returns a copy with palette regenerated from ANSI base16.
    /// </summary>
    public TerminalTheme RegeneratePalette(
        ReadOnlySpan<uint> base16,
        TerminalPaletteGenerationMode generationMode,
        IEnumerable<int>? explicitOverrides = null)
    {
        TerminalPalette palette = TerminalPalette.FromBase16(base16, generationMode, explicitOverrides);
        return new TerminalTheme(
            DefaultForeground,
            DefaultBackground,
            CursorColor,
            palette,
            generationMode,
            OscColorReportFormat,
            SelectionForeground,
            SelectionBackground,
            BoldColor,
            CursorTextColor);
    }

    private static TerminalTheme CreateDark()
    {
        TerminalPalette palette = TerminalPalette.CreateDefaultCanonical();
        return new TerminalTheme(
            TerminalThemeDefaults.DefaultDarkForeground,
            TerminalThemeDefaults.DefaultDarkBackground,
            TerminalThemeDefaults.DefaultDarkForeground,
            palette,
            TerminalPaletteGenerationMode.Canonical,
            TerminalOscColorReportFormat.Bit16);
    }

    private static TerminalTheme CreateLight()
    {
        TerminalPalette palette = TerminalPalette.CreateDefaultCanonical();
        return new TerminalTheme(
            TerminalThemeDefaults.DefaultLightForeground,
            TerminalThemeDefaults.DefaultLightBackground,
            TerminalThemeDefaults.DefaultLightForeground,
            palette,
            TerminalPaletteGenerationMode.Canonical,
            TerminalOscColorReportFormat.Bit16);
    }

    private static uint EnsureOpaque(uint argb)
    {
        return (argb & 0x00FFFFFFu) | 0xFF000000u;
    }
}
