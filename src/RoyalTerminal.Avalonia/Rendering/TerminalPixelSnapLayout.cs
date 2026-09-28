// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Pixel-exact coverage for a retained terminal image under an axis-aligned
/// compositor transform. Input and terminal grid coordinates remain logical.
/// </summary>
internal readonly record struct TerminalPixelSnapLayout(int Width, int Height, SKRect Destination)
{
    internal static bool TryCreate(
        float logicalWidth,
        float logicalHeight,
        float scaleX,
        float scaleY,
        SKMatrix matrix,
        out TerminalPixelSnapLayout layout)
    {
        layout = default;
        // A clip-inferred DPI, reflection, rotation, skew or perspective cannot
        // use this mapping: the raster scale must match the compositor exactly.
        if (matrix.SkewX != 0 || matrix.SkewY != 0 ||
            matrix.Persp0 != 0 || matrix.Persp1 != 0 || matrix.Persp2 != 1 ||
            matrix.ScaleX != scaleX || matrix.ScaleY != scaleY ||
            !TrySnapAxis(matrix.TransX, logicalWidth, scaleX, out int width, out float left) ||
            !TrySnapAxis(matrix.TransY, logicalHeight, scaleY, out int height, out float top))
        {
            return false;
        }

        layout = new TerminalPixelSnapLayout(width, height,
            new SKRect(left, top, left + width / scaleX, top + height / scaleY));
        return true;
    }

    private static bool TrySnapAxis(
        double deviceOrigin,
        double logicalLength,
        double scale,
        out int pixels,
        out float offset)
    {
        pixels = 0;
        offset = 0;
        if (!double.IsFinite(deviceOrigin) || !double.IsFinite(logicalLength) ||
            logicalLength < 0 || !double.IsFinite(scale) || scale <= 0)
        {
            return false;
        }

        // Ghostty GTK scale.zig (#14269): snap BOTH edges, not ceil(size * DPI).
        // The Skia translation is already in device pixels; don't scale it again.
        double start = Math.Round(deviceOrigin, MidpointRounding.AwayFromZero);
        double end = Math.Round(deviceOrigin + logicalLength * scale, MidpointRounding.AwayFromZero);
        double length = end - start;
        double logicalOffset = (start - deviceOrigin) / scale;
        double logicalEnd = logicalOffset + length / scale;
        if (!double.IsFinite(length) || length < 0 || length > int.MaxValue ||
            !float.IsFinite((float)logicalOffset) || !float.IsFinite((float)logicalEnd))
        {
            return false;
        }

        pixels = (int)length;
        offset = (float)logicalOffset;
        return true;
    }
}
