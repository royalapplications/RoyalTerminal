// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Protocol-neutral terminal raster image model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Identifies the terminal image protocol that produced a raster image.
/// </summary>
public enum TerminalRasterImageProtocol : byte
{
    /// <summary>Sixel DCS image payload.</summary>
    Sixel = 0,
}

/// <summary>
/// Draw layer for a protocol-neutral terminal raster image.
/// </summary>
public enum TerminalRasterImageLayer : byte
{
    /// <summary>Draw below terminal cell backgrounds.</summary>
    BelowBackground = 0,

    /// <summary>Draw above backgrounds but below text.</summary>
    BelowText = 1,

    /// <summary>Draw above text.</summary>
    AboveText = 2,
}

/// <summary>
/// Decoded protocol-neutral terminal raster image payload.
/// </summary>
public sealed class TerminalRasterImageSource
{
    /// <summary>
    /// Creates a decoded terminal raster image payload.
    /// </summary>
    public TerminalRasterImageSource(
        int imageId,
        TerminalRasterImageProtocol protocol,
        int widthPx,
        int heightPx,
        byte[] rgbaPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        ArgumentNullException.ThrowIfNull(rgbaPixels);

        int expectedLength = checked(widthPx * heightPx * 4);
        if (rgbaPixels.Length != expectedLength)
        {
            throw new ArgumentException("RGBA payload length does not match image dimensions.", nameof(rgbaPixels));
        }

        ImageId = imageId;
        Protocol = protocol;
        WidthPx = widthPx;
        HeightPx = heightPx;
        RgbaPixels = rgbaPixels;
    }

    /// <summary>Stable image id.</summary>
    public int ImageId { get; }

    /// <summary>Source image protocol.</summary>
    public TerminalRasterImageProtocol Protocol { get; }

    /// <summary>Decoded image width in pixels.</summary>
    public int WidthPx { get; }

    /// <summary>Decoded image height in pixels.</summary>
    public int HeightPx { get; }

    /// <summary>Decoded RGBA pixel payload.</summary>
    public byte[] RgbaPixels { get; }
}

/// <summary>
/// Placement of a terminal raster image anchored to an absolute terminal row.
/// </summary>
public sealed class TerminalRasterImagePlacement
{
    /// <summary>
    /// Creates a terminal raster image placement.
    /// </summary>
    public TerminalRasterImagePlacement(
        int imageId,
        TerminalRasterImageLayer layer,
        int anchorColumn,
        int anchorRow,
        int xOffsetPx,
        int yOffsetPx,
        int widthPx,
        int heightPx,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int cellWidthPx,
        int cellHeightPx)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentOutOfRangeException.ThrowIfNegative(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegative(heightPx);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceX);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceY);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceHeight);

        ImageId = imageId;
        Layer = layer;
        AnchorColumn = anchorColumn;
        AnchorRow = anchorRow;
        XOffsetPx = xOffsetPx;
        YOffsetPx = yOffsetPx;
        WidthPx = widthPx;
        HeightPx = heightPx;
        SourceX = sourceX;
        SourceY = sourceY;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        CellWidthPx = Math.Max(1, cellWidthPx);
        CellHeightPx = Math.Max(1, cellHeightPx);
    }

    /// <summary>Referenced image id.</summary>
    public int ImageId { get; }

    /// <summary>Draw layer.</summary>
    public TerminalRasterImageLayer Layer { get; }

    /// <summary>Column in the terminal grid where the image is anchored.</summary>
    public int AnchorColumn { get; }

    /// <summary>Absolute terminal row where the image is anchored.</summary>
    public int AnchorRow { get; }

    /// <summary>Pixel offset from the left edge of the anchor cell.</summary>
    public int XOffsetPx { get; }

    /// <summary>Pixel offset from the top edge of the anchor row.</summary>
    public int YOffsetPx { get; }

    /// <summary>Destination width in pixels.</summary>
    public int WidthPx { get; }

    /// <summary>Destination height in pixels.</summary>
    public int HeightPx { get; }

    /// <summary>Source X origin in pixels.</summary>
    public int SourceX { get; }

    /// <summary>Source Y origin in pixels.</summary>
    public int SourceY { get; }

    /// <summary>Source width in pixels.</summary>
    public int SourceWidth { get; }

    /// <summary>Source height in pixels.</summary>
    public int SourceHeight { get; }

    /// <summary>Cell width in pixels at placement time.</summary>
    public int CellWidthPx { get; }

    /// <summary>Cell height in pixels at placement time.</summary>
    public int CellHeightPx { get; }

    /// <summary>Returns a copy anchored to a different absolute row.</summary>
    public TerminalRasterImagePlacement WithAnchorRow(int anchorRow)
        => new(
            ImageId,
            Layer,
            AnchorColumn,
            anchorRow,
            XOffsetPx,
            YOffsetPx,
            WidthPx,
            HeightPx,
            SourceX,
            SourceY,
            SourceWidth,
            SourceHeight,
            CellWidthPx,
            CellHeightPx);

    /// <summary>Returns a copy anchored to a different absolute row and column.</summary>
    public TerminalRasterImagePlacement WithAnchor(int anchorColumn, int anchorRow)
        => new(
            ImageId,
            Layer,
            anchorColumn,
            anchorRow,
            XOffsetPx,
            YOffsetPx,
            WidthPx,
            HeightPx,
            SourceX,
            SourceY,
            SourceWidth,
            SourceHeight,
            CellWidthPx,
            CellHeightPx);
}
