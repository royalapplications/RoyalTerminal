// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Font rendering quality settings.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Text edge rendering mode used by terminal font rasterization.
/// </summary>
public enum TerminalFontEdging
{
    /// <summary>
    /// Renders glyph edges without antialiasing.
    /// </summary>
    Alias = 0,

    /// <summary>
    /// Renders glyph edges with grayscale antialiasing.
    /// </summary>
    Antialias = 1,

    /// <summary>
    /// Renders glyph edges with subpixel antialiasing when supported by the backend.
    /// </summary>
    SubpixelAntialias = 2,
}

/// <summary>
/// Font hinting strength used by terminal font rasterization.
/// </summary>
public enum TerminalFontHinting
{
    /// <summary>
    /// Disables font hinting.
    /// </summary>
    None = 0,

    /// <summary>
    /// Uses slight hinting to preserve glyph shapes while improving stem alignment.
    /// </summary>
    Slight = 1,

    /// <summary>
    /// Uses normal hinting.
    /// </summary>
    Normal = 2,

    /// <summary>
    /// Uses full hinting.
    /// </summary>
    Full = 3,
}

/// <summary>
/// Skia-independent terminal font rendering quality settings.
/// </summary>
public sealed record TerminalFontRenderingSettings
{
    /// <summary>
    /// Default rendering settings matching RoyalTerminal's historical Skia defaults.
    /// </summary>
    public static TerminalFontRenderingSettings Default { get; } = new();

    /// <summary>
    /// Gets whether subpixel glyph positioning is enabled.
    /// </summary>
    public bool SubpixelPositioning { get; init; } = true;

    /// <summary>
    /// Gets the glyph edge rendering mode.
    /// </summary>
    public TerminalFontEdging Edging { get; init; } = TerminalFontEdging.SubpixelAntialias;

    /// <summary>
    /// Gets the font hinting strength.
    /// </summary>
    public TerminalFontHinting Hinting { get; init; } = TerminalFontHinting.Slight;

    /// <summary>
    /// Gets whether glyphs snap to pixel boundaries on the baseline.
    /// </summary>
    public bool BaselineSnap { get; init; } = true;

    /// <summary>
    /// Gets whether embedded bitmap strikes are used when the font provides them.
    /// </summary>
    public bool EmbeddedBitmaps { get; init; }

    /// <summary>
    /// Gets whether glyphs are algorithmically emboldened.
    /// </summary>
    public bool Embolden { get; init; }

    /// <summary>
    /// Gets whether auto-hinting is forced instead of using native font hints.
    /// </summary>
    public bool ForceAutoHinting { get; init; }

    /// <summary>
    /// Gets whether metrics ignore hinting for improved precision.
    /// </summary>
    public bool LinearMetrics { get; init; }

    /// <summary>
    /// Returns a copy with unsupported enum values replaced by defaults.
    /// </summary>
    public TerminalFontRenderingSettings Normalize()
    {
        TerminalFontRenderingSettings defaults = Default;
        return this with
        {
            Edging = Enum.IsDefined(Edging) ? Edging : defaults.Edging,
            Hinting = Enum.IsDefined(Hinting) ? Hinting : defaults.Hinting,
        };
    }
}
