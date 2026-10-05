// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Glyphs;

/// <summary>Pixel placement of the outline's computed bounds, relative to the cell's top-left.</summary>
/// <param name="X">Left edge, in pixels.</param>
/// <param name="Y">Top edge, in pixels.</param>
/// <param name="Width">Horizontal extent.</param>
/// <param name="Height">Vertical extent.</param>
public readonly record struct TerminalGlyphPlacement(double X, double Y, double Width, double Height);

/// <summary>Ghostty-compatible glyph constraints using the renderer's cell grid as face metrics.</summary>
public static class TerminalGlyphGeometry
{
    /// <summary>
    /// Places the visible outline using its authored advance/line-height box.
    /// Uses current Ghostty normalization: advance equals height, cover equals
    /// contain and baseline equals start. Returns false for empty/degenerate
    /// outlines or invalid grid metrics. The caller clips to the actual cell span.
    /// </summary>
    public static bool TryPlace(TerminalGlyphRegistration glyph, double cellWidth, double cellHeight,
        int cellSpan, out TerminalGlyphPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        placement = default;
        TerminalGlyphBounds bounds = glyph.Outline.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || !double.IsFinite(cellWidth) || !double.IsFinite(cellHeight) ||
            cellWidth <= 0 || cellHeight <= 0 || cellSpan is < 1 or > 2) return false;
        TerminalGlyphLayout layout = glyph.Layout;
        bool stretch = layout.Size == TerminalGlyphSize.Stretch;
        // Ghostty constraint.stretch collapses extra-wide faces to one cell.
        int constraintSpan = stretch && cellWidth > .9 * cellHeight ? 1 : cellSpan;
        double scale = cellHeight / glyph.UnitsPerEm;
        double groupWidth = glyph.AdvanceWidth * scale, groupHeight = glyph.LineHeight * scale;
        double sx = 1, sy = 1;
        if (layout.Size is TerminalGlyphSize.Contain or TerminalGlyphSize.Cover or TerminalGlyphSize.Stretch)
        {
            sx = (constraintSpan - layout.Left - layout.Right) * cellWidth / groupWidth;
            sy = (1 - layout.Top - layout.Bottom) * cellHeight / groupHeight;
            if (!stretch) sx = sy = Math.Min(sx, sy);
        }
        groupWidth *= sx;
        groupHeight *= sy;
        double startX = layout.Left * cellWidth;
        double endX = constraintSpan * cellWidth - groupWidth - layout.Right * cellWidth;
        double x = layout.Horizontal switch
        {
            TerminalGlyphAlignment.Start => startX,
            TerminalGlyphAlignment.End => Math.Max(startX, endX),
            _ => Math.Max(startX, (startX + endX) / 2),
        };
        double startY = layout.Bottom * cellHeight;
        double endY = cellHeight - groupHeight - layout.Top * cellHeight;
        double bottom = layout.Vertical switch
        {
            TerminalGlyphAlignment.Start or TerminalGlyphAlignment.Baseline => startY,
            TerminalGlyphAlignment.End => endY,
            _ => (startY + endY) / 2,
        };
        double width = bounds.Width * scale * sx;
        double height = bounds.Height * scale * sy;
        placement = new(x + bounds.MinX * scale * sx,
            cellHeight - bottom - bounds.MinY * scale * sy - height, width, height);
        return true;
    }
}
