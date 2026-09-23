// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Glyphs;

/// <summary>A validated, owned TrueType simple-glyph outline in Y-up design coordinates.</summary>
public sealed class TerminalGlyphOutline
{
    private readonly ushort[] _contours;
    private readonly TerminalGlyphPoint[] _points;

    internal TerminalGlyphOutline(ushort[] contours, TerminalGlyphPoint[] points)
    {
        _contours = contours;
        _points = points;
    }

    /// <summary>Inclusive final point indices, strictly increasing in contour order.</summary>
    public ReadOnlySpan<ushort> ContourEnds => _contours;

    /// <summary>All contour points. The outline does not retain the input payload.</summary>
    public ReadOnlySpan<TerminalGlyphPoint> Points => _points;

    /// <summary>Gets the points belonging to one contour without allocating.</summary>
    public ReadOnlySpan<TerminalGlyphPoint> GetContour(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _contours.Length);
        int start = index == 0 ? 0 : _contours[index - 1] + 1;
        return _points.AsSpan(start, _contours[index] + 1 - start);
    }
}

/// <summary>A TrueType point; off-curve points are quadratic Bézier control points.</summary>
/// <param name="X">Horizontal design coordinate.</param>
/// <param name="Y">Vertical design coordinate, positive upwards.</param>
/// <param name="OnCurve">Whether the point lies on the outline.</param>
public readonly record struct TerminalGlyphPoint(int X, int Y, bool OnCurve);

/// <summary>Glyph Protocol rejection categories for a decoded binary glyf payload.</summary>
public enum TerminalGlyphDecodeError
{
    /// <summary>The payload was decoded successfully.</summary>
    None,
    /// <summary>The header, contours, flags or coordinate data are invalid or truncated.</summary>
    MalformedPayload,
    /// <summary>Composite glyphs are not permitted by Glyph Protocol.</summary>
    CompositeUnsupported,
    /// <summary>Hinting instructions are not permitted by Glyph Protocol.</summary>
    HintingUnsupported,
    /// <summary>The payload or expanded outline exceeds the protocol's resource bound.</summary>
    PayloadTooLarge,
}
