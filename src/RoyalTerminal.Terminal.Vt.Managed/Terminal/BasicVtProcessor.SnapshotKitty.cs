// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private Dictionary<uint, ManagedKittyGraphicsStore.PendingImage>? _snapshotPrimaryKitty;
    private Dictionary<uint, ManagedKittyGraphicsStore.PendingImage>? _snapshotAlternateKitty;
    private object? _snapshotKittyLineage;
    private ulong _snapshotKittyAlternateGeneration;

    private void InstallSnapshotKitty(GhosttySnapshotReadyState ready)
    {
        bool present = false;
        foreach (GhosttySnapshotScreen source in ready.Screens) present |= source.Kitty is not null;
        if (!present) return;
        _snapshotKittyLineage = _screen.SnapshotLineage;
        _snapshotKittyAlternateGeneration = _screen.GetSnapshotGeneration(1);
        foreach (GhosttySnapshotScreen source in ready.Screens)
        {
            if (source.Kitty is null) continue;
            ManagedKittyGraphicsStore store = source.State.Key == 0 ? _primaryKittyStore
                : _alternateKittyStore ??= new(_options.KittyGraphicsStorageLimitBytes);
            Dictionary<uint, ManagedKittyGraphicsStore.PendingImage> tickets = store.RestoreSnapshot(_screen, source, _options);
            if (source.State.Key == 0) _snapshotPrimaryKitty = tickets;
            else _snapshotAlternateKitty = tickets;
        }
        PublishKittyGraphics();
    }

    internal int ApplySnapshotKittyPixels(GhosttySnapshotKittyPayload payload)
    {
        _screen.ThrowIfSnapshotMutationFailed();
        if (!TryGetSnapshotKitty(payload.Key, out ManagedKittyGraphicsStore? store,
                out Dictionary<uint, ManagedKittyGraphicsStore.PendingImage>? tickets) ||
            !tickets!.TryGetValue(payload.ImageId, out ManagedKittyGraphicsStore.PendingImage ticket)) return 0;
        ManagedKittyGraphicsStore.Image? image = store!.Find(payload.ImageId);
        if (image is null || image.Transmission != ticket.Transmission || payload.Frame == 0 || payload.Frame > image.Animation.FrameCount) return 0;
        ManagedKittyImagePixels pending = image.Animation.FramePixels((int)payload.Frame - 1);
        if (!pending.IsPending || (ulong)pending.StorageBytes != payload.DeclaredLength) return 0;
        if ((ulong)payload.Pixels.Length < payload.DeclaredLength) throw new EndOfStreamException();
        if ((ulong)payload.Pixels.Length != payload.DeclaredLength) throw new InvalidDataException("Trailing Kitty image payload bytes.");
        byte[] pixels = payload.Pixels.ToArray();
        bool applied = payload.Frame == 1 ? ticket.Complete(store, pixels) : ticket.CompleteFrame(store, payload.Frame, pixels);
        if (applied && ReferenceEquals(store, _kittyStore))
        {
            // Completion can make a restored running animation schedulable
            // without any further child output arriving.
            AdvanceKittyAnimations();
            PublishKittyGraphics();
        }
        return applied ? payload.Pixels.Length : 0;
    }

    internal void ApplySnapshotKittyHistory(int key, GhosttySnapshotPage page, GhosttySnapshotKittyPlacement[] placements)
    {
        if (!TryGetSnapshotKitty(key, out ManagedKittyGraphicsStore? store,
                out Dictionary<uint, ManagedKittyGraphicsStore.PendingImage>? tickets)) return;
        store!.RestoreHistoryPlacements(_screen, key, page, placements, tickets!);
        if (ReferenceEquals(store, _kittyStore)) PublishKittyGraphics();
    }

    private bool TryGetSnapshotKitty(int key, out ManagedKittyGraphicsStore? store,
        out Dictionary<uint, ManagedKittyGraphicsStore.PendingImage>? tickets)
    {
        store = key == 0 ? _primaryKittyStore : _alternateKittyStore;
        tickets = key == 0 ? _snapshotPrimaryKitty : _snapshotAlternateKitty;
        return ReferenceEquals(_snapshotKittyLineage, _screen.SnapshotLineage) && store is not null && tickets is not null &&
            (key == 0 || _snapshotKittyAlternateGeneration == _screen.GetSnapshotGeneration(1));
    }
}
