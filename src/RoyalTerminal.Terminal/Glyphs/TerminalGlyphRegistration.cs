// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Glyphs;

/// <summary>Immutable session glyph, authored metrics and requested placement policy.</summary>
public sealed class TerminalGlyphRegistration
{
    internal TerminalGlyphRegistration(TerminalGlyphOutline outline, uint unitsPerEm, uint advanceWidth,
        uint lineHeight, byte width, TerminalGlyphLayout layout)
    {
        Outline = outline;
        UnitsPerEm = unitsPerEm;
        AdvanceWidth = advanceWidth;
        LineHeight = lineHeight;
        Width = width;
        Layout = layout;
    }

    /// <summary>Validated unhinted TrueType outline.</summary>
    public TerminalGlyphOutline Outline { get; }
    /// <summary>Positive design units per em.</summary>
    public uint UnitsPerEm { get; }
    /// <summary>Positive authored advance width in design units.</summary>
    public uint AdvanceWidth { get; }
    /// <summary>Positive authored line height in design units.</summary>
    public uint LineHeight { get; }
    /// <summary>Requested cell width (one or two); metadata, not a retroactive cell edit.</summary>
    public byte Width { get; }
    /// <summary>Requested scale, alignment and normalized fractional padding.</summary>
    public TerminalGlyphLayout Layout { get; }
}

/// <summary>Protocol sizing policy; renderer normalization is a separate concern.</summary>
public enum TerminalGlyphSize
{
    /// <summary>Scale using em height.</summary>
    Height,
    /// <summary>Scale using authored advance.</summary>
    Advance,
    /// <summary>Fit the authored extent within the available span.</summary>
    Contain,
    /// <summary>Cover the available span.</summary>
    Cover,
    /// <summary>Scale axes independently.</summary>
    Stretch,
}

/// <summary>Alignment of the authored extent within its rendering span.</summary>
public enum TerminalGlyphAlignment
{
    /// <summary>Align at the starting edge.</summary>
    Start,
    /// <summary>Center within the span.</summary>
    Center,
    /// <summary>Align at the ending edge.</summary>
    End,
    /// <summary>Vertical baseline alignment; not valid horizontally.</summary>
    Baseline,
}

/// <summary>Immutable glyph placement options, preserving the protocol's requested policy.</summary>
/// <param name="Size">Requested scale policy.</param>
/// <param name="Horizontal">Horizontal alignment.</param>
/// <param name="Vertical">Vertical alignment.</param>
/// <param name="Top">Top fractional inset.</param>
/// <param name="Right">Right fractional inset.</param>
/// <param name="Bottom">Bottom fractional inset.</param>
/// <param name="Left">Left fractional inset.</param>
public readonly record struct TerminalGlyphLayout(TerminalGlyphSize Size,
    TerminalGlyphAlignment Horizontal, TerminalGlyphAlignment Vertical,
    double Top, double Right, double Bottom, double Left);
