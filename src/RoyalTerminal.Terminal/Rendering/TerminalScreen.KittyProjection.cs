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
    private KittyOverscanProjection? _kittyOverscanProjection;

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
        _kittyOverscanProjection = null;
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

    private TerminalKittyImagePlacement[] RefreshKittyProjection(TerminalRenderOverscan overscan)
    {
        if (_kittyAnchoredPlacements is not { Length: > 0 } && _kittyPlaceholderScene is null) return _kittyPlacements;
        TerminalRenderViewport renderRows = GetRenderViewport(overscan);
        TerminalRenderOverscan captured = renderRows.CapturedOverscan;
        bool extended = captured != default;
        TerminalKittyLocatedPlaceholder[]? oldRuns = extended ? _kittyOverscanProjection?.Runs : _kittyPlaceholderRuns;
        KittyProjectionState? oldState = extended ? _kittyOverscanProjection?.State : _kittyProjectionState;
        KittyProjectionState state = new(_anchorRevision, ViewportTopAbsoluteRow,
            Columns, ViewportRows, _alternateBufferActive, captured);
        TerminalKittyLocatedPlaceholder[]? runs = _kittyPlaceholderScene is not null
            ? PreparePlaceholderRuns(renderRows, oldRuns) : oldRuns;
        bool runsChanged = !ReferenceEquals(runs, oldRuns);
        if (oldState == state && !runsChanged)
            return extended ? _kittyOverscanProjection!.Placements : _kittyPlacements;

        List<TerminalKittyImagePlacement> visible = new(_kittyAnchoredPlacements?.Length ?? 0);
        if (_kittyPlaceholderScene is { } scene) visible.AddRange(scene.Fixed);
        foreach (TerminalKittyAnchoredPlacement placement in _kittyAnchoredPlacements ?? [])
        {
            if (!TryResolveAnchor(placement.Anchor, out TerminalGridPosition origin)) continue;
            long column = OffsetKittyCoordinate(origin.Column, placement.ColumnOffset);
            long row = OffsetKittyCoordinate(origin.Row, placement.RowOffset, state.ViewportTop);
            AppendKittyProjection(visible, column, row, placement.Columns, placement.Rows, placement.Geometry, captured);
        }
        if (_kittyPlaceholderScene is { } placeholders) AppendPlaceholderProjection(visible, placeholders, runs, captured);
        visible.Sort(TerminalKittyImagePlacement.ComparePaintOrder);

        // Replace the immutable projection instead of changing an array retained
        // by a previous frame or a copy-on-write screen.
        TerminalKittyImagePlacement[] prepared = visible.ToArray();
        MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.KittyProjectionPrepared);
        if (extended)
        {
            // Keep one bounded extra-range cache separate from ordinary viewport
            // queries (including HasKittyGraphics), so alternating consumers don't
            // allocate/rebuild the same projection on every frame. COW copies may
            // share this immutable cache until their own rows/anchors change.
            _kittyOverscanProjection = new(state, runs, prepared);
        }
        else
        {
            _kittyPlacements = prepared;
            _kittyPlaceholderRuns = runs;
            _kittyProjectionState = state;
        }
        return prepared;
    }

    private void AppendKittyProjection(List<TerminalKittyImagePlacement> visible, long column, long row,
        uint columns, uint rows, TerminalKittyImagePlacement geometry, TerminalRenderOverscan overscan)
    {
        // Subtract the bounded extent instead of adding it to a possibly extreme
        // relative coordinate. This also rejects empty geometry without overflow.
        if (rows == 0 || columns == 0 || row <= -(long)overscan.Above - rows ||
            row >= (long)ViewportRows + overscan.Below || column <= -(long)columns || column >= Columns) return;
        visible.Add(new(geometry.ImageId, geometry.Layer,
            (int)Math.Clamp(column, int.MinValue, int.MaxValue), (int)Math.Clamp(row, int.MinValue, int.MaxValue),
            geometry.XOffsetPx, geometry.YOffsetPx, geometry.WidthPx, geometry.HeightPx,
            geometry.SourceX, geometry.SourceY, geometry.SourceWidth, geometry.SourceHeight,
            geometry.CellWidthPx, geometry.CellHeightPx, geometry.ScaleMode, geometry.ZIndex));
    }

    private readonly record struct KittyProjectionState(
        long AnchorRevision, int ViewportTop, int Columns, int Rows, bool Alternate, TerminalRenderOverscan Overscan);

    private sealed record KittyOverscanProjection(KittyProjectionState State,
        TerminalKittyLocatedPlaceholder[]? Runs, TerminalKittyImagePlacement[] Placements);

    private static long OffsetKittyCoordinate(int coordinate, long offset, int viewportOrigin = 0)
        => (long)Int128.Clamp((Int128)coordinate + offset - viewportOrigin, long.MinValue, long.MaxValue);
}
