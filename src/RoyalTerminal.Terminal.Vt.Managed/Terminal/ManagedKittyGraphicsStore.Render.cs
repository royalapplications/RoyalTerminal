// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

internal sealed partial class ManagedKittyGraphicsStore
{
    /// <summary>Publishes visible, pin-backed placements using the active viewport.</summary>
    internal void Publish(TerminalScreen screen, uint cellWidth, uint cellHeight)
    {
        ReapPrunedPlacements(screen);
        List<TerminalKittyImageSource> images = [];
        List<TerminalKittyImagePlacement> placements = [];
        HashSet<uint> included = [];
        int viewportTop = screen.ViewportTopAbsoluteRow;
        foreach ((PlacementKey key, Placement placement) in _placements)
        {
            if (!_images.TryGetValue(key.ImageId, out Image? image) ||
                !TryResolveRoot(screen, placement, out Placement? root, out long dx, out long dy) ||
                root?.Anchor is not TerminalScreenAnchor anchor ||
                !screen.TryResolveAnchor(anchor, out TerminalGridPosition origin))
            {
                continue;
            }

            ManagedKittyPlacementGeometry geometry = placement.Options.Calculate(
                (uint)image.Animation.CurrentImage.Width,
                (uint)image.Animation.CurrentImage.Height, cellWidth, cellHeight);
            if (geometry.SourceWidth == 0 || geometry.SourceHeight == 0 ||
                geometry.Width == 0 || geometry.Height == 0) continue;

            long column = (long)origin.Column + dx;
            long row = (long)origin.Row + dy - viewportTop;
            long bottom = row + (long)((geometry.OffsetY + (ulong)geometry.Height + Math.Max(1u, cellHeight) - 1) /
                Math.Max(1u, cellHeight));
            long right = column + (long)((geometry.OffsetX + (ulong)geometry.Width + Math.Max(1u, cellWidth) - 1) /
                Math.Max(1u, cellWidth));
            if (bottom <= 0 || row >= screen.ViewportRows || right <= 0 || column >= screen.Columns)
                continue;

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
            placements.Add(new(
                unchecked((int)image.Id), layer,
                Saturate(column), Saturate(row),
                Saturate(geometry.OffsetX), Saturate(geometry.OffsetY),
                Saturate(geometry.Width), Saturate(geometry.Height),
                Saturate(geometry.SourceX), Saturate(geometry.SourceY),
                Saturate(geometry.SourceWidth), Saturate(geometry.SourceHeight),
                scaleMode is TerminalKittyImagePlacementScaleMode.Columns or TerminalKittyImagePlacementScaleMode.ColumnsAndRows ? Saturate(cellWidth) : 0,
                scaleMode is TerminalKittyImagePlacementScaleMode.Rows or TerminalKittyImagePlacementScaleMode.ColumnsAndRows ? Saturate(cellHeight) : 0,
                scaleMode));
        }
        screen.ReplaceKittyGraphics(images, placements);
    }

    private static int Saturate(uint value) => (int)Math.Min(value, int.MaxValue);
    private static int Saturate(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
}
