// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    // Instance-local preparation scratch, never copied/adopted as screen state.
    // After publication it retains capacity, but no image/pixel references.
    private Dictionary<int, TerminalKittyImageSource>? _kittyImagePublicationScratch;

    private Dictionary<int, TerminalKittyImageSource> PrepareKittyImages(IReadOnlyList<TerminalKittyImageSource>? images)
    {
        Dictionary<int, TerminalKittyImageSource> prepared = _kittyImagePublicationScratch ??= new();
        try
        {
            prepared.EnsureCapacity(images?.Count ?? 0);
            if (images is not null)
                for (int i = 0; i < images.Count; i++)
                {
                    TerminalKittyImageSource image = images[i];
                    prepared[image.ImageId] = image;
                }
            MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.KittyImagesPrepared);
            return prepared;
        }
        catch
        {
            prepared.Clear();
            throw;
        }
    }

    private void PublishKittyImages(Dictionary<int, TerminalKittyImageSource> prepared)
    {
        Dictionary<int, TerminalKittyImageSource> previous = _kittyImagesById;
        _kittyImagesById = prepared;
        _kittyImagePublicationScratch = previous;
        previous.Clear();
    }

    internal void ReplaceKittyGraphicsWithPlaceholders(IReadOnlyList<TerminalKittyImageSource>? images,
        IReadOnlyList<TerminalKittyImagePlacement>? placements,
        IReadOnlyList<TerminalKittyPlaceholderTarget> targets,
        IReadOnlyList<TerminalKittyRelativePlacement> relatives, uint cellWidth, uint cellHeight)
    {
        ThrowIfSnapshotMutationFailed();
        TerminalKittyImagePlacement[] preparedPlacements = [];
        if (placements is { Count: > 0 })
        {
            preparedPlacements = new TerminalKittyImagePlacement[placements.Count];
            for (int i = 0; i < placements.Count; i++) preparedPlacements[i] = placements[i];
            Array.Sort(preparedPlacements, TerminalKittyImagePlacement.ComparePaintOrder);
        }
        MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.KittyPlacementsPrepared);
        TerminalKittyPlaceholderScene? scene = CreateKittyPlaceholderScene(targets, relatives,
            preparedPlacements, cellWidth, cellHeight);
        MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.KittyScenePrepared);
        Dictionary<int, TerminalKittyImageSource> preparedImages = PrepareKittyImages(images);

        // No input callbacks, allocations or monitor entry after publication.
        PublishKittyImages(preparedImages);
        _kittyPlacements = preparedPlacements;
        _kittyAnchoredPlacements = null;
        _kittyPlaceholderScene = scene;
        _kittyPlaceholderRuns = null;
        _kittyProjectionState = null;
        _kittyOverscanProjection = null;
        InvalidateViewportCore();
    }
}
