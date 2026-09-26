// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    /// <summary>
    /// Creates a copy-on-write state for a screen transaction (output hold or resize). The caller
    /// must hold the screen lock and retain no writable cell references across this call.
    /// </summary>
    internal TerminalScreen CreateStateCopy()
    {
        TerminalScreen copy = new(CopyRows(_rows));
        copy._glyphGlossary = _glyphGlossary is { Count: > 0 } ? _glyphGlossary.Copy() : null;
        copy._primaryRows = CopyOptionalRows(_primaryRows);
        copy._alternateRows = CopyOptionalRows(_alternateRows);
        copy._rasterImagesById = new(_rasterImagesById);
        copy._rasterPlacements = new(_rasterPlacements);
        copy._primaryRasterImagesById = CopyOptionalImages(_primaryRasterImagesById);
        copy._alternateRasterImagesById = CopyOptionalImages(_alternateRasterImagesById);
        copy._primaryRasterPlacements = CopyOptionalPlacements(_primaryRasterPlacements);
        copy._alternateRasterPlacements = CopyOptionalPlacements(_alternateRasterPlacements);
        CopyScalarStateTo(copy);
        CopyRegistry(_hyperlinksById, copy._hyperlinksById);
        CopyRegistry(_hyperlinkIdsByUrl, copy._hyperlinkIdsByUrl);
        copy._hyperlinkIdentities.CopyFrom(_hyperlinkIdentities);
        CopyRegistry(_kittyImagesById, copy._kittyImagesById);
        copy._kittyPlacements = _kittyPlacements;
        copy._kittyAnchoredPlacements = _kittyAnchoredPlacements;
        copy._kittyPlaceholderScene = _kittyPlaceholderScene;
        copy._kittyPlaceholderRuns = _kittyPlaceholderRuns;
        copy._kittyProjectionState = _kittyProjectionState;
        copy._trackedAnchors = new(_trackedAnchors);
        copy._snapshotPageTracker = _snapshotPageTracker?.Copy();
        return copy;

        TerminalRowBuffer? CopyOptionalRows(TerminalRowBuffer? rows) => rows is null
            ? null
            : ReferenceEquals(rows, _rows) ? copy._rows : CopyRows(rows);

        Dictionary<int, TerminalRasterImageSource>? CopyOptionalImages(Dictionary<int, TerminalRasterImageSource>? images) => images is null
            ? null
            : ReferenceEquals(images, _rasterImagesById) ? copy._rasterImagesById : new(images);

        List<TerminalRasterImagePlacement>? CopyOptionalPlacements(List<TerminalRasterImagePlacement>? placements) => placements is null
            ? null
            : ReferenceEquals(placements, _rasterPlacements) ? copy._rasterPlacements : new(placements);
    }

    /// <summary>
    /// Publishes an isolated transaction's complete state without copying its cell arrays again.
    /// The caller must hold the destination lock and discard the source after transfer.
    /// All allocations belong to staging; publication transfers registry ownership too,
    /// so an allocation failure cannot leave new rows with incomplete hyperlink tables.
    /// </summary>
    internal void AdoptStateFrom(TerminalScreen source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(this, source)) return;
        _glyphGlossary = source._glyphGlossary;
        _rows = source._rows;
        _primaryRows = source._primaryRows;
        _alternateRows = source._alternateRows;
        _rasterImagesById = source._rasterImagesById;
        _rasterPlacements = source._rasterPlacements;
        _primaryRasterImagesById = source._primaryRasterImagesById;
        _alternateRasterImagesById = source._alternateRasterImagesById;
        _primaryRasterPlacements = source._primaryRasterPlacements;
        _alternateRasterPlacements = source._alternateRasterPlacements;
        source.CopyScalarStateTo(this);
        _hyperlinksById = source._hyperlinksById;
        _hyperlinkIdsByUrl = source._hyperlinkIdsByUrl;
        _hyperlinkIdentities = source._hyperlinkIdentities;
        _kittyImagesById = source._kittyImagesById;
        _kittyPlacements = source._kittyPlacements;
        _kittyAnchoredPlacements = source._kittyAnchoredPlacements;
        _kittyPlaceholderScene = source._kittyPlaceholderScene;
        _kittyPlaceholderRuns = source._kittyPlaceholderRuns;
        _kittyProjectionState = source._kittyProjectionState;
        _trackedAnchors = source._trackedAnchors;
        _snapshotPageTracker = source._snapshotPageTracker;
        InvalidateAll();
    }

    private void CopyScalarStateTo(TerminalScreen destination)
    {
        destination.Columns = Columns;
        destination.ViewportRows = ViewportRows;
        destination._scrollbackLimit = _scrollbackLimit;
        destination._viewportTop = _viewportTop;
        destination._primaryScrollOffset = _primaryScrollOffset;
        destination._alternateBufferActive = _alternateBufferActive;
        destination._theme = _theme;
        destination._themeRevision = _themeRevision;
        destination.DefaultForeground = DefaultForeground;
        destination.DefaultBackground = DefaultBackground;
        destination._nextHyperlinkId = _nextHyperlinkId;
        destination._nextRasterImageId = _nextRasterImageId;
        destination._anchorRevision = _anchorRevision;
        destination.GlyphRevision = GlyphRevision;
        destination._snapshotLineage = _snapshotLineage;
        destination._snapshotAlternateGeneration = _snapshotAlternateGeneration;
        destination._snapshotRowGeometry = _snapshotRowGeometry;
        destination._snapshotAlternateLineLimit = _snapshotAlternateLineLimit;
        destination._snapshotScrollbackQuota = _snapshotScrollbackQuota;
    }

    private TerminalRowBuffer CopyRows(TerminalRowBuffer rows)
    {
        TerminalRowBuffer copy = new(rows.Count);
        for (int index = 0; index < rows.Count; index++)
        {
            copy.Add(rows[index].CreateStateCopy());
        }
        return copy;
    }

    private static void CopyRegistry<TKey, TValue>(Dictionary<TKey, TValue> source, Dictionary<TKey, TValue> destination)
        where TKey : notnull
    {
        destination.Clear();
        foreach ((TKey key, TValue value) in source)
        {
            destination.Add(key, value);
        }
    }
}
