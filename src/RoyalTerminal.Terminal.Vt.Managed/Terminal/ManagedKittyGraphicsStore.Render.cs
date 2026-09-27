// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

internal sealed partial class ManagedKittyGraphicsStore
{
    private PublicationScratch? _publication;

    /// <summary>Publishes immutable placement recipes for viewport and anchor projection.</summary>
    internal void Publish(TerminalScreen screen, uint cellWidth, uint cellHeight)
    {
        ReapPrunedPlacements(screen);
        PublicationScratch scratch = _publication ??= new();
        try
        {
            PublishCore(screen, cellWidth, cellHeight, scratch);
        }
        finally
        {
            // Match Ghostty's retained placement capacity, but never retain image
            // payloads or expose mutable scratch to a published/COW screen.
            scratch.Clear();
        }
    }

    private void PublishCore(TerminalScreen screen, uint cellWidth, uint cellHeight, PublicationScratch scratch)
    {
        List<TerminalKittyImageSource> images = scratch.Images;
        List<TerminalKittyAnchoredPlacement> placements = scratch.Placements;
        List<TerminalKittyPlaceholderTarget> targets = scratch.Targets;
        List<TerminalKittyRelativePlacement> relatives = scratch.Relatives;
        HashSet<uint> included = scratch.Included;
        bool hasVirtual = false;
        foreach (Placement value in _placements.Values) hasVirtual |= value.Virtual;
        Dictionary<Placement, PlacementKey> rootKeys = scratch.RootKeys;
        if (hasVirtual)
        {
            foreach ((PlacementKey key, Placement placement) in _placements)
            {
                rootKeys[placement] = key;
                targets.Add(new(ToProjectionKey(key), placement.Virtual, placement.Options.Columns, placement.Options.Rows));
                if (_images.TryGetValue(key.ImageId, out Image? image) && included.Add(image.Id)) images.Add(image.Source);
            }
        }
        foreach ((PlacementKey key, Placement placement) in _placements)
        {
            if (placement.Virtual || !_images.TryGetValue(key.ImageId, out Image? image) ||
                !TryResolveRoot(screen, placement, out Placement? root, out long dx, out long dy) ||
                root is null || !root.Virtual &&
                (root.Anchor is null || !screen.TryResolveAnchor(root.Anchor, out _)))
            {
                continue;
            }

            ManagedKittyPlacementGeometry geometry = placement.Options.Calculate(
                (uint)image.Animation.Width,
                (uint)image.Animation.Height, cellWidth, cellHeight);
            if (geometry.SourceWidth == 0 || geometry.SourceHeight == 0 ||
                geometry.Width == 0 || geometry.Height == 0) continue;

            if (included.Add(image.Id)) images.Add(image.Source);
            TerminalKittyImagePlacement renderGeometry = placement.GetRenderGeometry(image.Id, geometry, cellWidth, cellHeight);
            if (root.Virtual)
                relatives.Add(new(ToProjectionKey(rootKeys[root]), dx, dy, geometry.Columns, geometry.Rows, renderGeometry));
            else
                placements.Add(new(root.Anchor!, dx, dy, geometry.Columns, geometry.Rows, renderGeometry));
        }
        placements.Sort(static (left, right) =>
            TerminalKittyImagePlacement.ComparePaintOrder(left.Geometry, right.Geometry));
        screen.ReplaceAnchoredKittyGraphics(images, placements, targets, relatives, cellWidth, cellHeight);
    }

    private static TerminalKittyPlacementKey ToProjectionKey(PlacementKey key) => new(key.ImageId, key.Id, key.Internal);

    private static int Saturate(uint value) => (int)Math.Min(value, int.MaxValue);

    internal sealed partial class Placement
    {
        private RenderGeometryKey _renderKey;
        private TerminalKittyImagePlacement? _renderGeometry;

        internal TerminalKittyImagePlacement GetRenderGeometry(uint imageId, ManagedKittyPlacementGeometry geometry,
            uint cellWidth, uint cellHeight)
        {
            RenderGeometryKey key = new(imageId, geometry, Options.Columns, Options.Rows, Options.Z, cellWidth, cellHeight);
            if (_renderGeometry is not null && key == _renderKey) return _renderGeometry;
            int z = Options.Z;
            TerminalKittyImageLayer layer = z < int.MinValue / 2
                ? TerminalKittyImageLayer.BelowBackground
                : z < 0 ? TerminalKittyImageLayer.BelowText : TerminalKittyImageLayer.AboveText;
            TerminalKittyImagePlacementScaleMode scaleMode = (Options.Columns > 0, Options.Rows > 0) switch
            {
                (true, true) => TerminalKittyImagePlacementScaleMode.ColumnsAndRows,
                (true, false) => TerminalKittyImagePlacementScaleMode.Columns,
                (false, true) => TerminalKittyImagePlacementScaleMode.Rows,
                _ => TerminalKittyImagePlacementScaleMode.None,
            };
            TerminalKittyImagePlacement result = new(
                unchecked((int)imageId), layer, 0, 0,
                Saturate(geometry.OffsetX), Saturate(geometry.OffsetY),
                Saturate(geometry.Width), Saturate(geometry.Height),
                Saturate(geometry.SourceX), Saturate(geometry.SourceY),
                Saturate(geometry.SourceWidth), Saturate(geometry.SourceHeight),
                scaleMode is TerminalKittyImagePlacementScaleMode.Columns or TerminalKittyImagePlacementScaleMode.ColumnsAndRows ? Saturate(cellWidth) : 0,
                scaleMode is TerminalKittyImagePlacementScaleMode.Rows or TerminalKittyImagePlacementScaleMode.ColumnsAndRows ? Saturate(cellHeight) : 0,
                scaleMode, z);
            _renderKey = key;
            return _renderGeometry = result;
        }

        private readonly record struct RenderGeometryKey(uint ImageId, ManagedKittyPlacementGeometry Geometry,
            uint Columns, uint Rows, int Z, uint CellWidth, uint CellHeight);
    }

    // Each store, including a transactional copy, owns its own scratch. Reset
    // drops this owner so a retired large scene does not keep its capacity alive.
    private sealed class PublicationScratch
    {
        internal readonly List<TerminalKittyImageSource> Images = [];
        internal readonly List<TerminalKittyAnchoredPlacement> Placements = [];
        internal readonly List<TerminalKittyPlaceholderTarget> Targets = [];
        internal readonly List<TerminalKittyRelativePlacement> Relatives = [];
        internal readonly HashSet<uint> Included = [];
        internal readonly Dictionary<Placement, PlacementKey> RootKeys = [];

        internal void Clear()
        {
            Images.Clear();
            Placements.Clear();
            Targets.Clear();
            Relatives.Clear();
            Included.Clear();
            RootKeys.Clear();
        }
    }
}
