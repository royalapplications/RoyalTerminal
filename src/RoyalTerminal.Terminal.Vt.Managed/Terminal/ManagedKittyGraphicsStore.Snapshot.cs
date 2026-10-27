// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

internal sealed partial class ManagedKittyGraphicsStore
{
    internal Dictionary<uint, PendingImage> RestoreSnapshot(TerminalScreen screen, GhosttySnapshotScreen source,
        BasicVtProcessorOptions options)
    {
        Dictionary<uint, PendingImage> tickets = [];
        if (source.Kitty is not { } storage || !Enabled) return tickets;
        foreach (GhosttySnapshotKittyImage entry in storage.Images)
        {
            if (!entry.Valid || entry.Length > int.MaxValue ||
                GhosttySnapshotKittyImage.ExpectedLength(entry.Width, entry.Height, 1) is not { } rgbaLength ||
                rgbaLength > (ulong)Math.Max(0, options.KittyGraphicsMaxImageBytes)) continue;
            ulong total = entry.Length;
            if (entry.Animation is { } metadata)
                foreach (GhosttySnapshotKittyFrame frame in metadata.Frames)
                {
                    if (frame.Length > (ulong)byteLimit || total > (ulong)byteLimit - frame.Length)
                    { total = (ulong)byteLimit + 1; break; }
                    total += frame.Length;
                }
            if (total > (ulong)byteLimit) continue;
            ManagedKittyImagePixels root = ManagedKittyImagePixels.Pending((int)entry.Width, (int)entry.Height, entry.Format, (int)entry.Length);
            if (!TryAddPendingImage(screen, entry.Id, entry.Number, root, (entry.Flags & 1) != 0, out PendingImage ticket, out _)) continue;
            Image image = _images[entry.Id];
            image.ImplicitId = (entry.Flags & 2) != 0;
            if (entry.Animation is { } animation && TryReserveAnimation(screen, image, (long)(total - entry.Length)))
            {
                ManagedKittyImagePixels[] frames = new ManagedKittyImagePixels[animation.Frames.Length];
                uint[] gaps = new uint[frames.Length + 1]; gaps[0] = animation.RootGap;
                for (int i = 0; i < frames.Length; i++)
                {
                    frames[i] = ManagedKittyImagePixels.Pending((int)entry.Width, (int)entry.Height, 1, (int)animation.Frames[i].Length);
                    gaps[i + 1] = animation.Frames[i].Gap;
                }
                image.Animation = ManagedKittyAnimation.Restore(root, frames, gaps, animation.State,
                    animation.MaximumLoops, animation.CurrentLoop, (int)animation.CurrentIndex);
                CommitAnimationBytes(image);
            }
            tickets[entry.Id] = ticket;
        }
        int[] starts = new int[source.Pages.Length];
        for (int i = 1; i < starts.Length; i++) starts[i] = checked(starts[i - 1] + source.Pages[i - 1].Grid.Rows);
        RestorePlacements(screen, source.State.Key, storage.Placements, starts, source.Pages, null);
        if (storage.Loading.Length > 0)
        {
            Loading = ManagedKittyImageLoader.RestoreSnapshot(storage.Loading, options, out uint id);
            LoadingImageId = id;
            Image? target = Loading?.InitialCommand.Action == 'f' ? Find(id, Loading.InitialCommand.ImageNumber) : null;
            LoadingTargetGeneration = target?.Generation ?? 0;
            if (target is not null) LoadingImageId = target.Id;
        }
        _nextImageId = storage.NextImageId; _nextInternalPlacementId = storage.NextPlacementId;
        return tickets;
    }

    internal void RestoreHistoryPlacements(TerminalScreen screen, int key, GhosttySnapshotPage page,
        GhosttySnapshotKittyPlacement[] placements, Dictionary<uint, PendingImage> tickets)
        => RestorePlacements(screen, key, placements, [0], [page], tickets);

    private void RestorePlacements(TerminalScreen screen, int key, GhosttySnapshotKittyPlacement[] placements,
        ReadOnlySpan<int> pageStarts, ReadOnlySpan<GhosttySnapshotPage> pages, Dictionary<uint, PendingImage>? tickets)
    {
        foreach (GhosttySnapshotKittyPlacement p in placements)
        {
            if (!p.Key.Valid || !_images.TryGetValue(p.Key.ImageId, out Image? image)) continue;
            if (tickets is not null && (!tickets.TryGetValue(image.Id, out PendingImage ticket) || ticket.Transmission != image.Transmission)) continue;
            PlacementKey identity = Key(p.Key);
            if (_placements.ContainsKey(identity)) continue;
            TerminalScreenAnchor? anchor = null;
            PlacementKey? parent = null;
            switch (p.Location)
            {
                case 0:
                    if (p.Page >= pages.Length || p.Row >= pages[p.Page].Grid.Rows || p.Column >= pages[p.Page].Grid.Columns) continue;
                    anchor = screen.CreateSnapshotAnchor(key, pageStarts[p.Page] + p.Row, p.Column); break;
                case 1: break;
                case 2:
                    if (!p.Parent.Valid || !_placements.ContainsKey(Key(p.Parent))) continue;
                    parent = Key(p.Parent); break;
                default: continue;
            }
            try
            {
                _placements.Add(identity, new(anchor, parent, p.Location == 1, p.Horizontal, p.Vertical,
                    new(p.SourceX, p.SourceY, p.SourceWidth, p.SourceHeight, p.OffsetX, p.OffsetY, p.Columns, p.Rows, p.Z)));
                image.PlacementCount++; _generation++;
            }
            catch { if (anchor is not null) screen.ReleaseAnchor(anchor); throw; }
        }
    }

    internal GhosttySnapshotKittyStorage? CaptureSnapshot(TerminalScreen screen, int key, GhosttySnapshotPagePlan plan)
    {
        if (_images.Count == 0 && _placements.Count == 0 && Loading is null) return null;
        Image[] ordered = [.. _images.Values];
        Array.Sort(ordered, static (a, b) => a.Generation != b.Generation ? a.Generation.CompareTo(b.Generation) : a.Id.CompareTo(b.Id));
        GhosttySnapshotKittyImage[] images = new GhosttySnapshotKittyImage[ordered.Length];
        for (int i = 0; i < images.Length; i++)
        {
            Image image = ordered[i]; ManagedKittyAnimation animation = image.Animation;
            ManagedKittyImagePixels root = animation.FramePixels(0);
            GhosttySnapshotKittyFrame[] frames = new GhosttySnapshotKittyFrame[animation.FrameCount - 1];
            for (int f = 0; f < frames.Length; f++) frames[f] = new(animation.FrameGap(f + 1), (ulong)animation.FramePixels(f + 1).StorageBytes);
            GhosttySnapshotKittyAnimation state = new((byte)animation.State, animation.MaximumLoops, animation.CurrentLoop,
                (uint)animation.CurrentIndex, animation.FrameGap(0), frames);
            images[i] = new(image.Id, image.Number, (uint)root.Width, (uint)root.Height, root.Format,
                (byte)((image.Transient ? 1 : 0) | (image.ImplicitId ? 2 : 0)), (ulong)root.StorageBytes, state);
        }
        List<GhosttySnapshotKittyPlacement> placements = [];
        HashSet<PlacementKey> included = [];
        // Parents precede children even when the insertion order changed after
        // replacements; unresolved/cyclic chains are omitted like upstream.
        for (int pass = 0; pass <= _placements.Count; pass++)
        {
            bool changed = false;
            foreach ((PlacementKey identity, Placement placement) in _placements)
            {
                if (included.Contains(identity) || !_images.ContainsKey(identity.ImageId)) continue;
                if (placement.Parent is { } parent && !included.Contains(parent)) continue;
                if (CapturePlacement(screen, key, identity, placement, plan.Resident, out GhosttySnapshotKittyPlacement? encoded))
                { placements.Add(encoded!); included.Add(identity); changed = true; }
            }
            if (!changed) break;
        }
        return new(key, _nextImageId, _nextInternalPlacementId, images, [.. placements], Loading?.CaptureSnapshot(LoadingImageId) ?? []);
    }

    internal GhosttySnapshotKittyPagePlacements? CaptureHistoryPlacements(TerminalScreen screen, int key, GhosttySnapshotPagePlan.Range range)
    {
        List<GhosttySnapshotKittyPlacement> placements = [];
        List<GhosttySnapshotPagePlan.Range> ranges = [range];
        HashSet<PlacementKey> included = [];
        for (int pass = 0; pass <= _placements.Count; pass++)
        {
            bool changed = false;
            foreach ((PlacementKey identity, Placement placement) in _placements)
            {
                if (included.Contains(identity) || placement.Virtual ||
                    placement.Parent is { } parent && !included.Contains(parent)) continue;
                if (!CapturePlacement(screen, key, identity, placement, ranges, out GhosttySnapshotKittyPlacement? encoded)) continue;
                placements.Add(encoded!); included.Add(identity); changed = true;
            }
            if (!changed) break;
        }
        return placements.Count == 0 ? null : new(key, [.. placements]);
    }

    private static bool CapturePlacement(TerminalScreen screen, int key, PlacementKey identity, Placement p,
        List<GhosttySnapshotPagePlan.Range> ranges, out GhosttySnapshotKittyPlacement? encoded)
    {
        encoded = null; ushort page = 0, row = 0, column = 0;
        if (p.Anchor is { } anchor)
        {
            if (!screen.TryResolveSnapshotAnchor(anchor, key, out TerminalGridPosition cell)) return false;
            int index = -1;
            for (int i = 0; i < ranges.Count; i++)
                if (cell.Row >= ranges[i].Start && cell.Row < ranges[i].Start + ranges[i].Count) { index = i; break; }
            if (index < 0) return false;
            page = checked((ushort)index); row = checked((ushort)(cell.Row - ranges[index].Start)); column = checked((ushort)cell.Column);
        }
        ManagedKittyPlacementOptions o = p.Options;
        encoded = new(WireKey(identity), p.Parent is not null ? (byte)2 : p.Virtual ? (byte)1 : (byte)0,
            page, row, column, p.Parent is { } parent ? WireKey(parent) : default, p.HorizontalOffset, p.VerticalOffset,
            o.XOffset, o.YOffset, o.SourceX, o.SourceY, o.SourceWidth, o.SourceHeight, o.Columns, o.Rows, o.Z);
        return true;
    }
    private static PlacementKey Key(GhosttySnapshotKittyKey key) => new(key.ImageId, key.Id, key.Tag == 0);
    private static GhosttySnapshotKittyKey WireKey(PlacementKey key) => new(key.ImageId, key.Internal ? (byte)0 : (byte)1, key.Id);
}
