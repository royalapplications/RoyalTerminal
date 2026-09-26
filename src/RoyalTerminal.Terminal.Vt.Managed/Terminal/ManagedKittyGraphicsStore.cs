// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Owns the images, placements and bounded storage for one terminal buffer.</summary>
internal sealed partial class ManagedKittyGraphicsStore(int byteLimit)
{
    private readonly Dictionary<uint, Image> _images = [];
    private readonly Dictionary<PlacementKey, Placement> _placements = [];
    private uint _nextImageId = 2147483647;
    private uint _nextInternalPlacementId;
    private ulong _generation;
    private long _storedBytes;

    internal bool Enabled => byteLimit > 0;
    internal long StoredBytes => _storedBytes;
    internal int ImageCount => _images.Count;
    internal int PlacementCount => _placements.Count;
    internal ulong Revision => _generation;
    internal Dictionary<uint, Image>.ValueCollection Images => _images.Values;
    internal IEnumerable<KeyValuePair<PlacementKey, Placement>> Placements => _placements;
    internal ManagedKittyImageLoader? Loading { get; set; }
    internal uint LoadingImageId { get; set; }
    internal ulong LoadingTargetGeneration { get; set; }

    // Resize ticks animations and retires pruned placements before publication.
    // Fork their mutable ownership; loaders and frame pixels are not edited by
    // resize. No image bytes are copied, including incomplete chunked transfers.
    internal ManagedKittyGraphicsStore CreateStateCopy()
    {
        ManagedKittyGraphicsStore copy = new(byteLimit)
        {
            _nextImageId = _nextImageId,
            _nextInternalPlacementId = _nextInternalPlacementId,
            _generation = _generation,
            _storedBytes = _storedBytes,
            Loading = Loading,
            LoadingImageId = LoadingImageId,
            LoadingTargetGeneration = LoadingTargetGeneration,
        };
        foreach ((uint id, Image image) in _images) copy._images.Add(id, image.CreateStateCopy());
        foreach ((PlacementKey key, Placement placement) in _placements)
            copy._placements.Add(key, new(placement.Anchor, placement.Parent, placement.Virtual,
                placement.HorizontalOffset, placement.VerticalOffset, placement.Options));
        copy._marginScrollRestores.AddRange(_marginScrollRestores);
        return copy;
    }

    internal Image? Find(uint id, uint number = 0)
    {
        if (id != 0) return _images.GetValueOrDefault(id);
        if (number == 0) return null;
        return FindByNumber(number);
    }

    // Unlike ID-or-number addressing, d=n/N accepts number zero and selects
    // the newest unnumbered image (Ghostty ImageStorage.imageByNumber).
    internal Image? FindByNumber(uint number)
    {
        Image? newest = null;
        foreach (Image image in _images.Values)
            if (image.Number == number && (newest is null || image.Generation > newest.Generation)) newest = image;
        return newest;
    }

    internal uint AllocateImageId(bool imageNumber)
    {
        uint id = imageNumber ? 1 : _nextImageId;
        for (int count = 0; count < _images.Count + 2; count++)
        {
            if (id != 0 && !_images.ContainsKey(id)) break;
            id = unchecked(id + 1);
        }
        if (id == 0) id = 1;
        if (!imageNumber) _nextImageId = unchecked(id + 1);
        return id;
    }

    internal bool TryAddImage(TerminalScreen screen, uint id, uint number, ManagedKittyImagePixels decoded,
        bool transient, out string error)
    {
        error = "ENOMEM: out of memory";
        int storageBytes = decoded.StorageBytes;
        Image? existing = Find(id);
        long oldBytes = existing?.QuotaBytes ?? 0;
        if (storageBytes > byteLimit || storageBytes < 0) return false;
        _images.EnsureCapacity(_images.Count + 1);
        if (!TryReserve(screen, storageBytes - oldBytes, id)) return false;
        if (existing is not null) RemoveImage(screen, id);
        _images[id] = new(id, number, decoded, storageBytes, transient, ++_generation);
        _storedBytes += storageBytes;
        error = "OK";
        return true;
    }

    internal bool TryReserveAnimation(TerminalScreen screen, Image image, long additionalBytes)
    {
        // Edits are exempt from admission, even after RGB promotion takes the
        // existing image over quota. Appends follow reserveAnimationBytes:
        // other images can be evicted even when this reservation later fails.
        if (additionalBytes == 0) return true;
        if (additionalBytes < 0 || additionalBytes > byteLimit ||
            _storedBytes + additionalBytes - byteLimit > byteLimit) return false;
        return TryReserve(screen, additionalBytes, image.Id);
    }

    internal void ConvertImageToRgba(Image image)
    {
        if (!image.Animation.PromoteRootToRgba()) return;
        CommitAnimationBytes(image);
        MarkContentChanged(image);
    }

    internal void CommitAnimationBytes(Image image)
    {
        long bytes = image.Animation.StoredBytes;
        _storedBytes += bytes - image.QuotaBytes;
        image.QuotaBytes = bytes;
        _generation++;
    }

    internal void MarkContentChanged(Image image) => image.Generation = ++_generation;

    internal bool RemoveImage(TerminalScreen screen, uint id)
    {
        if (!_images.Remove(id, out Image? image)) return false;
        _generation++;
        _storedBytes -= image.QuotaBytes;
        foreach ((PlacementKey key, Placement _) in _placements)
            if (key.ImageId == id) RemovePlacement(screen, key);
        RemoveOrphans(screen);
        return true;
    }

    internal void Clear(TerminalScreen screen)
    {
        foreach (Placement placement in _placements.Values)
            if (placement.Anchor is not null) screen.ReleaseAnchor(placement.Anchor);
        _placements.Clear();
        _images.Clear();
        Loading = null;
        LoadingImageId = 0;
        LoadingTargetGeneration = 0;
        _nextImageId = 2147483647;
        _nextInternalPlacementId = 0;
        _marginScrollRestores.Clear();
        _storedBytes = 0;
        _generation++;
    }

    internal bool TryAddPlacement(TerminalScreen screen, Image image, ManagedKittyGraphicsCommand command,
        int absoluteRow, int column, uint cellWidth, uint cellHeight, out Placement? placement, out string error)
    {
        placement = null;
        error = "EINVAL: virtual placement cannot refer to a parent";
        bool virtualPlacement = command.Get('U') != 0;
        uint parentId = command.Get('P');
        if (virtualPlacement && parentId != 0) return false;
        ReapPrunedPlacements(screen);
        PlacementKey key = command.PlacementId > 0
            ? new(image.Id, command.PlacementId, false)
            : new(image.Id, NextInternalPlacementId(image.Id), true);
        PlacementKey? parent = null;
        if (parentId != 0)
        {
            if (!TryResolveParent(command.PlacementId > 0 ? key : null, parentId, command.Get('Q'), out PlacementKey resolved, out error)) return false;
            parent = resolved;
        }

        TerminalScreenAnchor? anchor = virtualPlacement || parent is not null ? null : screen.CreateAnchor(absoluteRow, column);
        placement = new(anchor, parent, virtualPlacement, command.GetSigned('H'), command.GetSigned('V'),
            ManagedKittyPlacementOptions.From(command, cellWidth, cellHeight));
        RemovePlacement(screen, key);
        _placements[key] = placement;
        image.PlacementCount++;
        _generation++;
        error = "OK";
        return true;
    }

    internal bool RemovePlacement(TerminalScreen screen, PlacementKey key)
    {
        if (!_placements.Remove(key, out Placement? placement)) return false;
        _generation++;
        if (placement.Anchor is not null) screen.ReleaseAnchor(placement.Anchor);
        if (_images.TryGetValue(key.ImageId, out Image? image)) image.PlacementCount--;
        return true;
    }

    internal void ReapPrunedPlacements(TerminalScreen screen)
    {
        foreach ((PlacementKey key, Placement placement) in _placements)
            if (placement.Anchor is not null && !screen.TryResolveAnchor(placement.Anchor, out _)) RemovePlacement(screen, key);
        RemoveOrphans(screen);
    }

    internal void RemoveOrphans(TerminalScreen screen)
    {
        bool removed;
        do
        {
            removed = false;
            foreach ((PlacementKey key, Placement placement) in _placements)
                if (placement.Parent is PlacementKey parent && !_placements.ContainsKey(parent)) removed |= RemovePlacement(screen, key);
        } while (removed);
    }

    internal bool TryResolveRoot(TerminalScreen screen, Placement placement, out Placement? root, out long horizontalOffset, out long verticalOffset)
    {
        root = placement;
        horizontalOffset = verticalOffset = 0;
        for (int depth = 0; depth <= 8; depth++)
        {
            if (root.Parent is not PlacementKey parent) return true;
            horizontalOffset = Math.Clamp(horizontalOffset + root.HorizontalOffset, int.MinValue, int.MaxValue);
            verticalOffset = Math.Clamp(verticalOffset + root.VerticalOffset, int.MinValue, int.MaxValue);
            if (!_placements.TryGetValue(parent, out root)) return false;
        }
        root = null;
        return false;
    }

    private bool TryResolveParent(PlacementKey? child, uint imageId, uint placementId, out PlacementKey parent, out string error)
    {
        parent = default;
        error = "ENOPARENT: parent image not found";
        if (!_images.ContainsKey(imageId)) return false;
        PlacementKey? candidate = null;
        if (placementId > 0)
        {
            PlacementKey exact = new(imageId, placementId, false);
            if (_placements.ContainsKey(exact)) candidate = exact;
        }
        else
        {
            foreach (PlacementKey key in _placements.Keys)
                if (key.ImageId == imageId && (candidate is null || key.PreferredOver(candidate.Value))) candidate = key;
        }
        error = "ENOPARENT: parent placement not found";
        if (candidate is not PlacementKey selected) return false;
        error = "EINVAL: placement cannot be its own parent";
        if (selected == child) return false;
        PlacementKey ancestor = selected;
        for (int depth = 1; ; depth++)
        {
            error = "ECYCLE: parent chain creates a cycle";
            if (ancestor == child) return false;
            error = "ENOENT: parent chain ancestor not found";
            if (!_placements.TryGetValue(ancestor, out Placement? placement)) return false;
            if (placement.Parent is not PlacementKey next) break;
            error = "ETOODEEP: parent chain too deep";
            if (depth >= 8) return false;
            ancestor = next;
        }
        parent = selected;
        error = "OK";
        return true;
    }

    private uint NextInternalPlacementId(uint imageId)
    {
        uint id;
        do { id = _nextInternalPlacementId; _nextInternalPlacementId = unchecked(id + 1); }
        while (_placements.ContainsKey(new(imageId, id, true)));
        return id;
    }

    private bool TryReserve(TerminalScreen screen, long additionalBytes, uint exceptId)
    {
        while (_storedBytes + additionalBytes > byteLimit)
        {
            Image? victim = null;
            foreach (Image image in _images.Values)
            {
                if (image.Id == exceptId) continue;
                if (victim is null || image.EvictionPriority < victim.EvictionPriority ||
                    (image.EvictionPriority == victim.EvictionPriority &&
                     (image.Generation < victim.Generation || image.Generation == victim.Generation && image.Id < victim.Id))) victim = image;
            }
            if (victim is null) return false;
            RemoveImage(screen, victim.Id);
        }
        return true;
    }

    internal sealed class Image(uint id, uint number, ManagedKittyImagePixels decoded, long quotaBytes, bool transient, ulong generation,
        ManagedKittyAnimation? animation = null)
    {
        private KittyGraphicsDecodedImage? _published;
        private TerminalKittyImageSource? _source;
        internal uint Id { get; } = id;
        internal uint Number { get; } = number;
        internal ulong Generation { get; set; } = generation;
        internal ManagedKittyAnimation Animation { get; } = animation ?? new(decoded);
        internal long QuotaBytes { get; set; } = quotaBytes;
        internal int PlacementCount { get; set; }
        internal int EvictionPriority => (transient ? 0 : 1) + (PlacementCount > 0 ? 2 : 0);
        internal Image CreateStateCopy() => new(Id, Number, Animation.CurrentPixels, QuotaBytes, transient, Generation, Animation.CreateStateCopy())
        {
            PlacementCount = PlacementCount,
            _published = _published,
            _source = _source,
        };
        internal TerminalKittyImageSource Source
        {
            get
            {
                KittyGraphicsDecodedImage current = Animation.CurrentImage;
                if (!ReferenceEquals(current, _published))
                {
                    _published = current;
                    _source = new(unchecked((int)Id), current.Width, current.Height, current.Rgba);
                }
                return _source!;
            }
        }
    }

    internal readonly record struct PlacementKey(uint ImageId, uint Id, bool Internal)
    {
        internal bool PreferredOver(PlacementKey other) => Internal != other.Internal ? !Internal : Id < other.Id;
    }

    internal sealed class Placement(TerminalScreenAnchor? anchor, PlacementKey? parent, bool virtualPlacement,
        int horizontalOffset, int verticalOffset, ManagedKittyPlacementOptions options)
    {
        internal TerminalScreenAnchor? Anchor { get; } = anchor;
        internal PlacementKey? Parent { get; } = parent;
        internal bool Virtual { get; } = virtualPlacement;
        internal int HorizontalOffset { get; } = horizontalOffset;
        internal int VerticalOffset { get; } = verticalOffset;
        internal ManagedKittyPlacementOptions Options { get; set; } = options;
    }
}
