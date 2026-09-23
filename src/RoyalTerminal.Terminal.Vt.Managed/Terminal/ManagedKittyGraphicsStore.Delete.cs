// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

internal sealed partial class ManagedKittyGraphicsStore
{
    internal bool ClearScreen(TerminalScreen screen, uint cellWidth, uint cellHeight)
    {
        bool changed = DeleteVisible(screen, cellWidth, cellHeight, deleteUnused: true);
        // Unlike protocol d=A, ED2 also discards images that had no placements.
        foreach (Image image in _images.Values)
            if (image.PlacementCount == 0) changed |= RemoveImage(screen, image.Id);
        return changed;
    }

    internal bool DeleteById(TerminalScreen screen, uint imageId, uint placementId, bool deleteUnused)
    {
        if (imageId == 0) return false;
        bool changed = DeleteMatchingPlacements(screen, (key, _) => key.ImageId == imageId &&
            (placementId == 0 || !key.Internal && key.Id == placementId), deleteUnused);
        if (deleteUnused && (placementId == 0 || changed))
            changed |= DeleteIfUnused(screen, imageId);
        return changed;
    }

    internal bool DeleteByRange(TerminalScreen screen, uint first, uint last, bool deleteUnused)
    {
        if (last == 0 || first > last) return false;
        bool changed = DeleteMatchingPlacements(screen,
            (key, _) => key.ImageId >= first && key.ImageId <= last, deleteUnused);
        if (!deleteUnused) return changed;
        List<uint> unused = [];
        foreach (Image image in _images.Values)
            if (image.Id >= first && image.Id <= last && image.PlacementCount == 0) unused.Add(image.Id);
        foreach (uint id in unused) changed |= RemoveImage(screen, id);
        return changed;
    }

    internal bool DeleteByZ(TerminalScreen screen, int z, bool deleteUnused)
        => DeleteMatchingPlacements(screen,
            (_, placement) => !placement.Virtual && placement.Options.Z == z, deleteUnused);

    internal bool DeleteAtCell(TerminalScreen screen, int column, int row,
        int? z, uint cellWidth, uint cellHeight, bool deleteUnused)
        => DeleteMatchingPlacements(screen, (key, placement) =>
        {
            if (z is int layer && placement.Options.Z != layer) return false;
            return TryGetCellRect(screen, key, placement, cellWidth, cellHeight,
                out long left, out long top, out long right, out long bottom) &&
                column >= left && column < right && row >= top && row < bottom;
        }, deleteUnused);

    internal bool DeleteByColumn(TerminalScreen screen, int column,
        uint cellWidth, uint cellHeight, bool deleteUnused)
        => DeleteMatchingPlacements(screen, (key, placement) =>
            TryGetCellRect(screen, key, placement, cellWidth, cellHeight,
                out long left, out _, out long right, out _) &&
            column >= left && column < right, deleteUnused);

    internal bool DeleteByRow(TerminalScreen screen, int row,
        uint cellWidth, uint cellHeight, bool deleteUnused)
        => DeleteMatchingPlacements(screen, (key, placement) =>
            TryGetCellRect(screen, key, placement, cellWidth, cellHeight,
                out _, out long top, out _, out long bottom) &&
            row >= top && row < bottom, deleteUnused);

    internal bool DeleteVisible(TerminalScreen screen, uint cellWidth, uint cellHeight, bool deleteUnused)
        => DeleteMatchingPlacements(screen, (key, placement) =>
        {
            if (placement.Anchor is not TerminalScreenAnchor anchor ||
                !screen.TryResolveAnchor(anchor, out TerminalGridPosition origin)) return false;
            int activeTop = Math.Max(0, screen.TotalRows - screen.ViewportRows);
            if (origin.Row >= activeTop + screen.ViewportRows) return false;
            // A placement anchored in history can still reach into the active viewport.
            if (!_images.TryGetValue(key.ImageId, out Image? image)) return false;
            ManagedKittyPlacementGeometry geometry = placement.Options.Calculate(
                (uint)image.Animation.CurrentImage.Width, (uint)image.Animation.CurrentImage.Height,
                cellWidth, cellHeight);
            long rows = (geometry.OffsetY + (long)geometry.Height + Math.Max(1u, cellHeight) - 1) /
                Math.Max(1u, cellHeight);
            return origin.Row + rows > activeTop;
        }, deleteUnused);

    private bool DeleteMatchingPlacements(TerminalScreen screen,
        Func<PlacementKey, Placement, bool> predicate, bool deleteUnused)
    {
        List<PlacementKey> selected = [];
        HashSet<uint>? candidates = deleteUnused ? [] : null;
        if (candidates is not null)
            foreach (Image image in _images.Values)
                if (image.PlacementCount > 0) candidates.Add(image.Id);
        foreach ((PlacementKey key, Placement placement) in _placements)
            if (predicate(key, placement)) selected.Add(key);
        foreach (PlacementKey key in selected) RemovePlacement(screen, key);
        RemoveOrphans(screen);
        bool changed = selected.Count > 0;
        if (candidates is not null)
            foreach (uint id in candidates)
                changed |= DeleteIfUnused(screen, id);
        return changed;
    }

    private bool DeleteIfUnused(TerminalScreen screen, uint id)
        => _images.TryGetValue(id, out Image? image) && image.PlacementCount == 0 &&
           RemoveImage(screen, id);

    private bool TryGetCellRect(TerminalScreen screen, PlacementKey key, Placement placement,
        uint cellWidth, uint cellHeight, out long left, out long top, out long right, out long bottom)
    {
        left = top = right = bottom = 0;
        if (!_images.TryGetValue(key.ImageId, out Image? image) ||
            !TryResolveRoot(screen, placement, out Placement? root, out long dx, out long dy) ||
            root?.Anchor is not TerminalScreenAnchor anchor ||
            !screen.TryResolveAnchor(anchor, out TerminalGridPosition origin)) return false;
        ManagedKittyPlacementGeometry geometry = placement.Options.Calculate(
            (uint)image.Animation.CurrentImage.Width, (uint)image.Animation.CurrentImage.Height,
            cellWidth, cellHeight);
        if (geometry.Columns == 0 || geometry.Rows == 0) return false;
        left = origin.Column + dx;
        top = origin.Row + dy - Math.Max(0, screen.TotalRows - screen.ViewportRows);
        right = left + geometry.Columns;
        bottom = top + geometry.Rows;
        return true;
    }
}
