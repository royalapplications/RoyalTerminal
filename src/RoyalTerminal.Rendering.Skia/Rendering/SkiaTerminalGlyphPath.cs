// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Glyphs;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Converts validated glyph outlines to reusable paths without intermediate raster storage.</summary>
internal static class SkiaTerminalGlyphPath
{
    /// <summary>
    /// Builds a caller-owned path in Y-up design coordinates. The renderer supplies
    /// the placement transform and foreground paint when drawing. Contour winding
    /// is preserved, including holes; consecutive off-curve controls imply midpoints.
    /// </summary>
    internal static SKPath Create(TerminalGlyphOutline outline)
        => Create(outline, 0, 0, 1, 1);

    // Normalize in double precision before Skia's float conversion. This retains
    // small details in an outline with large translated design coordinates.
    internal static SKPath CreateNormalized(TerminalGlyphOutline outline)
    {
        TerminalGlyphBounds bounds = outline.Bounds;
        return Create(outline, bounds.MinX, bounds.MinY,
            bounds.Width > 0 ? 1 / bounds.Width : 0, bounds.Height > 0 ? 1 / bounds.Height : 0);
    }

    private static SKPath Create(TerminalGlyphOutline outline, double originX, double originY, double scaleX, double scaleY)
    {
        ArgumentNullException.ThrowIfNull(outline);
        SKPath path = new() { FillType = SKPathFillType.Winding };
        try
        {
            for (int i = 0; i < outline.ContourEnds.Length; i++)
                AppendContour(path, outline.GetContour(i), originX, originY, scaleX, scaleY);
            return path;
        }
        catch
        {
            path.Dispose();
            throw;
        }
    }

    private static void AppendContour(SKPath path, ReadOnlySpan<TerminalGlyphPoint> points,
        double originX, double originY, double scaleX, double scaleY)
    {
        if (points.IsEmpty) return;
        TerminalGlyphPoint first = points[0], last = points[^1];
        int index = 0;
        SKPoint start;
        if (first.OnCurve)
        {
            start = Point(first);
            index = 1;
        }
        else start = last.OnCurve ? Point(last) : Midpoint(Point(last), Point(first));
        path.MoveTo(start);
        while (index < points.Length)
        {
            TerminalGlyphPoint point = points[index];
            if (point.OnCurve)
            {
                path.LineTo(Point(point));
                index++;
                continue;
            }
            TerminalGlyphPoint next = points[(index + 1) % points.Length];
            SKPoint control = Point(point);
            SKPoint end = next.OnCurve ? Point(next) : Midpoint(control, Point(next));
            // Skia represents TrueType quadratics directly; no cubic conversion
            // or temporary point array is required.
            path.QuadTo(control, end);
            index += next.OnCurve ? 2 : 1;
        }
        path.Close();

        SKPoint Point(TerminalGlyphPoint point) => new(
            (float)(((double)point.X - originX) * scaleX), (float)(((double)point.Y - originY) * scaleY));
    }

    private static SKPoint Midpoint(SKPoint left, SKPoint right) =>
        new((left.X + right.X) * 0.5f, (left.Y + right.Y) * 0.5f);
}
