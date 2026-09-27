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

    /// <summary>
    /// Retains tracked placement recipes, including images outside the viewport.
    /// Pixel-only updates preserve immutable projection/placeholder caches. Anchor
    /// moves and cell edits are still detected when the next frame is projected.
    /// </summary>
    internal void ReplaceAnchoredKittyGraphics(
        IReadOnlyList<TerminalKittyImageSource> images,
        IReadOnlyList<TerminalKittyAnchoredPlacement> placements,
        IReadOnlyList<TerminalKittyPlaceholderTarget> targets,
        IReadOnlyList<TerminalKittyRelativePlacement> relatives,
        uint cellWidth, uint cellHeight)
    {
        ThrowIfSnapshotMutationFailed();
        if (MatchesAnchoredKittyPlacements(placements) && MatchesKittyImageDimensions(images) &&
            MatchesKittyPlaceholderScene(targets, relatives, cellWidth, cellHeight))
        {
            // Preserve projection caches while publishing all pixel replacements
            // together. Registry scratch is reused, not shared with COW readers.
            PublishKittyImages(PrepareKittyImages(images));
            InvalidateViewportCore();
            return;
        }

        TerminalKittyAnchoredPlacement[] preparedPlacements = new TerminalKittyAnchoredPlacement[placements.Count];
        for (int i = 0; i < placements.Count; i++) preparedPlacements[i] = placements[i];
        MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.KittyPlacementsPrepared);
        // The publisher supplies paint-ordered recipes. Keep that order for
        // comparison and preserve the existing ordering of equal-z placements.
        TerminalKittyPlaceholderScene? scene = CreateKittyPlaceholderScene(targets, relatives, [], cellWidth, cellHeight);
        MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.KittyScenePrepared);
        Dictionary<int, TerminalKittyImageSource> preparedImages = PrepareKittyImages(images);
        PublishKittyImages(preparedImages);
        _kittyPlacements = [];
        _kittyAnchoredPlacements = preparedPlacements;
        _kittyPlaceholderScene = scene;
        _kittyPlaceholderRuns = null;
        _kittyProjectionState = null;
        InvalidateViewportCore();
    }

    private bool MatchesAnchoredKittyPlacements(IReadOnlyList<TerminalKittyAnchoredPlacement> placements)
    {
        if (_kittyAnchoredPlacements is null || _kittyAnchoredPlacements.Length != placements.Count) return false;
        for (int i = 0; i < placements.Count; i++)
        {
            TerminalKittyAnchoredPlacement left = _kittyAnchoredPlacements[i], right = placements[i];
            if (!ReferenceEquals(left.Anchor, right.Anchor) || left.ColumnOffset != right.ColumnOffset ||
                left.RowOffset != right.RowOffset || left.Columns != right.Columns || left.Rows != right.Rows ||
                !TerminalKittyImagePlacement.GeometryEquals(left.Geometry, right.Geometry)) return false;
        }
        return true;
    }

    private bool MatchesKittyImageDimensions(IReadOnlyList<TerminalKittyImageSource> images)
    {
        if (_kittyImagesById.Count != images.Count) return false;
        for (int i = 0; i < images.Count; i++)
        {
            TerminalKittyImageSource image = images[i];
            // Placeholder geometry also depends on source dimensions, even
            // when the virtual placement's ID and cell extent are unchanged.
            if (!_kittyImagesById.TryGetValue(image.ImageId, out TerminalKittyImageSource? prior) ||
                prior.WidthPx != image.WidthPx || prior.HeightPx != image.HeightPx) return false;
        }
        return true;
    }

    private void RefreshKittyProjection()
    {
        if (_kittyAnchoredPlacements is not { Length: > 0 } && _kittyPlaceholderScene is null) return;
        KittyProjectionState state = new(_anchorRevision, ViewportTopAbsoluteRow,
            Columns, ViewportRows, _alternateBufferActive);
        TerminalKittyLocatedPlaceholder[]? runs = _kittyPlaceholderScene is not null
            ? PreparePlaceholderRuns() : _kittyPlaceholderRuns;
        bool runsChanged = !ReferenceEquals(runs, _kittyPlaceholderRuns);
        if (_kittyProjectionState == state && !runsChanged) return;

        List<TerminalKittyImagePlacement> visible = new(_kittyAnchoredPlacements?.Length ?? 0);
        if (_kittyPlaceholderScene is { } scene) visible.AddRange(scene.Fixed);
        foreach (TerminalKittyAnchoredPlacement placement in _kittyAnchoredPlacements ?? [])
        {
            if (!TryResolveAnchor(placement.Anchor, out TerminalGridPosition origin)) continue;
            long column = origin.Column + placement.ColumnOffset;
            long row = origin.Row + placement.RowOffset - state.ViewportTop;
            AppendKittyProjection(visible, column, row, placement.Columns, placement.Rows, placement.Geometry);
        }
        if (_kittyPlaceholderScene is { } placeholders) AppendPlaceholderProjection(visible, placeholders, runs);
        visible.Sort(TerminalKittyImagePlacement.ComparePaintOrder);

        // Replace the immutable projection instead of changing an array retained
        // by a previous frame or a copy-on-write screen.
        TerminalKittyImagePlacement[] prepared = visible.ToArray();
        MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.KittyProjectionPrepared);
        _kittyPlacements = prepared;
        _kittyPlaceholderRuns = runs;
        _kittyProjectionState = state;
    }

    private void AppendKittyProjection(List<TerminalKittyImagePlacement> visible, long column, long row,
        uint columns, uint rows, TerminalKittyImagePlacement geometry)
    {
        if (row + rows <= 0 || row >= ViewportRows || column + columns <= 0 || column >= Columns) return;
        visible.Add(new(geometry.ImageId, geometry.Layer,
            (int)Math.Clamp(column, int.MinValue, int.MaxValue), (int)Math.Clamp(row, int.MinValue, int.MaxValue),
            geometry.XOffsetPx, geometry.YOffsetPx, geometry.WidthPx, geometry.HeightPx,
            geometry.SourceX, geometry.SourceY, geometry.SourceWidth, geometry.SourceHeight,
            geometry.CellWidthPx, geometry.CellHeightPx, geometry.ScaleMode, geometry.ZIndex));
    }

    private readonly record struct KittyProjectionState(
        long AnchorRevision, int ViewportTop, int Columns, int Rows, bool Alternate);
}
