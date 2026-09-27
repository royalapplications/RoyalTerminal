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
        foreach ((uint id, Image _) in _images) changed |= DeleteIfUnused(id);
        return changed;
    }

    internal bool DeleteById(TerminalScreen screen, uint imageId, uint placementId, bool deleteUnused)
    {
        if (imageId == 0 || !_images.ContainsKey(imageId)) return false;
        bool matched = placementId == 0 || _placements.ContainsKey(new(imageId, placementId, false));
        bool changed = DeleteMatchingPlacements(screen,
            new(DeleteKind.Id, ImageId: imageId, PlacementId: placementId), deleteUnused);
        if (deleteUnused && matched)
            changed |= DeleteIfUnused(imageId);
        return changed;
    }

    internal bool DeleteByRange(TerminalScreen screen, uint first, uint last, bool deleteUnused)
    {
        if (last == 0 || first > last) return false;
        bool changed = DeleteMatchingPlacements(screen,
            new(DeleteKind.Range, ImageId: first, LastImageId: last), deleteUnused);
        if (!deleteUnused) return changed;
        foreach ((uint id, Image _) in _images)
            if (id >= first && id <= last) changed |= DeleteIfUnused(id);
        return changed;
    }

    internal bool DeleteByZ(TerminalScreen screen, int z, bool deleteUnused)
        => DeleteMatchingPlacements(screen,
            new(DeleteKind.Z, Z: z), deleteUnused);

    internal bool DeleteAtCell(TerminalScreen screen, int column, int row,
        int? z, uint cellWidth, uint cellHeight, bool deleteUnused)
        => column >= 0 && column < screen.Columns && row >= 0 && row < screen.ViewportRows &&
        DeleteMatchingPlacements(screen, new(DeleteKind.Cell, Column: column, Row: row,
            Z: z, CellWidth: cellWidth, CellHeight: cellHeight), deleteUnused);

    internal bool DeleteByColumn(TerminalScreen screen, int column,
        uint cellWidth, uint cellHeight, bool deleteUnused)
        => DeleteMatchingPlacements(screen, new(DeleteKind.Column, Column: column,
            CellWidth: cellWidth, CellHeight: cellHeight), deleteUnused);

    internal bool DeleteByRow(TerminalScreen screen, int row,
        uint cellWidth, uint cellHeight, bool deleteUnused)
        => row >= 0 && row < screen.ViewportRows && DeleteMatchingPlacements(screen,
            new(DeleteKind.Row, Row: row, CellWidth: cellWidth, CellHeight: cellHeight), deleteUnused);

    internal bool DeleteVisible(TerminalScreen screen, uint cellWidth, uint cellHeight, bool deleteUnused)
        => DeleteMatchingPlacements(screen,
            new(DeleteKind.Visible, CellWidth: cellWidth, CellHeight: cellHeight), deleteUnused);

    private bool DeleteMatchingPlacements(TerminalScreen screen,
        in DeleteFilter filter, bool deleteUnused)
    {
        // Dictionary.Remove preserves its active enumerator on our target
        // runtimes. Like Ghostty, delete matching entries in place, then reap
        // relative descendants. No selection lists, closures or fallible growth
        // are needed before OR after mutation starts.
        bool changed = false;
        foreach ((PlacementKey key, Placement placement) in _placements)
        {
            if (!MatchesDelete(screen, key, placement, filter)) continue;
            changed |= RemovePlacement(screen, key);
            if (deleteUnused) changed |= DeleteIfUnused(key.ImageId);
        }
        return RemoveOrphans(screen, deleteUnused) || changed;
    }

    private bool DeleteIfUnused(uint id)
    {
        if (!_images.TryGetValue(id, out Image? image) || image.PlacementCount != 0) return false;
        _images.Remove(id);
        _storedBytes -= image.QuotaBytes;
        _generation++;
        // Do not call RemoveImage: it reaps orphans without uppercase deletion
        // semantics. The owning placement pass must finish before that cascade.
        return true;
    }

    private bool MatchesDelete(TerminalScreen screen, PlacementKey key, Placement placement, in DeleteFilter filter)
    {
        switch (filter.Kind)
        {
            case DeleteKind.Id:
                return key.ImageId == filter.ImageId && (filter.PlacementId == 0 || !key.Internal && key.Id == filter.PlacementId);
            case DeleteKind.Range:
                return key.ImageId >= filter.ImageId && key.ImageId <= filter.LastImageId;
            case DeleteKind.Z:
                return !placement.Virtual && placement.Options.Z == filter.Z;
            case DeleteKind.Visible:
                if (placement.Anchor is not TerminalScreenAnchor anchor ||
                    !screen.TryResolveAnchor(anchor, out TerminalGridPosition origin)) return false;
                int activeTop = Math.Max(0, screen.TotalRows - screen.ViewportRows);
                if (origin.Row >= activeTop + screen.ViewportRows) return false;
                // Active pins match even with an empty crop. History pins match
                // only if their cell rectangle extends into the active area.
                if (origin.Row >= activeTop) return true;
                if (!_images.TryGetValue(key.ImageId, out Image? image)) return false;
                ManagedKittyPlacementGeometry geometry = placement.Options.Calculate(
                    (uint)image.Animation.Width, (uint)image.Animation.Height, filter.CellWidth, filter.CellHeight);
                return geometry.Columns > 0 && geometry.Rows > 0 && origin.Row + (long)geometry.Rows > activeTop;
        }
        if (filter.Z is int z && placement.Options.Z != z) return false;
        if (!TryGetCellRect(screen, key, placement, filter.CellWidth, filter.CellHeight,
                out long left, out long top, out long right, out long bottom)) return false;
        return filter.Kind switch
        {
            DeleteKind.Cell => filter.Column >= left && filter.Column < right && filter.Row >= top && filter.Row < bottom,
            DeleteKind.Column => filter.Column >= left && filter.Column < right,
            DeleteKind.Row => filter.Row >= top && filter.Row < bottom,
            _ => false,
        };
    }

    private enum DeleteKind : byte { Id, Range, Z, Cell, Column, Row, Visible }
    private readonly record struct DeleteFilter(DeleteKind Kind, uint ImageId = 0, uint PlacementId = 0,
        uint LastImageId = 0, int Column = 0, int Row = 0, int? Z = null, uint CellWidth = 0, uint CellHeight = 0);

    private bool TryGetCellRect(TerminalScreen screen, PlacementKey key, Placement placement,
        uint cellWidth, uint cellHeight, out long left, out long top, out long right, out long bottom)
    {
        left = top = right = bottom = 0;
        if (!_images.TryGetValue(key.ImageId, out Image? image) ||
            // Relative/virtual placements have no independent screen pin for
            // geometric deletion. They follow their parent's deletion instead.
            placement.Anchor is not TerminalScreenAnchor anchor ||
            !screen.TryResolveAnchor(anchor, out TerminalGridPosition origin)) return false;
        ManagedKittyPlacementGeometry geometry = placement.Options.Calculate(
            (uint)image.Animation.Width, (uint)image.Animation.Height,
            cellWidth, cellHeight);
        if (geometry.Columns == 0 || geometry.Rows == 0) return false;
        left = origin.Column;
        top = origin.Row - Math.Max(0, screen.TotalRows - screen.ViewportRows);
        right = Math.Min(screen.Columns, left + geometry.Columns);
        bottom = Math.Min(screen.ViewportRows, top + geometry.Rows);
        return true;
    }
}
