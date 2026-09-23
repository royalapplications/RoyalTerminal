// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Pixel geometry of a Unicode placeholder fragment inside an aspect-fitted image.</summary>
internal readonly record struct TerminalKittyPlaceholderGeometry(
    uint OffsetX, uint OffsetY, uint SourceX, uint SourceY,
    uint SourceWidth, uint SourceHeight, uint Width, uint Height)
{
    /// <summary>
    /// Fits the entire image into the virtual grid, centers it and clips the run to
    /// the actual image. Virtual placements ignore ordinary source crop and offsets.
    /// Mirrors Ghostty graphics_unicode.Placement.renderPlacement and its u16 grid limit.
    /// </summary>
    internal static bool TryProject(in TerminalKittyPlaceholderRun run,
        uint imageWidth, uint imageHeight, uint requestedColumns, uint requestedRows,
        uint cellWidth, uint cellHeight, out TerminalKittyPlaceholderGeometry geometry)
    {
        geometry = default;
        if (imageWidth == 0 || imageHeight == 0 || cellWidth == 0 || cellHeight == 0 || run.Width == 0)
            return false;
        ulong columns = requestedColumns == 0 ? ((ulong)imageWidth + cellWidth - 1) / cellWidth : requestedColumns;
        ulong rows = requestedRows == 0 ? ((ulong)imageHeight + cellHeight - 1) / cellHeight : requestedRows;
        if (columns > ushort.MaxValue || rows > ushort.MaxValue ||
            columns * cellWidth > uint.MaxValue || rows * cellHeight > uint.MaxValue)
            return false;

        double gridWidth = columns * cellWidth;
        double gridHeight = rows * cellHeight;
        double scale = Math.Min(gridWidth / imageWidth, gridHeight / imageHeight);
        double padX = (gridWidth - imageWidth * scale) / 2;
        double padY = (gridHeight - imageHeight * scale) / 2;
        double runX = (double)run.ImageColumn * cellWidth;
        double runY = (double)run.ImageRow * cellHeight;
        double left = Math.Max(runX, padX);
        double top = Math.Max(runY, padY);
        double right = Math.Min(runX + (double)run.Width * cellWidth, padX + imageWidth * scale);
        double bottom = Math.Min(runY + cellHeight, padY + imageHeight * scale);
        if (right <= left || bottom <= top) return false;

        geometry = new(Round(left - runX), Round(top - runY),
            Round((left - padX) / scale), Round((top - padY) / scale),
            Round((right - left) / scale), Round((bottom - top) / scale),
            Round(right - left), Round(bottom - top));
        return geometry.Width > 0 && geometry.Height > 0;
    }

    private static uint Round(double value)
        => (uint)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, uint.MaxValue);
}
