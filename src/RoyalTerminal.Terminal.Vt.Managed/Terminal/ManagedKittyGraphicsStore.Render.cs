// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

internal sealed partial class ManagedKittyGraphicsStore
{
    /// <summary>Publishes immutable placement recipes for viewport and anchor projection.</summary>
    internal void Publish(TerminalScreen screen, uint cellWidth, uint cellHeight)
    {
        ReapPrunedPlacements(screen);
        List<TerminalKittyImageSource> images = [];
        List<TerminalKittyAnchoredPlacement> placements = [];
        HashSet<uint> included = [];
        foreach ((PlacementKey key, Placement placement) in _placements)
        {
            if (!_images.TryGetValue(key.ImageId, out Image? image) ||
                !TryResolveRoot(screen, placement, out Placement? root, out long dx, out long dy) ||
                root?.Anchor is not TerminalScreenAnchor anchor ||
                !screen.TryResolveAnchor(anchor, out _))
            {
                continue;
            }

            ManagedKittyPlacementGeometry geometry = placement.Options.Calculate(
                (uint)image.Animation.CurrentImage.Width,
                (uint)image.Animation.CurrentImage.Height, cellWidth, cellHeight);
            if (geometry.SourceWidth == 0 || geometry.SourceHeight == 0 ||
                geometry.Width == 0 || geometry.Height == 0) continue;

            if (included.Add(image.Id)) images.Add(image.Source);
            int z = placement.Options.Z;
            TerminalKittyImageLayer layer = z < int.MinValue / 2
                ? TerminalKittyImageLayer.BelowBackground
                : z < 0 ? TerminalKittyImageLayer.BelowText : TerminalKittyImageLayer.AboveText;
            TerminalKittyImagePlacementScaleMode scaleMode = (placement.Options.Columns > 0, placement.Options.Rows > 0) switch
            {
                (true, true) => TerminalKittyImagePlacementScaleMode.ColumnsAndRows,
                (true, false) => TerminalKittyImagePlacementScaleMode.Columns,
                (false, true) => TerminalKittyImagePlacementScaleMode.Rows,
                _ => TerminalKittyImagePlacementScaleMode.None,
            };
            TerminalKittyImagePlacement renderGeometry = new(
                unchecked((int)image.Id), layer,
                0, 0,
                Saturate(geometry.OffsetX), Saturate(geometry.OffsetY),
                Saturate(geometry.Width), Saturate(geometry.Height),
                Saturate(geometry.SourceX), Saturate(geometry.SourceY),
                Saturate(geometry.SourceWidth), Saturate(geometry.SourceHeight),
                scaleMode is TerminalKittyImagePlacementScaleMode.Columns or TerminalKittyImagePlacementScaleMode.ColumnsAndRows ? Saturate(cellWidth) : 0,
                scaleMode is TerminalKittyImagePlacementScaleMode.Rows or TerminalKittyImagePlacementScaleMode.ColumnsAndRows ? Saturate(cellHeight) : 0,
                scaleMode, z);
            placements.Add(new(anchor, dx, dy, geometry.Columns, geometry.Rows, renderGeometry));
        }
        screen.ReplaceAnchoredKittyGraphics(images, placements);
    }

    private static int Saturate(uint value) => (int)Math.Min(value, int.MaxValue);
}
