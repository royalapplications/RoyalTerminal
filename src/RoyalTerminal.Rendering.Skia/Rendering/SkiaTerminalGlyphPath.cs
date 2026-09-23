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
    {
        ArgumentNullException.ThrowIfNull(outline);
        SKPath path = new() { FillType = SKPathFillType.Winding };
        try
        {
            for (int i = 0; i < outline.ContourEnds.Length; i++) AppendContour(path, outline.GetContour(i));
            return path;
        }
        catch
        {
            path.Dispose();
            throw;
        }
    }

    private static void AppendContour(SKPath path, ReadOnlySpan<TerminalGlyphPoint> points)
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
    }

    private static SKPoint Point(TerminalGlyphPoint point) => new(point.X, point.Y);

    private static SKPoint Midpoint(SKPoint left, SKPoint right) =>
        new((left.X + right.X) * 0.5f, (left.Y + right.Y) * 0.5f);
}
