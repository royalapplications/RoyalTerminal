// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Ghostty-compatible minimum grid sizes and pixel-aligned host resize geometry.</summary>
public static class TerminalWindowResizeGeometry
{
    /// <summary>
    /// Computes the terminal control's requested size in device-independent units,
    /// including configured padding. Positive dimensions have minima of 40 columns
    /// and 10 rows and round up to physical pixels. Zero dimensions remain zero,
    /// meaning preserve the corresponding host size. All metrics are in DIPs except
    /// the positive physical-pixels-per-DIP <paramref name="scale"/>.
    /// </summary>
    /// <returns>False for an all-zero request, invalid metrics, or overflow.</returns>
    public static bool TryResolve(TerminalWindowResizeRequest request, double cellWidth, double cellHeight,
        double horizontalPadding, double verticalPadding, double scale, out double width, out double height)
    {
        width = height = 0;
        if (request is { Columns: 0, Rows: 0 } || !PositiveFinite(cellWidth) || !PositiveFinite(cellHeight) ||
            !PositiveFinite(scale) || !double.IsFinite(horizontalPadding) || horizontalPadding < 0 ||
            !double.IsFinite(verticalPadding) || verticalPadding < 0) return false;
        double requestedWidth = request.Columns == 0 ? 0 :
            Math.Ceiling((Math.Max(40, (int)request.Columns) * cellWidth + horizontalPadding) * scale) / scale;
        double requestedHeight = request.Rows == 0 ? 0 :
            Math.Ceiling((Math.Max(10, (int)request.Rows) * cellHeight + verticalPadding) * scale) / scale;
        if (!double.IsFinite(requestedWidth) || !double.IsFinite(requestedHeight)) return false;
        width = requestedWidth; height = requestedHeight;
        return true;
    }

    private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;
}
