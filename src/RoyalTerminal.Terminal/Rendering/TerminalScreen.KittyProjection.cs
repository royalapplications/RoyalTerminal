// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Immutable placement recipe. The anchor is resolved against the receiving screen,
/// so a synchronized-output copy never observes another screen's mutable positions.
/// </summary>
internal readonly record struct TerminalKittyAnchoredPlacement(
    TerminalScreenAnchor Anchor,
    long ColumnOffset,
    long RowOffset,
    uint Columns,
    uint Rows,
    TerminalKittyImagePlacement Geometry);

public sealed partial class TerminalScreen
{
    private TerminalKittyAnchoredPlacement[]? _kittyAnchoredPlacements;
    private KittyProjectionState? _kittyProjectionState;

    /// <summary>Retains tracked placement recipes, including images outside the viewport.</summary>
    internal void ReplaceAnchoredKittyGraphics(
        IReadOnlyList<TerminalKittyImageSource> images,
        IReadOnlyList<TerminalKittyAnchoredPlacement> placements)
    {
        ReplaceKittyGraphics(images, null);
        _kittyAnchoredPlacements = new TerminalKittyAnchoredPlacement[placements.Count];
        for (int i = 0; i < placements.Count; i++) _kittyAnchoredPlacements[i] = placements[i];
        Array.Sort(_kittyAnchoredPlacements, static (left, right) =>
            TerminalKittyImagePlacement.ComparePaintOrder(left.Geometry, right.Geometry));
    }

    private void RefreshKittyProjection()
    {
        if (_kittyAnchoredPlacements is not { Length: > 0 } anchored) return;
        KittyProjectionState state = new(_anchorRevision, ViewportTopAbsoluteRow,
            Columns, ViewportRows, _alternateBufferActive);
        if (_kittyProjectionState == state) return;

        List<TerminalKittyImagePlacement> visible = new(anchored.Length);
        foreach (TerminalKittyAnchoredPlacement placement in anchored)
        {
            if (!TryResolveAnchor(placement.Anchor, out TerminalGridPosition origin)) continue;
            long column = origin.Column + placement.ColumnOffset;
            long row = origin.Row + placement.RowOffset - state.ViewportTop;
            if (row + placement.Rows <= 0 || row >= ViewportRows ||
                column + placement.Columns <= 0 || column >= Columns) continue;

            TerminalKittyImagePlacement geometry = placement.Geometry;
            visible.Add(new(geometry.ImageId, geometry.Layer,
                (int)Math.Clamp(column, int.MinValue, int.MaxValue),
                (int)Math.Clamp(row, int.MinValue, int.MaxValue),
                geometry.XOffsetPx, geometry.YOffsetPx, geometry.WidthPx, geometry.HeightPx,
                geometry.SourceX, geometry.SourceY, geometry.SourceWidth, geometry.SourceHeight,
                geometry.CellWidthPx, geometry.CellHeightPx, geometry.ScaleMode, geometry.ZIndex));
        }

        // Replace the immutable projection instead of changing an array retained
        // by a previous frame or a copy-on-write screen.
        _kittyPlacements = visible.ToArray();
        _kittyProjectionState = state;
    }

    private readonly record struct KittyProjectionState(
        long AnchorRevision, int ViewportTop, int Columns, int Rows, bool Alternate);
}
