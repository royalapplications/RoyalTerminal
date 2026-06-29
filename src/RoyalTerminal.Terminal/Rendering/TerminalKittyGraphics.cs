// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Shared Kitty Graphics render snapshot model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Z-layer for a Kitty Graphics placement.
/// </summary>
public enum TerminalKittyImageLayer : byte
{
    /// <summary>Draw below terminal cell backgrounds.</summary>
    BelowBackground = 0,

    /// <summary>Draw above backgrounds but below text.</summary>
    BelowText = 1,

    /// <summary>Draw above text.</summary>
    AboveText = 2,
}

/// <summary>
/// Describes how a Kitty Graphics placement destination scales when renderer cell metrics differ.
/// </summary>
public enum TerminalKittyImagePlacementScaleMode : byte
{
    /// <summary>Do not scale destination pixels.</summary>
    None = 0,

    /// <summary>Scale both axes from the column cell metric.</summary>
    Columns = 1,

    /// <summary>Scale both axes from the row cell metric.</summary>
    Rows = 2,

    /// <summary>Scale width from column cells and height from row cells.</summary>
    ColumnsAndRows = 3,
}

/// <summary>
/// Snapshot of a decoded Kitty image payload.
/// </summary>
public sealed class TerminalKittyImageSource
{
    /// <summary>
    /// Creates a decoded Kitty image snapshot.
    /// </summary>
    public TerminalKittyImageSource(
        int imageId,
        int widthPx,
        int heightPx,
        byte[] rgbaPixels)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(imageId, 0);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        ArgumentNullException.ThrowIfNull(rgbaPixels);

        ImageId = imageId;
        WidthPx = widthPx;
        HeightPx = heightPx;
        RgbaPixels = rgbaPixels;
        ContentFingerprint = TerminalImageContentHash.HashBytes(rgbaPixels);
    }

    /// <summary>Stable Kitty image id.</summary>
    public int ImageId { get; }

    /// <summary>Decoded image width in pixels.</summary>
    public int WidthPx { get; }

    /// <summary>Decoded image height in pixels.</summary>
    public int HeightPx { get; }

    /// <summary>Decoded RGBA pixel payload.</summary>
    public byte[] RgbaPixels { get; }

    /// <summary>Stable fingerprint of the decoded RGBA pixel payload.</summary>
    public ulong ContentFingerprint { get; }
}

/// <summary>
/// Snapshot of a Kitty image placement resolved into viewport coordinates.
/// </summary>
public sealed class TerminalKittyImagePlacement
{
    /// <summary>
    /// Creates a viewport-relative Kitty image placement snapshot.
    /// </summary>
    public TerminalKittyImagePlacement(
        int imageId,
        TerminalKittyImageLayer layer,
        int viewportColumn,
        int viewportRow,
        int xOffsetPx,
        int yOffsetPx,
        int widthPx,
        int heightPx,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int cellWidthPx = 0,
        int cellHeightPx = 0,
        TerminalKittyImagePlacementScaleMode scaleMode = TerminalKittyImagePlacementScaleMode.None)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(imageId, 0);
        ImageId = imageId;
        Layer = layer;
        ViewportColumn = viewportColumn;
        ViewportRow = viewportRow;
        XOffsetPx = xOffsetPx;
        YOffsetPx = yOffsetPx;
        WidthPx = widthPx;
        HeightPx = heightPx;
        SourceX = sourceX;
        SourceY = sourceY;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        CellWidthPx = Math.Max(0, cellWidthPx);
        CellHeightPx = Math.Max(0, cellHeightPx);
        ScaleMode = scaleMode;
    }

    /// <summary>Referenced image id.</summary>
    public int ImageId { get; }

    /// <summary>Draw layer.</summary>
    public TerminalKittyImageLayer Layer { get; }

    /// <summary>Viewport-relative starting column.</summary>
    public int ViewportColumn { get; }

    /// <summary>Viewport-relative starting row. May be negative for partial top clipping.</summary>
    public int ViewportRow { get; }

    /// <summary>Pixel offset from the left edge of the starting cell.</summary>
    public int XOffsetPx { get; }

    /// <summary>Pixel offset from the top edge of the starting cell.</summary>
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

    /// <summary>Cell width in pixels at placement calculation time, or zero when unspecified.</summary>
    public int CellWidthPx { get; }

    /// <summary>Cell height in pixels at placement calculation time, or zero when unspecified.</summary>
    public int CellHeightPx { get; }

    /// <summary>Destination scaling behavior for placement-time cell metrics.</summary>
    public TerminalKittyImagePlacementScaleMode ScaleMode { get; }
}
